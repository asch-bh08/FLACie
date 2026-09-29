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
        val url = (if (album.isNotBlank()) albumArt(artist, album) else null) ?: song(artist, title)?.artUrl ?: return null
        return bytes(url)
    }

    fun song(artist: String, title: String): Song? {
        if (title.isBlank()) return null
        val arr = search("$artist $title", "song") ?: return null
        val best = (0 until arr.length()).map { arr.getJSONObject(it) }
            .firstOrNull { norm(it.optString("artistName")).contains(norm(artist)) || norm(artist).contains(norm(it.optString("artistName"))) } ?: return null
        return Song(best.optString("collectionName").replace(Regex("""\s*-\s*(Single|EP)$"""), ""), big(best.optString("artworkUrl100")))
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
