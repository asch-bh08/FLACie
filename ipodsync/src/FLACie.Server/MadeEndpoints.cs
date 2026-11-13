using FLACie.Core;

namespace FLACie.Server;

/// <summary>
/// Ready-made playlists (MadePlaylists) for the phone, as the web Search shows them (owner, 2 Nov):
/// GET /api/made?q=2010s: the lists matching a search, { lists: [{ id, name, subtitle, count, artKey }] }.
/// GET /api/made/{id}: one list with its songs, in the song shape /api/recommend uses.
/// POST /api/made/{id}/save: "Add to my playlists": a normal playlist of the account with the list's songs, { id, name }. The phone sees it with its next account sync.
/// </summary>
public static class MadeEndpoints
{
    public static void MapMade(this WebApplication app)
    {
        // the web's Search lists for the phone (6 Nov): with outside playlists on, the same lists the web shows (kind=playlists, genre and decade as its
        // Playlists filter); otherwise, or when the sources fail, our own as before. quick=1: only the lists saved already, at once (the phone asks
        // that first and the full answer after, owner 9 Nov)
        app.MapGet("/api/made", async (HttpContext ctx, string? q, string? kind, string? genre, string? decade, string? quick, SessionStore store, JellyfinClient jf, ListenLogStore log) =>
        {
            var s = await ApiEndpoints.CallerAsync(ctx, store, jf);
            var own = Lists(s, log);
            if (s is not null && own is not null && AppHost.WebPlaylists is { } w && (q ?? "").Trim().Length >= 2)
            {
                try
                {
                    var filter = kind == "playlists" || !string.IsNullOrWhiteSpace(genre) || !string.IsNullOrWhiteSpace(decade);
                    var got = filter ? await w.ForFilterAsync(s, q!.Trim(), string.IsNullOrWhiteSpace(genre) ? null : genre, ctx.RequestAborted, string.IsNullOrWhiteSpace(decade) ? null : decade)
                        : await w.ForSearchAsync(s, q!.Trim(), ctx.RequestAborted, quick == "1");
                    return WebListsApi.Lists(s, got, own);
                }
                catch (Exception e) when (e is not OperationCanceledException) { }
            }
            return MadeApi.Search(s, own, q ?? "");
        });

        // "Lists with <artist> in" (F26.2): lists already read that hold the artist's songs
        app.MapGet("/api/made/featuring", async (HttpContext ctx, string? artist, SessionStore store, JellyfinClient jf) =>
        {
            var s = await ApiEndpoints.CallerAsync(ctx, store, jf);
            return WebListsApi.Featuring(s, s is null || string.IsNullOrWhiteSpace(artist) || AppHost.WebPlaylists is not { } w ? [] : w.ListsWithArtist(artist.Trim()));
        });

        app.MapGet("/api/made/{id}", async (HttpContext ctx, string id, SessionStore store, JellyfinClient jf, ListenLogStore log) =>
        {
            var s = await ApiEndpoints.CallerAsync(ctx, store, jf);
            if (id.StartsWith("web:"))
            {
                if (s is null) return Results.Unauthorized();
                if (Web(id) is not { } l) return Results.NotFound();
                // a list known by name only (a SoundCloud set) has its songs read now; opening it asks for its missing songs, as on the web
                if (l.Tracks.Count == 0) try { l = await AppHost.WebPlaylists!.FillAsync(s, l, ctx.RequestAborted); } catch (Exception e) when (e is not OperationCanceledException) { }
                AppHost.WebPlaylists!.Interest(s, l, FetchTrigger.Opened);
                return WebListsApi.One(s, l);
            }
            return MadeApi.One(s, Lists(s, log), id);
        });

        app.MapPost("/api/made/{id}/save", async (HttpContext ctx, string id, SessionStore store, JellyfinClient jf, ListenLogStore log) =>
        {
            var s = await ApiEndpoints.CallerAsync(ctx, store, jf);
            var lists = Lists(s, log);
            // an outside list is kept as the songs the library has, in its order
            if (s is not null && lists is not null && Web(id) is { } wl) lists = [.. lists, WebListsApi.AsMade(s, wl)];
            var r = MadeApi.Save(s, lists, id, out var saved);
            // written to the account at once, so the phone's next sync (and the web's playlist list) has it
            if (saved) try { await s!.SaveAsync(jf, ctx.RequestAborted); } catch (Exception e) when (e is not OperationCanceledException) { }
            return r;
        }).DisableAntiforgery();
    }

    static WebPlaylist? Web(string id) => id.Split(':', 3) is ["web", var source, var lid] ? AppHost.WebPlaylists?.Find(source, lid) : null;

    static List<MadePlaylist>? Lists(UserSession? s, ListenLogStore log) =>
        s is null ? null : MadePlaylists.Cached(s, () => log.Exists(s) ? log.Read(s) : null, DateTime.UtcNow);
}

/// <summary>The bodies of /api/made, kept apart from the web plumbing. [lists] is null when nobody is signed in.</summary>
public static class MadeApi
{
    /// <summary>[first] is the list's first song, so the phone can show the cover it has for it (the phone's picture keys are its own).</summary>
    public static IResult Search(UserSession? s, IReadOnlyList<MadePlaylist>? lists, string q) =>
        s is null || lists is null ? Results.Unauthorized()
            : Results.Ok(new { lists = MadePlaylists.Search(lists, q).Select(m => new
            {
                id = m.Id, name = m.Name, subtitle = m.Subtitle, count = m.Paths.Count, artKey = m.ArtKey,
                first = m.Paths.Count > 0 && s.Library.ByPath(m.Paths[0]) is { } t ? Song(t) : null,
            }) });

    // the song shape /api/recommend uses
    static object Song(Track t) => new { path = t.Path, title = t.Title, artist = t.Artist, album = t.Album, durationMs = t.DurationMs };

    public static IResult One(UserSession? s, IReadOnlyList<MadePlaylist>? lists, string id)
    {
        if (s is null || lists is null) return Results.Unauthorized();
        if (MadePlaylists.Find(lists, id) is not { } m) return Results.NotFound();
        return Results.Ok(new
        {
            id = m.Id, name = m.Name, subtitle = m.Subtitle, count = m.Paths.Count, artKey = m.ArtKey,
            tracks = m.Paths.Select(s.Library.ByPath).OfType<Track>().Select(Song),
        });
    }

    public static IResult Save(UserSession? s, IReadOnlyList<MadePlaylist>? lists, string id, out bool saved)
    {
        saved = false;
        if (s is null || lists is null) return Results.Unauthorized();
        if (MadePlaylists.Find(lists, id) is not { } m) return Results.NotFound();
        var pl = MadePlaylists.SaveTo(s, m);
        saved = true;
        return Results.Ok(new { id = pl.Id, name = pl.Name, count = pl.Entries.Count });
    }
}

/// <summary>
/// The web's playlists for the phone's search (owner, 6 Nov: "everything we built, phone as well"): the lists from outside (Spotify, Deezer, SoundCloud) and
/// our own as /api/made cards, "web:source:id" for one from outside; the "Lists with" row; and an outside list opened with every song in order, the
/// ones the library lacks marked missing (the phone greys them and offers them for download).
/// </summary>
public static class WebListsApi
{
    static string From(string source) => source switch { "spotify" => "Spotify", "deezer" => "Deezer", "soundcloud" => "SoundCloud", "youtube" => "YouTube", _ => "FLACie" };
    static object Song(Track t) => new { path = t.Path, title = t.Title, artist = t.Artist, album = t.Album, durationMs = t.DurationMs };
    static Track? Have(UserSession s, WebTrack t) => s.Library.Find(t.Title, t.Artist) ?? s.Library.FindLoose(t.Title, t.Artist);

    /// <summary>As the web's search card: our own keeps its id; one from outside shows its size, where it is from and its own picture.</summary>
    static object Card(UserSession s, WebPlaylist l, IReadOnlyList<MadePlaylist> own, string? subtitle = null)
    {
        if (l.Source == "own" && MadePlaylists.Find(own, l.Id) is { } m)
            return new { id = m.Id, name = m.Name, subtitle = m.Subtitle, count = m.Paths.Count, artKey = m.ArtKey, picture = (string?)null,
                first = m.Paths.Count > 0 && s.Library.ByPath(m.Paths[0]) is { } t ? Song(t) : null };
        var have = l.Tracks.Select(t => Have(s, t)).OfType<Track>().ToList();
        var size = Math.Max(l.Tracks.Count, l.Size);
        return new
        {
            id = $"web:{l.Source}:{l.Id}", name = l.Name, subtitle = subtitle ?? $"{(size > 0 ? size + " songs · " : "")}{From(l.Source)}{(l.Editor ? " editors" : "")}",
            count = size, artKey = have.Select(t => t.ArtKey).FirstOrDefault(k => k is not null), picture = l.Picture is { Length: > 0 } p ? p : null,
            first = have.Count > 0 ? Song(have[0]) : null,
        };
    }

    public static IResult Lists(UserSession? s, IReadOnlyList<WebPlaylist> lists, IReadOnlyList<MadePlaylist> own) =>
        s is null ? Results.Unauthorized() : Results.Ok(new { lists = lists.Select(l => Card(s, l, own)) });

    public static IResult Featuring(UserSession? s, IReadOnlyList<(WebPlaylist List, IReadOnlyList<string> Songs)> lists) =>
        s is null ? Results.Unauthorized() : Results.Ok(new { lists = lists.Select(x => Card(s, x.List, [], WebPlaylistPick.SongsLabel(x.Songs))) });

    public static IResult One(UserSession? s, WebPlaylist l)
    {
        if (s is null) return Results.Unauthorized();
        var size = Math.Max(l.Tracks.Count, l.Size);
        return Results.Ok(new
        {
            id = $"web:{l.Source}:{l.Id}", name = l.Name, subtitle = $"{(size > 0 ? size + " songs · " : "")}{From(l.Source)}{(l.Editor ? " editors" : "")}", count = size,
            tracks = l.Tracks.Select(t => Have(s, t) is { } h
                ? (object)new { path = (string?)h.Path, title = h.Title, artist = h.Artist, album = h.Album, durationMs = h.DurationMs, missing = false }
                : new { path = (string?)null, title = t.Title, artist = t.Artist, album = "", durationMs = t.DurationMs, missing = true }),
        });
    }

    /// <summary>The songs the library has, in the list's order, as a list "Add to my playlists" can keep.</summary>
    public static MadePlaylist AsMade(UserSession s, WebPlaylist l)
    {
        var have = l.Tracks.Select(t => Have(s, t)).OfType<Track>().ToList();
        return new MadePlaylist($"web:{l.Source}:{l.Id}", l.Name, "", have.Select(t => t.ArtKey).FirstOrDefault(k => k is not null), have.Select(t => t.Path).ToList());
    }
}
