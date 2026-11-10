using System.Collections.Concurrent;

namespace FLACie.Server;

/// <summary>Jellyfin sent nothing back (not even its headers) within the first-reply limit for a song.</summary>
public sealed class JellyfinNoReplyException(TimeSpan waited) : TimeoutException($"Jellyfin did not answer within {waited.TotalSeconds:0.#} s")
{
    public TimeSpan Waited { get; } = waited;
}

/// <summary>
/// A time limit on Jellyfin's first reply to a song request, and nothing after it. The "media" client has no time limit (it carries whole lossless
/// songs, which can take many minutes), so a Jellyfin that took the request and never answered left the song buffering for good. Only the wait for
/// the headers is limited: once they are in, the body is read for as long as it takes and is never cut.
/// </summary>
public static class JellyfinFirstReply
{
    /// <summary>How long a song someone is waiting for may take to get its first reply from Jellyfin.</summary>
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);

    /// <summary>Sends <paramref name="req"/> reading headers only. Throws <see cref="JellyfinNoReplyException"/> when they take longer than
    /// <paramref name="limit"/>, and lets go of the request to Jellyfin; <paramref name="ct"/> (the browser letting go) is a plain cancel.</summary>
    public static async Task<HttpResponseMessage> SendAsync(HttpClient http, HttpRequestMessage req, TimeSpan limit, CancellationToken ct)
    {
        // the limit's own token cancels only the wait for the headers: it is never cancelled once they are in, so the body that follows is not tied to it
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var send = http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, waiting.Token);
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var done = await Task.WhenAny(send, Task.Delay(limit, timer.Token)).ConfigureAwait(false);
        if (done == send) { timer.Cancel(); return await send.ConfigureAwait(false); }
        ct.ThrowIfCancellationRequested();
        waiting.Cancel();
        try { (await send.ConfigureAwait(false)).Dispose(); }   // the headers came just as the limit was reached: dropped all the same, the answer is already "no"
        catch (Exception) { }
        throw new JellyfinNoReplyException(limit);
    }
}

/// <summary>
/// The songs Jellyfin just did not answer for, per account, for a short while: the browser's audio player can't see why a song failed (an HTTP status
/// never reaches it), so after an error it asks here (GET /api/stream/why) and says "Couldn't reach Jellyfin" instead of a general failure.
/// </summary>
public sealed class StreamStalls(Func<DateTime>? clock = null)
{
    static readonly TimeSpan Keep = TimeSpan.FromMinutes(2);
    readonly Func<DateTime> now = clock ?? (() => DateTime.UtcNow);
    readonly ConcurrentDictionary<(string User, string Path), DateTime> at = new();

    public void Mark(string user, string path)
    {
        var t = now();
        at[(user, path)] = t;
        if (at.Count > 2000) foreach (var kv in at) if (t - kv.Value > Keep) at.TryRemove(kv.Key, out _);   // never grows: old entries go as new ones come
    }

    public void Clear(string user, string path) => at.TryRemove((user, path), out _);

    public bool Recent(string user, string path) => at.TryGetValue((user, path), out var t) && now() - t <= Keep;
}
