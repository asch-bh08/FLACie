using System.Net;

namespace FLACie.Core;

/// <summary>Who is asking, which decides who waits: a song that is playing (its stream and its info) goes first, what is on screen (rows, their covers) next,
/// and background work (library sync, probes) only when nothing else is waiting, the server is not slow and nobody is streaming.</summary>
public enum GatePriority { Playing = 0, Visible = 1, Background = 2 }

public enum BreakerState { Closed, Open, HalfOpen }

/// <summary>Jellyfin did not accept the request for a reason that may pass (429 or 5xx); <see cref="RetryAfter"/> is what it asked us to wait.</summary>
public sealed class JellyfinHttpException(HttpStatusCode status, string message, TimeSpan? retryAfter = null) : HttpRequestException(message, null, status)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>The circuit breaker is open: Jellyfin has been failing, so requests fail at once instead of piling up on it.</summary>
public sealed class JellyfinUnavailableException(TimeSpan wait) : HttpRequestException($"Jellyfin is not answering; waiting {Math.Ceiling(wait.TotalSeconds)} s before trying again.")
{
    public TimeSpan Wait { get; } = wait;
}

public sealed class GateOptions
{
    public int MinLimit { get; set; } = 1;
    public int MaxLimit { get; set; } = 8;
    public int InitialLimit { get; set; } = 5;
    /// <summary>A request slower than this shrinks the limit (and holds background work back while the average stays above it).</summary>
    public int SlowMs { get; set; } = 1500;
    /// <summary>A request faster than this counts toward growing the limit.</summary>
    public int FastMs { get; set; } = 500;
    public int FastRunToGrow { get; set; } = 8;
    public int MaxRetries { get; set; } = 2;
    public int BreakerFailures { get; set; } = 6;
    public TimeSpan CoolDown { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan BaseBackoff { get; set; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(8);
    /// <summary>At most this many background requests run at once (and always one slot is left for the others).</summary>
    public int MaxBackground { get; set; } = 2;
    /// <summary>Minimum time between two shrinks, so one bad burst does not collapse the limit to 1.</summary>
    public TimeSpan ShrinkEvery { get; set; } = TimeSpan.FromSeconds(1);
}

/// <summary>What the admin page shows.</summary>
public sealed record GateMetrics(int Queued, int InFlight, int PeakInFlight, int Limit, BreakerState Breaker, long Retries, long Coalesced, long FastFails, double LatencyMs, int BackgroundHeld);

/// <summary>
/// The one door every request to Jellyfin goes through. A limited number run at once (an adaptive limit: it grows by one after a run of fast answers and halves
/// when Jellyfin is slow, answers 429 or 5xx, or times out), the rest wait in a priority queue. A failing Jellyfin trips a circuit breaker: requests fail fast for a
/// cool-down, then one probe goes through. Identical GETs in flight are shared. A caller that gives up leaves the queue, or cancels its request in flight.
/// </summary>
public sealed class JellyfinGate
{
    static readonly AsyncLocal<GatePriority?> ambient = new();
    /// <summary>The priority of requests made from the current async flow (Visible unless a caller said otherwise).</summary>
    public static GatePriority Current => ambient.Value ?? GatePriority.Visible;
    /// <summary>Everything started inside the returned scope has this priority.</summary>
    public static IDisposable Use(GatePriority p) { var old = ambient.Value; ambient.Value = p; return new Restore(old); }
    sealed class Restore(GatePriority? old) : IDisposable { public void Dispose() => ambient.Value = old; }

    public GateOptions Options { get; }
    /// <summary>True while someone is streaming: background work waits.</summary>
    public Func<bool> Streaming { get; set; } = () => false;
    public Func<long> NowMs { get; set; } = () => Environment.TickCount64;
    public Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;
    public Random Rng { get; set; } = new();

    readonly object lk = new();
    readonly LinkedList<Waiter>[] queues = [new(), new(), new()];
    int inFlight, bgInFlight, limit, peak, fastRun, consecutiveFailures;
    long retries, coalescedCount, fastFails, lastShrinkAt = long.MinValue, openUntil;
    double ewma;
    bool haveEwma, probeInFlight, redispatchScheduled;
    BreakerState state = BreakerState.Closed;

    public JellyfinGate(GateOptions? options = null) { Options = options ?? new(); limit = Math.Clamp(Options.InitialLimit, Options.MinLimit, Options.MaxLimit); }

    sealed class Waiter { public required GatePriority P; public TaskCompletionSource<bool> Start = new(TaskCreationOptions.RunContinuationsAsynchronously); public LinkedListNode<Waiter>? Node; }

    public int Limit { get { lock (lk) return limit; } }
    public BreakerState Breaker { get { lock (lk) { Tick(); return state; } } }
    public GateMetrics Metrics() { lock (lk) { Tick(); return new(queues.Sum(q => q.Count), inFlight, peak, limit, state, retries, coalescedCount, fastFails, ewma, queues[2].Count); } }

    // ---- running a request ----

    /// <summary>Runs <paramref name="send"/> under the gate. Transient failures (429, 5xx, timeouts, connection errors) are retried up to
    /// <paramref name="maxRetries"/> times after a jittered back-off (or the server's Retry-After); other exceptions come straight back.
    /// <paramref name="classify"/> lets a caller that returns a response instead of throwing say which responses count as a failure.</summary>
    public async Task<T> RunAsync<T>(GatePriority priority, Func<CancellationToken, Task<T>> send, CancellationToken ct, int? maxRetries = null, Func<T, Exception?>? classify = null)
    {
        var allowed = maxRetries ?? Options.MaxRetries;
        for (var attempt = 0; ; attempt++)
        {
            await AcquireAsync(priority, ct);
            var t0 = NowMs();
            Exception? fail = null, thrown = null; T result = default!; var ok = false;
            try { result = await send(ct); fail = classify?.Invoke(result); ok = fail is null; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { Release(priority, t0, Outcome.Neutral); throw; }
            catch (Exception e) when (IsTransient(e)) { fail = thrown = e; }
            catch { Release(priority, t0, Outcome.Success); throw; }   // the server answered (401, 404 ...): not a failure of the server
            Release(priority, t0, ok ? Outcome.Success : Outcome.Failure);
            if (ok) return result;
            if (attempt >= allowed || ct.IsCancellationRequested || Breaker == BreakerState.Open)
            {
                if (thrown is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(thrown).Throw();
                return result;   // a response the caller classified as bad: it gets the response itself
            }
            Interlocked.Increment(ref retries);
            var wait = Backoff(attempt);
            if (fail is JellyfinHttpException { RetryAfter: { } ra } && ra > wait) wait = ra;
            await Delay(wait, ct);
        }
    }

    // ---- coalescing identical GETs ----

    readonly Dictionary<string, Shared> shared = new();
    sealed class Shared { public required Task Task; public required CancellationTokenSource Cts; public int Refs; public required Type Type; }

    /// <summary>Like <see cref="RunAsync{T}"/>, but callers with the same <paramref name="key"/> (method + URL + token) at the same time share one request.</summary>
    public async Task<T> RunSharedAsync<T>(string key, GatePriority priority, Func<CancellationToken, Task<T>> send, CancellationToken ct)
    {
        Shared entry;
        lock (shared)
        {
            if (shared.TryGetValue(key, out var e) && e.Type == typeof(T)) { e.Refs++; entry = e; Interlocked.Increment(ref coalescedCount); }
            else
            {
                var cts = new CancellationTokenSource();
                entry = new Shared { Cts = cts, Refs = 1, Type = typeof(T), Task = null! };
                var task = RunAsync(priority, send, cts.Token);
                entry.Task = task;
                shared[key] = entry;
                _ = task.ContinueWith(_ => { lock (shared) { if (shared.TryGetValue(key, out var cur) && ReferenceEquals(cur, entry)) shared.Remove(key); } cts.Dispose(); }, TaskScheduler.Default);
            }
        }
        try { return await ((Task<T>)entry.Task).WaitAsync(ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            lock (shared)
            {
                if (--entry.Refs <= 0)
                {
                    if (shared.TryGetValue(key, out var cur) && ReferenceEquals(cur, entry)) shared.Remove(key);   // nobody is waiting: stop it, and let the next asker start a fresh one
                    try { entry.Cts.Cancel(); } catch (ObjectDisposedException) { }
                }
            }
            throw;
        }
    }

    // ---- queue ----

    Task AcquireAsync(GatePriority p, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var w = new Waiter { P = p };
        lock (lk)
        {
            Tick();
            if (state == BreakerState.Open) { fastFails++; return Task.FromException(new JellyfinUnavailableException(TimeSpan.FromMilliseconds(Math.Max(0, openUntil - NowMs())))); }
            w.Node = queues[(int)p].AddLast(w);
        }
        if (ct.CanBeCanceled)
            ct.Register(() =>
            {
                lock (lk) { if (w.Node?.List is { } l) { l.Remove(w.Node); w.Node = null; } else return; }
                w.Start.TrySetCanceled(ct);
            });
        Dispatch();
        return w.Start.Task;
    }

    void Tick()   // lk held
    {
        if (state == BreakerState.Open && NowMs() >= openUntil) { state = BreakerState.HalfOpen; probeInFlight = false; }
    }

    long lastSampleAt;
    /// <summary>Jellyfin has been slow lately. A reading older than 10 s is forgotten, so a quiet gate cannot stay "slow" for ever.</summary>
    bool Slow => haveEwma && ewma > Options.SlowMs && NowMs() - lastSampleAt < 10_000;

    void Dispatch()
    {
        List<Waiter>? grant = null; List<Waiter>? failed = null; var hold = false; TimeSpan failWait = default;
        lock (lk)
        {
            Tick();
            if (state == BreakerState.Open)
            {
                failWait = TimeSpan.FromMilliseconds(Math.Max(0, openUntil - NowMs()));
                failed = [];
                foreach (var q in queues) { foreach (var w in q) { w.Node = null; failed.Add(w); } q.Clear(); }
                fastFails += failed.Count;
            }
            else
            {
                while (inFlight < limit)
                {
                    if (state == BreakerState.HalfOpen && probeInFlight) break;
                    var q = queues.FirstOrDefault(x => x.Count > 0);
                    if (q is null) break;
                    var w = q.First!.Value;
                    if (w.P == GatePriority.Background && !BackgroundMayRun()) { hold = true; break; }
                    q.RemoveFirst(); w.Node = null;
                    inFlight++; peak = Math.Max(peak, inFlight);
                    if (w.P == GatePriority.Background) bgInFlight++;
                    if (state == BreakerState.HalfOpen) probeInFlight = true;
                    (grant ??= []).Add(w);
                }
                if (hold && !redispatchScheduled) { redispatchScheduled = true; _ = ReDispatchLaterAsync(); }
            }
        }
        if (failed is not null) foreach (var w in failed) w.Start.TrySetException(new JellyfinUnavailableException(failWait));
        if (grant is not null) foreach (var w in grant) w.Start.TrySetResult(true);
    }

    bool BackgroundMayRun() => !Streaming() && !Slow && bgInFlight < Options.MaxBackground && bgInFlight < Math.Max(1, limit - 1) && queues[0].Count == 0 && queues[1].Count == 0;

    async Task ReDispatchLaterAsync()
    {
        try { await Task.Delay(250); } catch { }
        lock (lk) redispatchScheduled = false;
        Dispatch();
    }

    enum Outcome { Success, Failure, Neutral }

    void Release(GatePriority p, long startedAt, Outcome o)
    {
        lock (lk)
        {
            inFlight--; if (p == GatePriority.Background) bgInFlight--;
            var ms = Math.Max(0, NowMs() - startedAt);
            if (o != Outcome.Neutral)
            {
                ewma = haveEwma ? ewma * 0.8 + ms * 0.2 : ms; haveEwma = true; lastSampleAt = NowMs();
                if (o == Outcome.Success && ms <= Options.SlowMs)
                {
                    consecutiveFailures = 0;
                    if (state == BreakerState.HalfOpen) { state = BreakerState.Closed; probeInFlight = false; }
                    if (ms <= Options.FastMs && ++fastRun >= Options.FastRunToGrow) { fastRun = 0; limit = Math.Min(Options.MaxLimit, limit + 1); }
                    else if (ms > Options.FastMs) fastRun = 0;
                }
                else
                {
                    fastRun = 0;
                    if (NowMs() - lastShrinkAt >= (long)Options.ShrinkEvery.TotalMilliseconds) { limit = Math.Max(Options.MinLimit, limit / 2); lastShrinkAt = NowMs(); }
                    if (o == Outcome.Failure)
                    {
                        consecutiveFailures++;
                        if (state == BreakerState.HalfOpen || consecutiveFailures >= Options.BreakerFailures) { state = BreakerState.Open; openUntil = NowMs() + (long)Options.CoolDown.TotalMilliseconds; probeInFlight = false; consecutiveFailures = 0; }
                    }
                    else if (state == BreakerState.HalfOpen) { state = BreakerState.Closed; probeInFlight = false; }   // slow, but it answered
                }
            }
            else if (state == BreakerState.HalfOpen) probeInFlight = false;
        }
        Dispatch();
    }

    static bool IsTransient(Exception e) => e switch
    {
        JellyfinHttpException => true,
        TimeoutException => true,
        OperationCanceledException => true,   // not the caller's (that case is caught first): a request timeout
        HttpRequestException h => h is not JellyfinUnavailableException && (h.StatusCode is null || (int)h.StatusCode >= 500 || h.StatusCode == HttpStatusCode.TooManyRequests),
        _ => false,
    };

    /// <summary>The wait before retry number <paramref name="attempt"/> + 1: base * 2^attempt, capped, scaled by a random 0.5 to 1 (jitter).</summary>
    public TimeSpan Backoff(int attempt)
    {
        var cap = Math.Min(Options.MaxBackoff.TotalMilliseconds, Options.BaseBackoff.TotalMilliseconds * Math.Pow(2, attempt));
        double r; lock (Rng) r = Rng.NextDouble();
        return TimeSpan.FromMilliseconds(cap * (0.5 + 0.5 * r));
    }
}

/// <summary>Sends requests marked by <see cref="JellyfinClient.Authorized"/> (audio, covers, anything proxied to Jellyfin) through the gate. Only the wait for the
/// response headers is gated: the body (a song playing for minutes) is read after the slot is free. These requests are not retried here (a request message can be
/// sent once), but a 429 or 5xx still counts against the limit and the breaker.</summary>
public sealed class GatedHandler(JellyfinGate gate) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (!request.Options.TryGetValue(JellyfinClient.GateKey, out var on) || !on) return await base.SendAsync(request, ct);
        return await gate.RunAsync(JellyfinGate.Current, c => base.SendAsync(request, c), ct, maxRetries: 0,
            classify: r => (int)r.StatusCode >= 500 || r.StatusCode == HttpStatusCode.TooManyRequests ? new JellyfinHttpException(r.StatusCode, $"{(int)r.StatusCode}") : null);
    }
}
