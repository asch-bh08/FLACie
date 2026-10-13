package com.ipodemu.input

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class WheelInertiaTest {
    private fun coast(w: WheelInertia, fromT: Long = 0, frameMs: Long = 16, limitMs: Long = 4000): Int {
        var total = 0; var t = fromT
        while (w.active && t - fromT < limitMs) { t += frameMs; total += w.step(t) }
        return total
    }

    @Test fun aSlowLiftDoesNotCoast() {
        val w = WheelInertia()
        assertFalse(w.start(120f, 0)); assertFalse(w.active); assertEquals(0, w.step(16))
    }

    @Test fun aFlickCoastsInItsDirectionAndStops() {
        val cw = WheelInertia(); assertTrue(cw.start(700f, 0))
        val n = coast(cw)
        assertTrue("clockwise flick scrolls forward ($n)", n in 4..20); assertFalse(cw.active)
        val ccw = WheelInertia(); ccw.start(-700f, 0)
        assertEquals(-n, coast(ccw))
    }

    @Test fun aHarderFlickGoesFurther() {
        val a = WheelInertia(); a.start(400f, 0); val slow = coast(a)
        val b = WheelInertia(); b.start(1200f, 0); val fast = coast(b)
        assertTrue("$slow < $fast", slow < fast)
    }

    @Test fun neverMoreThanTheCapInOneFrameOrInAll() {
        val w = WheelInertia(); w.start(100_000f, 0)
        var t = 0L; var total = 0
        while (w.active && t < 10_000) { t += 16; val n = w.step(t); assertTrue(Math.abs(n) <= 2); total += n }
        assertTrue(total <= WheelInertia.MAX_TOTAL)
    }

    @Test fun aTouchStopsItAtOnce() {
        val w = WheelInertia(); w.start(900f, 0); w.step(16); w.stop()
        assertFalse(w.active); assertEquals(0, w.step(32))
    }

    @Test fun aLongPauseBetweenFramesDoesNotJump() {
        val w = WheelInertia(); w.start(900f, 0)
        val n = w.step(5_000)   // the app was paused for 5 s: only one frame's worth of time counts
        assertTrue(Math.abs(n) <= 2)
    }
}
