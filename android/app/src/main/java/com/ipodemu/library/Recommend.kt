package com.ipodemu.library

/** A generated playlist shown in the modern Player's "For You" shelves. */
class Mix(val id: String, val title: String, val subtitle: String, val tracks: List<Track>) {
    val artKey: String? get() = tracks.firstNotNullOfOrNull { it.artKey }
}

/**
 * Local-only recommendations built from what the library knows: play counts, favourites and listening history.
 * Deterministic within a day (seeded by the date), so shelves don't reshuffle every time Home recomposes.
 */
object Recommender {
    private fun artistOf(t: Track) = t.albumArtist.ifEmpty { t.artist }
    private val NOT_AN_ARTIST = setOf("", "various artists", "unknown artist", "unknown", "va")

    fun mixes(lib: Library, ud: UserData, day: Long = System.currentTimeMillis() / 86_400_000L): List<Mix> {
        val music = lib.songs().filter { it.isMusic }
        if (music.size < 8) return emptyList()
        val by = lib.byPath()
        val rnd = java.util.Random(day)
        fun <T> List<T>.pick(n: Int) = shuffled(java.util.Random(rnd.nextLong())).take(n)

        val recentPaths = ud.recents.toList()
        val recentSet = recentPaths.take(60).toHashSet()
        val out = ArrayList<Mix>()

        // taste profile: artists and genres weighted by plays, favourites and recency
        val artistScore = HashMap<String, Double>(); val genreScore = HashMap<String, Double>()
        fun bump(t: Track, w: Double) { artistScore.merge(artistOf(t), w, Double::plus); if (t.genre.isNotEmpty()) genreScore.merge(t.genre.lowercase(), w, Double::plus) }
        ud.plays.forEach { (p, n) -> by[p]?.let { bump(it, n * 2.0) } }
        ud.favorites.forEach { p -> by[p]?.let { bump(it, 3.0) } }
        recentPaths.take(30).forEachIndexed { i, p -> by[p]?.let { bump(it, 2.0 - i * 0.05) } }
        val hasTaste = artistScore.isNotEmpty()

        if (hasTaste) {
            // Daily mixes: one per top artist, padded with the same genre
            val top = artistScore.entries.filter { it.key.lowercase() !in NOT_AN_ARTIST }.sortedByDescending { it.value }.take(3)
            top.forEachIndexed { i, (artist, _) ->
                val own = music.filter { artistOf(it) == artist }
                val genre = own.firstOrNull { it.genre.isNotEmpty() }?.genre?.lowercase()
                val similar = if (genre == null) emptyList() else music.filter { it.genre.lowercase() == genre && artistOf(it) != artist }.pick(24)
                val tracks = (own.pick(16) + similar).distinctBy { it.path }.shuffled(java.util.Random(day + i))
                if (tracks.size >= 5) out += Mix("daily$i", "Daily Mix ${i + 1}", "$artist and similar", tracks)
            }
            // Suggested: same artists/genres as what you play, but not played lately
            val favArtists = artistScore.entries.filter { it.key.lowercase() !in NOT_AN_ARTIST }.sortedByDescending { it.value }.take(8).map { it.key }.toSet()
            val favGenres = genreScore.entries.sortedByDescending { it.value }.take(4).map { it.key }.toSet()
            val suggested = music.filter { it.path !in recentSet && (artistOf(it) in favArtists || it.genre.lowercase() in favGenres) }.pick(30)
            if (suggested.size >= 5) out += Mix("suggested", "Suggested for You", "Based on what you play", suggested)
            // On Repeat / Rediscover
            val repeat = ud.plays.entries.filter { it.value >= 2 }.sortedByDescending { it.value }.mapNotNull { by[it.key] }.take(25)
            if (repeat.size >= 3) out += Mix("repeat", "On Repeat", "Your most played", repeat)
            val old = (ud.favorites.toList() + ud.plays.filterValues { it >= 2 }.keys).distinct().filter { it !in recentSet }.mapNotNull { by[it] }.pick(25)
            if (old.size >= 3) out += Mix("rediscover", "Rediscover", "Favourites you haven't heard lately", old)
        }
        // Discover: never played
        val unplayed = music.filter { (ud.plays[it.path] ?: 0) == 0 && it.path !in ud.favorites }.pick(30)
        if (unplayed.size >= 5) out += Mix("discover", "Discover", "Songs you haven't played yet", unplayed)
        // Genre and decade shelves
        music.filter { it.genre.isNotEmpty() }.groupBy { it.genre.lowercase() }.entries.sortedByDescending { it.value.size }.take(4).forEach { (g, list) ->
            if (list.size >= 6) out += Mix("genre:$g", list.first().genre.replaceFirstChar { it.uppercase() } + " Mix", "${list.size} songs", list.pick(40))
        }
        music.filter { it.year in 1950..2100 }.groupBy { it.year / 10 * 10 }.entries.sortedByDescending { it.value.size }.take(3).forEach { (d, list) ->
            if (list.size >= 8) out += Mix("decade:$d", "${d}s", "${list.size} songs", list.pick(40))
        }
        return out
    }

    fun find(id: String, lib: Library, ud: UserData): Mix? = mixes(lib, ud).firstOrNull { it.id == id }
}
