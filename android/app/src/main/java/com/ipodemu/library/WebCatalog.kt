package com.ipodemu.library

import org.json.JSONArray
import org.json.JSONObject
import java.net.URLEncoder

/**
 * Search results for music that isn't in the library yet. Deezer's public API (free, no key) ranks by popularity, so
 * "blinding lights" lists The Weeknd first instead of the covers that share the title; iTunes Search is the fallback when
 * Deezer can't be reached. Albums carry their tracklist (with durations), which an album download needs.
 */
object WebCatalog {
    data class Song(val artist: String, val title: String, val album: String, val artUrl: String?, val durationMs: Long, val trackNo: Int, val discNo: Int)
    /** [id] is a Deezer album id, or an iTunes collection id when [itunes]. */
    data class Album(val id: Long, val artist: String, val title: String, val artUrl: String?, val trackCount: Int, val year: String,
                     val type: String = "", val itunes: Boolean = false) {
        /** "Single", "EP" or "Album" as shown in results. */
        val kind: String get() = when {
            type == "single" || title.endsWith("- Single") || (type.isEmpty() && trackCount in 1..3) -> "Single"
            type == "ep" || title.endsWith("- EP") || (type.isEmpty() && trackCount in 4..6) -> "EP"
            else -> "Album"
        }
        val cleanTitle: String get() = title.replace(Regex("""\s*-\s*(Single|EP)$"""), "")
    }

    /** Songs matching every typed word, most popular first, originals before remixes/covers/sped-up versions the user
     * didn't type, one entry per song. */
    fun songs(query: String, limit: Int = 30): List<Song> {
        val words = words(query)
        val raw = deezerSongs(query) ?: itunesSongs(query) ?: return emptyList()
        return raw.filter { s -> SearchRank.matches(query, s.title, s.artist, s.album) }
            // karaoke/tribute/cover acts sit below real recordings unless asked for ("Karaoke Carpool" names it in the artist)
            .sortedBy { variant(it.title, words) + (if (typedNone(coverActs, words) && coverActs.containsMatchIn("${it.artist} ${it.album} ${it.title}")) 2 else 0) }
            .distinctBy { norm(primaryArtist(it.artist)) + "|" + norm(baseTitle(it.title)) }.take(limit)
    }

    fun albums(query: String, limit: Int = 12): List<Album> {
        val raw = deezerAlbums(query) ?: itunesAlbums(query) ?: return emptyList()
        return raw.filter { a -> SearchRank.matches(query, a.title, a.artist) }
            .distinctBy { norm(it.artist) + "|" + norm(it.cleanTitle.replace(editionWords, "")) + "|" + it.kind }
            .take(limit)
    }

    /** [albums] plus, first, the album the top song comes from ("blinding lights" offers After Hours, not just same-named singles). */
    fun albumsWithTop(query: String, top: Song?): List<Album> {
        val base = albums(query)
        val own = top?.takeIf { it.album.isNotBlank() }?.let { s ->
            (deezerAlbums("${s.artist} ${s.album}") ?: itunesAlbums("${s.artist} ${s.album}"))
                ?.firstOrNull { norm(primaryArtist(it.artist)) == norm(primaryArtist(s.artist)) && norm(it.cleanTitle) == norm(s.album) }
        }
        return if (own == null) base else (listOf(own) + base).distinctBy { it.id }
    }

    /** An album's songs in disc/track order. */
    fun tracks(album: Album): List<Song> = if (album.itunes) itunesTracks(album.id) else {
        val arr = json("https://api.deezer.com/album/${album.id}/tracks?limit=200")?.optJSONArray("data") ?: JSONArray()
        objects(arr).map { o ->
            Song(o.optJSONObject("artist")?.optString("name").orEmpty().ifBlank { album.artist }, o.optString("title"), album.cleanTitle, album.artUrl,
                o.optLong("duration") * 1000, o.optInt("track_position"), o.optInt("disk_number").coerceAtLeast(1))
        }.sortedWith(compareBy({ it.discNo }, { it.trackNo }))
    }

    /** Songs that go with the given one: the artist's other well-known songs, then the best-known songs of artists their listeners also play
     * (Deezer's "related artists"), interleaved so one artist doesn't take over. Never the song itself or another release of it. */
    fun related(artist: String, title: String, limit: Int = 24): List<Song> {
        val lead = primaryArtist(artist)
        val hit = json("https://api.deezer.com/search?q=${enc("artist:\"$lead\" track:\"${baseTitle(title)}\"")}&limit=5")?.optJSONArray("data")
        var artistId = objects(hit ?: JSONArray()).firstOrNull { norm(primaryArtist(it.optJSONObject("artist")?.optString("name").orEmpty())) == norm(lead) }?.optJSONObject("artist")?.optLong("id") ?: 0L
        if (artistId == 0L) artistId = objects(json("https://api.deezer.com/search/artist?q=${enc(lead)}&limit=1")?.optJSONArray("data") ?: JSONArray()).firstOrNull()?.optLong("id") ?: 0L
        if (artistId == 0L) return emptyList()
        val self = norm(baseTitle(title))
        fun songs(o: JSONObject?) = objects(o?.optJSONArray("data") ?: JSONArray()).map { t ->
            val al = t.optJSONObject("album")
            Song(t.optJSONObject("artist")?.optString("name").orEmpty(), t.optString("title"), al?.optString("title").orEmpty(), al?.optString("cover_xl")?.ifBlank { null }, t.optLong("duration") * 1000, 0, 0)
        }.filter { norm(baseTitle(it.title)) != self && !(variantWords.any { w -> it.title.contains(w, true) } && Regex("""[(\[]""").containsMatchIn(it.title)) }
        val own = songs(json("https://api.deezer.com/artist/$artistId/top?limit=10"))
        val rel = objects(json("https://api.deezer.com/artist/$artistId/related?limit=8")?.optJSONArray("data") ?: JSONArray()).map { it.optLong("id") }.filter { it != 0L }.take(6)
        val lists = ArrayList<List<Song>>()
        lists.add(own.take(3))
        rel.forEach { lists.add(songs(json("https://api.deezer.com/artist/$it/top?limit=5")).take(3)) }
        lists.add(own.drop(3))
        val out = ArrayList<Song>()
        var i = 0
        while (out.size < limit && lists.any { i < it.size }) { lists.forEach { l -> if (i < l.size) out.add(l[i]) }; i++ }
        return out.distinctBy { norm(primaryArtist(it.artist)) + "|" + norm(baseTitle(it.title)) }.take(limit)
    }

    /** "Song (feat. X)" -> "Song"; used to match file names and fold duplicates. */
    fun baseTitle(t: String) = t.replace(Regex("""\s*[(\[](feat|ft|with)[^)\]]*[)\]]""", RegexOption.IGNORE_CASE), "").trim()

    // ---- Deezer ----

    private fun deezerSongs(q: String): List<Song>? {
        val arr = json("https://api.deezer.com/search?q=${enc(q)}&limit=50")?.optJSONArray("data") ?: return null
        return objects(arr).map { o ->
            val al = o.optJSONObject("album")
            Song(o.optJSONObject("artist")?.optString("name").orEmpty(), o.optString("title"), al?.optString("title").orEmpty(),
                al?.optString("cover_xl")?.ifBlank { null }, o.optLong("duration") * 1000, 0, 0)
        }
    }

    private fun deezerAlbums(q: String): List<Album>? {
        val arr = json("https://api.deezer.com/search/album?q=${enc(q)}&limit=30")?.optJSONArray("data") ?: return null
        return objects(arr).map { o ->
            Album(o.optLong("id"), o.optJSONObject("artist")?.optString("name").orEmpty(), o.optString("title"), o.optString("cover_xl").ifBlank { null },
                o.optInt("nb_tracks"), "", o.optString("record_type"))
        }
    }

    // ---- iTunes (fallback) ----

    private fun itunesSongs(q: String): List<Song>? {
        val arr = json("https://itunes.apple.com/search?term=${enc(q)}&media=music&entity=song&limit=50")?.optJSONArray("results") ?: return null
        return objects(arr).map(::itunesSong)
    }

    private fun itunesAlbums(q: String): List<Album>? {
        val arr = json("https://itunes.apple.com/search?term=${enc(q)}&media=music&entity=album&limit=30")?.optJSONArray("results") ?: return null
        return objects(arr).map { o ->
            Album(o.optLong("collectionId"), o.optString("artistName"), o.optString("collectionName"), big(o.optString("artworkUrl100")),
                o.optInt("trackCount"), o.optString("releaseDate").take(4), itunes = true)
        }
    }

    private fun itunesTracks(id: Long): List<Song> {
        val arr = json("https://itunes.apple.com/lookup?id=$id&entity=song&limit=200")?.optJSONArray("results") ?: return emptyList()
        return objects(arr).filter { it.optString("wrapperType") == "track" }.map(::itunesSong).sortedWith(compareBy({ it.discNo }, { it.trackNo }))
    }

    private fun itunesSong(o: JSONObject) = Song(
        o.optString("artistName"), o.optString("trackName"), o.optString("collectionName").replace(Regex("""\s*-\s*(Single|EP)$"""), ""),
        big(o.optString("artworkUrl100")), o.optLong("trackTimeMillis"), o.optInt("trackNumber"), o.optInt("discNumber"),
    )

    // ---- ranking ----

    private val variantWords = listOf("remix", "mix", "live", "acoustic", "instrumental", "karaoke", "sped", "slowed", "reverb", "nightcore",
        "version", "cover", "tribute", "style", "edit", "demo", "8d", "lullaby", "piano", "jazz")
    private val editionWords = Regex("""\s*[(\[](deluxe|expanded|anniversary|remaster)[^)\]]*[)\]]""", RegexOption.IGNORE_CASE)

    /** 1 when the title has extra words in brackets or after " - " (a remix, live take, cover...) that weren't typed. */
    private fun variant(title: String, typed: List<String>): Int {
        val extra = Regex("""[(\[]([^)\]]*)[)\]]|\s-\s(.*)$""").findAll(title).map { (it.groupValues[1] + it.groupValues[2]).lowercase() }
            .filterNot { it.startsWith("feat") || it.startsWith("ft.") || it.startsWith("with ") }.joinToString(" ")
        return if (variantWords.any { it in extra } && typed.none { it in extra }) 1 else 0
    }

    private val coverActs = Regex("""karaoke|tribute|lullaby|kidz bop|in the style of|made famous|cover|8.bit|piano version|instrumental""", RegexOption.IGNORE_CASE)
    private fun typedNone(r: Regex, typed: List<String>) = !r.containsMatchIn(typed.joinToString(" "))
    private fun words(q: String) = q.lowercase().split(' ', '-').filter { it.isNotBlank() }
    private fun norm(s: String) = s.lowercase().filter { it.isLetterOrDigit() }
    private fun big(u: String) = u.takeIf { it.isNotEmpty() }?.replace("100x100bb", "600x600bb")
    private fun enc(s: String) = URLEncoder.encode(s.trim(), "UTF-8")
    private fun objects(a: JSONArray) = (0 until a.length()).mapNotNull { a.optJSONObject(it) }
    private fun json(url: String): JSONObject? = CoverLookup.bytes(url)?.let { try { JSONObject(String(it)) } catch (_: Exception) { null } }
}
