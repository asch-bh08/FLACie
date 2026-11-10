package com.ipodemu.library

/**
 * How much memory the decoded covers may hold, and which of Home's covers are kept decoded in memory (b126: memory at a warm start had gone from
 * 95 MB to 142 MB once Home's covers were kept ready, b125). Pure Kotlin, so it is unit tested without Android.
 */
object ArtBudget {
    private const val MB = 1024 * 1024

    /** The share of the app's memory class the decoded covers may use. */
    const val FRACTION = 12
    const val MIN_BYTES = 12 * MB
    const val MAX_BYTES = 24 * MB

    /**
     * Bytes for the in-memory cover cache: a twelfth of the memory class (ActivityManager.memoryClass, in MB), between 12 and 24 MB. Before, it was a sixth
     * of the largest heap (42 MB and more on most phones), and kept-ready covers filled it.
     */
    fun lruBytes(memoryClassMb: Int): Int {
        val share = memoryClassMb.toLong() * MB / FRACTION
        return share.coerceIn(MIN_BYTES.toLong(), MAX_BYTES.toLong()).toInt()
    }

    /** How many shelves of Home the first screen shows, and how many covers of each (Quick picks: a page of four and part of the next; cards: three and a part). */
    const val FIRST_SHELVES = 4
    const val FIRST_PER_SHELF = 8

    /**
     * The covers the first screen of Home draws: the first [perShelf] of each of the first [shelves] shelves, in drawing order, each key once (with the
     * size it is first drawn at). Only these are kept decoded in memory ahead of time; the rest of each shelf is kept on disk and decoded as it scrolls in.
     */
    fun firstView(shelves: List<List<Pair<String, Boolean>>>, shelvesShown: Int = FIRST_SHELVES, perShelf: Int = FIRST_PER_SHELF): List<Pair<String, Boolean>> {
        val out = LinkedHashMap<String, Boolean>()
        for (s in shelves.take(shelvesShown)) for ((k, thumb) in s.take(perShelf)) out.putIfAbsent(k, thumb)
        return out.entries.map { it.key to it.value }
    }

    /** A row cover decoded at the same sample as the list size is the same picture: it is kept once, under the list size's key, not twice. */
    fun sharesCopy(thumbSample: Int, listSample: Int): Boolean = thumbSample == listSample
}
