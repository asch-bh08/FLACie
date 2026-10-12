package com.ipodemu.playback

/**
 * The arithmetic behind editing the play queue (move a song, remove a song, put one back), with no player in it so it can be unit-tested.
 *
 * Two notions of order are used:
 *  - the queue order: the list as stored, [0, n). ExoPlayer's `moveMediaItem` / `removeMediaItem` work in it and keep the playing song playing.
 *  - the play order: the sequence in which songs are reached. It is the queue order, or with shuffle on the player's shuffle order. An
 *    "order" array holds the queue indices of all songs in play order.
 *
 * "Up next" lists the songs that follow the current one in play order; its rows are moved with [reorder] (shuffle) or [linearMoves] (queue
 * order), both of which give the same result for the upcoming sequence (see QueueEditTest).
 */
object QueueEdit {
    enum class Step { UP, DOWN, TOP }

    /** A list after an edit. [current] is the index of the current song (-1 when the list is empty). */
    class Edit<T>(val items: List<T>, val current: Int, val currentRemoved: Boolean = false, val stopped: Boolean = false)

    /** Moves the song at [from] to [to] (0-based, in the list as it is now). The current song stays the current song, at its new index. */
    fun <T> move(items: List<T>, current: Int, from: Int, to: Int): Edit<T> {
        if (from !in items.indices || to !in items.indices || from == to) return Edit(items, current)
        val out = items.toMutableList()
        val x = out.removeAt(from); out.add(to, x)
        val c = when {
            current < 0 -> current
            current == from -> to
            from < current && to >= current -> current - 1
            from > current && to <= current -> current + 1
            else -> current
        }
        return Edit(out, c)
    }

    /**
     * Takes the song at [index] out. A song before the current one shifts the current index down; a song after it changes nothing. Removing
     * the current song itself: the one that followed takes its index ([Edit.currentRemoved]); when it was the last, repeat-all wraps to the
     * first song and otherwise playback stops with the new last song current ([Edit.stopped]). The play-order version of this is [removePlan].
     */
    fun <T> remove(items: List<T>, current: Int, index: Int, wrap: Boolean = false): Edit<T> {
        if (index !in items.indices) return Edit(items, current)
        val out = items.toMutableList(); out.removeAt(index)
        if (out.isEmpty()) return Edit(out, -1, currentRemoved = index == current, stopped = index == current)
        return when {
            index < current -> Edit(out, current - 1)
            index > current -> Edit(out, current)
            index < out.size -> Edit(out, index, currentRemoved = true)
            wrap -> Edit(out, 0, currentRemoved = true)
            else -> Edit(out, out.lastIndex, currentRemoved = true, stopped = true)
        }
    }

    /** Puts a song back at [index] (Undo of a removal). Songs at or after it, the current one included, shift up. */
    fun <T> insert(items: List<T>, current: Int, index: Int, item: T): Edit<T> {
        val i = index.coerceIn(0, items.size)
        val out = items.toMutableList(); out.add(i, item)
        return Edit(out, if (current >= i && current >= 0) current + 1 else current)
    }

    // ---- play order ---------------------------------------------------------------------------------------------------------------

    /** [order] as given when it is a permutation of 0 until [size] (a stale shuffle order is not), else the queue order. */
    fun playOrder(size: Int, order: IntArray?): IntArray {
        if (order != null && isPermutation(order, size)) return order
        return IntArray(size) { it }
    }

    fun isPermutation(a: IntArray, size: Int): Boolean {
        if (a.size != size) return false
        val seen = BooleanArray(size)
        for (v in a) { if (v !in 0 until size || seen[v]) return false; seen[v] = true }
        return true
    }

    /** The songs that follow [current] in play order (queue indices). With repeat-all the ones before it follow too, after the wrap; never [current]. */
    fun upcoming(order: IntArray, current: Int, repeatAll: Boolean): List<Int> {
        val p = order.indexOf(current)
        if (p < 0) return emptyList()
        val out = ArrayList<Int>(order.size)
        for (k in p + 1 until order.size) out.add(order[k])
        if (repeatAll) for (k in 0 until p) out.add(order[k])
        return out
    }

    /** What the current song's removal does: the song to jump to first (-1 none), and whether playback is paused there. */
    class RemovePlan(val index: Int, val jumpTo: Int, val pauseAfterJump: Boolean, val emptiesQueue: Boolean)

    /**
     * How to take [index] out so that playback behaves: another song is removed with nothing else changing (the player keeps the current
     * song); the current song is left by first jumping to the song that plays next, which keeps playing (or stays paused if it was paused);
     * with nothing after it, to the song before it, paused, so Play replays that one instead of the queue ending in silence.
     * Returns null for an index that is not in the queue.
     */
    fun removePlan(order: IntArray, current: Int, index: Int, repeatAll: Boolean): RemovePlan? {
        if (order.indexOf(index) < 0) return null
        if (index != current) return RemovePlan(index, -1, false, false)
        if (order.size == 1) return RemovePlan(index, -1, false, true)
        val up = upcoming(order, current, repeatAll)
        if (up.isNotEmpty()) return RemovePlan(index, up.first(), false, false)
        val p = order.indexOf(current)
        return RemovePlan(index, order[p - 1], true, false)
    }

    /**
     * The play order after moving the upcoming song [item] one place [Step.UP] or [Step.DOWN] in Up next, or to its top ([Step.TOP], "Play
     * next"). The slots after the current song (cyclic with repeat-all) are refilled with the new upcoming sequence; everything else, the
     * current song's own slot included, stays. Null when nothing changes (not an upcoming song, already first / last).
     */
    fun reorder(order: IntArray, current: Int, repeatAll: Boolean, item: Int, step: Step): IntArray? {
        val up = upcoming(order, current, repeatAll)
        val newUp = movedUpcoming(up, item, step) ?: return null
        val p = order.indexOf(current)
        val slots = ArrayList<Int>(order.size)
        for (k in p + 1 until order.size) slots.add(k)
        if (repeatAll) for (k in 0 until p) slots.add(k)
        val out = order.copyOf()
        for ((k, s) in slots.withIndex()) out[s] = newUp[k]
        return out
    }

    /** The upcoming sequence with [item] moved one place or to the front; null when that changes nothing. */
    fun movedUpcoming(up: List<Int>, item: Int, step: Step): List<Int>? {
        val pos = up.indexOf(item)
        if (pos < 0) return null
        return when (step) {
            Step.UP -> if (pos == 0) null else up.toMutableList().also { val t = it[pos]; it[pos] = it[pos - 1]; it[pos - 1] = t }
            Step.DOWN -> if (pos == up.lastIndex) null else up.toMutableList().also { val t = it[pos]; it[pos] = it[pos + 1]; it[pos + 1] = t }
            Step.TOP -> if (pos == 0) null else ArrayList<Int>(up.size).also { it.add(item); for (x in up) if (x != item) it.add(x) }
        }
    }

    /**
     * The same edit as [reorder] for the plain queue order (no shuffle), as a list of ExoPlayer `moveMediaItem(from, to)` calls, in order.
     * A neighbour swap is one move (two when the two songs are the first and the last, the repeat-all wrap); "Play next" is one move.
     */
    fun linearMoves(current: Int, upcoming: List<Int>, item: Int, step: Step): List<Pair<Int, Int>> {
        val pos = upcoming.indexOf(item)
        if (pos < 0 || movedUpcoming(upcoming, item, step) == null) return emptyList()
        return when (step) {
            Step.UP -> swapMoves(item, upcoming[pos - 1])
            Step.DOWN -> swapMoves(item, upcoming[pos + 1])
            Step.TOP -> listOf(item to (if (item > current) current + 1 else current))
        }
    }

    /** `moveMediaItem` calls that exchange the songs at [a] and [b]. */
    fun swapMoves(a: Int, b: Int): List<Pair<Int, Int>> {
        if (a == b) return emptyList()
        val lo = minOf(a, b); val hi = maxOf(a, b)
        return if (hi - lo == 1) listOf(a to b) else listOf(lo to hi, hi - 1 to lo)
    }

    /** Where "Play next" puts the song at [index] in the queue order (just after the current song), or null when it is the current song or already next. */
    fun playNextTarget(current: Int, index: Int): Int? {
        if (index == current || index == current + 1 || current < 0) return null
        return if (index > current) current + 1 else current
    }

    /** What Up next may offer for the row at [pos] of [count] upcoming songs; [more] when the list is cut short and further songs follow. */
    class RowActions(val up: Boolean, val down: Boolean, val top: Boolean)
    fun rowActions(pos: Int, count: Int, more: Boolean = false): RowActions =
        RowActions(up = pos > 0, down = pos in 0 until count - 1 || (more && pos == count - 1), top = pos > 0)

    /**
     * An Undo of a removal in shuffle: the player puts a re-added song at a random place of the shuffle order; this puts it right after
     * [after] (the song that played before it, a queue index of the NEW numbering), or first when there was none.
     */
    fun placeAfter(order: IntArray, item: Int, after: Int): IntArray {
        val rest = order.filter { it != item }
        val at = if (after < 0) 0 else rest.indexOf(after).let { if (it < 0) rest.size else it + 1 }
        val out = ArrayList<Int>(order.size)
        out.addAll(rest.subList(0, at)); out.add(item); out.addAll(rest.subList(at, rest.size))
        return out.toIntArray()
    }
}
