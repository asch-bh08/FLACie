using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FLACie.Core;

/// <summary>A Jellyfin user session: who signed in and their access token.</summary>
public sealed record JellyfinAccount(string Server, string UserId, string UserName, string Token);

/// <summary>Talks to a Jellyfin server as one user: sign-in (password or Quick Connect), the music library, playlists and
/// the profile stored in the user's display preferences (same place and format as the Android app, so they share it).</summary>
public sealed partial class JellyfinClient(HttpClient http, string deviceId, string deviceName = "FLACie Web", JellyfinGate? gate = null)
{
    /// <summary>Marks a request message as one for Jellyfin, so <see cref="GatedHandler"/> lets it through the gate.</summary>
    public static readonly HttpRequestOptionsKey<bool> GateKey = new("flacie.gate");
    /// <summary>The one door to Jellyfin: concurrency limit, priorities, circuit breaker (see <see cref="JellyfinGate"/>).</summary>
    public JellyfinGate Gate { get; } = gate ?? new JellyfinGate();
    public const string Client = "ipodplayer"; // the Android app's client id: changing it would orphan saved profiles
    public const string Version = "0.8";
    /// <summary>The name sent with every request. It has to stay the same as the one the device signed in with.</summary>
    public const string ClientName = Client; // Jellyfin keys a device's session by this name: a different one splits it in two and breaks remote control
    const string ProfileKey = "ipodplayer.profile";

    public static string Normalise(string server)
    {
        var s = server.Trim().TrimEnd('/');
        return s.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? s : "http://" + s;
    }

    public string DeviceId => deviceId;

    /// <summary>How many calls this client made to Jellyfin in the last minute, by kind (for the admin dashboard).</summary>
    public JellyfinCallStats Stats { get; } = new();

    /// <summary>When this machine reaches Jellyfin at a different address than the users do (a Docker host that cannot loop back to its
    /// own public/Tailscale address), requests to <see cref="PublicBase"/> are sent to <see cref="InternalBase"/> instead. Accounts and the
    /// shared profile keep the public address, so the phone still gets one it can use.</summary>
    public string? PublicBase { get; set; }
    public string? InternalBase { get; set; }
    string Route(string url) => InternalBase is { Length: > 0 } i && PublicBase is { Length: > 0 } p && url.StartsWith(p, StringComparison.OrdinalIgnoreCase) ? i.TrimEnd('/') + url[p.Length..] : url;

    /// <summary>A GET on the user's Jellyfin server with their token (JSON, or null for an empty reply).</summary>
    public Task<JsonNode?> GetAsync(JellyfinAccount a, string path, CancellationToken ct = default, int? slowMs = null) => Send(HttpMethod.Get, a.Server + path, a.Token, null, ct, slowMs);
    public Task<JsonNode?> PostAsync(JellyfinAccount a, string path, JsonNode? body = null, CancellationToken ct = default) => Send(HttpMethod.Post, a.Server + path, a.Token, body, ct);
    public Task<JsonNode?> DeleteAsync(JellyfinAccount a, string path, CancellationToken ct = default) => Send(HttpMethod.Delete, a.Server + path, a.Token, null, ct);

    /// <summary>The live connection Jellyfin's remote-control and SyncPlay messages arrive on.</summary>
    public Uri SocketUri(JellyfinAccount a) => new("ws" + Route(a.Server.TrimEnd('/'))[4..] + $"/socket?ApiKey={Uri.EscapeDataString(a.Token)}&deviceId={Uri.EscapeDataString(deviceId)}");

    /// <summary>One song by Jellyfin id, for songs another device sends that this library hasn't listed.</summary>
    public async Task<Track?> TrackAsync(JellyfinAccount a, string id, CancellationToken ct = default)
    {
        try { return await GetAsync(a, $"/Users/{a.UserId}/Items/{id}", ct) is { } o ? TrackTitles.CleanAll([ToTrack(a.Server, o)])[0] : null; } catch (Exception) { return null; }
    }

    string Auth(string? token) => $"MediaBrowser Client=\"{ClientName}\", Device=\"{deviceName}\", DeviceId=\"{deviceId}\", Version=\"{Version}\"" + (token is null ? "" : $", Token=\"{token}\"");

    /// <summary>Raised with the token whenever Jellyfin answers 401 to a request made with it, so whoever remembers who a token belongs to can forget it at once.</summary>
    public event Action<string>? TokenRefused;

    async Task<JsonNode?> Send(HttpMethod method, string url, string? token, JsonNode? body, CancellationToken ct, int? slowMs = null)
    {
        async Task<string> Once(CancellationToken c)
        {
            Stats.HitUrl(url);
            using var req = new HttpRequestMessage(method, Route(url));
            req.Headers.TryAddWithoutValidation("Authorization", Auth(token));
            if (body is not null) req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            else if (method == HttpMethod.Post) req.Content = new StringContent("", Encoding.UTF8, "application/json");
            using var res = await http.SendAsync(req, c);
            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                if (token is not null) try { TokenRefused?.Invoke(token); } catch (Exception) { }
                throw new UnauthorizedAccessException("Jellyfin didn't accept that sign-in.");
            }
            if (!res.IsSuccessStatusCode)
            {
                // Jellyfin says what is wrong in the body (e.g. a SyncPlay refusal); keep it so the page can show it
                var why = (await res.Content.ReadAsStringAsync(c)).Trim();
                var msg = $"{(int)res.StatusCode} {res.ReasonPhrase}{(why.Length > 0 && why.Length < 300 ? ": " + why : "")}";
                if ((int)res.StatusCode >= 500 || res.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    var ra = res.Headers.RetryAfter is { } r ? r.Delta ?? (r.Date is { } d ? d - DateTimeOffset.UtcNow : null) : null;
                    throw new JellyfinHttpException(res.StatusCode, msg, ra);
                }
                throw new HttpRequestException(msg, null, res.StatusCode);
            }
            return await res.Content.ReadAsStringAsync(c);
        }
        // every request waits its turn at the gate; only reads are retried, and identical reads in flight at the same moment are one request
        var text = method == HttpMethod.Get
            ? await Gate.RunSharedAsync($"GET {url}|{token}", JellyfinGate.Current, Once, ct, slowMs)
            : await Gate.RunAsync(JellyfinGate.Current, Once, ct, maxRetries: 0, slowMs: slowMs);
        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    // ---- sign in ----

    public async Task<JellyfinAccount> SignInAsync(string server, string user, string password, CancellationToken ct = default)
    {
        var s = Normalise(server);
        var auth = await Send(HttpMethod.Post, $"{s}/Users/AuthenticateByName", null, new JsonObject { ["Username"] = user, ["Pw"] = password }, ct)
                   ?? throw new InvalidOperationException("Empty sign-in response");
        return ToAccount(s, auth);
    }

    /// <summary>Starts Quick Connect: show <c>Code</c> to the user, then poll <see cref="QuickConnectPollAsync"/> with <c>Secret</c>.</summary>
    public async Task<(string Code, string Secret)> QuickConnectStartAsync(string server, CancellationToken ct = default)
    {
        var r = await Send(HttpMethod.Post, $"{Normalise(server)}/QuickConnect/Initiate", null, null, ct) ?? throw new InvalidOperationException("Quick Connect is off on this server.");
        return (r["Code"]!.GetValue<string>(), r["Secret"]!.GetValue<string>());
    }

    /// <summary>Null until the code is approved in another Jellyfin app.</summary>
    public async Task<JellyfinAccount?> QuickConnectPollAsync(string server, string secret, CancellationToken ct = default)
    {
        var s = Normalise(server);
        var r = await Send(HttpMethod.Get, $"{s}/QuickConnect/Connect?secret={Uri.EscapeDataString(secret)}", null, null, ct);
        if (r?["Authenticated"]?.GetValue<bool>() != true) return null;
        var auth = await Send(HttpMethod.Post, $"{s}/Users/AuthenticateWithQuickConnect", null, new JsonObject { ["Secret"] = secret }, ct)
                   ?? throw new InvalidOperationException("Empty sign-in response");
        return ToAccount(s, auth);
    }

    static JellyfinAccount ToAccount(string server, JsonNode auth) =>
        new(server, auth["User"]!["Id"]!.GetValue<string>(), auth["User"]!["Name"]?.GetValue<string>() ?? "", auth["AccessToken"]!.GetValue<string>());

    public Task SignOutAsync(JellyfinAccount a, CancellationToken ct = default) => Send(HttpMethod.Post, $"{a.Server}/Sessions/Logout", a.Token, null, ct);

    // ---- library ----

    /// <summary>Every song the user can see.</summary>
    public async Task<List<Track>> AllAudioAsync(JellyfinAccount a, CancellationToken ct = default, Action<List<Track>>? progress = null, Action<Track, MediaInfo>? facts = null) =>
        (await ListAudioAsync(a, null, ct, progress, facts)).Tracks;

    /// <summary>The songs of a listing, the newest DateLastSaved among them (the cursor for the next delta) and the library's total.</summary>
    public sealed record AudioListing(List<Track> Tracks, string? MaxSaved, int Total);

    public const int ListingPage = 500;
    /// <summary>What counts as slow for an answer that carries hundreds of songs with their streams.</summary>
    public const int BigAnswerMs = 12_000;

    /// <summary>
    /// Jellyfin's MinDateLastSaved is inclusive: asking with the newest DateLastSaved seen would bring that very song back with every delta (and a "changed"
    /// library that never settles). The cursor is therefore moved on by one tick (100 ns), so only songs saved after it come back.
    /// </summary>
    public static string AfterCursor(string cursor) =>
        DateTime.TryParse(cursor, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) && d.Ticks < DateTime.MaxValue.Ticks
            ? d.AddTicks(1).ToString("O", CultureInfo.InvariantCulture) : cursor;

    /// <summary>The Jellyfin id of every song the user can see, and nothing else (a light listing: no streams, no pictures). Used to find songs that were removed.</summary>
    public async Task<HashSet<string>> ListAudioIdsAsync(JellyfinAccount a, CancellationToken ct = default)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var start = 0; ; start += 2000)
        {
            var page = await Send(HttpMethod.Get, $"{a.Server}/Users/{a.UserId}/Items?IncludeItemTypes=Audio&Recursive=true&SortBy=SortName&Fields=&EnableImages=false&EnableUserData=false&EnableTotalRecordCount=false&StartIndex={start}&Limit=2000", a.Token, null, ct, BigAnswerMs);
            var items = page?["Items"]?.AsArray();
            if (items is null || items.Count == 0) break;
            foreach (var o in items) if (o?["Id"]?.GetValue<string>() is { Length: > 0 } id) ids.Add(id.Replace("-", ""));
            if (items.Count < 2000) break;
        }
        return ids;
    }

    /// <summary>
    /// The library in pages of <see cref="ListingPage"/> songs, each song with its streams (codec, bit depth, sample rate, bit rate) so nobody has to ask for them one by one:
    /// <paramref name="facts"/> gets every song's real facts, <paramref name="progress"/> the songs so far after each page (so a first sync can show songs while it runs).
    /// With <paramref name="since"/> (a DateLastSaved from an earlier listing) only songs saved since then come back: a delta. A song saved exactly at that moment that
    /// <paramref name="alreadyKnown"/> (by normalised id) says the library has is the one the cursor came from, not a change: a Jellyfin that answers "since" inclusively
    /// would hand it back with every delta.
    /// </summary>
    public async Task<AudioListing> ListAudioAsync(JellyfinAccount a, string? since, CancellationToken ct = default, Action<List<Track>>? progress = null, Action<Track, MediaInfo>? facts = null, Func<string, bool>? alreadyKnown = null)
    {
        var sync = new object(); var all = new List<Track>(); string? maxSaved = null; long maxTicks = long.MinValue;
        var after = since is null ? "" : "&MinDateLastSaved=" + Uri.EscapeDataString(AfterCursor(since));
        async Task<int> Page(int start)
        {
            // a page of 500 songs with their streams is a big answer: it is judged by its own (long) yardstick, not as "Jellyfin is slow"
            var page = await Send(HttpMethod.Get, $"{a.Server}/Users/{a.UserId}/Items?IncludeItemTypes=Audio&Recursive=true&SortBy=SortName&Fields=Path,DateCreated,DateLastSaved,Genres,MediaSources,MediaStreams&EnableUserData=false&StartIndex={start}&Limit={ListingPage}{after}", a.Token, null, ct, BigAnswerMs);
            var items = page?["Items"]?.AsArray().OfType<JsonObject>().ToList() ?? [];
            var tracks = items.Select(o => (Track: ToTrack(a.Server, o), Info: ParseMediaInfo(o), Saved: o["DateLastSaved"]?.GetValue<string>())).ToList();
            List<Track>? snapshot = null;
            lock (sync)
            {
                foreach (var (t, info, saved) in tracks)
                {
                    // newest by time, not by text (a fraction of a second is written with as many digits as it needs)
                    if (saved is not null && DateTime.TryParse(saved, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var savedAt) && savedAt.Ticks > maxTicks) { maxTicks = savedAt.Ticks; maxSaved = saved; }
                    if (since is not null && saved == since && alreadyKnown is not null && t.JellyfinId is { } jid && alreadyKnown(jid.Replace("-", "").ToLowerInvariant())) continue;
                    all.Add(t);
                    if (info is { Known: true }) facts?.Invoke(t, info);
                }
                if (progress is not null) snapshot = [.. all];
            }
            if (snapshot is not null) progress!(TrackTitles.CleanAll(snapshot));
            return page?["TotalRecordCount"]?.GetValue<int>() ?? 0;
        }
        var total = await Page(0);
        if (total > ListingPage)
        {
            using var gate = new SemaphoreSlim(4);
            await Task.WhenAll(Enumerable.Range(1, (total - 1) / ListingPage).Select(async i =>
            {
                await gate.WaitAsync(ct);
                try { await Page(i * ListingPage); } finally { gate.Release(); }
            }));
        }
        return new AudioListing(TrackTitles.CleanAll(all), maxSaved, total);
    }

    /// <summary>
    /// What Jellyfin has counted for this user across every app and device: songs played (most played first) and favourites, with the play count and the
    /// last time. This is the account's whole listening history, from before FLACie, and what the Quick picks learn from.
    /// </summary>
    public async Task<List<JfPlay>> PlayStatsAsync(JellyfinAccount a, CancellationToken ct = default)
    {
        var res = new Dictionary<string, JfPlay>(StringComparer.OrdinalIgnoreCase);
        async Task Pull(string filter, string sort)
        {
            var page = await Send(HttpMethod.Get, $"{a.Server}/Users/{a.UserId}/Items?IncludeItemTypes=Audio&Recursive=true&Filters={filter}&SortBy={sort}&SortOrder=Descending&EnableUserData=true&EnableImages=false&EnableTotalRecordCount=false&Limit=3000", a.Token, null, ct);
            foreach (var o in page?["Items"]?.AsArray() ?? [])
            {
                if (o?["Id"]?.GetValue<string>() is not { Length: > 0 } id) continue;
                var ud = o["UserData"];
                var plays = ud?["PlayCount"] is JsonValue pv && pv.TryGetValue<int>(out var pc) ? pc : 0;
                long last = 0;
                if (ud?["LastPlayedDate"]?.GetValue<string>() is { Length: > 0 } ls && DateTime.TryParse(ls, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var lp)) last = new DateTimeOffset(lp).ToUnixTimeMilliseconds();
                var fav = ud?["IsFavorite"] is JsonValue fv && fv.TryGetValue<bool>(out var fb) && fb;
                var key = id.Replace("-", "");
                res[key] = res.TryGetValue(key, out var old) ? new(key, Math.Max(old.Plays, plays), Math.Max(old.LastMs, last), old.Favourite || fav) : new(key, plays, last, fav);
            }
        }
        await Pull("IsPlayed", "PlayCount");
        try { await Pull("IsFavorite", "SortName"); } catch (Exception) when (res.Count > 0) { }
        return [.. res.Values];
    }

    public static Track ToTrack(string server, JsonNode o)
    {
        var id = o["Id"]!.GetValue<string>();
        string Str(string k) => o[k]?.GetValue<string>() ?? "";
        var artist = o["Artists"]?.AsArray().FirstOrDefault()?.GetValue<string>() ?? Str("AlbumArtist");
        var albumId = Str("AlbumId");
        // a song on a compilation gets its own album's cover from the catalog, whatever the file's embedded picture is.
        // Otherwise the ALBUM's picture (AlbumId + its image tag) when the item says it has one: every song of an album then shares one key, so one
        // cover is fetched, kept and cached for the whole album (Jellyfin gives each song with embedded art its own tag: ten thousand covers otherwise).
        // On a compilation each song keeps its own picture (the album's is just one of them); a catalog lookup when no picture exists at all.
        var onCompilation = Art.OnCompilation(artist, Str("AlbumArtist"), Str("Album"));
        var art = onCompilation && Art.LookupKey(artist, "", Str("Name")) is { } own ? own
                : !onCompilation && albumId.Length > 0 && o["AlbumPrimaryImageTag"] is JsonValue at ? "jf" + albumId + "~" + at.ToString()
                : o["ImageTags"]?["Primary"] is JsonValue pt ? "jf" + id + "~" + pt.ToString()
                : albumId.Length > 0 && o["AlbumPrimaryImageTag"] is JsonValue at2 ? "jf" + albumId + "~" + at2.ToString()
                : Art.LookupKey(artist, Str("Album"), Str("Name"));
        DateTime.TryParse(Str("DateCreated"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var created);
        return new Track($"{server}/Audio/{id}/stream?static=true", Str("Name"), artist, Str("Album"), Str("AlbumArtist").Length > 0 ? Str("AlbumArtist") : artist,
            o["IndexNumber"]?.GetValue<int>() ?? 0, o["ParentIndexNumber"]?.GetValue<int>() ?? 0, (o["RunTimeTicks"]?.GetValue<long>() ?? 0) / 10_000,
            o["ProductionYear"]?.GetValue<int>() ?? 0, art, created == default ? 0 : new DateTimeOffset(created).ToUnixTimeMilliseconds(),
            TrackSource.Jellyfin, Str("Path"), id, o["Genres"]?.AsArray().FirstOrDefault()?.GetValue<string>() ?? "");
    }

    /// <summary>The cover key of a Jellyfin item record (what another device is playing, a session's NowPlayingItem): the album's picture with its image tag (the same key
    /// the library uses, so it is already on disk), else the item's own, always with the tag (a key without one could never be told from a changed picture).</summary>
    public static string? ItemArtKey(JsonNode? item)
    {
        if (item is null) return null;
        if (item["AlbumId"]?.GetValue<string>() is { Length: > 0 } album && item["AlbumPrimaryImageTag"] is JsonValue at) return "jf" + album + "~" + at;
        if (item["Id"]?.GetValue<string>() is { Length: > 0 } id && item["ImageTags"]?["Primary"] is JsonValue pt) return "jf" + id + "~" + pt;
        return null;
    }

    /// <summary>The user's own playlists with their songs' item ids.</summary>
    public async Task<List<(string Id, string Name, List<string> ItemIds)>> PlaylistsAsync(JellyfinAccount a, CancellationToken ct = default)
    {
        var res = new List<(string, string, List<string>)>();
        var lists = await Send(HttpMethod.Get, $"{a.Server}/Users/{a.UserId}/Items?IncludeItemTypes=Playlist&Recursive=true", a.Token, null, ct);
        foreach (var p in lists?["Items"]?.AsArray() ?? [])
        {
            if (p is null) continue;
            var id = p["Id"]!.GetValue<string>();
            var items = await Send(HttpMethod.Get, $"{a.Server}/Playlists/{id}/Items?UserId={a.UserId}&Fields=Path", a.Token, null, ct);
            res.Add((id, p["Name"]?.GetValue<string>() ?? "", (items?["Items"]?.AsArray() ?? []).Where(i => i?["MediaType"]?.GetValue<string>() == "Audio").Select(i => i!["Id"]!.GetValue<string>()).ToList()));
        }
        return res;
    }

    public string StreamUrl(JellyfinAccount a, string itemId) => $"{a.Server}/Audio/{itemId}/stream?static=true";
    /// <summary>A cover. Always the same parameters for the same use (a fixed width and quality, the item's image tag), so Jellyfin's own image cache answers repeats. width 0 = the original.</summary>
    public string ImageUrl(JellyfinAccount a, string itemId, int width = 400, string? tag = null, bool webp = false) =>
        $"{a.Server}/Items/{itemId}/Images/Primary?" + (width > 0 ? $"maxWidth={width}&quality={(webp ? 80 : 90)}" : "quality=100") + (webp ? "&format=Webp" : "") + (tag is { Length: > 0 } ? $"&tag={Uri.EscapeDataString(tag)}" : "");

    /// <summary>A request with this user's token, for proxying audio and covers.</summary>
    public HttpRequestMessage Authorized(JellyfinAccount a, string url) { Stats.HitUrl(url); var r = new HttpRequestMessage(HttpMethod.Get, Route(url)); r.Options.Set(GateKey, true); r.Headers.TryAddWithoutValidation("Authorization", Auth(a.Token)); return r; }

    // ---- profile ----

    string PrefsUrl(JellyfinAccount a) => $"{a.Server}/DisplayPreferences/{Client}?userId={Uri.EscapeDataString(a.UserId)}&client={Client}";

    public async Task<JsonObject?> PullProfileAsync(JellyfinAccount a, CancellationToken ct = default)
    {
        var dto = await Send(HttpMethod.Get, PrefsUrl(a), a.Token, null, ct);
        var raw = dto?["CustomPrefs"]?[ProfileKey]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(raw) || raw == "null") return null;
        try { return JsonNode.Parse(raw) as JsonObject; } catch (JsonException) { return null; }
    }

    public Task PushProfileAsync(JellyfinAccount a, JsonObject profile, CancellationToken ct = default) => PushProfileAsync(a, profile.ToJsonString(), ct);

    /// <summary>Stores an already serialised profile (so the caller can take the snapshot under its own lock).</summary>
    public async Task PushProfileAsync(JellyfinAccount a, string profileJson, CancellationToken ct = default)
    {
        var dto = await Send(HttpMethod.Get, PrefsUrl(a), a.Token, null, ct) as JsonObject ?? new JsonObject();
        var custom = dto["CustomPrefs"] as JsonObject ?? new JsonObject();
        custom[ProfileKey] = profileJson;
        dto["CustomPrefs"] = custom; dto["Client"] = Client;
        await Send(HttpMethod.Post, PrefsUrl(a), a.Token, dto, ct);
    }

    /// <summary>Total size on disk of the account's songs on this Jellyfin (it reports each file's size only with the media sources, so this pages through them).</summary>
    public async Task<(int Songs, long Bytes)> LibrarySizeAsync(JellyfinAccount a, CancellationToken ct = default)
    {
        long bytes = 0; var songs = 0;
        for (var start = 0; ; start += 1000)
        {
            var page = await Send(HttpMethod.Get, $"{a.Server}/Users/{a.UserId}/Items?IncludeItemTypes=Audio&Recursive=true&Fields=MediaSources&EnableUserData=false&EnableImages=false&StartIndex={start}&Limit=1000", a.Token, null, ct, BigAnswerMs);
            var items = page?["Items"]?.AsArray();
            if (items is null || items.Count == 0) break;
            foreach (var o in items) { songs++; bytes += o?["MediaSources"]?.AsArray().FirstOrDefault()?["Size"]?.GetValue<long>() ?? 0; }
            if (items.Count < 1000) break;
        }
        return (songs, bytes);
    }
}
