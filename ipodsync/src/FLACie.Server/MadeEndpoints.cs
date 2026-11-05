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
        app.MapGet("/api/made", async (HttpContext ctx, string? q, SessionStore store, JellyfinClient jf, ListenLogStore log) =>
            MadeApi.Search(Lists(await ApiEndpoints.CallerAsync(ctx, store, jf), log), q ?? ""));

        app.MapGet("/api/made/{id}", async (HttpContext ctx, string id, SessionStore store, JellyfinClient jf, ListenLogStore log) =>
        {
            var s = await ApiEndpoints.CallerAsync(ctx, store, jf);
            return MadeApi.One(s, Lists(s, log), id);
        });

        app.MapPost("/api/made/{id}/save", async (HttpContext ctx, string id, SessionStore store, JellyfinClient jf, ListenLogStore log) =>
        {
            var s = await ApiEndpoints.CallerAsync(ctx, store, jf);
            var r = MadeApi.Save(s, Lists(s, log), id, out var saved);
            // written to the account at once, so the phone's next sync (and the web's playlist list) has it
            if (saved) try { await s!.SaveAsync(jf, ctx.RequestAborted); } catch (Exception e) when (e is not OperationCanceledException) { }
            return r;
        }).DisableAntiforgery();
    }

    static List<MadePlaylist>? Lists(UserSession? s, ListenLogStore log) =>
        s is null ? null : MadePlaylists.For(s, log.Exists(s) ? log.Read(s) : null, DateTime.UtcNow);
}

/// <summary>The bodies of /api/made, kept apart from the web plumbing. [lists] is null when nobody is signed in.</summary>
public static class MadeApi
{
    public static IResult Search(IReadOnlyList<MadePlaylist>? lists, string q) =>
        lists is null ? Results.Unauthorized()
            : Results.Ok(new { lists = MadePlaylists.Search(lists, q).Select(m => new { id = m.Id, name = m.Name, subtitle = m.Subtitle, count = m.Paths.Count, artKey = m.ArtKey }) });

    public static IResult One(UserSession? s, IReadOnlyList<MadePlaylist>? lists, string id)
    {
        if (s is null || lists is null) return Results.Unauthorized();
        if (MadePlaylists.Find(lists, id) is not { } m) return Results.NotFound();
        return Results.Ok(new
        {
            id = m.Id, name = m.Name, subtitle = m.Subtitle, count = m.Paths.Count, artKey = m.ArtKey,
            tracks = m.Paths.Select(s.Library.ByPath).OfType<Track>().Select(t => new { path = t.Path, title = t.Title, artist = t.Artist, album = t.Album, durationMs = t.DurationMs }),
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
