package com.ipodemu.input

import kotlin.math.abs
import kotlin.math.atan2
import kotlin.math.hypot
import kotlin.math.roundToInt

enum class Zone { CENTER, MENU, NEXT, PLAY_PAUSE, PREV }

/** Pure-Kotlin click wheel model. Feed touch samples in; get detents and taps out. */
class WheelPhysics(var config: Config = Config()) {
    data class Config(
        val degPerDetent: Float = 34f,
        val slopDeg: Float = 8f,
        val centerFrac: Float = 0.38f,
        val minRadiusFrac: Float = 0.3f,
        val accelMinDps: Float = 220f,
        val accelMaxDps: Float = 900f,
        val maxGain: Float = 2f,
        /** Angular speed is measured over this trailing window, so uneven touch-event timing cannot spike it. */
        val velocityWindowMs: Long = 140,
        /** Time constant of the speed smoothing. */
        val velocityTauMs: Float = 110f,
        /** How fast the acceleration multiplier may rise / fall (multiplier units per second). */
        val gainRiseRate: Float = 4f,
        val gainFallRate: Float = 7f,
        /** Hard cap on list steps produced by one touch sample. */
        val maxStepsPerSample: Int = 3,
        val idleResetMs: Long = 220,
        val tapMaxMs: Long = 450,
        val centerSlopFrac: Float = 0.3f,
    )

    private var cx = 0f
    private var cy = 0f
    private var radius = 1f

    private var active = false
    private var zone: Zone? = null
    private var rotating = false
    private var startX = 0f
    private var startY = 0f
    private var downT = 0L
    private var lastT = 0L
    private var lastAngle = 0.0
    private var pendingDeg = 0f
    private var acc = 0f
    private var omega = 0f
    private var gainState = 1f
    private var lastVelT = 0L
    private var unwrapped = 0.0
    private val histT = LongArray(24)
    private val histA = DoubleArray(24)
    private var histN = 0
    private var movedFar = false
    private var emitted = 0
    private var inCenter = false

    /** Smoothed signed angular speed, deg/s (positive = clockwise). */
    val velocityDps: Float get() = omega
    val isRotating: Boolean get() = rotating
    /** Zone under the finger while the touch has not yet become a rotation. */
    val pendingZone: Zone? get() = if (active && !rotating && !movedFar) zone else null

    fun setGeometry(cx: Float, cy: Float, radius: Float) {
        this.cx = cx; this.cy = cy; this.radius = radius
    }

    fun contains(x: Float, y: Float) = hypot(x - cx, y - cy) <= radius

    fun down(x: Float, y: Float, t: Long): Boolean {
        val r = hypot(x - cx, y - cy)
        if (r > radius) return false
        active = true; rotating = false; movedFar = false
        startX = x; startY = y; downT = t; lastT = t
        acc = 0f; omega = 0f; pendingDeg = 0f; emitted = 0; inCenter = false
        gainState = 1f; lastVelT = t; unwrapped = 0.0; histN = 0; pushHist(t, 0.0)
        lastAngle = angleOf(x, y)
        zone = if (r <= radius * config.centerFrac) Zone.CENTER else zoneOfAngle(lastAngle)
        return true
    }

    /** Returns signed detents produced by this sample. [extraGain] >= 1 boosts long lists. */
    fun move(x: Float, y: Float, t: Long, extraGain: Float = 1f): Int {
        if (!active) return 0
        if (zone == Zone.CENTER && !rotating) {
            if (hypot(x - startX, y - startY) > radius * config.centerSlopFrac) movedFar = true
            if (!movedFar) return 0
            // finger left the centre button: re-evaluate as a ring touch
            zone = zoneOfAngle(angleOf(x, y))
            lastAngle = angleOf(x, y); lastT = t; pendingDeg = 0f
            return 0
        }
        if (hypot(x - cx, y - cy) < radius * config.minRadiusFrac) { lastT = t; inCenter = true; return 0 }
        val a = angleOf(x, y)
        if (inCenter) { // came back out of the hub: resync so the jump across the centre is not counted as rotation
            inCenter = false; lastAngle = a; lastT = t; return 0
        }
        var d = Math.toDegrees(a - lastAngle).toFloat()
        while (d > 180f) d -= 360f
        while (d <= -180f) d += 360f
        val dt = (t - lastT).coerceAtLeast(1L)
        lastAngle = a; lastT = t
        updateVelocity(d, t, dt)

        if (!rotating) {
            pendingDeg += d
            if (abs(pendingDeg) < config.slopDeg) return 0
            rotating = true
            d = pendingDeg
            pendingDeg = 0f
        }
        if (d * acc < 0f) acc = 0f
        acc += d * gainState * extraGain.coerceAtLeast(1f)
        var n = (acc / config.degPerDetent).toInt()
        acc -= n * config.degPerDetent
        val cap = config.maxStepsPerSample
        if (n > cap) n = cap else if (n < -cap) n = -cap
        emitted += kotlin.math.abs(n)
        return n
    }

    /** Returns a tap zone if the touch was a tap (no rotation, short, in place). */
    fun up(x: Float, y: Float, t: Long): Zone? {
        if (!active) return null
        active = false
        // A touch that wobbled past the slop but never produced a scroll step is still a tap.
        val tap = emitted == 0 && !movedFar && (t - downT) <= config.tapMaxMs &&
            hypot(x - startX, y - startY) <= radius * config.centerSlopFrac
        val z = zone
        rotating = false; omega = 0f
        return if (tap) z else null
    }

    fun cancel() { active = false; rotating = false; omega = 0f }

    private fun pushHist(t: Long, a: Double) {
        if (histN == histT.size) { System.arraycopy(histT, 1, histT, 0, histN - 1); System.arraycopy(histA, 1, histA, 0, histN - 1); histN-- }
        histT[histN] = t; histA[histN] = a; histN++
    }

    /** Speed over a trailing time window (immune to bursty event timestamps), smoothed, with a slew-limited multiplier. */
    private fun updateVelocity(d: Float, t: Long, dt: Long) {
        if (dt > config.idleResetMs) { histN = 0; omega = 0f; gainState = 1f }
        unwrapped += d
        pushHist(t, unwrapped)
        while (histN > 2 && t - histT[0] > config.velocityWindowMs) {
            System.arraycopy(histT, 1, histT, 0, histN - 1); System.arraycopy(histA, 1, histA, 0, histN - 1); histN--
        }
        val span = t - histT[0]
        if (span >= 40) {
            val v = ((unwrapped - histA[0]) / span * 1000.0).toFloat()
            val dtv = (t - lastVelT).coerceAtLeast(1L).toFloat()
            val alpha = 1f - kotlin.math.exp(-dtv / config.velocityTauMs)
            omega += (v - omega) * alpha
        }
        val dtg = (t - lastVelT).coerceIn(1L, 100L) / 1000f
        lastVelT = t
        val target = gain(abs(omega))
        val step = if (target > gainState) config.gainRiseRate * dtg else config.gainFallRate * dtg
        gainState = if (target > gainState) minOf(target, gainState + step) else maxOf(target, gainState - step)
    }

    fun gain(absDps: Float): Float {
        val u = ((absDps - config.accelMinDps) / (config.accelMaxDps - config.accelMinDps)).coerceIn(0f, 1f)
        val s = u * u * (3f - 2f * u)
        return 1f + (config.maxGain - 1f) * s
    }

    private fun angleOf(x: Float, y: Float) = atan2((y - cy).toDouble(), (x - cx).toDouble())

    private fun zoneOfAngle(a: Double): Zone {
        val deg = Math.toDegrees(a).roundToInt() // 0 = right, 90 = down (screen coords)
        return when {
            deg in -135..-46 -> Zone.MENU
            deg in -45..45 -> Zone.NEXT
            deg in 46..135 -> Zone.PLAY_PAUSE
            else -> Zone.PREV
        }
    }
}
