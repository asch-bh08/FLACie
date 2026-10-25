using FLACie.Core;
using Microsoft.AspNetCore.Http;

namespace FLACie.Server;

/// <summary>
/// What a new account's listening log starts from, so recommendations are not empty on the first day: the plays it already has
/// on record, the songs it has hearted, and the plays Jellyfin counted (each count becomes a finished play).
/// </summary>
public static class ListenSeed
{
    public static IReadOnlyList<ListenEvent> From(IReadOnlyList<PlayedEntry> history, IReadOnlyList<PlaylistEntry> favourites, IReadOnlyList<JfPlay> jellyfin, Library lib)
    {
        var res = new List<ListenEvent>();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var h in history)
        {
            var len = lib.Resolve(h.Path, h.Title, h.Artist) is { } t ? t.DurationMs / 1000.0 : 0;
            // the history only says a song was started; a song that was skipped at once is not told apart, so it counts as heard
            res.Add(new ListenEvent($"seed:h:{h.At}:{h.Path}", ListenKind.Play, h.At, h.Path, h.Title, h.Artist, len, len, "history"));
        }
        foreach (var f in favourites)
            res.Add(new ListenEvent("seed:f:" + f.Path, ListenKind.Heart, now, f.Path, f.Title, f.Artist));
        foreach (var p in jellyfin)
        {
            if (lib.ByJellyfinId(p.Id) is not { } t) continue;
            var len = t.DurationMs / 1000.0;
            var at = p.LastMs > 0 ? p.LastMs : now;
            for (var i = 0; i < Math.Min(p.Plays, 50); i++)   // a few dozen plays say as much as a few hundred, and keep the first-day log small
                res.Add(new ListenEvent($"seed:j:{p.Id}:{i}", ListenKind.Play, at, t.Path, t.Title, t.Artist, len, len, "jellyfin"));
        }
        return res;
    }
}

/// <summary>The body of POST /api/listen, kept apart from the web plumbing.</summary>
public static class ListenApi
{
    /// <summary>Keeps the events for that account. The first time an account is seen its log is started from [seed] (what it has played and liked so far).</summary>
    public static IResult Accept(ListenLogStore store, UserSession? s, IReadOnlyList<ListenEvent> events, Func<IReadOnlyList<ListenEvent>>? seed = null)
    {
        if (s is null) return Results.Unauthorized();
        if (!store.Exists(s)) store.Append(s, seed?.Invoke() ?? []);
        store.Touch(s);   // an account with nothing to start from still has a log from now on, so it is not seeded again
        var before = store.Read(s).Count;
        store.Append(s, events);
        return Results.Ok(new { kept = store.Read(s).Count - before });
    }
}
