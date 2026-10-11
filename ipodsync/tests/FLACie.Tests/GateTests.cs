using System.Net;
using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>The one door to Jellyfin: concurrency limit, adaptive limit, priorities, back-off with Retry-After, cancellation of queued and running requests,
/// the circuit breaker, coalescing, and background work held while a song streams or the server is slow.</summary>
public class GateTests
{
    static JellyfinGate Gate(Action<GateOptions>? tweak = null, Func<long>? now = null)
    {
        var o = new GateOptions { InitialLimit = 3, MaxLimit = 8, MaxBackground = 2, BaseBackoff = TimeSpan.FromMilliseconds(10), MaxBackoff = TimeSpan.FromMilliseconds(80) };
        tweak?.Invoke(o);
        var g = new JellyfinGate(o) { Delay = (_, _) => Task.CompletedTask };
        if (now is not null) g.NowMs = now;
        return g;
    }

    static async Task Until(Func<bool> cond, int ms = 3000)
    {
        var until = Environment.TickCount64 + ms;
        while (!cond()) { if (Environment.TickCount64 > until) throw new TimeoutException("condition not met"); await Task.Delay(5); }
    }

    // a request that runs until the test lets it go
    sealed class Held
    {
        readonly TaskCompletionSource go = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Started;
        public Task<int> Run(JellyfinGate g, GatePriority p, CancellationToken ct = default) =>
            g.RunAsync(p, async c => { Interlocked.Increment(ref Started); await go.Task.WaitAsync(c); return 1; }, ct);
        public void Release() => go.TrySetResult();
    }

    [Fact]
    public async Task No_more_requests_run_at_once_than_the_limit_and_the_rest_wait_their_turn()
    {
        var g = Gate(o => { o.InitialLimit = 3; o.MaxLimit = 3; });
        var h = new Held();
        var all = Enumerable.Range(0, 10).Select(_ => h.Run(g, GatePriority.Visible)).ToList();
        await Until(() => h.Started == 3);
        await Task.Delay(50);
        Assert.Equal(3, h.Started);
        Assert.Equal(3, g.Metrics().InFlight);
        Assert.Equal(7, g.Metrics().Queued);
        h.Release();
        await Task.WhenAll(all);
        Assert.Equal(3, g.Metrics().PeakInFlight);
        Assert.Equal(0, g.Metrics().InFlight);
    }

    [Fact]
    public async Task The_song_playing_goes_first_then_what_is_visible_then_background_work()
    {
        var g = Gate(o => { o.InitialLimit = 1; o.MaxLimit = 1; });
        var h = new Held(); var order = new List<string>();
        var first = h.Run(g, GatePriority.Visible);
        await Until(() => h.Started == 1);
        Task Queue(string name, GatePriority p) => g.RunAsync(p, _ => { lock (order) order.Add(name); return Task.FromResult(1); }, default);
        var bg = Queue("background", GatePriority.Background);
        var vis = Queue("visible", GatePriority.Visible);
        var play = Queue("playing", GatePriority.Playing);
        await Task.Delay(30);
        Assert.Empty(order);
        h.Release();
        await Task.WhenAll(first, bg, vis, play).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["playing", "visible", "background"], order);
    }

    [Fact]
    public async Task A_429_with_Retry_After_waits_at_least_that_long_and_retries_at_most_twice()
    {
        var waits = new List<TimeSpan>();
        var g = Gate(); g.Delay = (d, _) => { waits.Add(d); return Task.CompletedTask; };
        var calls = 0;
        var res = await g.RunAsync(GatePriority.Visible, _ => ++calls <= 2 ? throw new JellyfinHttpException(HttpStatusCode.TooManyRequests, "429", TimeSpan.FromSeconds(7)) : Task.FromResult("ok"), default);
        Assert.Equal("ok", res); Assert.Equal(3, calls);
        Assert.Equal(2, waits.Count);
        Assert.All(waits, w => Assert.True(w >= TimeSpan.FromSeconds(7), "Retry-After is honoured"));

        // a server that keeps failing is asked three times in all (the first try and two retries), never more
        waits.Clear(); calls = 0;
        var g2 = Gate(); g2.Delay = (d, _) => { waits.Add(d); return Task.CompletedTask; };
        await Assert.ThrowsAsync<JellyfinHttpException>(() => g2.RunAsync<string>(GatePriority.Visible, _ => { calls++; throw new JellyfinHttpException(HttpStatusCode.BadGateway, "502"); }, default));
        Assert.Equal(3, calls);
        Assert.Equal(2, waits.Count);
        Assert.Equal(2, g2.Metrics().Retries);
    }

    [Fact]
    public void The_back_off_is_jittered_doubles_and_is_capped()
    {
        var g = Gate(o => { o.BaseBackoff = TimeSpan.FromMilliseconds(100); o.MaxBackoff = TimeSpan.FromMilliseconds(500); });
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var cap = Math.Min(500, 100 * Math.Pow(2, attempt));
            var seen = Enumerable.Range(0, 50).Select(_ => g.Backoff(attempt).TotalMilliseconds).ToList();
            Assert.All(seen, ms => Assert.InRange(ms, cap * 0.5 - 0.001, cap + 0.001));
            Assert.True(seen.Distinct().Count() > 1, "jitter: not always the same wait");
        }
    }

    [Fact]
    public async Task An_answer_that_is_not_a_server_failure_is_not_retried()
    {
        var g = Gate(); var calls = 0;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => g.RunAsync<int>(GatePriority.Visible, _ => { calls++; throw new UnauthorizedAccessException(); }, default));
        await Assert.ThrowsAsync<HttpRequestException>(() => g.RunAsync<int>(GatePriority.Visible, _ => { calls++; throw new HttpRequestException("nope", null, HttpStatusCode.NotFound); }, default));
        Assert.Equal(2, calls);
        Assert.Equal(BreakerState.Closed, g.Breaker);
    }

    [Fact]
    public async Task A_request_that_is_still_queued_leaves_the_queue_and_never_reaches_Jellyfin()
    {
        var g = Gate(o => { o.InitialLimit = 1; o.MaxLimit = 1; });
        var h = new Held();
        var running = h.Run(g, GatePriority.Visible);
        await Until(() => h.Started == 1);
        using var cts = new CancellationTokenSource();
        var sent = 0;
        var queued = g.RunAsync(GatePriority.Visible, _ => { Interlocked.Increment(ref sent); return Task.FromResult(1); }, cts.Token);
        await Until(() => g.Metrics().Queued == 1);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.Equal(0, g.Metrics().Queued);
        h.Release(); await running;
        await Task.Delay(30);
        Assert.Equal(0, sent);
        Assert.Equal(0, g.Metrics().InFlight);
    }

    [Fact]
    public async Task A_request_in_flight_is_cancelled_and_frees_its_slot_without_counting_as_a_failure()
    {
        var g = Gate(o => { o.InitialLimit = 2; o.MaxLimit = 2; });
        using var cts = new CancellationTokenSource();
        var seenCancel = new TaskCompletionSource();
        var t = g.RunAsync<int>(GatePriority.Playing, async c => { c.Register(() => seenCancel.TrySetResult()); await Task.Delay(Timeout.Infinite, c); return 1; }, cts.Token);
        await Until(() => g.Metrics().InFlight == 1);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => t);
        await seenCancel.Task.WaitAsync(TimeSpan.FromSeconds(2));   // the call to Jellyfin itself was told to stop
        Assert.Equal(0, g.Metrics().InFlight);
        Assert.Equal(2, g.Limit);                                   // an abandoned stream is not Jellyfin's fault: the limit stays
        Assert.Equal(BreakerState.Closed, g.Breaker);
    }

    [Fact]
    public async Task The_breaker_opens_after_repeated_failures_fails_fast_then_lets_one_probe_through_and_closes()
    {
        long now = 1_000;
        var g = Gate(o => { o.BreakerFailures = 3; o.MaxRetries = 0; o.CoolDown = TimeSpan.FromSeconds(15); }, () => now);
        var sends = 0;
        Task<int> Fail() => g.RunAsync<int>(GatePriority.Visible, _ => { sends++; throw new JellyfinHttpException(HttpStatusCode.InternalServerError, "500"); }, default, maxRetries: 0);
        for (var i = 0; i < 3; i++) await Assert.ThrowsAsync<JellyfinHttpException>(Fail);
        Assert.Equal(BreakerState.Open, g.Breaker);
        // open: the next ones fail at once and never reach Jellyfin
        var before = sends;
        var e = await Assert.ThrowsAsync<JellyfinUnavailableException>(() => g.RunAsync(GatePriority.Visible, _ => { sends++; return Task.FromResult(1); }, default));
        Assert.True(e.Wait > TimeSpan.Zero);
        Assert.Equal(before, sends);
        Assert.True(g.Metrics().FastFails >= 1);
        // cool-down over: half open, one probe goes through; a failed probe opens it again for another cool-down
        now += 15_001;
        Assert.Equal(BreakerState.HalfOpen, g.Breaker);
        await Assert.ThrowsAsync<JellyfinHttpException>(Fail);
        Assert.Equal(BreakerState.Open, g.Breaker);
        now += 15_001;
        Assert.Equal(1, await g.RunAsync(GatePriority.Visible, _ => Task.FromResult(1), default));   // the probe succeeds
        Assert.Equal(BreakerState.Closed, g.Breaker);
        Assert.Equal(1, await g.RunAsync(GatePriority.Visible, _ => Task.FromResult(1), default));
    }

    [Fact]
    public async Task While_the_breaker_is_open_waiting_requests_fail_at_once_instead_of_piling_up()
    {
        long now = 1_000;
        var g = Gate(o => { o.InitialLimit = 1; o.MaxLimit = 1; o.BreakerFailures = 1; o.MaxRetries = 0; }, () => now);
        var gate = new TaskCompletionSource();
        var first = g.RunAsync<int>(GatePriority.Visible, async _ => { await gate.Task; throw new JellyfinHttpException(HttpStatusCode.ServiceUnavailable, "503"); }, default, maxRetries: 0);
        await Until(() => g.Metrics().InFlight == 1);
        var queued = Enumerable.Range(0, 5).Select(_ => g.RunAsync(GatePriority.Visible, _ => Task.FromResult(1), default)).ToList();
        await Until(() => g.Metrics().Queued == 5);
        gate.SetResult();
        await Assert.ThrowsAsync<JellyfinHttpException>(() => first);
        foreach (var q in queued) await Assert.ThrowsAsync<JellyfinUnavailableException>(() => q);
        Assert.Equal(0, g.Metrics().Queued);
    }

    [Fact]
    public async Task Identical_requests_in_flight_are_one_request_and_one_asker_leaving_does_not_cancel_it_for_the_rest()
    {
        var g = Gate();
        var runs = 0; var gate = new TaskCompletionSource<string>();
        Task<string> Ask(CancellationToken ct = default) => g.RunSharedAsync("GET /x|tok", GatePriority.Visible, async c => { Interlocked.Increment(ref runs); return await gate.Task.WaitAsync(c); }, ct);
        var many = Enumerable.Range(0, 5).Select(_ => Ask()).ToList();
        using var leaver = new CancellationTokenSource();
        var quitter = Ask(leaver.Token);
        await Until(() => runs == 1);
        leaver.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => quitter);
        gate.SetResult("body");
        Assert.All(await Task.WhenAll(many), r => Assert.Equal("body", r));
        Assert.Equal(1, runs);
        Assert.Equal(5, g.Metrics().Coalesced);
    }

    [Fact]
    public async Task When_every_asker_of_a_shared_request_has_left_the_request_itself_is_cancelled()
    {
        var g = Gate();
        var told = new TaskCompletionSource();
        Task<int> Ask(CancellationToken ct) => g.RunSharedAsync("GET /y|tok", GatePriority.Visible, async c => { c.Register(() => told.TrySetResult()); await Task.Delay(Timeout.Infinite, c); return 1; }, ct);
        using var a = new CancellationTokenSource(); using var b = new CancellationTokenSource();
        var ta = Ask(a.Token); var tb = Ask(b.Token);
        await Until(() => g.Metrics().InFlight == 1);
        a.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ta);
        Assert.False(told.Task.IsCompleted);                      // one is still waiting for it
        b.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tb);
        await told.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Until(() => g.Metrics().InFlight == 0);
    }

    [Fact]
    public async Task Background_work_waits_while_a_song_streams_and_starts_when_it_ends()
    {
        var streaming = true;
        var g = Gate(); g.Streaming = () => streaming;
        var started = 0;
        var bg = g.RunAsync(GatePriority.Background, _ => { Interlocked.Increment(ref started); return Task.FromResult(1); }, default);
        var vis = await g.RunAsync(GatePriority.Visible, _ => Task.FromResult(2), default);   // everything else is not held
        Assert.Equal(2, vis);
        await Task.Delay(400);
        Assert.Equal(0, started);
        Assert.Equal(1, g.Metrics().BackgroundHeld);
        streaming = false;
        await bg.WaitAsync(TimeSpan.FromSeconds(3));            // picked up by the next look (every 250 ms)
        Assert.Equal(1, started);
    }

    [Fact]
    public async Task Background_work_waits_while_Jellyfin_is_slow_and_runs_once_it_has_recovered()
    {
        long now = 100_000;
        var g = Gate(o => { o.SlowMs = 1500; o.InitialLimit = 4; }, () => now);
        // one slow answer (3 s): the average is above "slow" and the limit has halved
        await g.RunAsync(GatePriority.Visible, _ => { now += 3_000; return Task.FromResult(1); }, default);
        Assert.True(g.Limit < 4);
        var started = 0;
        var bg = g.RunAsync(GatePriority.Background, _ => { Interlocked.Increment(ref started); return Task.FromResult(1); }, default);
        await Task.Delay(400);
        Assert.Equal(0, started);
        now += 11_000;   // no news for 10 s: the reading is forgotten, a quiet gate is not "slow" for ever
        await bg.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, started);
    }

    [Fact]
    public async Task The_next_songs_start_runs_while_a_song_streams_but_not_while_Jellyfin_is_slow_and_never_ahead_of_someone_waiting()
    {
        long now = 100_000;
        var g = Gate(o => { o.InitialLimit = 4; o.SlowMs = 1500; }, () => now);
        g.Streaming = () => true;                                   // a song is being served
        Assert.Equal(1, await g.RunAsync(GatePriority.Prefetch, _ => Task.FromResult(1), default).WaitAsync(TimeSpan.FromSeconds(2)));
        // slow Jellyfin (a few slow answers in a row: one outlier among fast ones does not make a slow server): the prefetch is held
        for (var i = 0; i < 4; i++) await g.RunAsync(GatePriority.Visible, _ => { now += 4_000; return Task.FromResult(0); }, default);
        var started = 0;
        var pre = g.RunAsync(GatePriority.Prefetch, _ => { Interlocked.Increment(ref started); return Task.FromResult(1); }, default);
        await Task.Delay(400);
        Assert.Equal(0, started);
        now += 11_000;
        await pre.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, started);
    }

    [Fact]
    public async Task The_limit_grows_after_a_run_of_fast_answers_and_halves_on_a_slow_one_never_below_one()
    {
        long now = 1_000;
        var g = Gate(o => { o.InitialLimit = 4; o.MaxLimit = 6; o.FastRunToGrow = 3; o.FastMs = 500; o.SlowMs = 1500; o.ShrinkEvery = TimeSpan.FromMilliseconds(100); }, () => now);
        Task Fast() => g.RunAsync(GatePriority.Visible, _ => { now += 50; return Task.FromResult(1); }, default);
        for (var i = 0; i < 3; i++) await Fast();
        Assert.Equal(5, g.Limit);
        for (var i = 0; i < 9; i++) await Fast();
        Assert.Equal(6, g.Limit);                                   // capped
        await g.RunAsync(GatePriority.Visible, _ => { now += 2_000; return Task.FromResult(1); }, default);
        Assert.Equal(3, g.Limit);
        for (var i = 0; i < 8; i++) { now += 200; await g.RunAsync(GatePriority.Visible, _ => { now += 2_000; return Task.FromResult(1); }, default); }
        Assert.Equal(1, g.Limit);
    }

    [Fact]
    public async Task A_big_answer_is_judged_by_its_own_yardstick_not_as_a_slow_server()
    {
        long now = 1_000;
        var g = Gate(o => { o.InitialLimit = 4; o.SlowMs = 1500; }, () => now);
        // a page of 500 songs with their streams that takes 4 s of a 12 s allowance
        await g.RunAsync(GatePriority.Visible, _ => { now += 4_000; return Task.FromResult(1); }, default, slowMs: JellyfinClient.BigAnswerMs);
        Assert.Equal(4, g.Limit);
        Assert.Equal(BreakerState.Closed, g.Breaker);
        var started = false;
        await g.RunAsync(GatePriority.Background, _ => { started = true; return Task.FromResult(1); }, default).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(started);
    }

    [Fact]
    public async Task The_priority_of_a_flow_is_set_with_Use_and_restored_after()
    {
        Assert.Equal(GatePriority.Visible, JellyfinGate.Current);
        using (JellyfinGate.Use(GatePriority.Background))
        {
            Assert.Equal(GatePriority.Background, JellyfinGate.Current);
            await Task.Yield();
            Assert.Equal(GatePriority.Background, JellyfinGate.Current);
        }
        Assert.Equal(GatePriority.Visible, JellyfinGate.Current);
    }
}
