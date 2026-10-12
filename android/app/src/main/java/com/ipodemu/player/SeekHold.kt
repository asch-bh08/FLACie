package com.ipodemu.player

/** The seek bar's position rules, apart from the drawing: the knob follows the finger, then stays where it was let go until playback reports that place. */
object SeekHold {
    /** Playback is "there" when it is within this fraction of the place the finger let go. */
    const val SETTLED = 0.012f
    /** The hold gives up after this long even if playback never arrives (a failed seek). */
    const val GIVE_UP_MS = 1500L

    /** Where the knob is drawn. */
    fun shown(drag: Float?, pending: Float?, playing: Float): Float = (drag ?: pending ?: playing).coerceIn(0f, 1f)

    /** True when the held position can be dropped because playback reached it. */
    fun settled(pending: Float?, playing: Float): Boolean = pending != null && kotlin.math.abs(playing - pending) < SETTLED
}
