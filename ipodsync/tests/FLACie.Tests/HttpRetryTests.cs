using System.Net;
using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>
/// The one way the server asks another service again (catalogs, lyrics, the Cover Art Archive, MusicBrainz, the notifier, the family's FLACie Web, Alexa):
/// at most three tries, exponential back-off with jitter, the service's Retry-After honoured (and a long one not waited out), nothing retried that waiting
/// cannot change (4xx other than 408 and 429), and a write never sent twice unless the first certainly did not arrive.
/// </summary>
public class HttpRetryTests
{
    sealed class Script(params Func<HttpRequestMessage, HttpResponseMessage>[] steps) : HttpMessageHandler
    {
        public int Calls; public readonly List<string> Methods = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Methods.Add(request.Method.Method);
            var step = steps[Math.Min(Calls++, steps.Length - 1)];
            return Task.FromResult(step(request));
        }
    }

    static HttpResponseMessage Code(int code, string? retryAfter = null, string body = "")
    {
        var r = new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent(body) };
        if (retryAfter is not null) r.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
        return r;
    }

    static Func<HttpRequestMessage, HttpResponseMessage> Throw(Exception e) => _ => throw e;

    readonly List<TimeSpan> waits = [];
    RetryPolicy Policy(int attempts = 3) => new() { Attempts = attempts, Random = () => 1.0, Delay = (d, _) => { waits.Add(d); return Task.CompletedTask; } };

    static Func<HttpRequestMessage> Get(string url = "https://svc.example/x") => () => new HttpRequestMessage(HttpMethod.Get, url);
    static Func<HttpRequestMessage> Post() => () => new HttpRequestMessage(HttpMethod.Post, "https://svc.example/x") { Content = new StringContent("{}") };

    [Fact]
    public async Task A_busy_answer_is_asked_again_after_a_growing_jittered_wait()
    {
        var h = new Script(_ => Code(503), _ => Code(502), _ => Code(200, body: "ok"));
        using var res = await HttpRetry.SendAsync(new HttpClient(h), Get(), Policy(), default);
        Assert.Equal(200, (int)res.StatusCode);
        Assert.Equal(3, h.Calls);
        Assert.Equal([TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(800)], waits);
    }

    [Fact]
    public async Task Three_tries_at_most_and_the_last_answer_comes_back()
    {
        var h = new Script(_ => Code(500));
        using var res = await HttpRetry.SendAsync(new HttpClient(h), Get(), Policy(), default);
        Assert.Equal(500, (int)res.StatusCode);
        Assert.Equal(3, h.Calls);
    }

    [Theory]
    [InlineData(400)] [InlineData(401)] [InlineData(403)] [InlineData(404)] [InlineData(501)]
    public async Task An_answer_waiting_cannot_change_is_not_asked_again(int code)
    {
        var h = new Script(_ => Code(code));
        using var res = await HttpRetry.SendAsync(new HttpClient(h), Get(), Policy(), default);
        Assert.Equal(1, h.Calls);
        Assert.Empty(waits);
    }

    [Theory]
    [InlineData(408)] [InlineData(429)] [InlineData(504)]
    public async Task Timeouts_and_rate_limits_are_asked_again(int code)
    {
        var h = new Script(_ => Code(code), _ => Code(200));
        using var res = await HttpRetry.SendAsync(new HttpClient(h), Get(), Policy(), default);
        Assert.Equal(200, (int)res.StatusCode);
        Assert.Equal(2, h.Calls);
    }

    [Fact]
    public async Task The_services_Retry_After_is_honoured_and_never_shorter_than_the_backoff()
    {
        var h = new Script(_ => Code(429, "3"), _ => Code(429, "0"), _ => Code(200));
        using var res = await HttpRetry.SendAsync(new HttpClient(h), Get(), Policy(), default);
        Assert.Equal([TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(800)], waits);
    }

    [Fact]
    public async Task A_Retry_After_given_as_a_date_counts_too()
    {
        var at = DateTimeOffset.UtcNow.AddSeconds(5).ToString("R");
        var h = new Script(_ => Code(503, at), _ => Code(200));
        using var res = await HttpRetry.SendAsync(new HttpClient(h), Get(), Policy(), default);
        Assert.InRange(waits.Single().TotalSeconds, 3, 6);
    }

    [Fact]
    public async Task A_long_Retry_After_is_not_waited_out_the_answer_comes_back_at_once()
    {
        var h = new Script(_ => Code(429, "120"));
        using var res = await HttpRetry.SendAsync(new HttpClient(h), Get(), Policy(), default);
        Assert.Equal(429, (int)res.StatusCode);
        Assert.Equal(1, h.Calls);
        Assert.Empty(waits);
    }

    [Fact]
    public async Task A_dropped_connection_or_a_timeout_is_tried_again_for_a_read()
    {
        var h = new Script(Throw(new HttpRequestException("reset", null, null)), Throw(new TaskCanceledException("timeout")), _ => Code(200));
        using var res = await HttpRetry.SendAsync(new HttpClient(h), Get(), Policy(), default);
        Assert.Equal(200, (int)res.StatusCode);
        Assert.Equal(3, h.Calls);
    }

    [Fact]
    public async Task The_last_failure_is_thrown_when_every_try_fails()
    {
        var h = new Script(Throw(new HttpRequestException("down")));
        await Assert.ThrowsAsync<HttpRequestException>(() => HttpRetry.SendAsync(new HttpClient(h), Get(), Policy(), default));
        Assert.Equal(3, h.Calls);
    }

    [Fact]
    public async Task The_callers_own_cancel_stops_at_once()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var h = new Script(_ => Code(503));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HttpRetry.SendAsync(new HttpClient(h), Get(), Policy(), cts.Token));
        Assert.True(h.Calls <= 1);
    }

    [Fact]
    public async Task A_write_is_asked_again_only_when_it_certainly_did_not_arrive()
    {
        // refused before anything was sent, or turned away unread (429, 503): safe to send again
        var notSent = new Script(Throw(new HttpRequestException(HttpRequestError.ConnectionError, "refused")), _ => Code(429), _ => Code(503), _ => Code(200));
        using (var res = await HttpRetry.SendAsync(new HttpClient(notSent), Post(), Policy(attempts: 4), default)) Assert.Equal(200, (int)res.StatusCode);
        Assert.Equal(4, notSent.Calls);
        // a 500 or a timeout may mean the work was done: never sent twice
        var maybeDone = new Script(_ => Code(500), _ => Code(200));
        using (var res = await HttpRetry.SendAsync(new HttpClient(maybeDone), Post(), Policy(), default)) Assert.Equal(500, (int)res.StatusCode);
        var timedOut = new Script(Throw(new TaskCanceledException("timeout")), _ => Code(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HttpRetry.SendAsync(new HttpClient(timedOut), Post(), Policy(), default));
        Assert.Equal(1, maybeDone.Calls); Assert.Equal(1, timedOut.Calls);
    }

    [Fact]
    public async Task A_write_the_caller_says_is_safe_to_repeat_is_asked_again_like_a_read()
    {
        var h = new Script(_ => Code(500), _ => Code(200));
        using var res = await HttpRetry.SendAsync(new HttpClient(h), Post(), Policy(), default, idempotent: true);
        Assert.Equal(200, (int)res.StatusCode);
        Assert.Equal(["POST", "POST"], h.Methods);
    }

    [Fact]
    public async Task Each_try_is_a_fresh_request()
    {
        var made = 0;
        var h = new Script(_ => Code(503), _ => Code(200));
        using var res = await HttpRetry.SendAsync(new HttpClient(h), () => { made++; return new HttpRequestMessage(HttpMethod.Get, "https://svc.example/x"); }, Policy(), default);
        Assert.Equal(2, made);
    }

    [Fact]
    public void The_default_backoff_is_short_and_capped()
    {
        var p = new RetryPolicy { Random = () => 1.0 };
        Assert.Equal(3, p.Attempts);
        Assert.Equal(TimeSpan.FromMilliseconds(400), p.Backoff(0));
        Assert.Equal(TimeSpan.FromMilliseconds(800), p.Backoff(1));
        Assert.Equal(p.MaxDelay, p.Backoff(20));
        Assert.Equal(TimeSpan.FromMilliseconds(200), new RetryPolicy { Random = () => 0.0 }.Backoff(0));   // jitter: up to half shorter
    }

    [Fact]
    public async Task GetString_reads_the_body_or_throws_with_the_status()
    {
        Assert.Equal("hi", await HttpRetry.GetStringAsync(new HttpClient(new Script(_ => Code(503), _ => Code(200, body: "hi"))), "https://svc.example/x", default, Policy()));
        var e = await Assert.ThrowsAsync<HttpRequestException>(() => HttpRetry.GetStringAsync(new HttpClient(new Script(_ => Code(404))), "https://svc.example/x", default, Policy()));
        Assert.Equal(HttpStatusCode.NotFound, e.StatusCode);
    }
}
