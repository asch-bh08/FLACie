package com.ipodemu.input

import kotlin.math.abs
import kotlin.math.exp

/**
 * The coast of a click wheel after a fast flick: the finger lifts at some angular speed and the wheel carries on, slowing down exponentially, handing out the
 * same detents the finger would have. Pure (the caller supplies the clock), so it is unit-tested; IpodView runs it once per frame.
 */
class WheelInertia(
    private val degPerDetent: Float = 34f,
    private val tauMs: Float = 350f,
    private val minStartDps: Float = 300f,
    private val stopDps: Float = 45f,
    /** However hard the flick, the coast is at most this many detents in one frame, and at most [MAX_TOTAL] in all. */
    private val maxPerStep: Int = 2,
) {
    private var omega = 0f
    private var carry = 0f
    private var last = 0L
    private var total = 0
    var active = false; private set

    /** Starts the coast at [velocityDps] (signed, deg/s) at time [t]; false (nothing to do) when the flick was too slow. */
    fun start(velocityDps: Float, t: Long): Boolean {
        if (abs(velocityDps) < minStartDps) { stop(); return false }
        omega = velocityDps; carry = 0f; last = t; total = 0; active = true
        return true
    }

    /** Detents to hand out since the last call (signed), advancing to time [t]. Ends itself when the wheel has slowed down enough. */
    fun step(t: Long): Int {
        if (!active) return 0
        val dt = (t - last).coerceIn(0L, 100L); last = t
        omega *= exp(-dt / tauMs)
        carry += omega * dt / 1000f / degPerDetent
        var n = carry.toInt().coerceIn(-maxPerStep, maxPerStep)
        if (total + abs(n) > MAX_TOTAL) n = (if (n < 0) -1 else 1) * (MAX_TOTAL - total).coerceAtLeast(0)
        carry -= n; total += abs(n)
        if (abs(omega) < stopDps || total >= MAX_TOTAL) stop()
        return n
    }

    /** A new touch, a key, or leaving the screen ends the coast at once. */
    fun stop() { active = false; omega = 0f; carry = 0f }

    companion object { const val MAX_TOTAL = 40 }
}
