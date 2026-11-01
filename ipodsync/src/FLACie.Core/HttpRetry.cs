using System.Net;

namespace FLACie.Core;

/// <summary>How <see cref="HttpRetry"/> asks again: how many tries, how long between them, and the longest Retry-After worth waiting for. Time and chance are
/// injected so the rules are tested without sleeping.</summary>
public sealed class RetryPolicy
{
    /// <summary>Tries in all, the first included (3: the first and two more).</summary>
    public int Attempts { get; init; } = 3;
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromMilliseconds(400);
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(5);
    /// <summary>A service that says "come back in more than this" is not waited for: its answer goes back to the caller at once.</summary>
    public TimeSpan MaxRetryAfter { get; init; } = TimeSpan.FromSeconds(20);
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;
    public Func<double> Random { get; init; } = System.Random.Shared.NextDouble;

    /// <summary>Three tries, 0.2-0.4 s then 0.4-0.8 s apart: background work and anything a person can wait a second for.</summary>
    public static RetryPolicy Default { get; } = new();
    /// <summary>Two tries, 0.1-0.25 s apart: something a person is waiting for right now (a search as they type, the first cover of a page).</summary>
    public static RetryPolicy Quick { get; } = new() { Attempts = 2, BaseDelay = TimeSpan.FromMilliseconds(250), MaxRetryAfter = TimeSpan.FromSeconds(3) };

    /// <summary>The wait before try number [attempt] + 2: BaseDelay doubled each time, at most MaxDelay, shortened by up to half at random (jitter), so many
    /// callers that failed together do not all come back in the same instant.</summary>
    public TimeSpan Backoff(int attempt)
    {
        var ceil = Math.Min(MaxDelay.TotalMilliseconds, BaseDelay.TotalMilliseconds * Math.Pow(2, Math.Clamp(attempt, 0, 30)));
        return TimeSpan.FromMilliseconds(ceil * (0.5 + 0.5 * Math.Clamp(Random(), 0, 1)));
    }
}

/// <summary>
/// The one way the server asks another service again when the first answer was a hiccup: a dropped or refused connection, a timeout, 408, 429, 500, 502,
/// 503 or 504. At most <see cref="RetryPolicy.Attempts"/> tries, exponential back-off with jitter, the service's own Retry-After honoured (never shorter
/// than the back-off, and one longer than <see cref="RetryPolicy.MaxRetryAfter"/> is not waited out: that answer comes back at once). Never asked again:
/// anything waiting cannot change (every other 4xx, 501), and the caller's own cancel. A write (POST, PATCH) is sent again only when it certainly did not
/// arrive: the connection was refused before anything was sent, or the service turned it away unread (408, 429, 503); after a 500 or a timeout it may
/// have been done, so it is not repeated unless the caller says it is safe to (<c>idempotent: true</c>, for a lookup sent as a POST).
/// The Jellyfin calls have their own gate with the same rules (<see cref="JellyfinGate"/>); MusicBrainz and the Cover Art Archive add their own pacing.
/// </summary>
public static class HttpRetry
{
    /// <summary>An answer that may be different if asked again.</summary>
    public static bool Transient(HttpStatusCode code) => (int)code is 408 or 429 or 500 or 502 or 503 or 504;

    /// <summary>An answer that says the request was not acted on (safe to send a write again).</summary>
    static bool NotProcessed(HttpStatusCode code) => (int)code is 408 or 429 or 503;

    static bool Idempotent(HttpMethod m) => m == HttpMethod.Get || m == HttpMethod.Head || m == HttpMethod.Options || m == HttpMethod.Put || m == HttpMethod.Delete || m == HttpMethod.Trace;

    /// <summary>The connection failed before the request was sent: nothing reached the service.</summary>
    static bool NotSent(Exception e) => e is HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError or HttpRequestError.SecureConnectionError };

    /// <summary>The service's Retry-After (seconds or a date), or null.</summary>
    public static TimeSpan? RetryAfter(HttpResponseMessage res)
    {
        if (res.Headers.RetryAfter is not { } ra) return null;
        var d = ra.Delta ?? (ra.Date is { } at ? at - DateTimeOffset.UtcNow : null);
        return d is { } x ? (x < TimeSpan.Zero ? TimeSpan.Zero : x) : null;
    }

    /// <summary>Sends the request [make] builds (a fresh one each try: a request message can be sent only once), asking again as above. Returns the last
    /// answer (the caller checks its status as before) or throws the last failure. [idempotent]: null decides by the method.</summary>
    public static async Task<HttpResponseMessage> SendAsync(HttpClient http, Func<HttpRequestMessage> make, RetryPolicy? policy, CancellationToken ct,
        bool? idempotent = null, HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        var p = policy ?? RetryPolicy.Default;
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var last = attempt >= p.Attempts - 1;
            using var req = make();
            var safe = idempotent ?? Idempotent(req.Method);
            HttpResponseMessage res;
            try { res = await http.SendAsync(req, completion, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e) when (!last && e is HttpRequestException or OperationCanceledException or IOException && (safe || NotSent(e)))
            {
                await p.Delay(p.Backoff(attempt), ct);
                continue;
            }
            if (last || !Transient(res.StatusCode) || !(safe || NotProcessed(res.StatusCode))) return res;
            var ra = RetryAfter(res);
            if (ra > p.MaxRetryAfter) return res;
            var wait = p.Backoff(attempt);
            if (ra > wait) wait = ra.Value;
            res.Dispose();
            await p.Delay(wait, ct);
        }
    }

    /// <summary>A GET's body as text, asking again as above; an answer that is still not a success throws <see cref="HttpRequestException"/> with its status.</summary>
    public static async Task<string> GetStringAsync(HttpClient http, string url, CancellationToken ct, RetryPolicy? policy = null)
    {
        using var res = await SendAsync(http, () => new HttpRequestMessage(HttpMethod.Get, url), policy, ct);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"{(int)res.StatusCode} {res.ReasonPhrase}", null, res.StatusCode);
        return await res.Content.ReadAsStringAsync(ct);
    }
}
