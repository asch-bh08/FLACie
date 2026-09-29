package com.ipodemu.library

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL

/**
 * Talks to a Plex Media Server directly from the phone -- no PC involved. Auth is a server-issued
 * token (plex.tv account token, or one pulled from Preferences.xml on the server), sent as the
 * X-Plex-Token header on every request here.
 *
 * A track's streaming URL points straight at its original media part (no transcode -- Plex only
 * transcodes when you ask it to), and deliberately does NOT carry the token: PlayerController
 * attaches X-Plex-Token as a real HTTP header per-request instead, so the token never ends up in
 * a URL, request log, or track path string (same approach as JellyfinDirectClient).
 */
class PlexDirectClient {
    suspend fun testConnection(url: String, token: String): Pair<Boolean, String?> = withContext(Dispatchers.IO) {
        try {
            val mc = JSONObject(get("${base(url)}/library/sections", token)).getJSONObject("MediaContainer")
            true to mc.optString("title1").ifEmpty { null }
        } catch (e: Exception) {
            false to (e.message ?: "Connection failed")
        }
    }

    /** The key of this server's first music ("artist") library section, if it has one. */
    suspend fun firstMusicSectionKey(url: String, token: String): String? = withContext(Dispatchers.IO) {
        val mc = JSONObject(get("${base(url)}/library/sections", token)).getJSONObject("MediaContainer")
        val dirs = mc.optJSONArray("Directory") ?: return@withContext null
        for (i in 0 until dirs.length()) {
            val d = dirs.getJSONObject(i)
            if (d.optString("type") == "artist") return@withContext d.getString("key")
        }
        null
    }

    /** Pages through the whole music section via X-Plex-Container-Start/Size and MediaContainer's
     * totalSize, same reasoning as JellyfinDirectClient.allAudio: a "recent" endpoint would silently
     * cap the library, so this always walks the full section. */
    suspend fun allAudio(url: String, token: String, sectionKey: String, pageSize: Int = 500): List<Track> = withContext(Dispatchers.IO) {
        val out = ArrayList<Track>()
        var start = 0
        while (true) {
            val endpoint = "${base(url)}/library/sections/$sectionKey/all?type=10&X-Plex-Container-Start=$start&X-Plex-Container-Size=$pageSize"
            val mc = JSONObject(get(endpoint, token)).getJSONObject("MediaContainer")
            val arr = mc.optJSONArray("Metadata") ?: JSONArray()
            out += tracksFrom(url, arr)
            if (arr.length() == 0) break
            start += arr.length()
            val total = mc.optInt("totalSize", start)
            if (start >= total) break
        }
        out
    }

    suspend fun search(url: String, token: String, sectionKey: String, term: String, limit: Int = 50): List<Track> = withContext(Dispatchers.IO) {
        if (term.isBlank()) return@withContext emptyList()
        val q = java.net.URLEncoder.encode(term, "UTF-8")
        val endpoint = "${base(url)}/library/sections/$sectionKey/search?type=10&query=$q&X-Plex-Container-Size=$limit"
        val mc = JSONObject(get(endpoint, token)).getJSONObject("MediaContainer")
        tracksFrom(url, mc.optJSONArray("Metadata") ?: JSONArray())
    }

    private fun tracksFrom(url: String, arr: JSONArray): List<Track> {
        fun str(o: JSONObject, key: String) = if (o.isNull(key)) "" else o.optString(key)
        val out = ArrayList<Track>()
        for (i in 0 until arr.length()) {
            val o = arr.getJSONObject(i)
            val media = o.optJSONArray("Media") ?: continue
            if (media.length() == 0) continue
            val parts = media.getJSONObject(0).optJSONArray("Part") ?: continue
            if (parts.length() == 0) continue
            val part = parts.getJSONObject(0)
            val partKey = part.optString("key").takeIf { it.isNotEmpty() } ?: continue
            val artist = str(o, "grandparentTitle")
            out += Track(
                path = "${base(url)}$partKey",
                title = str(o, "title").ifEmpty { "(untitled)" },
                artist = artist,
                album = str(o, "parentTitle"),
                albumArtist = artist,
                genre = "",
                trackNo = o.optInt("index"),
                discNo = o.optInt("parentIndex"),
                durationMs = o.optLong("duration"),
                year = o.optInt("year"),
                isMusic = true,
                artKey = plexArtKeyOf(o),
                mtime = 0L,
                size = part.optLong("size"),
                source = TrackSource.PLEX,
            )
        }
        return out
    }

    private fun base(url: String) = url.trimEnd('/')

    private fun get(url: String, token: String): String {
        val conn = URL(url).openConnection() as HttpURLConnection
        conn.connectTimeout = 5000
        conn.readTimeout = 15000
        conn.requestMethod = "GET"
        conn.setRequestProperty("X-Plex-Token", token)
        conn.setRequestProperty("Accept", "application/json")
        try {
            if (conn.responseCode !in 200..299) throw IOException("HTTP ${conn.responseCode}")
            return conn.inputStream.bufferedReader().use { it.readText() }
        } finally {
            conn.disconnect()
        }
    }
}

/** "px<ratingKey>" of the album (or the track) that has a thumb. */
private fun plexArtKeyOf(o: JSONObject): String? {
    val album = o.optString("parentRatingKey")
    if (album.isNotEmpty() && o.optString("parentThumb").isNotEmpty()) return "px$album"
    if (o.optString("thumb").isNotEmpty()) return "px" + o.optString("ratingKey")
    return null
}
