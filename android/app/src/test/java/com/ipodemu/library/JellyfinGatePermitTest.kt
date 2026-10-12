package com.ipodemu.library

import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.IOException
import java.util.concurrent.atomic.AtomicInteger

/** The stream opens of the player share the gate's slots: they are given back however an open ends (done, failed, cancelled, interrupted). */
class JellyfinGatePermitTest {
    private val one = GateConfig(initialLimit = 1, minLimit = 1, maxLimit = 1, playingReserve = 0)
    private fun gate(cfg: GateConfig = one) = JellyfinGate(cfg, { GateResponse(200, ByteArray(0), emptyMap()) }, CoroutineScope(SupervisorJob() + Dispatchers.Default))

    private suspend fun until(what: String, ms: Long = 3000, cond: () -> Boolean) {
        val t0 = System.currentTimeMillis()
        while (!cond()) { if (System.currentTimeMillis() - t0 > ms) throw AssertionError("timed out waiting for $what"); delay(5) }
    }

    @Test fun aCancelledOpenGivesItsSlotBack() = runBlocking {
        val g = gate()
        val hold = CompletableDeferred<Unit>()
        val a = launch(Dispatchers.Default) { g.withPermit(Priority.PLAYING, "stream") { hold.await() } }
        until("the open to start") { g.stats().inFlight == 1 }
        a.cancelAndJoin()
        assertEquals(0, g.stats().inFlight)
        assertEquals("the next open is served at once", 7, withTimeout(2000) { g.withPermit(Priority.PLAYING, "stream") { 7 } })
    }

    @Test fun aSkippedSongsQueuedOpenLeavesTheQueueWithoutTakingASlot() = runBlocking {
        val g = gate()
        val hold = CompletableDeferred<Unit>()
        val first = launch(Dispatchers.Default) { g.withPermit(Priority.PLAYING, "stream") { hold.await() } }
        until("first open") { g.stats().inFlight == 1 }
        val waiting = (1..5).map { launch(Dispatchers.Default) { g.withPermit(Priority.PLAYING, "stream") { delay(10_000) } } }
        until("five queued") { g.stats().queued == 5 }
        waiting.forEach { it.cancelAndJoin() }   // five skips: five abandoned opens
        assertEquals(0, g.stats().queued)
        assertEquals(1, g.stats().inFlight)
        hold.complete(Unit); first.join()
        assertEquals(0, g.stats().inFlight)
        assertEquals(0, g.stats().queued)
    }

    @Test fun aBurstOfOpensNeverExceedsTheLimitAndAllSlotsComeBack() = runBlocking {
        val g = gate(GateConfig(initialLimit = 2, minLimit = 2, maxLimit = 2, playingReserve = 0))
        val cur = AtomicInteger(); var max = 0
        val jobs = (1..40).map {
            async(Dispatchers.Default) {
                try {
                    g.withPermit(Priority.PLAYING, "stream") {
                        val c = cur.incrementAndGet(); synchronized(this@JellyfinGatePermitTest) { if (c > max) max = c }
                        delay(2); cur.decrementAndGet()
                        if (it % 7 == 0) throw IOException("HTTP 503")
                    }
                } catch (_: IOException) { }
            }
        }
        jobs.awaitAll()
        assertTrue("never more than 2 at once, was $max", max <= 2)
        assertEquals(0, g.stats().inFlight); assertEquals(0, g.stats().queued)
        assertEquals(40L, g.stats().perKind["stream"]!!.toLong())
    }

    @Test fun anInterruptedPlayerThreadLeavesWithoutLeakingAPermit() {
        val g = gate()
        val hold = CompletableDeferred<Unit>()
        val holder = CoroutineScope(Dispatchers.Default).launch { g.withPermit(Priority.PLAYING, "stream") { hold.await() } }
        runBlocking { until("holder to run") { g.stats().inFlight == 1 } }
        var thrown: Throwable? = null
        // ExoPlayer's loading thread: blocking, and interrupted when the player abandons the load (a skip)
        val t = Thread { try { g.withPermitBlocking(Priority.PLAYING, "stream") { 1 } } catch (e: Throwable) { thrown = e } }
        t.start()
        runBlocking { until("the thread to queue") { g.stats().queued == 1 } }
        t.interrupt(); t.join(3000)
        assertNotNull("the interrupted open ended with an exception", thrown)
        assertEquals(0, g.stats().queued)
        hold.complete(Unit)
        runBlocking { holder.join() }
        assertEquals(0, g.stats().inFlight)
        assertEquals(5, g.withPermitBlocking(Priority.PLAYING, "stream") { 5 })
    }

    @Test fun reconnectWaitsGrowAndAreCapped() {
        // 2, 4, 8, 16, 32, 60, 60 s (the top of the jitter range)
        val tops = (0..6).map { reconnectDelay(it, 0.0) }
        assertEquals(listOf(2_000L, 4_000L, 8_000L, 16_000L, 32_000L, 60_000L, 60_000L), tops)
        // jitter shortens by up to half
        assertEquals(1_000L, reconnectDelay(0, 1.0))
        assertEquals(30_000L, reconnectDelay(9, 1.0))
        assertTrue((0..40).all { reconnectDelay(it, 0.37) in 1_000L..60_000L })
    }
}
