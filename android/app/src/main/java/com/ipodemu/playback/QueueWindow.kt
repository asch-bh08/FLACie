package com.ipodemu.playback

/**
 * Which songs of a (possibly 10,000-song) queue the system media session shows to Bluetooth, Android Auto and the lock screen: the one that plays, [BEHIND] songs before it
 * and [AHEAD] after it, in PLAY order (a shuffled queue is not played in list order). Pure and unit-tested.
 */
object QueueWindow {
    const val BEHIND = 20
    const val AHEAD = 80
    const val NONE = -1

    /** Queue indices in play order around [current]; [next] and [previous] give the neighbour in play order (or [NONE]). Never repeats an index (repeat-all would loop). */
    fun indices(current: Int, count: Int, next: (Int) -> Int, previous: (Int) -> Int, behind: Int = BEHIND, ahead: Int = AHEAD): List<Int> {
        if (count <= 0 || current !in 0 until count) return emptyList()
        val seen = HashSet<Int>(); seen.add(current)
        val before = ArrayList<Int>(); var i = current
        for (n in 0 until behind) { i = previous(i); if (i == NONE || i !in 0 until count || !seen.add(i)) break; before.add(i) }
        val after = ArrayList<Int>(); i = current
        for (n in 0 until ahead) { i = next(i); if (i == NONE || i !in 0 until count || !seen.add(i)) break; after.add(i) }
        return before.asReversed() + current + after
    }

    /** Where [queueIndex] sits in a window, or [NONE]. */
    fun positionOf(window: List<Int>, queueIndex: Int): Int = window.indexOf(queueIndex).takeIf { it >= 0 } ?: NONE
}
