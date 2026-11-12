package com.ipodemu.library

/**
 * Autoplay never floods one artist (owner, 12 Nov: "Dreamer" and then about ten Livin' Joy songs). Of the next songs, at most [maxSeed] are by the artist
 * of the song asked for (loved ones do not count towards that), and no artist plays twice in a row while something else can go between. The web's
 * AutoplaySpread does the same.
 */
object AutoplaySpread {
    const val MAX_SEED = 2

    fun <T> spread(songs: List<T>, artistOf: (T) -> String, seedArtist: String, loved: (T) -> Boolean = { false }, maxSeed: Int = MAX_SEED): List<T> {
        val seed = primaryArtist(seedArtist)
        fun a(t: T) = primaryArtist(artistOf(t))
        val kept = ArrayList<T>(); var seedCount = 0
        for (t in songs) {
            if (a(t) == seed && !loved(t)) { if (seedCount >= maxSeed) continue; seedCount++ }
            kept.add(t)
        }
        val out = ArrayList<T>(); val waiting = ArrayList<T>()
        for (t in kept) {
            if (out.isNotEmpty() && a(out.last()) == a(t)) { waiting.add(t); continue }
            out.add(t)
            var i = 0
            while (i < waiting.size) { if (a(out.last()) != a(waiting[i])) { out.add(waiting.removeAt(i)); i = 0 } else i++ }
        }
        out.addAll(waiting)
        return out
    }

    const val SEED_COUNT = 8

    /** The songs Autoplay goes by, oldest first (owner, 12 Nov: "the songs at the end of a playlist, or the song I asked for or clicked play on"): up to
     *  the song playing, the last [SEED_COUNT] the person chose themselves; Autoplay's own picks ([isAuto]) never count, so it does not drift away. */
    fun <T> seeds(queue: List<T>, index: Int, isAuto: (T) -> Boolean): List<T> =
        queue.take((index + 1).coerceIn(0, queue.size)).filterNot(isAuto).takeLast(SEED_COUNT)

    /** The chosen song the lookup starts from: the one playing when the person chose it; under Autoplay, each Autoplay song takes the next chosen song
     *  from the end back (one song asked for stays that song). */
    fun <T> lead(queue: List<T>, index: Int, isAuto: (T) -> Boolean): T? {
        val s = seeds(queue, index, isAuto)
        if (s.isEmpty()) return null
        val cur = queue.getOrNull(index)
        if (cur == null || !isAuto(cur)) return s.last()
        val autoSoFar = queue.take(index + 1).count(isAuto)
        return s[s.size - 1 - (autoSoFar - 1) % s.size]
    }
}
