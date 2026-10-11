package com.ipodemu.playback

/**
 * The decision behind fast Next/Previous presses, with no player in it (so it can be tested): presses that follow each other within [burstMs]
 * are not each sent to the player (each one would open a stream that the next press abandons). The first press acts at once; later ones only
 * move [target], which the UI shows straight away, and the player is sent there once, after the last press settles.
 */
class SkipBurst(private val burstMs: Long = 400L) {
    enum class Press {
        /** Not part of a burst: do the normal skip now. */
        ACT_NOW,
        /** Part of a burst: [target] moved (schedule the settle). */
        MOVED,
        /** Part of a burst but there is nowhere to go. */
        IGNORED,
    }

    /** Queue index the burst is heading for, or -1 when no burst is running. */
    @Volatile var target = -1; private set
    private var lastAt = Long.MIN_VALUE / 2

    /** One press at time [now]; [step] gives the index after/before a given index (negative = none). */
    fun press(now: Long, currentIndex: Int, step: (Int) -> Int): Press {
        val inBurst = target >= 0 || now - lastAt < burstMs
        lastAt = now
        if (!inBurst) return Press.ACT_NOW
        val to = step(if (target >= 0) target else currentIndex)
        if (to < 0) return Press.IGNORED
        target = to
        return Press.MOVED
    }

    /** The presses stopped: the index to jump to (or -1), and the burst is over. */
    fun settle(): Int { val t = target; target = -1; return t }

    fun reset() { target = -1 }
}
