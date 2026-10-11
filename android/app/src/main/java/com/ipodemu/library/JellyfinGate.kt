package com.ipodemu.library

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Deferred
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.async
import kotlinx.coroutines.delay
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeoutOrNull
import java.io.IOException
import java.util.TreeSet
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.atomic.AtomicLong
import kotlin.random.Random

/** Who is waiting: the song that plays, the rows on screen, or work nobody is looking at (bulk sync, prefetch, probes). Lower ordinal goes first. */
enum class Priority { PLAYING, VISIBLE, BACKGROUND }

enum class BreakerState { CLOSED, OPEN, HALF_OPEN }

class GateRequest(
    val method: String = "GET",
    val url: String,
    val headers: Map<String, String> = emptyMap(),
    val body: ByteArray? = null,
    val contentType: String? = null,
    /** What this request is for ("list", "cover", "info", "session" ...), only for the counters. */
    val kind: String = "other",
    /** A request that legitimately takes long (a 500-item page with stream facts): judged against a longer "slow" limit. */
    val heavy: Boolean = false,
)

class GateResponse(val code: Int, val body: ByteArray, val headers: Map<String, String> = emptyMap()) {
    val ok: Boolean get() = code in 200..299
    val text: String get() = String(body, Charsets.UTF_8)
    /** The server's Retry-After in ms (seconds form only), or null. */
    val retryAfterMs: Long? get() = headers["retry-after"]?.trim()?.toLongOrNull()?.let { it * 1000L }
}

/** The breaker is open: Jellyfin has been failing, so this request failed at once instead of piling on. */
class GateOpenException : IOException("Jellyfin is not answering right now")

class GateConfig(
    val initialLimit: Int = 5, val minLimit: Int = 1, val maxLimit: Int = 8,
    /** Playing work may run this many above the limit, so the song that plays never queues behind covers. */
    val playingReserve: Int = 2,
    val slowMs: Long = 2500, val heavySlowMs: Long = 25_000, val fastMs: Long = 400, val growAfter: Int = 15,
    val maxRetries: Int = 2, val backoffBaseMs: Long = 400, val backoffMaxMs: Long = 15_000, val retryAfterMaxMs: Long = 30_000,
    val breakerFailures: Int = 6, val breakerCooldownMs: Long = 10_000, val breakerMaxCooldownMs: Long = 60_000,
    val backgroundPollMs: Long = 250,
    /** Background work holds back while the average answer of ordinary requests is slower than this. */
    val slowServerMs: Long = 1200,
)

class GateStats(
    val queued: Int, val inFlight: Int, val peak: Int, val limit: Int, val breaker: BreakerState, val total: Long,
    val retries: Long, val coalesced: Long, val rejected: Long, val failures: Long, val avgMs: Long, val perKind: Map<String, Int>,
) {
    fun line(): String = "queued=$queued inflight=$inFlight peak=$peak limit=$limit breaker=$breaker total=$total retries=$retries coalesced=$coalesced " +
        "rejected=$rejected failed=$failures avgMs=$avgMs kinds=" + perKind.entries.sortedBy { it.key }.joinToString(",") { "${it.key}:${it.value}" }
}

typealias GateTransport = suspend (GateRequest) -> GateResponse

/**
 * The one door to the Jellyfin server. Every request waits here for a free slot, so the server never sees a flood: lists, covers, session
 * calls and the player's stream opens share a small, self-adjusting number of simultaneous requests (AIMD: slow answers, 429 and 5xx cut
 * the number in half, a run of fast answers adds one), failures back off with jitter (and Retry-After), a circuit breaker stops asking
 * while the server is plainly down, identical GETs in flight are made once, and work is ordered by [Priority].
 * Cancelling the calling coroutine removes the request from the queue or cancels the HTTP call itself.
 *
 * Time, delay, randomness and the "screen is being touched" test are injected so the behaviour can be tested without a network or a clock.
 */
class JellyfinGate(
    private val cfg: GateConfig = GateConfig(),
    private val transport: GateTransport,
    private val scope: CoroutineScope,
    private val now: () -> Long = { System.nanoTime() / 1_000_000L },
    private val sleep: suspend (Long) -> Unit = { delay(it) },
    private val random: Random = Random.Default,
    private val busy: () -> Boolean = { false },
) {
    private class Waiter(var priority: Priority, val seq: Long) {
        val granted = CompletableDeferred<Unit>()
        @Volatile var admitted = false
        var wasBackground = false
    }

    /** The priority of one piece of work, raised when a more urgent caller joins it. */
    private class PrioBox(@Volatile var priority: Priority) { var waiter: Waiter? = null }

    private class Shared(val job: Deferred<GateResponse>, val box: PrioBox) { var refs = 0 }

    private val lock = Any()
    private val queue = TreeSet<Waiter>(compareBy<Waiter>({ it.priority.ordinal }, { it.seq }))
    private var seq = 0L
    private var inFlight = 0
    private var inFlightBg = 0
    private var peak = 0
    private var limit = cfg.initialLimit.coerceIn(cfg.minLimit, cfg.maxLimit)
    private var fastRun = 0
    private var holdUntil = 0L

    private var breaker = BreakerState.CLOSED
    private var consecutiveFailures = 0
    private var openUntil = 0L
    private var cooldown = cfg.breakerCooldownMs
    private var probing = false

    private var avgMs = 0.0
    private var lastSampleAt = 0L

    private val shared = HashMap<String, Shared>()
    private val kinds = ConcurrentHashMap<String, AtomicInteger>()
    private val total = AtomicLong(); private val retries = AtomicLong(); private val coalescedN = AtomicLong()
    private val rejected = AtomicLong(); private val failures = AtomicLong()

    // ---- public API ---------------------------------------------------------------------------------------------

    suspend fun execute(req: GateRequest, priority: Priority): GateResponse {
        return if (req.method == "GET") coalesced(req, priority) else run(req, PrioBox(priority))
    }

    /** Runs [block] (a blocking network call, e.g. a media stream's open) while holding a slot; [codeOf] turns a failure into its HTTP status (0 = no answer). */
    suspend fun <T> withPermit(priority: Priority, kind: String, codeOf: (Throwable) -> Int = { 0 }, block: suspend () -> T): T {
        count(kind)
        val box = PrioBox(priority)
        val probe = breakerCheck(priority)
        val w = try { acquire(box) } catch (e: Throwable) { if (probe) synchronized(lock) { probing = false }; throw e }
        val t0 = now()
        var code = 0
        try {
            val r = block(); code = 200; return r
        } catch (e: CancellationException) { code = -1; throw e }
        catch (e: Throwable) { code = codeOf(e); throw e }
        finally {
            release(w)
            if (code != -1) outcome(code, now() - t0, false, null, probe) else if (probe) synchronized(lock) { probing = false }
        }
    }

    /** [withPermit] for a thread that cannot suspend (ExoPlayer's loading thread). Interrupting the thread abandons the wait. */
    fun <T> withPermitBlocking(priority: Priority, kind: String, codeOf: (Throwable) -> Int = { 0 }, block: () -> T): T =
        runBlocking { withPermit(priority, kind, codeOf) { block() } }

    fun stats(): GateStats = synchronized(lock) {
        GateStats(queue.size, inFlight, peak, limit, breakerStateLocked(), total.get(), retries.get(), coalescedN.get(), rejected.get(), failures.get(),
            avgMs.toLong(), kinds.mapValues { it.value.get() })
    }

    fun resetPeak() = synchronized(lock) { peak = inFlight }

    /** Current simultaneous-request limit (changes with AIMD). */
    val currentLimit: Int get() = synchronized(lock) { limit }

    fun backoffMs(attempt: Int): Long {
        val ceil = (cfg.backoffBaseMs shl attempt.coerceIn(0, 20)).coerceAtMost(cfg.backoffMaxMs)
        return (ceil * (0.5 + 0.5 * random.nextDouble())).toLong().coerceAtLeast(1)
    }

    // ---- core ---------------------------------------------------------------------------------------------------

    private fun count(kind: String) { total.incrementAndGet(); kinds.getOrPut(kind) { AtomicInteger() }.incrementAndGet() }

    private suspend fun run(req: GateRequest, box: PrioBox): GateResponse {
        count(req.kind)
        var attempt = 0
        while (true) {
            val probe = breakerCheck(box.priority)
            val w = try { acquire(box) } catch (e: Throwable) { if (probe) synchronized(lock) { probing = false }; throw e }
            var resp: GateResponse? = null; var err: IOException? = null
            val t0 = now()
            var cancelled = false
            try {
                resp = transport(req)
            } catch (e: CancellationException) { cancelled = true; throw e }
            catch (e: IOException) { err = e }
            finally {
                release(w)
                if (cancelled && probe) synchronized(lock) { probing = false }
            }
            val dt = now() - t0
            val code = resp?.code ?: 0
            outcome(code, dt, req.heavy, resp?.retryAfterMs, probe)
            val retryable = req.method == "GET" && (err != null || code == 429 || code in 500..599)
            if (!retryable || attempt >= cfg.maxRetries) {
                if (err != null) throw err
                return resp!!
            }
            retries.incrementAndGet()
            val ra = resp?.retryAfterMs
            sleep(if (ra != null) ra.coerceAtMost(cfg.retryAfterMaxMs) else backoffMs(attempt))
            attempt++
        }
    }

    private suspend fun coalesced(req: GateRequest, priority: Priority): GateResponse {
        val key = req.url + "\n" + (req.headers["Authorization"] ?: "")
        val sh: Shared
        synchronized(lock) {
            val e = shared[key]?.takeIf { !it.job.isCompleted }
            if (e != null) {
                e.refs++; coalescedN.incrementAndGet(); promoteLocked(e.box, priority); sh = e
            } else {
                val box = PrioBox(priority)
                lateinit var self: Shared
                val job = scope.async(start = CoroutineStart.LAZY) { run(req, box) }
                self = Shared(job, box); self.refs = 1; shared[key] = self; sh = self
            }
        }
        sh.job.start()
        try { return sh.job.await() }
        finally {
            synchronized(lock) {
                sh.refs--
                if (sh.refs == 0) {
                    if (shared[key] === sh) shared.remove(key)
                    if (!sh.job.isCompleted) sh.job.cancel()
                }
            }
        }
    }

    private fun promoteLocked(box: PrioBox, p: Priority) {
        if (p.ordinal >= box.priority.ordinal) return
        box.priority = p
        val w = box.waiter
        if (w != null && !w.admitted && queue.remove(w)) { w.priority = p; queue.add(w); pumpLocked() }
    }

    // ---- slots --------------------------------------------------------------------------------------------------

    private suspend fun acquire(box: PrioBox): Waiter {
        val w: Waiter
        synchronized(lock) {
            w = Waiter(box.priority, ++seq); box.waiter = w
            queue.add(w); pumpLocked()
        }
        try {
            if (w.priority == Priority.BACKGROUND || box.priority == Priority.BACKGROUND) {
                while (!w.granted.isCompleted) {
                    if (withTimeoutOrNull(cfg.backgroundPollMs) { w.granted.await() } == null) synchronized(lock) { pumpLocked() }
                }
                w.granted.await()
            } else w.granted.await()
            return w
        } catch (e: Throwable) {
            synchronized(lock) { if (w.admitted) releaseLocked(w) else queue.remove(w) }
            throw e
        } finally { box.waiter = null }
    }

    private fun backgroundOkLocked(): Boolean {
        val t = now()
        if (t < holdUntil) return false
        if (busy()) return false
        if (avgMs > cfg.slowServerMs && t - lastSampleAt < 10_000) return false
        return inFlightBg < maxOf(1, limit / 2)
    }

    private fun pumpLocked() {
        while (queue.isNotEmpty()) {
            val w = queue.first()
            val cap = if (w.priority == Priority.PLAYING) limit + cfg.playingReserve else limit
            if (inFlight >= cap) break
            if (w.priority == Priority.BACKGROUND && !backgroundOkLocked()) break
            queue.pollFirst()
            w.admitted = true; inFlight++
            if (w.priority == Priority.BACKGROUND) { inFlightBg++; w.wasBackground = true }
            if (inFlight > peak) peak = inFlight
            w.granted.complete(Unit)
        }
    }

    private fun release(w: Waiter) = synchronized(lock) { releaseLocked(w) }
    private fun releaseLocked(w: Waiter) {
        inFlight--; if (w.wasBackground) inFlightBg--
        pumpLocked()
    }

    // ---- breaker + AIMD -----------------------------------------------------------------------------------------

    private fun breakerStateLocked(): BreakerState =
        if (breaker == BreakerState.OPEN && now() >= openUntil) BreakerState.HALF_OPEN else breaker

    /** Throws when the breaker is open; returns true when this request is the half-open probe. */
    private fun breakerCheck(priority: Priority): Boolean = synchronized(lock) {
        when (breakerStateLocked()) {
            BreakerState.CLOSED -> false
            BreakerState.OPEN -> if (priority == Priority.PLAYING) false else { rejected.incrementAndGet(); throw GateOpenException() }
            BreakerState.HALF_OPEN -> {
                breaker = BreakerState.HALF_OPEN
                if (!probing) { probing = true; true }
                else if (priority == Priority.PLAYING) false
                else { rejected.incrementAndGet(); throw GateOpenException() }
            }
        }
    }

    private fun outcome(code: Int, ms: Long, heavy: Boolean, retryAfter: Long?, probe: Boolean) = synchronized(lock) {
        val t = now()
        val failed = code == 0 || code in 500..599
        val throttled = code == 429
        if (probe) probing = false
        if (failed || throttled) {
            failures.incrementAndGet()
            limit = maxOf(cfg.minLimit, limit / 2); fastRun = 0
            if (throttled) retryAfter?.let { holdUntil = maxOf(holdUntil, t + it.coerceAtMost(cfg.retryAfterMaxMs)) }
            if (failed) {
                consecutiveFailures++
                if (breakerStateLocked() == BreakerState.HALF_OPEN) {
                    cooldown = (cooldown * 2).coerceAtMost(cfg.breakerMaxCooldownMs); breaker = BreakerState.OPEN; openUntil = t + cooldown
                } else if (breaker == BreakerState.CLOSED && consecutiveFailures >= cfg.breakerFailures) {
                    breaker = BreakerState.OPEN; openUntil = t + cooldown
                }
            }
        } else {
            consecutiveFailures = 0
            if (breaker != BreakerState.CLOSED && breakerStateLocked() == BreakerState.HALF_OPEN) { breaker = BreakerState.CLOSED; cooldown = cfg.breakerCooldownMs }
            if (!heavy) { avgMs = if (lastSampleAt == 0L || avgMs == 0.0) ms.toDouble() else avgMs * 0.8 + ms * 0.2; lastSampleAt = t }
            if (ms > (if (heavy) cfg.heavySlowMs else cfg.slowMs)) { limit = maxOf(cfg.minLimit, limit / 2); fastRun = 0 }
            else if (ms <= cfg.fastMs || heavy) {
                if (++fastRun >= cfg.growAfter) { fastRun = 0; if (limit < cfg.maxLimit) limit++ }
            } else fastRun = 0
        }
        pumpLocked()
    }
}
