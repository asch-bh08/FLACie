using FLACie.Core;
using Microsoft.AspNetCore.Http;

namespace FLACie.Server;

/// <summary>
/// What a new account's listening log starts from, so recommendations are not empty on the first day: the plays it already has
/// on record and the songs it has hearted. Jellyfin's play counts are not written here: they reach the engine as their own input, and
/// written as plays too they would count twice. ([jellyfin] is kept in the signature so callers need not change.)
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
        // "seed:" and "day:" ids are the server's own (the first-day seed and folded old plays); a client cannot send them
        events = events.Where(e => e.Id is not null && !e.Id.StartsWith("seed:") && !e.Id.StartsWith("day:")).ToList();
        // seed ids are the same every time, so two first requests arriving together cannot seed twice
        if (!store.Exists(s)) store.Append(s, seed?.Invoke() ?? []);
        store.Touch(s);   // an account with nothing to start from still has a log from now on, so it is not seeded again
        return Results.Ok(new { kept = store.Append(s, events) });
    }
}
