using FLACie.Core;

namespace FLACie.Server;

/// <summary>
/// GET /api/recommend/next?session=[path],[path],…&amp;n=10: what to play after the songs played so far (the last one is playing now).
/// GET /api/recommend/home: the shelves the web Home shows, for the phone. Both are worked out from the account's listening log, its
/// library and the songs' vibes; the online related list is used only as far as it is already cached, so neither ever waits on it.
/// </summary>
public static class RecommendEndpoints
{
    public static void MapRecommend(this WebApplication app)
    {
        app.MapGet("/api/recommend/next", async (HttpContext ctx, string? session, int? n, SessionStore store, JellyfinClient jf, ListenLogStore log, VibeIndex vibes, RelatedCache related) =>
            RecommendApi.Next(RecommendApi.InputsOf(await ApiEndpoints.CallerAsync(ctx, store, jf), log, vibes, related), session ?? "", n ?? 10, DateTime.UtcNow, Environment.TickCount64));

        app.MapGet("/api/recommend/home", async (HttpContext ctx, SessionStore store, JellyfinClient jf, ListenLogStore log, VibeIndex vibes, RelatedCache related) =>
            RecommendApi.Home(RecommendApi.InputsOf(await ApiEndpoints.CallerAsync(ctx, store, jf), log, vibes, related), DateTime.UtcNow));
    }
}

/// <summary>The bodies of /api/recommend, kept apart from the web plumbing.</summary>
public static class RecommendApi
{
    /// <summary>Everything a recommendation is worked out from. [Events] is null while the account has no listening log yet.</summary>
    public sealed record Inputs(Library Lib, IReadOnlyList<PlayedEntry> History, IReadOnlyList<PlaylistEntry> Favourites, IReadOnlyList<Playlist> Playlists,
        IReadOnlyList<JfPlay> Jellyfin, IReadOnlyList<ListenEvent>? Events, Func<Track, Vibe?> Vibe, RelatedCache Related);

    public static Inputs? InputsOf(UserSession? s, ListenLogStore log, VibeIndex vibes, RelatedCache related) =>
        s is null ? null : new Inputs(s.Library, s.History, s.Favorites, s.Playlists, s.PlayStats, log.Exists(s) ? log.Read(s) : null, vibes.Of, related);

    public static IResult Next(Inputs? i, string session, int n, DateTime utcNow, long seed)
    {
        if (i is null) return Results.Unauthorized();
        var played = session.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(i.Lib.ByPath).OfType<Track>().ToList();
        var related = played.Count > 0 ? i.Related.Peek(played[^1].Artist, played[^1].Title) : [];
        var taste = HomeBuilder.Taste(i.Lib, i.History, i.Favourites, i.Playlists, utcNow, i.Jellyfin, null, i.Events);
        var picks = Engine.Next(i.Lib, taste, i.Vibe, played, related.ToList(), utcNow, new EngineOptions(Take: Math.Clamp(n, 1, 50), Seed: seed));
        return Results.Ok(new { picks = picks.Select(p => p.Track is { } t ? Song(t, false, p.Why) : Song(p.New!, p.Why)) });
    }

    public static IResult Home(Inputs? i, DateTime utcNow)
    {
        if (i is null) return Results.Unauthorized();
        var shelves = HomeBuilder.Build(i.Lib, i.History, i.Favourites, i.Playlists, utcNow, i.Jellyfin, null, i.Events, i.Vibe);
        return Results.Ok(new
        {
            shelves = shelves.Select(s => new
            {
                id = s.Id, title = s.Title, subtitle = s.Subtitle, rotates = s.Rotates,
                tracks = s.Tracks.Select(t => Song(t, false, null)),
                albums = s.Albums.Select(a => new { name = a.Name, artist = a.Tracks.Count > 0 ? a.Tracks[0].AlbumArtist : "", songs = a.Tracks.Select(t => t.Path) }),
            }),
        });
    }

    static object Song(Track t, bool isNew, string? why) => new { path = (string?)t.Path, title = t.Title, artist = t.Artist, album = t.Album, durationMs = t.DurationMs, isNew, why };
    static object Song(CatalogSong s, string why) => new { path = (string?)null, title = s.Title, artist = s.Artist, album = s.Album, durationMs = s.DurationMs, isNew = true, why };
}
