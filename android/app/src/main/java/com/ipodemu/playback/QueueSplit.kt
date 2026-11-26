package com.ipodemu.playback

/**
 * A very long queue (a 10,000-song library) starts playing at once: only a window of songs around the chosen one goes into the player first, the rest is
 * added in the background in order (owner, 26 Nov: a big queue took 2-5 s to start). Pure and unit-tested; PlayerController does the adding.
 */
object QueueSplit {
    const val BEHIND = 50
    const val AHEAD = 150

    /** The part of the list that goes in first: songs [from] until [to], with the chosen song at [chosenInWindow] of them. */
    class Plan(val from: Int, val to: Int, val chosenInWindow: Int)

    /** The window for a list of [size] songs started at [index], or null when the list is short enough ([limit]) to go in whole. */
    fun plan(size: Int, index: Int, limit: Int, behind: Int = BEHIND, ahead: Int = AHEAD): Plan? {
        if (size <= limit) return null
        val i = index.coerceIn(0, size - 1)
        val from = maxOf(0, i - behind); val to = minOf(size, i + ahead + 1)
        return Plan(from, to, i - from)
    }

    /**
     * The whole queue once the rest has been made: [before] and [after] go round the window, in their own order. [current] is what the queue is now (the window,
     * plus anything the listener added after it meanwhile): the first [windowSize] songs are the window, anything past that stays at the end.
     */
    fun <T> whole(current: List<T>, before: List<T>, after: List<T>, windowSize: Int): List<T> {
        val w = windowSize.coerceIn(0, current.size)
        return before + current.subList(0, w) + after + current.subList(w, current.size)
    }
}
