package com.ipodemu.player

/** The seek bar's text and haptic rules, apart from the drawing (unit-tested). */
object ScrubModel {
    /** A tick of haptic feedback each time the finger crosses one of these fractions of the bar. */
    const val TICK_STEPS = 10

    /** True when going from [prev] to [cur] crossed a tick line (a tenth of the bar), in either direction. */
    fun tickCrossed(prev: Float, cur: Float, steps: Int = TICK_STEPS): Boolean {
        val a = Math.floor((prev.coerceIn(0f, 1f) * steps).toDouble()).toInt()
        val b = Math.floor((cur.coerceIn(0f, 1f) * steps).toDouble()).toInt()
        return a != b
    }

    /** The time under the finger, "1:37". */
    fun bubbleText(fraction: Float, durationMs: Long): String = fmtTime((fraction.coerceIn(0f, 1f) * durationMs.coerceAtLeast(0L)).toLong())

    /** The time left, "-2:19" (never negative). */
    fun remainingText(positionMs: Long, durationMs: Long): String = "-" + fmtTime((durationMs - positionMs).coerceAtLeast(0L))

    /** How a screen reader says a time: "1 minute 37 seconds", "5 minutes 2 seconds", "45 seconds", "1 hour 2 minutes". */
    fun spoken(ms: Long): String {
        val total = (ms / 1000).coerceAtLeast(0L)
        val h = total / 3600; val m = (total % 3600) / 60; val s = total % 60
        val parts = ArrayList<String>()
        if (h > 0) parts += if (h == 1L) "1 hour" else "$h hours"
        if (m > 0) parts += if (m == 1L) "1 minute" else "$m minutes"
        if (s > 0 || parts.isEmpty()) parts += if (s == 1L) "1 second" else "$s seconds"
        return parts.joinToString(" ")
    }

    /** The state description of the seek slider: "1 minute 37 seconds of 5 minutes 2 seconds". */
    fun stateDescription(positionMs: Long, durationMs: Long): String =
        if (durationMs <= 0L) spoken(positionMs) else spoken(positionMs) + " of " + spoken(durationMs)

    /**
     * Where the scrub pill sits: centred under the knob, but kept inside the bar. Returns the pill's left edge in the same unit as the arguments
     * ([trackStart] and [trackWidth] describe the track the knob runs along).
     */
    fun bubbleLeft(fraction: Float, trackStart: Float, trackWidth: Float, bubbleWidth: Float, containerWidth: Float): Float {
        val centre = trackStart + trackWidth * fraction.coerceIn(0f, 1f)
        return (centre - bubbleWidth / 2f).coerceIn(0f, (containerWidth - bubbleWidth).coerceAtLeast(0f))
    }
}
