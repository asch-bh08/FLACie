using FLACie.Core;

namespace FLACie.Server;

/// <summary>
/// GET /api/recommend/playlist?id=[playlist]&amp;n=10&amp;skip=0: songs from the account's library that go with one of its own playlists and are not in it
/// (<see cref="PlaylistRecs"/>). [id] is the playlist's Jellyfin id when it has one, else the app's own id (as the DJ's playlist switch knows it).
/// 404 when the account has no such playlist; an empty list for an empty playlist.
/// </summary>
public static class PlaylistRecsApi
{
    public static Playlist? Find(UserSession s, string id)
    {
        static string Norm(string? x) => (x ?? "").Replace("-", "").ToLowerInvariant();
        return id.Length == 0 ? null : s.Playlists.FirstOrDefault(p => p.Id == id) ?? s.Playlists.FirstOrDefault(p => p.JellyfinId is { } j && Norm(j) == Norm(id));
    }

    public static List<Track> Picks(UserSession s, Playlist p, Func<Track, Vibe?>? vibe, int n, int skip) =>
        PlaylistRecs.Pick(s.Library, s.Resolve(p.Entries), vibe, n, skip);

    public static IResult Get(UserSession? s, string? id, int? n, int? skip, Func<Track, Vibe?>? vibe)
    {
        if (s is null) return Results.Unauthorized();
        if (Find(s, id ?? "") is not { } p) return Results.NotFound(new { error = "No such playlist" });
        var picks = Picks(s, p, vibe, Math.Clamp(n ?? 10, 1, 50), Math.Max(0, skip ?? 0));
        return Results.Ok(new { picks = picks.Select(t => new { path = t.Path, title = t.Title, artist = t.Artist, album = t.Album, durationMs = t.DurationMs, artKey = t.ArtKey }) });
    }
}
