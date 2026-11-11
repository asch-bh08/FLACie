package com.ipodemu.player

/**
 * What a touch on the seek frame means (owner, 11 Nov: "the seek bar is too easily skipped by accident when I'm trying to swipe"). A finger that lands on
 * the frame used to seek at once, so a swipe across the cover (to the next page, or down to close) that began on the frame jumped the song. Now:
 * a quick tap still seeks to that point; a finger that moves away quickly is a swipe and is left to the page; seeking by dragging starts only once the
 * finger has rested for [HOLD_MS] (and stayed within the touch slop) first.
 */
object SeekIntent {
    const val HOLD_MS = 150L
    /** Taps longer than this are not taps (a slow press that never moved is still a hold, which then scrubs). */
    const val TAP_MS = 400L

    enum class Kind { WAIT, SCRUB, TAP, SWIPE }

    /** [elapsedMs] since the finger landed, [movedPx] how far it is from where it landed, [released] whether it has lifted. */
    fun decide(elapsedMs: Long, movedPx: Float, slopPx: Float, released: Boolean): Kind = when {
        movedPx > slopPx && elapsedMs < HOLD_MS -> Kind.SWIPE
        released && movedPx <= slopPx && elapsedMs <= TAP_MS -> Kind.TAP
        released -> if (elapsedMs >= HOLD_MS && movedPx <= slopPx) Kind.TAP else Kind.SWIPE
        elapsedMs >= HOLD_MS -> Kind.SCRUB
        else -> Kind.WAIT
    }
}
