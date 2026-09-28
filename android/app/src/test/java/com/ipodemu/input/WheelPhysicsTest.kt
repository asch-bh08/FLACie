package com.ipodemu.input

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import kotlin.math.cos
import kotlin.math.sin

class WheelPhysicsTest {
    private fun wheel() = WheelPhysics(
        WheelPhysics.Config(degPerDetent = 18f, accelMinDps = 120f, accelMaxDps = 900f, maxGain = 4f)
    ).also { it.setGeometry(0f, 0f, 100f) }

    private fun defaultWheel() = WheelPhysics().also { it.setGeometry(0f, 0f, 100f) }

    /** Sweep [deg] degrees over [ms] at 60Hz starting at [startDeg] on radius 80; returns total detents. */
    private fun sweep(w: WheelPhysics, startDeg: Float, deg: Float, ms: Int, r: Float = 80f): Int {
        fun px(a: Float) = r * cos(Math.toRadians(a.toDouble())).toFloat()
        fun py(a: Float) = r * sin(Math.toRadians(a.toDouble())).toFloat()
        w.down(px(startDeg), py(startDeg), 0)
        var total = 0
        val steps = (ms / 16).coerceAtLeast(1)
        for (i in 1..steps) {
            val a = startDeg + deg * i / steps
            total += w.move(px(a), py(a), (i * 16).toLong())
        }
        return total
    }

    @Test fun slowClockwiseIsOneToOne() = assertTrue(sweep(wheel(), -90f, 90f, 3000) in 4..5)
    @Test fun slowCounterClockwiseIsNegative() = assertTrue(sweep(wheel(), -90f, -90f, 3000) in -5..-4)
    @Test fun fastSpinAccelerates() {
        val fast = sweep(wheel(), -90f, 360f, 300)
        assertTrue("fast=$fast", fast > 22) // 360 degrees at 18 degrees/step is 20 steps without acceleration
    }
    @Test fun crossingSeamDoesNotJump() = assertTrue(sweep(wheel(), 150f, 90f, 3000) in 4..5)
    @Test fun tapCentreIsCentre() {
        val w = wheel()
        w.down(5f, 5f, 0)
        assertEquals(Zone.CENTER, w.up(5f, 5f, 100))
    }
    @Test fun tapRingZones() {
        fun tap(x: Float, y: Float): Zone? { val w = wheel(); w.down(x, y, 0); return w.up(x, y, 80) }
        assertEquals(Zone.MENU, tap(0f, -80f))
        assertEquals(Zone.PLAY_PAUSE, tap(0f, 80f))
        assertEquals(Zone.PREV, tap(-80f, 0f))
        assertEquals(Zone.NEXT, tap(80f, 0f))
    }
    @Test fun rotationIsNotATap() {
        val w = wheel(); sweep(w, -90f, 60f, 400)
        assertNull(w.up(0f, 0f, 500))
    }
    @Test fun defaultsAreCalm() {
        val n = sweep(defaultWheel(), -90f, 360f, 4000)
        assertTrue("one slow revolution = $n rows", n in 9..11)
    }
    @Test fun wobbleIsStillATap() {
        val w = wheel()
        w.down(0f, -80f, 0)
        // 8 degrees of finger roll: past the slop, but under one 18-degree detent
        val a = Math.toRadians(-90.0 + 9.0)
        w.move((80 * cos(a)).toFloat(), (80 * sin(a)).toFloat(), 60)
        assertEquals(Zone.MENU, w.up((80 * cos(a)).toFloat(), (80 * sin(a)).toFloat(), 120))
    }
    @Test fun crossingTheHubDoesNotJump() {
        val w = wheel()
        w.down(80f, 0f, 0)
        var total = w.move(80f, 12f, 30) // start rotating clockwise
        total += w.move(10f, 5f, 60)      // into the hub (ignored)
        total += w.move(-80f, -12f, 90)   // out the other side: resync, no jump
        total += w.move(-80f, -10f, 120)
        assertTrue("jump=$total", kotlin.math.abs(total) <= 2)
    }
    @Test fun outsideWheelIgnored() = assertTrue(!wheel().down(150f, 0f, 0))
    @Test fun reversalDropsRemainder() {
        val w = wheel()
        // 10deg fwd (0 detents at 18), then 10 deg back: must not emit
        assertEquals(0, sweep(w, 0f, 10f, 2000))
        assertEquals(0, w.move(80f * cos(0.0).toFloat(), 0f, 3000))
    }
}

class WheelSmoothnessTest {
    /** Constant-speed rotation with jittery event timing must give an even step rate, not bursts. */
    @Test fun jitteryTimestampsDoNotCauseBursts() {
        val w = WheelPhysics(WheelPhysics.Config(degPerDetent = 20f)).also { it.setGeometry(0f, 0f, 100f) }
        val r = 80f
        var t = 0L
        var ang = -90.0
        w.down((r * cos(Math.toRadians(ang))).toFloat(), (r * sin(Math.toRadians(ang))).toFloat(), t)
        val perSecond = IntArray(3)
        val rnd = java.util.Random(7)
        // 200 deg/s for 3 s, but event spacing wobbles between 1 ms and 40 ms (batched input)
        while (t < 3000) {
            val dt = if (rnd.nextInt(4) == 0) 1L else 8L + rnd.nextInt(33)
            t += dt; ang += 200.0 * dt / 1000.0
            val n = w.move((r * cos(Math.toRadians(ang))).toFloat(), (r * sin(Math.toRadians(ang))).toFloat(), t)
            perSecond[(t.coerceAtMost(2999) / 1000).toInt()] += n
        }
        // ideal is 10 steps per second at gain 1; accept modest acceleration but no wild swings
        for (s in perSecond) assertTrue("steps in a second: ${perSecond.toList()}", s in 8..16)
    }
}
