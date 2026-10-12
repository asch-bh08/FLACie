package com.ipodemu.library

import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.advanceUntilIdle
import kotlinx.coroutines.test.currentTime
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test
import java.io.IOException
import kotlin.random.Random

@OptIn(ExperimentalCoroutinesApi::class)
class JellyfinGateTest {
    private fun req(url: String = "http://x/a", kind: String = "t", method: String = "GET") = GateRequest(method, url, kind = kind)
    private fun resp(code: Int = 200, vararg h: Pair<String, String>) = GateResponse(code, "ok".toByteArray(), h.toMap())

    private class Rig(val ts: TestScope, cfg: GateConfig, var busy: Boolean = false, transport: suspend (GateRequest) -> GateResponse) {
        val gate = JellyfinGate(cfg, transport, CoroutineScope(SupervisorJob() + kotlinx.coroutines.test.StandardTestDispatcher(ts.testScheduler)),
            now = { ts.testScheduler.currentTime }, sleep = { delay(it) }, random = Random(1), busy = { busy })
    }

    private fun TestScope.rig(cfg: GateConfig = GateConfig(), busy: Boolean = false, transport: suspend (GateRequest) -> GateResponse) = Rig(this, cfg, busy, transport)

    private val one = GateConfig(initialLimit = 1, minLimit = 1, maxLimit = 1, playingReserve = 0, visibleFloor = 1)   // one at a time: the tests of ordering and cancelling need a single slot (the production floor for visible rows is 4)

    @Test fun concurrencyLimitIsRespected() = runTest {
        var cur = 0; var max = 0
        val r = rig(GateConfig(initialLimit = 4, minLimit = 4, maxLimit = 4, playingReserve = 0)) {
            cur++; max = maxOf(max, cur); delay(100); cur--; resp()
        }
        (1..30).map { async { r.gate.execute(req("http://x/$it"), Priority.VISIBLE) } }.awaitAll()
        assertEquals(4, max)
        assertEquals(4, r.gate.stats().peak)
        assertEquals(0, r.gate.stats().inFlight)
        assertEquals(30L, r.gate.stats().total)
    }

    @Test fun shrinksOnRateLimitAndServerErrorsAndGrowsAfterFastRun() = runTest {
        var code = 200
        val r = rig(GateConfig(initialLimit = 6, maxRetries = 0, breakerFailures = 100)) { resp(code) }
        r.gate.execute(req("http://x/1"), Priority.VISIBLE)
        assertEquals(6, r.gate.currentLimit)
        code = 429; r.gate.execute(req("http://x/2"), Priority.VISIBLE)
        assertEquals(3, r.gate.currentLimit)
        code = 503; r.gate.execute(req("http://x/3"), Priority.VISIBLE)
        assertEquals(1, r.gate.currentLimit)
        code = 503; r.gate.execute(req("http://x/4"), Priority.VISIBLE)
        assertEquals("never below the minimum", 1, r.gate.currentLimit)
        code = 200
        repeat(15) { r.gate.execute(req("http://x/ok$it"), Priority.VISIBLE) }
        assertEquals("one more after a run of fast answers", 2, r.gate.currentLimit)
        repeat(15 * 20) { r.gate.execute(req("http://x/more$it"), Priority.VISIBLE) }
        assertEquals("never above the maximum", 8, r.gate.currentLimit)
    }

    @Test fun slowAnswerShrinksTheLimit() = runTest {
        val r = rig(GateConfig(initialLimit = 6)) { delay(3000); resp() }
        r.gate.execute(req(), Priority.VISIBLE)
        assertEquals(3, r.gate.currentLimit)
    }

    @Test fun heavyRequestsAreJudgedByTheLongerLimit() = runTest {
        val r = rig(GateConfig(initialLimit = 6)) { delay(8000); resp() }
        r.gate.execute(GateRequest(url = "http://x/page", heavy = true), Priority.BACKGROUND)
        assertEquals(6, r.gate.currentLimit)
    }

    @Test fun backoffIsJitteredExponentialAndBounded() = runTest {
        val r = rig { resp() }
        for (attempt in 0..12) {
            repeat(50) {
                val ms = r.gate.backoffMs(attempt)
                val ceil = (400L shl attempt).coerceAtMost(15_000)
                assertTrue("attempt $attempt gave $ms", ms >= ceil / 2 - 1 && ms <= ceil)
            }
        }
        val seen = (1..50).map { r.gate.backoffMs(3) }.toSet()
        assertTrue("jitter should vary", seen.size > 5)
    }

    @Test fun retryAfterIsHonouredAndAtMostTwoRetries() = runTest {
        var calls = 0
        val r = rig(GateConfig(breakerFailures = 100)) { calls++; resp(429, "retry-after" to "3") }
        val t0 = currentTime
        val out = r.gate.execute(req(), Priority.VISIBLE)
        assertEquals(429, out.code)
        assertEquals("1 try + 2 retries, never more", 3, calls)
        assertEquals("waited the server's 3 s twice", 6000L, currentTime - t0)
        assertEquals(2L, r.gate.stats().retries)
    }

    @Test fun networkErrorsAreRetriedTwiceThenThrown() = runTest {
        var calls = 0
        val r = rig(GateConfig(breakerFailures = 100)) { calls++; throw IOException("down") }
        try { r.gate.execute(req(), Priority.VISIBLE); fail() } catch (e: IOException) { assertEquals("down", e.message) }
        assertEquals(3, calls)
    }

    @Test fun postsAreNeverRetried() = runTest {
        var calls = 0
        val r = rig { calls++; resp(503) }
        assertEquals(503, r.gate.execute(req(method = "POST"), Priority.VISIBLE).code)
        assertEquals(1, calls)
    }

    @Test fun clientErrorsAreNotRetried() = runTest {
        var calls = 0
        val r = rig { calls++; resp(404) }
        assertEquals(404, r.gate.execute(req(), Priority.VISIBLE).code)
        assertEquals(1, calls)
        assertEquals(6, r.gate.currentLimit.coerceAtLeast(6))
    }

    @Test fun circuitBreakerOpensHalfOpensAndCloses() = runTest {
        var failing = true; var calls = 0
        val r = rig(GateConfig(maxRetries = 0, breakerFailures = 3, breakerCooldownMs = 10_000)) { calls++; if (failing) resp(503) else resp() }
        repeat(3) { r.gate.execute(req("http://x/$it"), Priority.VISIBLE) }
        assertEquals(BreakerState.OPEN, r.gate.stats().breaker)
        val before = calls
        try { r.gate.execute(req("http://x/fast"), Priority.VISIBLE); fail("should fail fast") } catch (e: GateOpenException) { }
        assertEquals("open means no request reaches the server", before, calls)
        assertEquals(1L, r.gate.stats().rejected)
        advanceTimeBy(10_001)
        assertEquals(BreakerState.HALF_OPEN, r.gate.stats().breaker)
        // the probe fails: open again, with a longer cool-down
        r.gate.execute(req("http://x/probe1"), Priority.VISIBLE)
        assertEquals(BreakerState.OPEN, r.gate.stats().breaker)
        advanceTimeBy(10_001)
        assertEquals("cool-down doubled", BreakerState.OPEN, r.gate.stats().breaker)
        advanceTimeBy(10_000)
        assertEquals(BreakerState.HALF_OPEN, r.gate.stats().breaker)
        failing = false
        assertEquals(200, r.gate.execute(req("http://x/probe2"), Priority.VISIBLE).code)
        assertEquals(BreakerState.CLOSED, r.gate.stats().breaker)
    }

    @Test fun playingWorkStillGetsThroughAnOpenBreakerAsAProbe() = runTest {
        var failing = true
        val r = rig(GateConfig(maxRetries = 0, breakerFailures = 2)) { if (failing) resp(503) else resp() }
        repeat(2) { r.gate.execute(req("http://x/$it"), Priority.VISIBLE) }
        assertEquals(BreakerState.OPEN, r.gate.stats().breaker)
        failing = false
        assertEquals(200, r.gate.execute(req("http://x/song"), Priority.PLAYING).code)
    }

    @Test fun cancellingWhileQueuedRemovesTheEntry() = runTest {
        val hold = CompletableDeferred<Unit>(); var calls = 0
        val r = rig(one) { calls++; hold.await(); resp() }
        val a = launch { r.gate.execute(req("http://x/1"), Priority.VISIBLE) }
        runCurrent()
        val b = launch { r.gate.execute(req("http://x/2"), Priority.VISIBLE) }
        runCurrent()
        assertEquals(1, r.gate.stats().queued)
        b.cancelAndJoin()
        assertEquals(0, r.gate.stats().queued)
        hold.complete(Unit); a.join()
        assertEquals("the cancelled request never reached the server", 1, calls)
        assertEquals(0, r.gate.stats().inFlight)
    }

    @Test fun cancellingInFlightCancelsTheCallAndFreesTheSlot() = runTest {
        var cancelled = false; var started = 0
        val r = rig(one) { started++; try { delay(100_000); resp() } catch (e: kotlinx.coroutines.CancellationException) { cancelled = true; throw e } }
        val a = launch { r.gate.execute(req("http://x/1"), Priority.VISIBLE) }
        runCurrent()
        assertEquals(1, r.gate.stats().inFlight)
        a.cancelAndJoin()
        assertTrue(cancelled)
        assertEquals(0, r.gate.stats().inFlight)
        // the freed slot serves the next one
        val b = async { r.gate.execute(req("http://x/2"), Priority.VISIBLE) }
        runCurrent()
        assertEquals(2, started)
        b.cancelAndJoin()
    }

    @Test fun identicalInFlightGetsAreMadeOnce() = runTest {
        var calls = 0
        val r = rig { calls++; delay(50); resp() }
        val all = (1..5).map { async { r.gate.execute(req("http://x/cover"), Priority.VISIBLE) } } + async { r.gate.execute(req("http://x/other"), Priority.VISIBLE) }
        all.awaitAll()
        assertEquals(2, calls)
        assertEquals(4L, r.gate.stats().coalesced)
        // afterwards the same URL is requested again (nothing is cached by the gate)
        r.gate.execute(req("http://x/cover"), Priority.VISIBLE)
        assertEquals(3, calls)
    }

    @Test fun coalescedRequestSurvivesOneCancellationAndStopsWhenAllCancel() = runTest {
        var cancelled = false; var calls = 0
        val r = rig { calls++; try { delay(1000); resp() } catch (e: kotlinx.coroutines.CancellationException) { cancelled = true; throw e } }
        val a = async { r.gate.execute(req("http://x/c"), Priority.VISIBLE) }
        val b = async { r.gate.execute(req("http://x/c"), Priority.VISIBLE) }
        runCurrent()
        a.cancelAndJoin()
        assertFalse("another caller still wants it", cancelled)
        assertEquals(200, b.await().code)
        assertEquals(1, calls)
        val c = async { r.gate.execute(req("http://x/d"), Priority.VISIBLE) }
        val d = async { r.gate.execute(req("http://x/d"), Priority.VISIBLE) }
        runCurrent()
        c.cancelAndJoin(); d.cancelAndJoin()
        runCurrent()
        assertTrue("nobody wants it any more: the HTTP call is cancelled", cancelled)
        assertEquals(0, r.gate.stats().inFlight)
    }

    @Test fun priorityOrderPlayingThenVisibleThenBackground() = runTest {
        val hold = CompletableDeferred<Unit>(); val order = ArrayList<String>()
        val r = rig(one) { order += it.url.substringAfterLast('/'); if (it.url.endsWith("/first")) hold.await(); resp() }
        val f = launch { r.gate.execute(req("http://x/first"), Priority.VISIBLE) }
        runCurrent()
        launch { r.gate.execute(req("http://x/bg"), Priority.BACKGROUND) }
        launch { r.gate.execute(req("http://x/vis"), Priority.VISIBLE) }
        launch { r.gate.execute(req("http://x/play"), Priority.PLAYING) }
        runCurrent()
        hold.complete(Unit); f.join()
        advanceUntilIdle()
        assertEquals(listOf("first", "play", "vis", "bg"), order)
    }

    @Test fun backgroundWaitsWhileTheScreenIsTouchedAndWhileHigherWorkIsQueued() = runTest {
        var done = 0
        val r = rig(GateConfig(initialLimit = 4), busy = true) { done++; resp() }
        val bg = launch { r.gate.execute(req("http://x/bg"), Priority.BACKGROUND) }
        advanceTimeBy(5000)
        assertEquals("touch holds background back", 0, done)
        r.busy = false
        advanceTimeBy(600); runCurrent()
        assertEquals(1, done)
        bg.join()
    }

    @Test fun backgroundHoldsBackWhileTheServerIsSlow() = runTest {
        var latency = 2000L; var bgRan = false
        val r = rig(GateConfig(initialLimit = 4, slowMs = 100_000)) { if (it.kind == "bg") bgRan = true; delay(latency); resp() }
        r.gate.execute(req("http://x/a"), Priority.VISIBLE)   // average is now 2 s: slow server
        val bg = launch { r.gate.execute(GateRequest(url = "http://x/bg", kind = "bg"), Priority.BACKGROUND) }
        advanceTimeBy(5000)
        assertFalse(bgRan)
        latency = 50
        advanceTimeBy(6000)   // no samples for 10 s: it may probe again
        bg.join()
        assertTrue(bgRan)
    }

    @Test fun playingMayUseTheReserveWhenTheLimitIsFull() = runTest {
        val hold = CompletableDeferred<Unit>(); var started = 0
        val r = rig(GateConfig(initialLimit = 1, minLimit = 1, maxLimit = 1, playingReserve = 2)) { started++; hold.await(); resp() }
        launch { r.gate.execute(req("http://x/1"), Priority.VISIBLE) }
        launch { r.gate.execute(req("http://x/2"), Priority.VISIBLE) }
        launch { r.gate.execute(req("http://x/p1"), Priority.PLAYING) }
        launch { r.gate.execute(req("http://x/p2"), Priority.PLAYING) }
        launch { r.gate.execute(req("http://x/p3"), Priority.PLAYING) }
        runCurrent()
        assertEquals("1 normal + 2 reserved", 3, started)
        hold.complete(Unit)
        advanceUntilIdle()
        assertEquals(5, started)
    }

    @Test fun joiningAWaitingBackgroundRequestAsVisiblePromotesIt() = runTest {
        val hold = CompletableDeferred<Unit>(); val order = ArrayList<String>()
        val r = rig(one) { order += it.url.substringAfterLast('/'); if (it.url.endsWith("/first")) hold.await(); resp() }
        launch { r.gate.execute(req("http://x/first"), Priority.VISIBLE) }
        runCurrent()
        launch { r.gate.execute(req("http://x/shared"), Priority.BACKGROUND) }
        launch { r.gate.execute(req("http://x/mid"), Priority.VISIBLE) }
        runCurrent()
        launch { r.gate.execute(req("http://x/shared"), Priority.PLAYING) }
        runCurrent()
        hold.complete(Unit)
        advanceUntilIdle()
        assertEquals(listOf("first", "shared", "mid"), order)
    }

    @Test fun withPermitCountsAndReleases() = runTest {
        val r = rig(one) { resp() }
        val x = r.gate.withPermit(Priority.PLAYING, "stream") { 42 }
        assertEquals(42, x)
        assertEquals(0, r.gate.stats().inFlight)
        assertEquals(1, r.gate.stats().perKind["stream"])
        try { r.gate.withPermit(Priority.PLAYING, "stream") { throw IOException("x") }; fail() } catch (e: IOException) { }
        assertEquals(0, r.gate.stats().inFlight)
        assertEquals(1L, r.gate.stats().failures)
    }

    @Test fun countersPerKind() = runTest {
        val r = rig { resp() }
        r.gate.execute(req("http://x/1", kind = "cover"), Priority.VISIBLE)
        r.gate.execute(req("http://x/2", kind = "cover"), Priority.VISIBLE)
        r.gate.execute(req("http://x/3", kind = "list"), Priority.VISIBLE)
        val s = r.gate.stats()
        assertEquals(2, s.perKind["cover"]); assertEquals(1, s.perKind["list"])
        assertTrue(s.line().contains("cover:2"))
    }
}
