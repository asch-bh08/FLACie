package com.ipodemu.player

import com.ipodemu.library.AudioFacts
import com.ipodemu.library.Group
import com.ipodemu.library.SearchRank
import com.ipodemu.library.Track
import com.ipodemu.library.hiRes
import com.ipodemu.library.searchGlue
import com.ipodemu.library.sortKey

/**
 * The filtering and sorting behind Explore, as plain functions: no Compose, no Android. The screen runs [ExploreEngine.run] on a background
 * thread (10 000 songs filtered and sorted on the main thread was a visible stall on the RG Rotate each time a filter or letter changed).
 */
internal class ExploreQuery(
    val tab: String, val query: String = "", val letter: String = "", val genre: String = "", val decade: String = "", val quality: String = "",
    val depth: String = "", val rate: String = "", val codec: String = "", val kbps: String = "", val sort: String = "az",
) {
    val filtering: Boolean get() = query.isNotBlank() || letter.isNotEmpty() || genre.isNotEmpty() || decade.isNotEmpty() || quality.isNotEmpty() ||
        depth.isNotEmpty() || rate.isNotEmpty() || codec.isNotEmpty() || kbps.isNotEmpty()
}

/** [tab] says which view this answers (a result for another tab is ignored); [songs] for Songs, [groups] for Albums, Artists and Genres. */
internal class ExploreResult(val tab: String, val songs: List<Track>, val groups: List<Group>, val fuzzy: Boolean, val total: Int = 0)

private val LOSSLESS_EXT = setOf("flac", "alac", "wav", "aiff", "aif", "ape", "wv", "dsf", "tta")
internal fun trackExt(t: Track) = (t.filePath.ifEmpty { t.path.substringBefore('?') }).substringAfterLast('.', "").lowercase()
internal fun decadeOf(y: Int) = if (y >= 1900) "${y / 10 * 10}s" else ""

internal fun rateBand(hz: Int, band: String) = when (band) {
    "lo" -> hz in 1..43_999; "44" -> hz in 44_000..44_200; "48" -> hz in 47_900..48_100; "88" -> hz in 88_000..88_300
    "96" -> hz in 95_900..96_100; "176" -> hz in 176_000..176_500; "192" -> hz >= 191_900; else -> true
}

internal fun kbpsBand(k: Int, band: String) = k > 0 && when (band) {
    "128" -> k <= 128; "192" -> k in 129..192; "256" -> k in 193..256; "320" -> k in 257..320; "700" -> k in 321..700; "900" -> k in 701..900; "1000" -> k in 901..1000
    "1500" -> k in 1001..1500; "2000" -> k in 1501..2000; "3000" -> k in 2001..3000; "4000" -> k in 3001..4000; "5000" -> k in 4001..5000; "max" -> k > 5000; else -> true
}

/** The codec family of a song ("flac", "mp3", "aac" ...): the real codec when the server read it, else the file extension. */
internal fun codecFamily(t: Track): String {
    val c = (AudioFacts.of(t)?.codec?.takeIf { it.isNotEmpty() } ?: trackExt(t)).lowercase()
    return when {
        "alac" in c -> "alac"; "flac" in c -> "flac"; "mp3" in c || "mpeg" in c || c == "mp2" -> "mp3"
        "aac" in c || "m4a" in c || "mp4" in c -> "aac"; "opus" in c -> "opus"; "vorbis" in c || "ogg" in c || c == "oga" -> "ogg"
        "wma" in c || "asf" in c -> "wma"; "wavpack" in c || c == "wv" -> "wv"; "pcm" in c || "wav" in c -> "wav"
        "aif" in c -> "aiff"; "ape" in c || "monkey" in c -> "ape"; "dsd" in c || "dsf" in c || "dff" in c -> "dsd"; else -> ""
    }
}

internal object ExploreEngine {
    /** Sorts by a key computed ONCE per item (sortedBy calls its selector on every comparison: ~n log n times). Stable, like sortedBy. */
    fun <T, K : Comparable<K>> List<T>.sortedByCached(descending: Boolean = false, key: (T) -> K): List<T> {
        if (size < 2) return this
        val keyed = map { key(it) to it }
        val sorted = if (descending) keyed.sortedWith(compareByDescending { it.first }) else keyed.sortedWith(compareBy { it.first })
        return sorted.map { it.second }
    }

    private class ArtistKeyed(val t: Track, val artist: String)

    fun sortSongs(l: List<Track>, sort: String): List<Track> = when (sort) {
        "za" -> l.sortedByCached(true) { sortKey(it.title) }
        "artist" -> l.map { ArtistKeyed(it, sortKey(it.artist)) }
            .sortedWith(compareBy<ArtistKeyed>({ it.artist }, { it.t.album }, { it.t.discNo }, { it.t.trackNo })).map { it.t }
        "new" -> l.sortedByDescending { it.mtime }
        "year" -> l.sortedByDescending { it.year }
        "oldest" -> l.filter { it.year > 0 }.sortedBy { it.year }
        "long" -> l.sortedByDescending { it.durationMs }
        else -> l
    }

    fun sortAlbums(l: List<Group>, sort: String): List<Group> = when (sort) {
        "za" -> l.sortedByCached(true) { sortKey(it.name) }
        "artist" -> l.sortedByCached { g -> val t = g.tracks.first(); sortKey(t.albumArtist.ifEmpty { t.artist }) }
        "new" -> l.sortedByCached(true) { g -> g.tracks.maxOf { it.mtime } }
        "year" -> l.sortedByCached(true) { g -> g.tracks.maxOf { it.year } }
        "oldest" -> l.filter { g -> g.tracks.any { it.year > 0 } }.sortedByCached { g -> g.tracks.filter { it.year > 0 }.minOf { it.year } }
        "long" -> l.sortedByDescending { it.tracks.size }
        else -> l
    }

    fun sortGroups(l: List<Group>, sort: String): List<Group> = when (sort) {
        "za" -> l.sortedByCached(true) { sortKey(it.name) }
        "long" -> l.sortedByDescending { it.tracks.size }
        else -> l
    }

    /**
     * [songs] is the whole library, A to Z (as Library.songs() returns it); [albums], [artists] and [genres] are asked for only by their own tab.
     * Text matching: exact first; only when nothing at all matches exactly, within a few typos.
     */
    fun run(q: ExploreQuery, songs: List<Track>, albums: () -> List<Group>, artists: () -> List<Group>, genres: () -> List<Group>): ExploreResult {
        val words = SearchRank.words(q.query)
        val fuzzy = words.isNotEmpty() && songs.none { exactHit(words, it) }
        fun hit(vararg fields: String): Boolean {
            if (words.isEmpty()) return true
            val text = fields.joinToString(" ")
            return if (fuzzy) SearchRank.fuzzyHit(words, text) else { val glue = SearchRank.words(text).joinToString(""); words.all { glue.contains(it) } }
        }
        fun match(t: Track): Boolean {
            if (q.genre.isNotEmpty() && !t.genre.trim().equals(q.genre, true)) return false
            if (q.decade.isNotEmpty() && decadeOf(t.year) != q.decade) return false
            // the server-read facts are looked up once per song, not once per filter
            val f = if (q.quality.isEmpty() && q.depth.isEmpty() && q.rate.isEmpty() && q.kbps.isEmpty() && q.codec.isEmpty()) null else AudioFacts.of(t)
            if (q.quality.isNotEmpty() && !(if (q.quality == "hires") t.hiRes else (q.quality == "lossless") == (f?.lossless ?: (trackExt(t) in LOSSLESS_EXT)))) return false
            if (q.depth.isNotEmpty() && !(f != null && f.depth > 0 && f.depth.toString() == q.depth)) return false
            if (q.rate.isNotEmpty() && !(f != null && rateBand(f.rate, q.rate))) return false
            if (q.codec.isNotEmpty() && codecFamily(t) != q.codec) return false
            if (q.kbps.isNotEmpty() && !(f != null && kbpsBand(f.kbps, q.kbps))) return false
            return true
        }
        fun letterOk(name: String) = q.letter.isEmpty() || letterOf(sortKey(name)) == q.letter
        return when (q.tab) {
            "Songs" -> {
                // the unfiltered list is the library itself (already A to Z): nothing to copy or sort
                val l = if (!q.filtering) songs else songs.filter { letterOk(it.title) && match(it) && (words.isEmpty() || (if (fuzzy) SearchRank.fuzzyHit(words, it.title + " " + it.artist + " " + it.album) else exactHit(words, it))) }
                ExploreResult(q.tab, sortSongs(l, q.sort), emptyList(), fuzzy, songs.size)
            }
            "Albums" -> ExploreResult(q.tab, emptyList(), sortAlbums(
                albums().filter { g -> letterOk(g.name) && hit(g.name, g.tracks.first().albumArtist.ifEmpty { g.tracks.first().artist }) && g.tracks.any { match(it) } }, q.sort), fuzzy)
            "Artists" -> ExploreResult(q.tab, emptyList(), sortGroups(
                artists().filter { letterOk(it.name) && hit(it.name) && it.tracks.any { t -> match(t) } }, q.sort), fuzzy)
            else -> ExploreResult(q.tab, emptyList(), sortGroups(
                genres().filter { letterOk(it.name) && hit(it.name) && it.tracks.any { t -> match(t) } }, q.sort), fuzzy)
        }
    }

    private fun exactHit(words: List<String>, t: Track): Boolean = words.all { t.searchGlue.contains(it) }
}
