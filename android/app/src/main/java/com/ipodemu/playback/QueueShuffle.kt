package com.ipodemu.playback

/**
 * The shuffle toggle as play orders (queue indexes in the queue as it was built). Same rules as FLACie.Core's QueueShuffle on the server: on keeps everything up to
 * the current song and shuffles the rest; off goes back to the original order with the same song playing. "Shuffle all" is separate and stays as it is.
 */
object QueueShuffle {
    /** The order with the first [current]+1 songs in place and the rest shuffled ([current] -1: nothing playing, all shuffled). */
    fun on(count: Int, current: Int, random: kotlin.random.Random = kotlin.random.Random.Default): IntArray {
        val order = IntArray(count) { it }
        var i = count - 1
        while (i > current + 1) {
            val j = random.nextInt(current + 1, i + 1)
            val x = order[i]; order[i] = order[j]; order[j] = x
            i--
        }
        return order
    }

    /** A new queue started on shuffle at [first]: that song, then every other one shuffled ([first] outside the queue: all shuffled). The player's own
     *  order put [first] anywhere, so the songs before it never played and Autoplay took over early. */
    fun start(count: Int, first: Int, random: kotlin.random.Random = kotlin.random.Random.Default): IntArray {
        if (first !in 0 until count) return IntArray(count) { it }.also { it.shuffle(random) }
        val rest = (0 until count).filter { it != first }.shuffled(random)
        return (listOf(first) + rest).toIntArray()
    }

    /** The original order back, and where the song at [pos] of [order] now is (-1 when [pos] is outside it). */
    fun off(order: IntArray, pos: Int): Pair<IntArray, Int> = IntArray(order.size) { it } to (if (pos in order.indices) order[pos] else -1)
}
