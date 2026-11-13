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

    public IReadOnlyList<(WebPlaylist List, IReadOnlyList<string> Songs)> ListsWithArtist(string artist) => WebPlaylistPick.WithArtistIn(store.All, artist);

    /// <summary>[quick]: only what is saved already, at once, asking no one (owner, 9 Nov: "show the saved playlists at once"); the page asks again
    /// without it for the full answer.</summary>
    public async Task<IReadOnlyList<WebPlaylist>> ForSearchAsync(UserSession s, string query, CancellationToken ct, bool quick = false)
    {
        var (found, kind) = quick ? Saved(s, query) : await GatherAsync(s, query, ct);
        var artist = kind == PlaylistQueryKind.Artist ? SearchRankTop.Artist(Lib(s).Artists, query, s.PlayStats)?.Name : null;
        var shown = kind == PlaylistQueryKind.Artist && artist is not null ? WebPlaylistPick.ForArtist(found, artist, now()) : WebPlaylistPick.ForAll(found, query, kind, now(), artist);
        // what showed interest (F26.1): on an artist search only the artist's best-of (its top half); on a playlist, genre or decade search the lists it
        // names (their first 2 songs, so they start at once). Every other list shown downloads nothing until it is opened or played
        foreach (var l in shown.Where(l => l.Source != "own" && !quick))
        {
            if (kind == PlaylistQueryKind.Artist) { if (l.Kind == WebPlaylistKind.Artist) Interest(s, l, FetchTrigger.ArtistSearch); }
            else if (WebPlaylistPick.Relevance(l, query) > 0) Interest(s, l, FetchTrigger.Popular);
        }
        return shown;
    }

    public async Task<IReadOnlyList<WebPlaylist>> ForFilterAsync(UserSession s, string query, string? genre, CancellationToken ct, string? decade = null)
    {
        var (found, _) = await GatherAsync(s, query.Trim().Length > 0 ? query : genre ?? decade ?? "", ct);
        return WebPlaylistPick.ForFilter(found.Concat(store.All).DistinctBy(l => l.Source + ":" + l.Id), query, genre, now(), decade);
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
        // read a week ago or more: read again in the background (a SoundCloud or YouTube list grows); the new songs show the next time it is looked at
        if (trigger is FetchTrigger.Opened or FetchTrigger.Played && store.NeedsRefresh(list.Source, list.Id)) _ = Task.Run(() => FillAsync(s, list, CancellationToken.None));
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
            downloads.Song(s, new CatalogSong(t.Artist, t.Title, "", null, t.DurationMs, 0, 0), kind: "Playlist");
    }

    /// <summary>Anyone's SoundCloud playlists by name (flacie-ytdl /find-sets; owner, 12 Nov: "a playlist I saw on Spotify or my mate's one should be in
    /// my library"): asked once a day per search, kept in the store like the others, so they show at once from then on (and to everyone); their songs
    /// are read when one is opened or played, and opening one downloads what is missing (Interest, FetchTrigger.Opened).</summary>
    public static readonly TimeSpan FindKept = TimeSpan.FromDays(1);
    readonly Dictionary<string, DateTime> findAsked = [];

    List<WebPlaylist> FoundBefore(string query) =>
        store.All.Where(l => (l.Source == "soundcloud" || (l.Source == "youtube" && gate.IsOn("youtube"))) && l.Kind == WebPlaylistKind.Other && WebPlaylistPick.Relevance(l, query) > 0).ToList();

    async Task<List<WebPlaylist>> FindSetsAsync(UserSession s, string query, CancellationToken ct)
    {
        var key = Matching.NormTitle(query);
        if (s.Services.FileMoverUrl.Length == 0 || key.Length < 3) return [];
        lock (findAsked) { if (findAsked.TryGetValue(key, out var at) && now() - at < FindKept) return FoundBefore(query); }
        if (!gate.TryTake("soundcloud")) return FoundBefore(query);
        try
        {
            var answer = await MoverAsync(s, "/find-sets", new System.Text.Json.Nodes.JsonObject { ["q"] = query.Trim() }, ct);
            if (answer is null) { gate.Failed("soundcloud"); return FoundBefore(query); }
            lock (findAsked) { findAsked[key] = now(); if (findAsked.Count > 2000) findAsked.Clear(); }
            // (YouTube's lists only while its switch on Server admin is on)
            var sets = SoundCloudSets.FromSearch(answer).Where(l => l.Source != "youtube" || gate.IsOn("youtube")).ToList();
            foreach (var l in sets) if (store.Use(l.Source, l.Id) is null) store.Put(l);
            store.Save();
            return sets.Select(l => store.Use(l.Source, l.Id) ?? l).ToList();
        }
        catch (Exception e) when (e is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException && !ct.IsCancellationRequested) { gate.Failed("soundcloud"); return FoundBefore(query); }
    }

    /// <summary>F25.3: the sets of the artist's own SoundCloud account, by name (flacie-ytdl /artist-sets), remembered 7 days.</summary>
    public static readonly TimeSpan AccountKept = TimeSpan.FromDays(7);
    readonly Dictionary<string, (DateTime At, List<WebPlaylist> Sets)> soundcloud = [];

    async Task<List<WebPlaylist>> SoundCloudSetsAsync(UserSession s, string artist, Library lib, CancellationToken ct)
    {
        var key = Matching.PrimaryArtist(artist);
        List<WebPlaylist>? had = null; var fresh = false;
        lock (soundcloud) if (soundcloud.TryGetValue(key, out var known)) { had = known.Sets; fresh = now() - known.At < AccountKept; }
        if (fresh) return had!.Select(l => store.Use(l.Source, l.Id) ?? l).ToList();
        had ??= Saved("soundcloud", artist);
        if (had.Count > 0)
        {
            Refresh("soundcloud:" + key, () => FetchSoundCloudAsync(s, artist, key, lib, CancellationToken.None));
            return had.Select(l => store.Use(l.Source, l.Id) ?? l).ToList();
        }
        return await FetchSoundCloudAsync(s, artist, key, lib, ct);
    }

    async Task<List<WebPlaylist>> FetchSoundCloudAsync(UserSession s, string artist, string key, Library lib, CancellationToken ct)
    {
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
        // read once, then again weekly (a SoundCloud or YouTube list grows): until then, and if the re-read fails, the songs read before
        var stale = store.NeedsRefresh(list.Source, list.Id);
        if (list.Tracks.Count > 0 && !stale) return list;
        var have = store.Use(list.Source, list.Id);
        if (have is { Tracks.Count: > 0 } && !stale) return have;
        var before = have is { Tracks.Count: > 0 } ? have : list.Tracks.Count > 0 ? list : null;
        var read = await ReadAsync(s, (have ?? list) with { Tracks = [] }, ct);
        return read.Tracks.Count > 0 ? read : before ?? read;
    }

    /// <summary>The list's songs read from its source (Deezer's open API, or flacie-ytdl for SoundCloud and YouTube), stored; the list as it was
    /// (no songs) when the source can't be asked now or fails.</summary>
    async Task<WebPlaylist> ReadAsync(UserSession s, WebPlaylist list, CancellationToken ct)
    {
        // F26.1: a Deezer list found by name: its songs from Deezer's open API
        if (list.Source == "deezer")
        {
            if (!gate.TryTake("deezer")) { await Task.Delay(WebSourceGate.Gap, ct); if (!gate.TryTake("deezer")) return list; }
            try { var full = await DeezerTracksAsync(list, ct); if (full.Tracks.Count > 0) { store.Put(full); store.Save(); } return full; }
            catch (Exception e) when (e is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException && !ct.IsCancellationRequested) { gate.Failed("deezer"); return list; }
        }
        if (list.Source is not ("soundcloud" or "youtube") || s.Services.FileMoverUrl.Length == 0) return list;
        if (!gate.TryTake(list.Source)) { await Task.Delay(WebSourceGate.Gap, ct); if (!gate.TryTake(list.Source)) return list; }
        try
        {
            // a SoundCloud set, or a YouTube playlist found by name: read with flacie-ytdl as a pasted link is
            var (url, service) = list.Source == "youtube" ? ("https://www.youtube.com/playlist?list=" + list.Id, PlaylistLinks.Service.YouTube) : ("https://soundcloud.com/" + list.Id, PlaylistLinks.Service.SoundCloud);
            var answer = await MoverAsync(s, "/playlist", new System.Text.Json.Nodes.JsonObject { ["url"] = url }, ct);
            if (answer is null || PlaylistLinks.FromYtdl(answer, service) is not { } read) return list;
            var full = list with { Tracks = read.Tracks.Select(t => new WebTrack(SpotifyEmbed.Clean(t.Title), SpotifyEmbed.Clean(t.Artist))).Take(WebPlaylist.MaxTracks).ToList() };
            store.Put(full); store.Save();
            return full;
        }
        catch (Exception e) when (e is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException && !ct.IsCancellationRequested) { gate.Failed(list.Source); return list; }
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

    /// <summary>How long a search waits for a source that has nothing saved yet; one slower than this finishes in the background, for next time (owner,
    /// 6 Nov: the playlists took a while to come).</summary>
    public static readonly TimeSpan FirstAnswer = TimeSpan.FromSeconds(2.5);

    async Task<(List<WebPlaylist> Found, PlaylistQueryKind Kind)> GatherAsync(UserSession s, string query, CancellationToken ct)
    {
        var lib = Lib(s);
        var kind = WebPlaylistPick.Classify(query, lib.Artists.Select(a => a.Name), lib.Songs.Select(t => t.Genre).Where(g => g.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
        var found = new List<WebPlaylist>();
        // FLACie's own lists, always: the fallback when the others cannot be read
        found.AddRange(own(s).Select(m => Own(m, lib)));
        // the sources are asked side by side, never one after another; each answers at once from what it has saved (and looks again in the background
        // when that is old). Only a source with nothing saved is waited for, and at most FirstAnswer: it carries on in the background either way.
        var asks = new List<Task<List<WebPlaylist>>>();
        // Spotify's fixed lists that this search is about (only those are read; never a crawl)
        foreach (var f in Fixed)
        {
            var probe = new WebPlaylist("spotify", f.Id, f.Name, f.Kind, f.Subject, []);
            if (WebPlaylistPick.Relevance(probe, query) > 0) asks.Add(Task.Run(async () => await SpotifyAsync(f.Id, f.Kind, f.Subject, CancellationToken.None) is { } l ? new List<WebPlaylist> { l } : []));
            // an artist search also looks inside the ones already read (WarmFixed reads them once a day) for that artist's songs
            else if (kind == PlaylistQueryKind.Artist && store.All.FirstOrDefault(l => l.Source == "spotify" && l.Id == f.Id) is { } known) found.Add(known);
        }
        WarmFixed();
        var artist = kind == PlaylistQueryKind.Artist ? SearchRankTop.Artist(lib.Artists, query, s.PlayStats) : null;
        if (artist is not null)
        {
            // F26.1: Deezer's playlist search: the editors' best-of (with its songs) and the fans' and editors' lists that name the artist (names only)
            asks.Add(Task.Run(() => DeezerListsAsync(artist.Name, CancellationToken.None)));
            // F25.3: the artist's own SoundCloud sets, quietly (names only; songs read when one is opened)
            asks.Add(Task.Run(() => SoundCloudSetsAsync(s, artist.Name, lib, CancellationToken.None)));
        }
        // anyone's SoundCloud playlists named like the search (not on an artist search: that has the artist's own sets above)
        if (artist is null && query.Trim().Length >= 3) asks.Add(Task.Run(() => FindSetsAsync(s, query, CancellationToken.None)));
        if (asks.Count > 0)
        {
            try { await Task.WhenAny(Task.WhenAll(asks), Task.Delay(FirstAnswer, ct)); } catch (OperationCanceledException) { }
            foreach (var a in asks) if (a.IsCompletedSuccessfully) found.AddRange(a.Result);
        }
        // lists read before (opened, or Deezer's that were filled) that hold several of the artist's songs
        if (artist is not null)
            found.AddRange(store.All.Where(l => l.Source == "deezer" && l.Tracks.Count > 0 && WebPlaylistPick.HoldsCount(l, artist.Name) >= 2 && found.All(f => f.Id != l.Id || f.Source != l.Source)));
        return (found, kind);
    }

    /// <summary>The lists for a search from what is saved: our own, Spotify's fixed lists already read, and the artist's Deezer and SoundCloud lists.</summary>
    (List<WebPlaylist> Found, PlaylistQueryKind Kind) Saved(UserSession s, string query)
    {
        var lib = Lib(s);
        var kind = WebPlaylistPick.Classify(query, lib.Artists.Select(a => a.Name), lib.Songs.Select(t => t.Genre).Where(g => g.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
        var found = own(s).Select(m => Own(m, lib)).ToList();
        var artist = kind == PlaylistQueryKind.Artist ? SearchRankTop.Artist(lib.Artists, query, s.PlayStats) : null;
        foreach (var f in Fixed)
            if (store.All.FirstOrDefault(l => l.Source == "spotify" && l.Id == f.Id) is { } known && (artist is not null || WebPlaylistPick.Relevance(known, query) > 0)) found.Add(known);
        // playlists found by name before (by anyone): at once
        if (artist is null) found.AddRange(FoundBefore(query));
        if (artist is not null)
        {
            found.AddRange(Saved("deezer", artist.Name));
            found.AddRange(Saved("soundcloud", artist.Name));
            found.AddRange(store.All.Where(l => l.Source == "deezer" && l.Tracks.Count > 0 && WebPlaylistPick.HoldsCount(l, artist.Name) >= 2));
        }
        return (found.DistinctBy(l => l.Source + ":" + l.Id).ToList(), kind);
    }

    DateTime warmedAt = DateTime.MinValue;

    /// <summary>Reads Spotify's fixed lists in the background, at most once in 20 hours and one a second (14 pages a day), so an artist search can look
    /// inside them without waiting.</summary>
    void WarmFixed()
    {
        lock (queued) { if (now() - warmedAt < TimeSpan.FromHours(20)) return; warmedAt = now(); }
        _ = Task.Run(async () =>
        {
            foreach (var f in Fixed)
            {
                if (!store.NeedsFetch("spotify", f.Id)) continue;
                await Task.Delay(WebSourceGate.Gap + TimeSpan.FromMilliseconds(100));
                try { await SpotifyAsync(f.Id, f.Kind, f.Subject, CancellationToken.None); } catch (Exception) { }
            }
        });
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
    public async Task<WebPlaylist?> DeezerArtistAsync(string artist, CancellationToken ct) =>
        (await DeezerListsAsync(artist, ct)).FirstOrDefault(l => l.Kind == WebPlaylistKind.Artist && l.Tracks.Count > 0);

    /// <summary>One Deezer playlist search per artist, kept a day.</summary>
    public static readonly TimeSpan DeezerListsKept = TimeSpan.FromDays(1);
    readonly Dictionary<string, (DateTime At, List<WebPlaylist> Lists)> deezerLists = [];

    /// <summary>
    /// F26.1: Deezer's playlist search for an artist (public, no key): the editors' best-of with its first 100 songs, and every list whose name holds
    /// the artist (fans' "Best Of", "The Prodigy Mix", "... Remixes") by name, size and picture; their songs are read when one is opened.
    /// </summary>
    public async Task<List<WebPlaylist>> DeezerListsAsync(string artist, CancellationToken ct)
    {
        var key = string.Join(' ', Matching.SearchWords(artist));
        List<WebPlaylist>? had = null; var fresh = false;
        lock (deezerLists) if (deezerLists.TryGetValue(key, out var known)) { had = known.Lists; fresh = now() - known.At < DeezerListsKept; }
        if (fresh) return had!.Select(l => store.Use(l.Source, l.Id) ?? l).ToList();
        // what Deezer said before (kept in the saved lists, so a restart does not forget it): shown now, and Deezer asked again in the background
        had ??= Saved("deezer", artist);
        if (had.Count > 0)
        {
            Refresh("deezer:" + key, () => FetchDeezerAsync(artist, key, CancellationToken.None));
            return had.Select(l => store.Use(l.Source, l.Id) ?? l).ToList();
        }
        return await FetchDeezerAsync(artist, key, ct);
    }

    /// <summary>Lists of [source] saved before that are about [artist] (Deezer's search answer, the artist's SoundCloud sets).</summary>
    List<WebPlaylist> Saved(string source, string artist) =>
        store.All.Where(l => l.Source == source && (string.Equals(Matching.PrimaryArtist(l.Subject), Matching.PrimaryArtist(artist), StringComparison.OrdinalIgnoreCase)
            || l.Subject.StartsWith(artist + " ", StringComparison.OrdinalIgnoreCase))).ToList();   // "The Prodigy remixes": the artist's remix sets

    readonly HashSet<string> refreshing = [];

    /// <summary>Looks again in the background, once at a time per source and subject.</summary>
    void Refresh(string what, Func<Task> look)
    {
        lock (refreshing) if (!refreshing.Add(what)) return;
        _ = Task.Run(async () => { try { await look(); } catch (Exception) { } finally { lock (refreshing) refreshing.Remove(what); } });
    }

    async Task<List<WebPlaylist>> FetchDeezerAsync(string artist, string key, CancellationToken ct)
    {
        if (!gate.TryTake("deezer")) { lock (deezerLists) return deezerLists.TryGetValue(key, out var old) ? old.Lists : Saved("deezer", artist); }
        static string S(JsonNode? n, string k) => n?[k] is JsonValue v && v.TryGetValue<string>(out var x) ? x : "";
        static long L(JsonNode? n, string k) => n?[k] is JsonValue v && v.TryGetValue<long>(out var x) ? x : 0;
        try
        {
            var found = JsonNode.Parse(await http.GetStringAsync($"https://api.deezer.com/search/playlist?q={Uri.EscapeDataString(artist)}&limit=50", ct))?["data"] as JsonArray ?? [];
            var lists = new List<WebPlaylist>();
            foreach (var p in found.OfType<JsonObject>())
            {
                var title = SpotifyEmbed.Clean(S(p, "title")); var user = S(p["user"], "name"); var id = L(p, "id");
                if (id <= 0 || title.Length == 0) continue;
                var best = IsEditorsBestOf(title, user, artist);
                if (!best && !WebPlaylistPick.TitleHolds(title, artist)) continue;
                var editor = user.StartsWith("Deezer", StringComparison.OrdinalIgnoreCase);
                var picture = S(p, "picture_medium") is { Length: > 0 } pic ? pic : null;
                var l = new WebPlaylist("deezer", id.ToString(), title, best ? WebPlaylistKind.Artist : WebPlaylistKind.Other, artist, [], Editor: editor, Size: (int)L(p, "nb_tracks"), Picture: picture);
                lists.Add(store.Use("deezer", l.Id) is { Tracks.Count: > 0 } have ? have with { Editor = editor, Size = l.Size, Picture = picture, Kind = l.Kind } : l);
            }
            // the editors' best-of with its songs (an artist search downloads its top half)
            if (lists.FindIndex(l => l.Kind == WebPlaylistKind.Artist) is >= 0 and var b && lists[b].Tracks.Count == 0)
            {
                if (!gate.TryTake("deezer")) await Task.Delay(WebSourceGate.Gap, ct);
                lists[b] = await DeezerTracksAsync(lists[b], ct);
            }
            foreach (var l in lists) if (store.Use(l.Source, l.Id) is not { Tracks.Count: > 0 } || l.Tracks.Count > 0) store.Put(l);
            store.Save();
            lock (deezerLists) deezerLists[key] = (now(), lists);
            return lists;
        }
        catch (Exception e) when (e is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException && !ct.IsCancellationRequested) { gate.Failed("deezer"); return []; }
    }

    /// <summary>A Deezer list with its first 100 songs.</summary>
    async Task<WebPlaylist> DeezerTracksAsync(WebPlaylist l, CancellationToken ct)
    {
        static string S(JsonNode? n, string k) => n?[k] is JsonValue v && v.TryGetValue<string>(out var x) ? x : "";
        var tracks = (JsonNode.Parse(await http.GetStringAsync($"https://api.deezer.com/playlist/{l.Id}/tracks?limit={WebPlaylist.PageTracks}", ct))?["data"] as JsonArray ?? [])
            .OfType<JsonObject>().Select(t => new WebTrack(SpotifyEmbed.Clean(S(t, "title")), SpotifyEmbed.Clean(S(t["artist"], "name")), (t["duration"] is JsonValue dv && dv.TryGetValue<long>(out var sec) ? sec : 0) * 1000))
            .Where(t => t.Title.Length > 0).Take(WebPlaylist.PageTracks).ToList();
        return l with { Tracks = tracks };
    }
}
