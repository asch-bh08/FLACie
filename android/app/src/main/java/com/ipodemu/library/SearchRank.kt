package com.ipodemu.library

import java.text.Normalizer

/**
 * Search that ranks, the same algorithm as FLACie Web (ipodsync/src/FLACie.Core/Search.cs). The old matcher glued title, artist and album into
 * one string and looked for each typed word inside it, so "no ma" matched "Bruno Mars" across the join, a one-letter word matched almost
 * everything, nothing was ranked beyond the title, and one typo meant no results. This works on whole words:
 * exact word > word start > inside a word > a dotted name typed apart (will.i.am) > one or two typos, weighted by where it hit (title, then
 * artist, then album), with a bonus when the query is the whole title or "artist title". Every typed word has to hit something; typos only
 * count when too little matches cleanly.
 */
object SearchRank {
    class Doc(title: String, artist: String, album: String) {
        val title = words(title); val artist = words(artist); val album = words(album)
        val titleGlue = this.title.joinToString(""); val artistGlue = this.artist.joinToString("")
    }
    class Query(q: String) {
        val tokens = words(q)
        val glued = tokens.joinToString("")
        val empty get() = tokens.isEmpty()
    }

    private val diacritics = Regex("""\p{Mn}+""")

    /**
     * Typo-tolerant match for Explore's filter box (used only when nothing matches exactly): every query word is inside the text, or within a few typos
     * of a word in it ("beyonse", "metalica", "daft pnuk"; swapped neighbouring letters count as one typo). Words of 3 letters or fewer stay exact,
     * 4 to 7 letters allow one typo, 8 or more allow two.
     */
    fun fuzzyHit(queryWords: List<String>, text: String): Boolean {
        if (queryWords.isEmpty()) return false
        val tokens = words(text)
        val glue = tokens.joinToString("")
        return queryWords.all { w ->
            glue.contains(w) || run {
                val tol = if (w.length <= 3) 0 else if (w.length <= 7) 1 else 2
                tol > 0 && tokens.any { t ->
                    Math.abs(t.length - w.length) <= tol + 2 && (typos(w, t, tol) <= tol || (t.length > w.length && typos(w, t.substring(0, w.length), tol) <= tol))
                }
            }
        }
    }

    /** Edit distance counting a swap of two neighbouring letters as one change; gives up (returns more than [max]) early. */
    private fun typos(a: String, b: String, max: Int): Int {
        if (Math.abs(a.length - b.length) > max) return max + 1
        var prev2 = IntArray(b.length + 1); var prev = IntArray(b.length + 1) { it }; var cur = IntArray(b.length + 1)
        for (i in 1..a.length) {
            cur[0] = i; var best = cur[0]
            for (j in 1..b.length) {
                val cost = if (a[i - 1] == b[j - 1]) 0 else 1
                var v = minOf(prev[j] + 1, cur[j - 1] + 1, prev[j - 1] + cost)
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) v = minOf(v, prev2[j - 2] + 1)
                cur[j] = v; if (v < best) best = v
            }
            if (best > max) return max + 1
            val t = prev2; prev2 = prev; prev = cur; cur = t
        }
        return prev[b.length]
    }

    /** Lower-case words, accents dropped; "&" and "+" read as "and"; dots and apostrophes inside a name are joiners ("will.i.am" is one word). */
    fun words(s: String): List<String> {
        if (s.isEmpty()) return emptyList()
        val d = Normalizer.normalize(s.lowercase().replace("&", " and ").replace("+", " and "), Normalizer.Form.NFD).replace(diacritics, "")
        val out = ArrayList<String>(); val sb = StringBuilder()
        for (c in d) {
            when {
                c.isLetterOrDigit() -> sb.append(c)
                c == '.' || c == '\'' || c == '’' || c == '`' -> {}
                else -> if (sb.isNotEmpty()) { out.add(sb.toString()); sb.clear() }
            }
        }
        if (sb.isNotEmpty()) out.add(sb.toString())
        return out
    }

    fun score(q: Query, d: Doc, fuzzy: Boolean): Int {
        if (q.empty) return 0
        var total = 0
        for (t in q.tokens) {
            val best = maxOf(best(t, d.title, 10, fuzzy), best(t, d.artist, 8, fuzzy), best(t, d.album, 4, fuzzy))
            if (best == 0) { total = -1; break }
            total += best
        }
        if (total < 0 && q.glued.length >= 4 && (runs(d.artist, q.glued) || runs(d.title, q.glued) || runs(d.album, q.glued))) total = 6 * q.tokens.size
        if (total <= 0) return 0
        val both = d.artistGlue + d.titleGlue; val both2 = d.titleGlue + d.artistGlue
        total += when {
            d.titleGlue == q.glued -> 60
            both == q.glued || both2 == q.glued -> 50
            d.titleGlue.startsWith(q.glued) -> 25
            d.artistGlue == q.glued -> 20
            else -> 0
        }
        return total
    }

    private fun runs(words: List<String>, glued: String): Boolean {
        for (i in words.indices) {
            var acc = ""
            for (j in i until words.size) {
                acc += words[j]
                if (acc == glued) return true
                if (!glued.startsWith(acc)) break
            }
        }
        return false
    }

    private fun best(t: String, words: List<String>, weight: Int, fuzzy: Boolean): Int {
        var best = 0
        for (w in words) {
            val s = when {
                w == t -> weight * 3
                t.length >= 2 && w.startsWith(t) -> weight * 2
                t.length >= 3 && w.contains(t) -> weight
                fuzzy && t.length >= 4 && near(t, w) -> maxOf(1, weight / 2)
                else -> continue
            }
            if (s > best) best = s
        }
        return best
    }

    private fun near(a: String, b: String): Boolean {
        if (a[0] != b[0]) return false
        val max = if (a.length >= 9) 2 else 1
        if (kotlin.math.abs(a.length - b.length) <= max && distance(a, b, max) <= max) return true
        return b.length > a.length && distance(a, b.substring(0, a.length), max) <= max
    }

    /** Damerau-Levenshtein distance, giving up once it passes [limit]. */
    fun distance(a: String, b: String, limit: Int): Int {
        if (kotlin.math.abs(a.length - b.length) > limit) return limit + 1
        var prev2 = IntArray(b.length + 1); var prev = IntArray(b.length + 1) { it }; var cur = IntArray(b.length + 1)
        for (i in 1..a.length) {
            cur[0] = i; var rowMin = cur[0]
            for (j in 1..b.length) {
                val cost = if (a[i - 1] == b[j - 1]) 0 else 1
                var v = minOf(prev[j] + 1, cur[j - 1] + 1, prev[j - 1] + cost)
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) v = minOf(v, prev2[j - 2] + 1)
                cur[j] = v; if (v < rowMin) rowMin = v
            }
            if (rowMin > limit) return limit + 1
            val t = prev2; prev2 = prev; prev = cur; cur = t
        }
        return prev[b.length]
    }

    /** The items that match, best first. Typos are tried only when fewer than [enough] match cleanly. */
    fun <T> rank(items: List<T>, doc: (T) -> Doc, query: String, take: Int, enough: Int = 4): List<T> {
        val q = Query(query)
        if (q.empty) return emptyList()
        fun run(fuzzy: Boolean) = items.mapNotNull { val s = score(q, doc(it), fuzzy); if (s > 0) it to s else null }
        var hits = run(false)
        if (hits.size < enough) hits = run(true)
        return hits.sortedByDescending { it.second }.take(take).map { it.first }
    }

    /** True when every typed word hits, with typos allowed: for filtering online catalog results. */
    fun matches(query: String, title: String, artist: String, album: String = "") = score(Query(query), Doc(title, artist, album), true) > 0
}

/** Search words worked out once per song/album instead of on every keystroke; starts over when the library's lists change. */
class SearchDocs {
    private val songs = java.util.IdentityHashMap<Track, SearchRank.Doc>()
    private val albums = java.util.IdentityHashMap<Group, SearchRank.Doc>()
    @Synchronized fun song(t: Track): SearchRank.Doc = songs.getOrPut(t) { SearchRank.Doc(t.title, t.artist, t.album) }
    @Synchronized fun album(g: Group): SearchRank.Doc = albums.getOrPut(g) { val t = g.tracks.firstOrNull(); SearchRank.Doc(g.name, t?.albumArtist?.ifEmpty { t.artist } ?: "", "") }
}

private var docsFor: Any? = null
private var docs = SearchDocs()
@Synchronized fun searchDocs(lib: Library): SearchDocs {
    val key = lib.songs()
    if (docsFor !== key) { docs = SearchDocs(); docsFor = key }
    return docs
}
