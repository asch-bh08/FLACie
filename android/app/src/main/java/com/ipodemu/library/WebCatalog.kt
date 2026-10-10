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

    /** Songs that go with the given one: the artist's other well-known songs, then those of artists listeners of this artist also play
     * (MusicBrainz finds the artist, ListenBrainz's listening data names the similar ones, Deezer has their popular songs), interleaved so one
     * artist doesn't take over. Never the song itself or another release of it. */
    fun related(artist: String, title: String, limit: Int = 24): List<Song> {
        val lead = primaryArtist(artist)
        val similar = ArrayList<String>()
        try {
            val mb = json("https://musicbrainz.org/ws/2/artist?query=artist:${enc("\"$lead\"")}&fmt=json&limit=1")?.optJSONArray("artists")
            val mbid = mb?.optJSONObject(0)?.optString("id").orEmpty()
            if (mbid.isNotBlank()) {
                val raw = CoverLookup.bytes("https://labs.api.listenbrainz.org/similar-artists/json?artist_mbids=$mbid&algorithm=session_based_days_9000_session_300_contribution_5_threshold_15_limit_50_skip_30")
                val arr = raw?.let { try { JSONArray(String(it)) } catch (_: Exception) { null } }
                if (arr != null) objects(arr).map { it.optString("name") }.filter { it.isNotBlank() && norm(it) != norm(lead) }.take(8).forEach { similar.add(it) }
            }
        } catch (_: Exception) { }
        val self = norm(baseTitle(title))
        fun of(name: String, take: Int): List<Song> {
            val arr = json("https://api.deezer.com/search?q=${enc(name)}&limit=40")?.optJSONArray("data") ?: return emptyList()
            return objects(arr).map { t ->
                val al = t.optJSONObject("album")
                Song(t.optJSONObject("artist")?.optString("name").orEmpty(), t.optString("title"), al?.optString("title").orEmpty(), al?.optString("cover_xl")?.ifBlank { null }, t.optLong("duration") * 1000, 0, 0)
            }.filter { norm(primaryArtist(it.artist)) == norm(primaryArtist(name)) && norm(baseTitle(it.title)) != self && !(variantWords.any { w -> it.title.contains(w, true) } && Regex("""[(\[]""").containsMatchIn(it.title)) && !coverActs.containsMatchIn(it.album + " " + it.title) }
                .distinctBy { norm(baseTitle(it.title)) }.take(take)
        }
        android.util.Log.d("FLACie", "related: ${similar.size} similar artists for $lead")
        val lists = ArrayList<List<Song>>()
        lists.add(of(lead, 5)); similar.forEach { lists.add(of(it, 3)) }
        val out = ArrayList<Song>()
        var i = 0
        while (out.size < limit && lists.any { i < it.size }) { lists.forEach { l -> if (i < l.size) out.add(l[i]) }; i++ }
        return out.distinctBy { norm(primaryArtist(it.artist)) + "|" + norm(baseTitle(it.title)) }.take(limit)
    }

    /** The best-known songs of one artist (Deezer's top list for that artist), most popular first; empty when the artist is not found. */
    fun topSongs(artist: String, limit: Int = 25): List<String> {
        val lead = primaryArtist(artist); if (lead.isEmpty()) return emptyList()
        val found = json("https://api.deezer.com/search/artist?q=${enc(lead)}&limit=5")?.optJSONArray("data") ?: return emptyList()
        val hit = objects(found).firstOrNull { norm(primaryArtist(it.optString("name"))) == norm(lead) } ?: return emptyList()
        val top = json("https://api.deezer.com/artist/${hit.optLong("id")}/top?limit=$limit")?.optJSONArray("data") ?: return emptyList()
        return objects(top).map { it.optString("title") }.filter { t -> t.isNotBlank() && !(variantWords.any { w -> t.contains(w, true) } && Regex("""[(\[]""").containsMatchIn(t)) }
    }

    private val trailingGroup = Regex("""\s*[(\[]([^)\]]*)[)\]]\s*$""")
    // a different recording of the song: these stay in the title (a "(Live)" or "(Remix)" is not the song itself)
    private val versionWord = Regex("""\b(live|remix|mix|acoustic|instrumental|demo|cover|karaoke|session|unplugged|vip|rework|bootleg|extended|sped|slowed|reverb|a cappella|acapella|nightcore)\b""", RegexOption.IGNORE_CASE)
    private val partMarker = Regex("""^\s*(pt|part)\b""", RegexOption.IGNORE_CASE)
    private val dashNote = Regex("""\s+-\s+(?:\d{4}\s+)?(?:remaster(?:ed)?|from\b|single version|album version|original (?:motion picture )?soundtrack|mono\b|stereo\b|bonus|deluxe|explicit|clean|radio edit)[^-]*$""", RegexOption.IGNORE_CASE)

    /**
     * The song's own name, for searching and matching file names: "Song (feat. X)", "Song (From "Some Movie")", "Song (Remastered 2011)",
     * "Song - Remastered" and "Song [Deluxe]" all become "Song". A trailing "(Live)", "(Remix)", "(Acoustic)", "(Part 2)" ... is a different
     * recording or a different song, so it stays. Leading brackets ("(Don't Fear) The Reaper") are part of the title and stay. Same rules as FLACie Web.
     */
    fun baseTitle(t: String): String {
        var s = t.replace(Regex("""\s*[(\[](feat|ft|with|featuring)[^)\]]*[)\]]""", RegexOption.IGNORE_CASE), "").trim()
        repeat(4) {
            val m = trailingGroup.find(s) ?: return@repeat
            if (m.range.first == 0 || versionWord.containsMatchIn(m.groupValues[1]) || partMarker.containsMatchIn(m.groupValues[1])) return@repeat
            s = s.substring(0, m.range.first).trimEnd()
        }
        s = dashNote.replace(s, "").trim()
        return s.ifEmpty { t.trim() }
    }

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
