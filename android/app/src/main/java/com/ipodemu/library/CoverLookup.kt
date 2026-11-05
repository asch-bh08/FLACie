package com.ipodemu.library

import android.util.Base64
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL
import java.net.URLEncoder

/**
 * Cover art for tracks no server has art for (NAS files, Soulseek downloads), from the iTunes Search API (free, no key).
 * The lookup is encoded in the art key itself ("it" + base64 of artist/album/title), so it survives restarts with no registry.
 */
object CoverLookup {
    class Song(val album: String, val artUrl: String?)

    private val sizedPath = Regex("""/(\d{3,4})x(\d{3,4})(bb|-)""")
    /** The same catalogue cover at 250 px (Deezer's and iTunes' addresses name the size): what a list row or a card shows, a twentieth of the bytes of the 1000 px one. */
    fun small(url: String): String = sizedPath.replace(url) { m -> if (m.groupValues[1].toInt() > 250) "/250x250${m.groupValues[3]}" else m.value }

    /** An album name as a catalog search wants it: "[CD1]", "(Deluxe)" and the like taken off, as the server does (owner, 2 Nov: Voodoo People had no cover,
     *  "Their Law: The Singles 1990-2005 [CD1]" found nothing). */
    fun searchable(album: String): String = album.replace(Regex("""\s*[(\[][^)\]]*[)\]]"""), "").trim().ifEmpty { album }

    /** The file a cover is kept in. */
    fun fileName(key: String): String {
        val k = key   // (not tied to the lookup rules: that made every catalog cover be fetched again at once, and Deezer's limit left many missing)
        return if (k.length <= 120) "$k.jpg" else "h" + java.security.MessageDigest.getInstance("SHA-1").digest(k.toByteArray()).joinToString("") { "%02x".format(it) } + ".jpg"
    }

    fun key(artist: String, album: String, title: String): String? {
        if (artist.isBlank() || (album.isBlank() && title.isBlank())) return null
        val raw = "$artist\n$album\n$title".take(150)
        return "it" + java.util.Base64.getUrlEncoder().withoutPadding().encodeToString(raw.toByteArray())
    }

    /** Resolves an "it" key to image bytes (blocking). */
    fun fetch(key: String): ByteArray? {
        val parts = String(Base64.decode(key.removePrefix("it"), Base64.URL_SAFE or Base64.NO_WRAP or Base64.NO_PADDING)).split('\n')
        if (parts.size < 3) return null
        val (artist, album, title) = parts
        // Deezer first (generous rate limit); iTunes allows ~20 lookups a minute, which a screen of NAS songs used up. Deezer's quota answer ends the lookup
        // with CatalogBusy (the caller asks again soon) instead of falling through to iTunes and its tighter limit (owner, 5 Nov)
        val url = (if (album.isNotBlank()) deezer("search/album", "$artist ${searchable(album)}", artist, album, title) else null)
            ?: (if (title.isNotBlank()) deezer("search", "$artist $title", artist, "", title) else null)
            ?: (if (album.isNotBlank()) albumArt(artist, album) else null) ?: song(artist, title)?.artUrl ?: return null
        return bytes(url)
    }

    /** Deezer is over its quota (a 200 with {"error": ...}): "ask again in a moment", not "no cover" (owner, 5 Nov). */
    class CatalogBusy : Exception("catalog busy")

    private fun deezer(endpoint: String, q: String, artist: String, album: String, title: String): String? {
        val body = bytes("https://api.deezer.com/$endpoint?q=${URLEncoder.encode(q, "UTF-8")}&limit=25") ?: return null
        return pickDeezer(String(body), endpoint, artist, album, title)
    }

    private val compilation = Regex("""\b(now|hits?|best of|greatest|various|top \d+|mix|party|summer|workout|anthems?|ministry|essentials?|chart|collection|playlist|radio|karaoke|tribute)\b""", RegexOption.IGNORE_CASE)
    private fun normTitle(s: String) = s.lowercase().replace(Regex("""\s*[(\[](feat|ft|with)[^)\]]*[)\]]"""), "").replace(Regex("""\s+(feat|ft)\.?\s.*$"""), "").replace(Regex("[^a-z0-9]"), "")

    /**
     * The cover of the best Deezer result for a song, scored as the server does (MediaEndpoints.OnlineAlbumUncached): by this artist, and the album itself
     * (an album search) or the song's title (a song search). A song's own release before a compilation it is also on (Careless Whisper showed Wham!'s The
     * Final; owner, 2 Nov). A result matching neither album nor title is another release by the artist: no cover is better than a wrong one (the album search
     * for "Now That's What I Call Music! #1's" used to give any Nickelback album). Throws [CatalogBusy] on Deezer's quota answer.
     */
    internal fun pickDeezer(body: String, endpoint: String, artist: String, album: String, title: String): String? {
        val json = try { JSONObject(body) } catch (_: Exception) { return null }
        if (json.has("error")) throw CatalogBusy()
        val arr = json.optJSONArray("data") ?: return null
        val a = norm(artist)
        val byArtist = (0 until arr.length()).map { arr.getJSONObject(it) }.filter { o ->
            val n = norm(o.optJSONObject("artist")?.optString("name").orEmpty()); n.isNotEmpty() && (n.contains(a) || a.contains(n))
        }
        val song = endpoint == "search"
        val wantAlbum = norm(searchable(album))
        fun albumTitle(o: JSONObject) = if (song) o.optJSONObject("album")?.optString("title").orEmpty() else o.optString("title")
        fun score(o: JSONObject): Int {
            val al = albumTitle(o); var s = 0
            if (wantAlbum.isNotEmpty() && norm(searchable(al)) == wantAlbum) s += 100
            if (compilation.containsMatchIn(al) && !compilation.containsMatchIn(album)) s -= 50
            if (song && normTitle(o.optString("title")).let { it.isNotEmpty() && it == normTitle(title) }) s += 10
            if (song && album.isBlank() && pickSingle(al, title)) s += 20
            return s
        }
        // a plain title match on a compilation scores 10 - 50, but when it is all the catalog has the song's cover is still that song's (server: same rule
        // for an album-less lookup, as the title matched)
        val hit = byArtist.filter { score(it) >= 10 }.maxByOrNull { score(it) }
            ?: (if (song) byArtist.firstOrNull { normTitle(it.optString("title")).let { t -> t.isNotEmpty() && t == normTitle(title) } } else null)
            ?: return null
        return (hit.optString("cover_big").ifBlank { hit.optJSONObject("album")?.optString("cover_big").orEmpty() }).ifBlank { null }
    }

    fun song(artist: String, title: String): Song? {
        if (title.isBlank()) return null
        val arr = search("$artist $title", "song") ?: return null
        val best = (0 until arr.length()).map { arr.getJSONObject(it) }
            .firstOrNull { norm(it.optString("artistName")).contains(norm(artist)) || norm(artist).contains(norm(it.optString("artistName"))) } ?: return null
        return Song(best.optString("collectionName").replace(Regex("""\s*-\s*(Single|EP)$"""), ""), big(best.optString("artworkUrl100")))
    }

    /** The most popular song matching free text ("rap god" -> Eminem, "Rap God"), as artist to title. */
    fun topSong(query: String): Pair<String, String>? {
        val arr = search(query, "song") ?: return null
        val words = query.lowercase().split(' ').filter { it.isNotBlank() }
        // every typed word must appear in the credit or title, so a loose hit on one word is not offered
        val hits = (0 until arr.length()).map { arr.getJSONObject(it) }
            .filter { o -> "${o.optString("artistName")} ${o.optString("trackName")}".lowercase().let { h -> words.all { it in h } } }
        // the original over a variant ("Espresso" over "Espresso (On Vacation Version)", a remix or a cover "- Artist for
        // Babies"), unless the variant's words were typed; "(feat. ...)" is not a variant
        fun variant(title: String): Boolean {
            val extra = Regex("""[(\[]([^)\]]*)[)\]]|\s-\s(.*)$""").findAll(title).map { (it.groupValues[1] + it.groupValues[2]).trim().lowercase() }
                .filterNot { it.startsWith("feat") || it.startsWith("ft.") || it.startsWith("with ") }.joinToString(" ")
            return extra.isNotBlank() && !extra.split(' ').filter { it.length > 2 }.all { it in words }
        }
        return (hits.firstOrNull { !variant(it.optString("trackName")) } ?: hits.firstOrNull())
            ?.let { it.optString("artistName") to it.optString("trackName") }
            ?.takeIf { it.first.isNotBlank() && it.second.isNotBlank() }
    }

    private fun albumArt(artist: String, album: String): String? {
        val arr = search("$artist $album", "album") ?: return null
        val best = (0 until arr.length()).map { arr.getJSONObject(it) }
            .firstOrNull { norm(it.optString("artistName")).contains(norm(artist)) || norm(artist).contains(norm(it.optString("artistName"))) } ?: return null
        return big(best.optString("artworkUrl100"))
    }

    /** [album] is the song [title]'s own release: named after it ("Careless Whisper EP", "Careless Whisper (Remastered)"). */
    internal fun pickSingle(album: String, title: String): Boolean { val t = norm(title); return t.isNotEmpty() && norm(album).startsWith(t) }
    private fun norm(s: String) = s.lowercase().replace(Regex("[^a-z0-9]"), "")
    private fun big(u: String) = u.takeIf { it.isNotEmpty() }?.replace("100x100bb", "600x600bb")

    private fun search(term: String, entity: String): org.json.JSONArray? {
        val q = URLEncoder.encode(term, "UTF-8")
        val body = bytes("https://itunes.apple.com/search?term=$q&entity=$entity&limit=8") ?: return null
        return try { JSONObject(String(body)).optJSONArray("results") } catch (_: Exception) { null }
    }

    fun bytes(url: String, headers: Map<String, String> = emptyMap()): ByteArray? = try {
        val c = URL(url).openConnection() as HttpURLConnection
        c.connectTimeout = 6000; c.readTimeout = 10000; c.instanceFollowRedirects = true
        headers.forEach { (k, v) -> c.setRequestProperty(k, v) }
        try { if (c.responseCode in 200..299) c.inputStream.use { it.readBytes() } else null } finally { c.disconnect() }
    } catch (_: Exception) { null }
}
