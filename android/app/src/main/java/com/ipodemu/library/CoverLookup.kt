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

    fun key(artist: String, album: String, title: String): String? {
        if (artist.isBlank() || (album.isBlank() && title.isBlank())) return null
        val raw = "$artist\n$album\n$title".take(150)
        return "it" + Base64.encodeToString(raw.toByteArray(), Base64.URL_SAFE or Base64.NO_WRAP or Base64.NO_PADDING)
    }

    /** Resolves an "it" key to image bytes (blocking). */
    fun fetch(key: String): ByteArray? {
        val parts = String(Base64.decode(key.removePrefix("it"), Base64.URL_SAFE or Base64.NO_WRAP or Base64.NO_PADDING)).split('\n')
        if (parts.size < 3) return null
        val (artist, album, title) = parts
        // Deezer first (generous rate limit); iTunes allows ~20 lookups a minute, which a screen of NAS songs used up
        val url = (if (album.isNotBlank()) deezer("search/album", "$artist $album", artist) else null)
            ?: (if (title.isNotBlank()) deezer("search", "$artist $title", artist) else null)
            ?: (if (album.isNotBlank()) albumArt(artist, album) else null) ?: song(artist, title)?.artUrl ?: return null
        return bytes(url)
    }

    /** The first Deezer result by this artist: an album's cover, or a song's album cover. */
    private fun deezer(endpoint: String, q: String, artist: String): String? {
        val body = bytes("https://api.deezer.com/$endpoint?q=${URLEncoder.encode(q, "UTF-8")}&limit=10") ?: return null
        val arr = try { JSONObject(String(body)).optJSONArray("data") } catch (_: Exception) { null } ?: return null
        val a = norm(artist)
        val hit = (0 until arr.length()).map { arr.getJSONObject(it) }.firstOrNull { o ->
            val n = norm(o.optJSONObject("artist")?.optString("name").orEmpty()); n.isNotEmpty() && (n.contains(a) || a.contains(n))
        } ?: return null
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
