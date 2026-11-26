package com.ipodemu.library

/**
 * Autoplay never floods one artist (owner, 12 Nov: "Dreamer" and then about ten Livin' Joy songs). Of the next songs, at most [maxSeed] are by the artist
 * of the song asked for (loved ones do not count towards that), and no artist plays twice in a row while something else can go between. The web's
 * AutoplaySpread does the same.
 */
object AutoplaySpread {
    const val MAX_SEED = 2
    /** Loved songs may come more often when they fit (owner, 26 Nov: "I don't mind them all being from the same artist as long as the song is good and I
     *  like it"), but never a flood: at most [MAX_RUN] by one artist in a row and [MAX_WINDOW] in any [WINDOW]. Same as the web. */
    const val MAX_RUN = 3; const val MAX_WINDOW = 4; const val WINDOW = 10; const val MAX_LOVED = 4

    /** [already]: the seed artist's songs Autoplay already queued in this run; the cap is for the whole run. The seed artist's loved songs have a cap of
     *  their own ([maxLoved]); unknown ones stay at [maxSeed] and never play back to back, a loved one may follow its own artist up to [MAX_RUN] in a row. */
    fun <T> spread(songs: List<T>, artistOf: (T) -> String, seedArtist: String, loved: (T) -> Boolean = { false }, maxSeed: Int = MAX_SEED,
                   already: Int = 0, maxLoved: Int = MAX_LOVED): List<T> {
        val seed = primaryArtist(seedArtist)
        fun a(t: T) = primaryArtist(artistOf(t))
        val kept = ArrayList<T>(); var seedCount = already; var lovedCount = 0
        for (t in songs) {
            if (a(t) == seed) {
                if (loved(t)) { if (lovedCount >= maxLoved) continue; lovedCount++ }
                else { if (seedCount >= maxSeed) continue; seedCount++ }
            }
            kept.add(t)
        }
        fun run(o: List<T>): Int { var n = 0; var i = o.size - 1; while (i >= 0 && a(o[i]) == a(o.last())) { n++; i-- }; return n }
        fun fits(o: List<T>, t: T) = o.isEmpty() || a(o.last()) != a(t) || (loved(t) && loved(o.last()) && run(o) < MAX_RUN)
        val out = ArrayList<T>(); val waiting = ArrayList<T>()
        for (t in kept) {
            if (!fits(out, t)) { waiting.add(t); continue }
            out.add(t)
            var i = 0
            while (i < waiting.size) { if (fits(out, waiting[i])) { out.add(waiting.removeAt(i)); i = 0 } else i++ }
        }
        out.addAll(waiting)
        // hard rule: no artist more than MAX_WINDOW times in any WINDOW songs in a row
        val final = ArrayList<T>()
        for (t in out) if (final.takeLast(WINDOW - 1).count { a(it) == a(t) } < MAX_WINDOW) final.add(t)
        return final
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

    /** Search: a song's mixes and versions found by one search show as one row, the plain version where the best ranked of them was (owner, 13 Nov:
     *  "show one main one then hide the different versions"); the web's Library.OneVersion. */
    fun oneVersion(ranked: List<Track>): List<Track> {
        val groups = ranked.groupBy { it.songKey }
        val shown = HashSet<String>(); val out = ArrayList<Track>()
        for (t in ranked) {
            val g = groups[t.songKey]!!
            if (g.size == 1) { out.add(t); continue }
            if (!shown.add(t.songKey)) continue
            out.add(g.firstOrNull { normTitle(it.title) == it.songKey.substringBefore('|') } ?: t)
        }
        return out
    }
}

/** Opening an album asks for its missing songs once per album while the app runs (not on every visit). */
object AlbumAutoFetch {
    private val asked = HashSet<String>()
    @Synchronized fun first(albumKey: String): Boolean = asked.add(albumKey)
}
