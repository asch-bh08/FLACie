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
public sealed partial class JellyfinClient(HttpClient http, string deviceId, string deviceName = "FLACie Web")
{
    public const string Client = "ipodplayer"; // the Android app's client id: changing it would orphan saved profiles
    public const string Version = "0.8";
    const string ProfileKey = "ipodplayer.profile";

    public static string Normalise(string server)
    {
        var s = server.Trim().TrimEnd('/');
        return s.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? s : "http://" + s;
    }

    public string DeviceId => deviceId;

    /// <summary>When this machine reaches Jellyfin at a different address than the users do (a Docker host that cannot loop back to its
    /// own public/Tailscale address), requests to <see cref="PublicBase"/> are sent to <see cref="InternalBase"/> instead. Accounts and the
    /// shared profile keep the public address, so the phone still gets one it can use.</summary>
    public string? PublicBase { get; set; }
    public string? InternalBase { get; set; }
    string Route(string url) => InternalBase is { Length: > 0 } i && PublicBase is { Length: > 0 } p && url.StartsWith(p, StringComparison.OrdinalIgnoreCase) ? i.TrimEnd('/') + url[p.Length..] : url;

    /// <summary>A GET on the user's Jellyfin server with their token (JSON, or null for an empty reply).</summary>
    public Task<JsonNode?> GetAsync(JellyfinAccount a, string path, CancellationToken ct = default) => Send(HttpMethod.Get, a.Server + path, a.Token, null, ct);
    public Task<JsonNode?> PostAsync(JellyfinAccount a, string path, JsonNode? body = null, CancellationToken ct = default) => Send(HttpMethod.Post, a.Server + path, a.Token, body, ct);
    public Task<JsonNode?> DeleteAsync(JellyfinAccount a, string path, CancellationToken ct = default) => Send(HttpMethod.Delete, a.Server + path, a.Token, null, ct);

    /// <summary>The live connection Jellyfin's remote-control and SyncPlay messages arrive on.</summary>
    public Uri SocketUri(JellyfinAccount a) => new("ws" + Route(a.Server.TrimEnd('/'))[4..] + $"/socket?api_key={Uri.EscapeDataString(a.Token)}&deviceId={Uri.EscapeDataString(deviceId)}");

    /// <summary>One song by Jellyfin id, for songs another device sends that this library hasn't listed.</summary>
    public async Task<Track?> TrackAsync(JellyfinAccount a, string id, CancellationToken ct = default)
    {
        try { return await GetAsync(a, $"/Users/{a.UserId}/Items/{id}", ct) is { } o ? ToTrack(a.Server, o) : null; } catch (Exception) { return null; }
    }

    string Auth(string? token) => $"MediaBrowser Client=\"{Client}\", Device=\"{deviceName}\", DeviceId=\"{deviceId}\", Version=\"{Version}\"" + (token is null ? "" : $", Token=\"{token}\"");

    async Task<JsonNode?> Send(HttpMethod method, string url, string? token, JsonNode? body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, Route(url));
        req.Headers.TryAddWithoutValidation("Authorization", Auth(token));
        if (body is not null) req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        else if (method == HttpMethod.Post) req.Content = new StringContent("", Encoding.UTF8, "application/json");
        using var res = await http.SendAsync(req, ct);
        if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized) throw new UnauthorizedAccessException("Jellyfin didn't accept that sign-in.");
        if (!res.IsSuccessStatusCode)
        {
            // Jellyfin says what is wrong in the body (e.g. a SyncPlay refusal); keep it so the page can show it
            var why = (await res.Content.ReadAsStringAsync(ct)).Trim();
            throw new HttpRequestException($"{(int)res.StatusCode} {res.ReasonPhrase}{(why.Length > 0 && why.Length < 300 ? ": " + why : "")}", null, res.StatusCode);
        }
        var text = await res.Content.ReadAsStringAsync(ct);
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

    /// <summary>Every song the user can see, paged.</summary>
    public async Task<List<Track>> AllAudioAsync(JellyfinAccount a, CancellationToken ct = default)
    {
        var all = new List<Track>();
        for (var start = 0; ; start += 2500)
        {
            var page = await Send(HttpMethod.Get, $"{a.Server}/Users/{a.UserId}/Items?IncludeItemTypes=Audio&Recursive=true&SortBy=SortName&Fields=Path,DateCreated,Genres&EnableUserData=false&StartIndex={start}&Limit=2500", a.Token, null, ct);
            var items = page?["Items"]?.AsArray();
            if (items is null || items.Count == 0) break;
            foreach (var o in items) if (o is not null) all.Add(ToTrack(a.Server, o));
            if (items.Count < 2500) break;
        }
        return all;
    }

    public static Track ToTrack(string server, JsonNode o)
    {
        var id = o["Id"]!.GetValue<string>();
        string Str(string k) => o[k]?.GetValue<string>() ?? "";
        var artist = o["Artists"]?.AsArray().FirstOrDefault()?.GetValue<string>() ?? Str("AlbumArtist");
        var albumId = Str("AlbumId");
        // the song's own picture first (embedded art differs per song); the album's only when the song has none, because a shared
        // folder.jpg would put one cover on every song in a folder; a catalog lookup when neither exists
        // a song on a compilation gets its own album's cover from the catalog, whatever the file's embedded picture is
        var art = Art.OnCompilation(artist, Str("AlbumArtist"), Str("Album")) && Art.LookupKey(artist, "", Str("Name")) is { } own ? own
                : o["ImageTags"]?["Primary"] is not null ? "jf" + id
                : albumId.Length > 0 && o["AlbumPrimaryImageTag"] is not null ? "jf" + albumId
                : Art.LookupKey(artist, Str("Album"), Str("Name"));
        DateTime.TryParse(Str("DateCreated"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var created);
        return new Track($"{server}/Audio/{id}/stream?static=true", Str("Name"), artist, Str("Album"), Str("AlbumArtist").Length > 0 ? Str("AlbumArtist") : artist,
            o["IndexNumber"]?.GetValue<int>() ?? 0, o["ParentIndexNumber"]?.GetValue<int>() ?? 0, (o["RunTimeTicks"]?.GetValue<long>() ?? 0) / 10_000,
            o["ProductionYear"]?.GetValue<int>() ?? 0, art, created == default ? 0 : new DateTimeOffset(created).ToUnixTimeMilliseconds(),
            TrackSource.Jellyfin, Str("Path"), id, o["Genres"]?.AsArray().FirstOrDefault()?.GetValue<string>() ?? "");
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
    public string ImageUrl(JellyfinAccount a, string itemId, int width = 400) => $"{a.Server}/Items/{itemId}/Images/Primary?maxWidth={width}&quality=90";

    /// <summary>A request with this user's token, for proxying audio and covers.</summary>
    public HttpRequestMessage Authorized(JellyfinAccount a, string url) { var r = new HttpRequestMessage(HttpMethod.Get, Route(url)); r.Headers.TryAddWithoutValidation("Authorization", Auth(a.Token)); return r; }

    // ---- profile ----

    string PrefsUrl(JellyfinAccount a) => $"{a.Server}/DisplayPreferences/{Client}?userId={Uri.EscapeDataString(a.UserId)}&client={Client}";

    public async Task<JsonObject?> PullProfileAsync(JellyfinAccount a, CancellationToken ct = default)
    {
        var dto = await Send(HttpMethod.Get, PrefsUrl(a), a.Token, null, ct);
        var raw = dto?["CustomPrefs"]?[ProfileKey]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(raw) || raw == "null") return null;
        try { return JsonNode.Parse(raw) as JsonObject; } catch (JsonException) { return null; }
    }

    public async Task PushProfileAsync(JellyfinAccount a, JsonObject profile, CancellationToken ct = default)
    {
        var dto = await Send(HttpMethod.Get, PrefsUrl(a), a.Token, null, ct) as JsonObject ?? new JsonObject();
        var custom = dto["CustomPrefs"] as JsonObject ?? new JsonObject();
        custom[ProfileKey] = profile.ToJsonString();
        dto["CustomPrefs"] = custom; dto["Client"] = Client;
        await Send(HttpMethod.Post, PrefsUrl(a), a.Token, dto, ct);
    }

    /// <summary>Total size on disk of the account's songs on this Jellyfin (it reports each file's size only with the media sources, so this pages through them).</summary>
    public async Task<(int Songs, long Bytes)> LibrarySizeAsync(JellyfinAccount a, CancellationToken ct = default)
    {
        long bytes = 0; var songs = 0;
        for (var start = 0; ; start += 1000)
        {
            var page = await Send(HttpMethod.Get, $"{a.Server}/Users/{a.UserId}/Items?IncludeItemTypes=Audio&Recursive=true&Fields=MediaSources&EnableUserData=false&EnableImages=false&StartIndex={start}&Limit=1000", a.Token, null, ct);
            var items = page?["Items"]?.AsArray();
            if (items is null || items.Count == 0) break;
            foreach (var o in items) { songs++; bytes += o?["MediaSources"]?.AsArray().FirstOrDefault()?["Size"]?.GetValue<long>() ?? 0; }
            if (items.Count < 1000) break;
        }
        return (songs, bytes);
    }
}
