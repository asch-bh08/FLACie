using FLACie.Core;

namespace FLACie.Server;

/// <summary>
/// GET /api/library/fixes: what the server knows about the library that the phone, reading Jellyfin itself, can't: the name a song's other version shows
/// ("Voodoo People (05 Edit)"), a song's own album and cover when its file is filed under a playlist's name ("Grenade" on "Pop Megahits"), and the
/// second copies of a recording that the song lists hide. By Jellyfin item id. (Owner, 13 Nov: "make sure the phone has it all".)
/// </summary>
public static class LibraryFixesApi
{
    public sealed record Fix(string Id, string? Title, string? Album, string? AlbumArtist, string? Art);

    public static (List<Fix> Fixes, List<string> Hidden) Build(Library lib)
    {
        var fixes = new List<Fix>(); var hidden = new List<string>();
        foreach (var t in lib.Songs)
        {
            if (t.JellyfinId is not { } id) continue;
            var title = lib.DisplayTitle(t);
            var album = AlbumFixes.Lookup?.Invoke(t.FilePath ?? t.Path);
            if (title != t.Title || album is not null)
                fixes.Add(new(id, title != t.Title ? title : null, album?.Album, album?.AlbumArtist, album?.ArtUrl));
        }
        // (the second copies are not in Songs; their own album still lists them)
        foreach (var g in lib.Albums) foreach (var t in g.Tracks) if (t.JellyfinId is { } id && lib.IsDuplicate(t)) hidden.Add(id);
        return (fixes, hidden);
    }

    public static void Map(WebApplication app) =>
        app.MapGet("/api/library/fixes", async (HttpContext ctx, SessionStore store, JellyfinClient jf) =>
        {
            if (await ApiEndpoints.CallerAsync(ctx, store, jf) is not { } s) return Results.Unauthorized();
            var (fixes, hidden) = Build(s.Library);
            return Results.Ok(new { fixes = fixes.Select(f => new { id = f.Id, title = f.Title, album = f.Album, albumArtist = f.AlbumArtist, art = f.Art }), hidden });
        });
}
