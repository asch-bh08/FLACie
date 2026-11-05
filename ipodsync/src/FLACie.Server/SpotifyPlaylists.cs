using System.Text.Json.Nodes;
using FLACie.Core;

namespace FLACie.Server;

/// <summary>
/// F23: the playlists Search shows. Spotify's editorial lists (decades, genres, charts: a fixed list of ids) read from their public embed page; an
/// artist's best-of from Deezer ("100% &lt;artist&gt;", Deezer's own editors; Spotify's This Is lists can only be found with an account, which FLACie does
/// not use); FLACie's own made lists as the fallback, so the row is never empty. Lists are fetched only when a search needs them, through
/// <see cref="WebSourceGate"/> (1 a second per source, 6 hours' rest after an error) into <see cref="WebPlaylistStore"/>. Songs are matched to the
/// library like Import; the missing ones are downloaded as interest shows (<see cref="PlaylistFetchPolicy"/>), through the normal download pipeline.
/// </summary>
public sealed class SpotifyPlaylists(HttpClient http, WebPlaylistStore store, WebSourceGate gate, IDownloads downloads, Func<UserSession, long?> freeBytes,
    Func<UserSession, IReadOnlyList<MadePlaylist>> own, Func<DateTime> now, Func<UserSession, Library>? library = null) : IWebPlaylists
{
    Library Lib(UserSession s) => library?.Invoke(s) ?? s.Library;

    public static readonly (string Id, string Name, WebPlaylistKind Kind, string Subject)[] Fixed =
    [
        ("37i9dQZF1DXaKIA8E7WcJj", "All Out 60s", WebPlaylistKind.Decade, "1960s"), ("37i9dQZF1DWTJ7xPn4vNaz", "All Out 70s", WebPlaylistKind.Decade, "1970s"),
        ("37i9dQZF1DX4UtSsGT1Sbe", "All Out 80s", WebPlaylistKind.Decade, "1980s"), ("37i9dQZF1DXbTxeAdrVG2l", "All Out 90s", WebPlaylistKind.Decade, "1990s"),
        ("37i9dQZF1DX4o1oenSJRJd", "All Out 2000s", WebPlaylistKind.Decade, "2000s"), ("37i9dQZF1DX5Ejj0EkURtP", "All Out 2010s", WebPlaylistKind.Decade, "2010s"),
        ("37i9dQZF1DXcBWIGoYBM5M", "Today's Top Hits", WebPlaylistKind.Chart, "Top Hits"), ("37i9dQZF1DWXRqgorJj26U", "Rock Classics", WebPlaylistKind.Genre, "Rock"),
        ("37i9dQZF1DWUa8ZRTfalHk", "Pop Rising", WebPlaylistKind.Genre, "Pop"), ("37i9dQZF1DX0XUsuxWHRQd", "RapCaviar", WebPlaylistKind.Genre, "Hip-Hop"),
        ("37i9dQZF1DX4dyzvuaRJ0n", "mint", WebPlaylistKind.Genre, "Dance"), ("37i9dQZF1DX1lVhptIYRda", "Hot Country", WebPlaylistKind.Genre, "Country"),
        ("37i9dQZF1DXbITWG1ZJKYt", "Jazz Classics", WebPlaylistKind.Genre, "Jazz"), ("37i9dQZF1DWWEJlAGA9gs0", "Classical Essentials", WebPlaylistKind.Genre, "Classical"),
    ];

    /// <summary>The same list is not queued again for the same person within this time (a search runs on every keystroke).</summary>
    public static readonly TimeSpan Again = TimeSpan.FromHours(1);
    readonly Dictionary<string, DateTime> queued = [];

    public WebPlaylist? Find(string source, string id) => store.Use(source, id);

    public async Task<IReadOnlyList<WebPlaylist>> ForSearchAsync(UserSession s, string query, CancellationToken ct)
    {
        var (found, kind) = await GatherAsync(s, query, ct);
        var shown = WebPlaylistPick.ForAll(found, query, kind, now());
        // what showed interest: the artist's best-of (an artist search), and popular lists shown (their first 2 songs, so they start at once)
        foreach (var l in shown.Where(l => l.Source != "own"))
            Interest(s, l, kind == PlaylistQueryKind.Artist && l.Kind == WebPlaylistKind.Artist ? FetchTrigger.ArtistSearch : FetchTrigger.Popular);
        return shown;
    }

    public async Task<IReadOnlyList<WebPlaylist>> ForFilterAsync(UserSession s, string query, string? genre, CancellationToken ct)
    {
        var (found, _) = await GatherAsync(s, query.Trim().Length > 0 ? query : genre ?? "", ct);
        return WebPlaylistPick.ForFilter(found.Concat(store.All).DistinctBy(l => l.Source + ":" + l.Id), query, genre, now());
    }

    public void Interest(UserSession s, WebPlaylist list, FetchTrigger trigger, int from = 0)
    {
        if (!downloads.Ready(s)) return;
        // a SoundCloud set is known by name only until someone opens or plays it: its songs are read first (F25.3)
        if (list.Tracks.Count == 0)
        {
            if (trigger is FetchTrigger.Opened or FetchTrigger.Played) _ = Task.Run(async () => { var full = await FillAsync(s, list, CancellationToken.None); if (full.Tracks.Count > 0) Interest(s, full, trigger, from); });
            return;
        }
        var key = $"{s.Id}|{list.Source}:{list.Id}|{trigger}|{from}";
        lock (queued)
        {
            if (queued.TryGetValue(key, out var at) && now() - at < Again) return;
            queued[key] = now();
            if (queued.Count > 5000) queued.Clear();
        }
        store.Use(list.Source, list.Id);
        var lib = Lib(s);
        foreach (var t in PlaylistFetchPolicy.Plan(trigger, list.Tracks, t => lib.Find(t.Title, t.Artist) is not null, freeBytes(s), from))
            downloads.Song(s, new CatalogSong(t.Artist, t.Title, "", null, t.DurationMs, 0, 0));
    }

    /// <summary>F25.3: the sets of the artist's own SoundCloud account, by name (flacie-ytdl /artist-sets), remembered 7 days.</summary>
    public static readonly TimeSpan AccountKept = TimeSpan.FromDays(7);
    readonly Dictionary<string, (DateTime At, List<WebPlaylist> Sets)> soundcloud = [];

    async Task<List<WebPlaylist>> SoundCloudSetsAsync(UserSession s, string artist, Library lib, CancellationToken ct)
    {
        var key = Matching.PrimaryArtist(artist);
        lock (soundcloud) if (soundcloud.TryGetValue(key, out var known) && now() - known.At < AccountKept) return known.Sets.Select(l => store.Use(l.Source, l.Id) ?? l).ToList();
        if (s.Services.FileMoverUrl.Length == 0 || !gate.TryTake("soundcloud")) return [];
        try
        {
            var answer = await MoverAsync(s, "/artist-sets", new System.Text.Json.Nodes.JsonObject { ["q"] = artist }, ct);
            if (answer is null) { gate.Failed("soundcloud"); return []; }
            var albums = lib.Albums.Where(a => a.Tracks.Any(t => Matching.PrimaryArtist(t.Artist) == key)).Select(a => a.Name);
            var sets = SoundCloudSets.FromAnswer(answer, artist, albums);
            foreach (var l in sets) if (store.Use(l.Source, l.Id) is null) store.Put(l);
            lock (soundcloud) soundcloud[key] = (now(), sets);
            store.Save();
            return sets.Select(l => store.Use(l.Source, l.Id) ?? l).ToList();
        }
        catch (Exception e) when (e is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException && !ct.IsCancellationRequested) { gate.Failed("soundcloud"); return []; }
    }

    public async Task<WebPlaylist> FillAsync(UserSession s, WebPlaylist list, CancellationToken ct)
    {
        if (list.Tracks.Count > 0 || list.Source != "soundcloud" || s.Services.FileMoverUrl.Length == 0) return list;
        if (store.Use(list.Source, list.Id) is { Tracks.Count: > 0 } have) return have;
        if (!gate.TryTake("soundcloud")) { await Task.Delay(WebSourceGate.Gap, ct); if (!gate.TryTake("soundcloud")) return list; }
        try
        {
            var answer = await MoverAsync(s, "/playlist", new System.Text.Json.Nodes.JsonObject { ["url"] = "https://soundcloud.com/" + list.Id }, ct);
            if (answer is null || PlaylistLinks.FromYtdl(answer, PlaylistLinks.Service.SoundCloud) is not { } read) return list;
            var full = list with { Tracks = read.Tracks.Select(t => new WebTrack(SpotifyEmbed.Clean(t.Title), SpotifyEmbed.Clean(t.Artist))).Take(WebPlaylist.MaxTracks).ToList() };
            store.Put(full); store.Save();
            return full;
        }
        catch (Exception e) when (e is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException && !ct.IsCancellationRequested) { gate.Failed("soundcloud"); return list; }
    }

    /// <summary>flacie-ytdl through the person's file mover (POST [path] with its key); null on a refusal or an error answer.</summary>
    async Task<string?> MoverAsync(UserSession s, string path, System.Text.Json.Nodes.JsonObject body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, s.Services.FileMoverUrl.TrimEnd('/') + path)
        { Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json") };
        req.Headers.TryAddWithoutValidation("X-Api-Key", s.Services.FileMoverKey);
        using var res = await http.SendAsync(req, ct);
        return res.IsSuccessStatusCode ? await res.Content.ReadAsStringAsync(ct) : null;
    }

    async Task<(List<WebPlaylist> Found, PlaylistQueryKind Kind)> GatherAsync(UserSession s, string query, CancellationToken ct)
    {
        var lib = Lib(s);
        var kind = WebPlaylistPick.Classify(query, lib.Artists.Select(a => a.Name), lib.Songs.Select(t => t.Genre).Where(g => g.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
        var found = new List<WebPlaylist>();
        // FLACie's own lists, always: the fallback when the others cannot be read
        found.AddRange(own(s).Select(m => Own(m, lib)));
        // Spotify's fixed lists that this search is about (only those are read; never a crawl)
        foreach (var f in Fixed)
        {
            var probe = new WebPlaylist("spotify", f.Id, f.Name, f.Kind, f.Subject, []);
            if (WebPlaylistPick.Relevance(probe, query) <= 0) continue;
            if (await SpotifyAsync(f.Id, f.Kind, f.Subject, ct) is { } l) found.Add(l);
        }
        // an artist's best-of from Deezer's editors
        if (kind == PlaylistQueryKind.Artist && SearchRankTop.Artist(lib.Artists, query, s.PlayStats) is { } artist)
        {
            if (await DeezerArtistAsync(artist.Name, ct) is { } d) found.Add(d);
            // F25.3: the artist's own SoundCloud sets, quietly (names only; songs read when one is opened)
            found.AddRange(await SoundCloudSetsAsync(s, artist.Name, lib, ct));
        }
        return (found, kind);
    }

    static WebPlaylist Own(MadePlaylist m, Library lib)
    {
        var parts = m.Id.Split(':', 3);
        var kind = parts.Length < 2 ? WebPlaylistKind.Other : parts[1] switch { "decade" => WebPlaylistKind.Decade, "genre" => WebPlaylistKind.Genre, "artist" => WebPlaylistKind.Artist, _ => WebPlaylistKind.Other };
        var subject = kind switch { WebPlaylistKind.Decade => m.Name, WebPlaylistKind.Artist => m.Name.StartsWith("Best of ") ? m.Name[8..] : m.Name, _ => m.Name };
        var tracks = m.Paths.Select(lib.ByPath).OfType<Track>().Select(t => new WebTrack(t.Title, t.Artist, t.DurationMs)).ToList();
        return new("own", m.Id, m.Name, kind, subject, tracks);
    }

    /// <summary>A Spotify list by its fixed id: from the store when read today, else from its embed page (when the gate lets it); else what the store has.</summary>
    public async Task<WebPlaylist?> SpotifyAsync(string id, WebPlaylistKind kind, string subject, CancellationToken ct)
    {
        if (!store.NeedsFetch("spotify", id) || !gate.TryTake("spotify")) return store.Use("spotify", id);
        if (SpotifyEmbed.Url(id) is not { } url) return null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (FLACie personal music player)");
            using var res = await http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) { gate.Failed("spotify"); return store.Use("spotify", id); }
            if (SpotifyEmbed.Parse(await res.Content.ReadAsStringAsync(ct)) is not { } p) { gate.Failed("spotify"); return store.Use("spotify", id); }
            var l = new WebPlaylist("spotify", id, p.Name, kind, subject, p.Tracks);
            store.Put(l); store.Save();
            return l;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested) { gate.Failed("spotify"); return store.Use("spotify", id); }
    }

    /// <summary>
    /// A Deezer editor's "100% X", "This Is X" or "Best of X" where X is exactly the artist: "100% Kay The Prodigy" and "100% Prodigy" (Mobb Deep)
    /// sit beside "100% The Prodigy" in Deezer's answer, and fans' lists use the same names.
    /// </summary>
    public static bool IsEditorsBestOf(string title, string user, string artist)
    {
        if (!user.StartsWith("Deezer", StringComparison.OrdinalIgnoreCase)) return false;
        var t = title.Trim();
        var name = Matching.SearchWords(artist);
        foreach (var prefix in new[] { "100%", "100 %", "This Is", "Best of" })
            if (t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var w = Matching.SearchWords(t[prefix.Length..]);   // "The" counts here (Matching.PrimaryArtist drops it): The Prodigy is not Prodigy
                return w.Count > 0 && name.Take(w.Count).SequenceEqual(w) && (name.Count == w.Count || name[w.Count] is "feat" or "ft" or "featuring" or "with");
            }
        return false;
    }

    /// <summary>Deezer's editorial best-of for an artist ("100% The Prodigy", or "This Is" / "Best of"), its first 100 songs; null when it has none.</summary>
    public async Task<WebPlaylist?> DeezerArtistAsync(string artist, CancellationToken ct)
    {
        var lead = Matching.PrimaryArtist(artist);
        if (store.All.FirstOrDefault(l => l.Source == "deezer" && l.Kind == WebPlaylistKind.Artist && Matching.PrimaryArtist(l.Subject) == lead) is { } known
            && !store.NeedsFetch("deezer", known.Id)) return store.Use("deezer", known.Id);
        if (!gate.TryTake("deezer")) return null;
        try
        {
            var found = JsonNode.Parse(await http.GetStringAsync($"https://api.deezer.com/search/playlist?q={Uri.EscapeDataString(artist)}&limit=25", ct))?["data"] as JsonArray;
            static string S(JsonNode? n, string k) => n?[k] is JsonValue v && v.TryGetValue<string>(out var x) ? x : "";
            var hit = found?.OfType<JsonObject>().FirstOrDefault(p => IsEditorsBestOf(S(p, "title"), S(p["user"], "name"), artist));
            if (hit?["id"] is not JsonValue idv || !idv.TryGetValue<long>(out var pid)) return null;
            if (!gate.TryTake("deezer")) await Task.Delay(WebSourceGate.Gap, ct);
            var tracks = (JsonNode.Parse(await http.GetStringAsync($"https://api.deezer.com/playlist/{pid}/tracks?limit={WebPlaylist.MaxTracks}", ct))?["data"] as JsonArray ?? [])
                .OfType<JsonObject>().Select(t => new WebTrack(SpotifyEmbed.Clean(S(t, "title")), SpotifyEmbed.Clean(S(t["artist"], "name")), (t["duration"] is JsonValue dv && dv.TryGetValue<long>(out var sec) ? sec : 0) * 1000))
                .Where(t => t.Title.Length > 0).Take(WebPlaylist.MaxTracks).ToList();
            if (tracks.Count == 0) return null;
            var l = new WebPlaylist("deezer", pid.ToString(), SpotifyEmbed.Clean(S(hit, "title")), WebPlaylistKind.Artist, artist, tracks);
            store.Put(l); store.Save();
            return l;
        }
        catch (Exception e) when (e is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException && !ct.IsCancellationRequested) { gate.Failed("deezer"); return null; }
    }
}
