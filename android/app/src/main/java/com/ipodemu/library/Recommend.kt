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

/** One row on Home: songs or albums. */
class HomeShelf(val id: String, val title: String, val subtitle: String, val tracks: List<Track> = emptyList(), val albums: List<Group> = emptyList())

/**
 * Home the way YouTube Music lays it out, the same shelves as FLACie Web (ipodsync/src/FLACie.Core/Home.cs): Listen again, Quick picks, a Daily
 * Mix that changes every hour and cycles through moods and genres, mood and genre rows, Albums for you. Seeded by the hour / the day, so a shelf is
 * steady while you look at it and new picks turn up in the next slot.
 */
object HomeShelves {
    class Mood(val name: String, val sub: String, val genres: List<String>)
    val moods = listOf(
        Mood("Relax", "Slow it down", listOf("ambient", "chill", "lounge", "jazz", "acoustic", "folk", "classical", "soul", "easy", "lo-fi", "lofi", "downtempo", "new age", "bossa", "piano", "instrumental", "singer", "soft")),
        Mood("Workout", "Keep moving", listOf("hip hop", "hip-hop", "rap", "trap", "dance", "electronic", "edm", "house", "techno", "drum", "metal", "punk", "hardcore", "workout", "rock", "hardstyle")),
        Mood("Party", "Turn it up", listOf("dance", "pop", "disco", "funk", "reggaeton", "latin", "house", "club", "r&b", "party", "hip hop", "electro")),
        Mood("Feel good", "Good vibes", listOf("pop", "funk", "soul", "reggae", "disco", "indie", "happy", "summer", "ska")),
        Mood("Late night", "After dark", listOf("r&b", "rnb", "hip hop", "trap", "soul", "chill", "trip", "downtempo", "alternative", "indie")),
        Mood("Focus", "Head down", listOf("instrumental", "classical", "ambient", "post-rock", "electronic", "lo-fi", "lofi", "piano", "study")),
    )
    private fun inMood(t: Track, m: Mood) = t.genre.isNotEmpty() && m.genres.any { t.genre.contains(it, true) }
    fun slot(nowMs: Long, hours: Int = 1) = nowMs / (3_600_000L * hours)
    private fun <T> Iterable<T>.seeded(seed: Long) = shuffled(java.util.Random(seed))
    private fun spread(tracks: Iterable<Track>, per: Int, take: Int): List<Track> {
        val seen = HashMap<String, Int>(); val out = ArrayList<Track>()
        for (t in tracks) { val a = primaryArtist(t.artist); val n = (seen[a] ?: 0) + 1; seen[a] = n; if (n <= per) out.add(t); if (out.size >= take) break }
        return out
    }

    /** The moods with enough songs in this library to be worth a chip on Home. */
    fun moodsAvailable(lib: Library): List<String> = lib.songs().let { s -> moods.filter { m -> s.count { it.isMusic && inMood(it, m) } >= 6 }.map { it.name } }

    /** One mood's songs for today, spread across artists. */
    fun moodTracks(lib: Library, name: String, nowMs: Long = System.currentTimeMillis()): List<Track> {
        val m = moods.firstOrNull { it.name == name } ?: return emptyList()
        return spread(lib.songs().filter { it.isMusic && inMood(it, m) }.seeded(slot(nowMs, 24) * 17 + name.length), 2, 40)
    }

    fun build(lib: Library, ud: UserData, nowMs: Long = System.currentTimeMillis()): List<HomeShelf> {
        val songs = lib.songs().filter { it.isMusic }
        if (songs.isEmpty()) return emptyList()
        val by = lib.byPath()
        val out = ArrayList<HomeShelf>()
        val s = slot(nowMs); val day = slot(nowMs, 24)
        val listen = ArrayList<Track>(); val listenKeys = HashSet<String>()
        for (p in ud.recents) { val t = by[p] ?: lib.resolve(p, ud.meta[p]) ?: continue; if (listenKeys.add(t.matchKey)) listen.add(t); if (listen.size >= 16) break }
        val plays = HashMap<String, Int>()
        ud.plays.forEach { (p, n) -> by[p]?.let { plays.merge(it.matchKey, n, Int::plus) } }
        val favKeys = ud.favorites.mapNotNull { by[it]?.matchKey }.toHashSet()
        val listKeys = ud.playlists.flatMap { it.paths }.mapNotNull { by[it]?.matchKey }.toHashSet()
        fun taste(t: Track): Int { val k = t.matchKey; return (plays[k] ?: 0) * 2 + (if (k in favKeys) 3 else 0) + (if (k in listKeys) 1 else 0) }

        if (listen.isNotEmpty()) out.add(HomeShelf("listen-again", "Listen again", "Your recent plays", listen))
        val again = listen.take(8).map { it.matchKey }.toHashSet()
        val picks = spread(songs.filter { it.matchKey !in again && (it.durationMs == 0L || it.durationMs > 40_000) }.seeded(s * 31 + 7).sortedByDescending { taste(it) }, 2, 20)
        if (picks.isNotEmpty()) out.add(HomeShelf("quick-picks", "Quick picks", "Songs to start with, based on what you play", picks))

        val genres = songs.filter { it.genre.isNotEmpty() }.groupBy { it.genre.lowercase() }.entries.sortedByDescending { it.value.size }.take(8)
        val mixes = ArrayList<Triple<String, String, List<Track>>>()
        for (m in moods) { val l = songs.filter { inMood(it, m) }; if (l.size >= 8) mixes.add(Triple(m.name, m.sub, l)) }
        for (g in genres) { if (g.value.size >= 8 && mixes.none { it.first.equals(g.key, true) }) mixes.add(Triple(g.value.first().genre, "More ${g.value.first().genre}", g.value)) }
        if (mixes.isNotEmpty()) {
            val now = mixes[(s % mixes.size).toInt()]
            out.add(HomeShelf("daily-mix", "Daily Mix: ${now.first}", now.second + " · new every hour", spread(now.third.seeded(s).sortedByDescending { if (taste(it) > 0) 1 else 0 }, 3, 30)))
        }

        // "Similar to": the artist you favour most, then songs of the same genre by other artists
        val topArtist = songs.filter { taste(it) > 0 }.groupBy { primaryArtist(it.artist) }.filterKeys { it.isNotEmpty() }.maxByOrNull { e -> e.value.sumOf { taste(it) } }
        topArtist?.value?.firstOrNull { it.genre.isNotEmpty() }?.let { seed ->
            val sim = spread(songs.filter { it.genre.equals(seed.genre, true) && primaryArtist(it.artist) != topArtist.key }.seeded(day * 7 + 3), 2, 14)
            if (sim.size >= 6) out.add(HomeShelf("similar", "Similar to ${topArtist.value.first().artist}", "Same sound, other artists", sim))
        }
        val albums = lib.albums().filter { it.tracks.size >= 3 }.map { it to it.tracks.sumOf { t -> taste(t) } / it.tracks.size }
        val forYou = (albums.filter { it.second > 0 }.sortedByDescending { it.second }.take(6).map { it.first } + albums.filter { it.second == 0 }.map { it.first }.seeded(day * 5 + 1).take(10)).distinctBy { it.tracks.first().albumKey }.take(12)
        if (forYou.isNotEmpty()) out.add(HomeShelf("albums-for-you", "Albums for you", "From your library", albums = forYou))
        val recent = lib.recentAlbums(14)
        if (recent.isNotEmpty()) out.add(HomeShelf("recently-added", "Recently added", "New in your library", albums = recent))
        return out
    }
}
