using System.Collections.Concurrent;
using FLACie.Core;

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

    /// <summary>The waits before asking for a song again after a passing error: two more tries, quickly.</summary>
    public static readonly TimeSpan[] SongRetries = [TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(500)];

    /// <summary>
    /// <see cref="SendAsync"/> for a song someone is waiting for, asked again (a fresh request from <paramref name="make"/>, after each of
    /// <paramref name="waits"/>) when Jellyfin answers with an error that may pass (5xx, 429) or the connection fails. Nothing has been sent to the browser
    /// yet, so a try that worked is all it sees. Before this, one 503 reached the browser as it was: it waited 1.5 s, and four in a row skipped the song
    /// without a word (owner, 25 Nov: "sometimes about 10 s to play, sometimes not at all"). No reply at all (<see cref="JellyfinNoReplyException"/>) is
    /// not tried again here: that already took the whole limit, and the player says so and tries once more itself.
    /// </summary>
    public static async Task<HttpResponseMessage> SendSongAsync(HttpClient http, Func<HttpRequestMessage> make, TimeSpan limit, CancellationToken ct, IReadOnlyList<TimeSpan>? waits = null)
    {
        waits ??= SongRetries;
        for (var attempt = 0; ; attempt++)
        {
            var last = attempt >= waits.Count;
            var req = make();
            HttpResponseMessage res;
            try { res = await SendAsync(http, req, limit, ct).ConfigureAwait(false); }
            catch (HttpRequestException e) when (!last && !ct.IsCancellationRequested && e is not JellyfinUnavailableException && (e.StatusCode is null || Passing(e.StatusCode.Value)))
            {
                req.Dispose();
                await Task.Delay(waits[attempt], ct).ConfigureAwait(false);
                continue;
            }
            if (last || !Passing(res.StatusCode)) return res;
            res.Dispose(); req.Dispose();
            await Task.Delay(waits[attempt], ct).ConfigureAwait(false);
        }
    }

    static bool Passing(System.Net.HttpStatusCode code) => (int)code >= 500 || code == System.Net.HttpStatusCode.TooManyRequests;
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
