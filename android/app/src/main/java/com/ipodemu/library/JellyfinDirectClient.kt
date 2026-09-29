package com.ipodemu.library

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL

/**
 * Talks to a Jellyfin server directly from the phone -- no PC or ipodsync involved, so playback
 * doesn't depend on anything else being turned on. Auth is a server-issued API key (Jellyfin
 * dashboard > Advanced > API Keys), sent as the X-Emby-Token header for every request here.
 *
 * Stream URLs this returns deliberately do NOT carry the key (unlike a plain HTML <audio src>,
 * which has no other way to authenticate): PlayerController attaches X-Emby-Token as a real HTTP
 * header per-request instead (see JellyfinAuthDataSource), so the key never ends up in a URL,
 * request log, or track path string.
 */
class JellyfinDirectClient {
    suspend fun testConnection(url: String, apiKey: String): Pair<Boolean, String?> = withContext(Dispatchers.IO) {
        try {
            val json = JSONObject(get("${base(url)}/System/Info", apiKey))
            true to json.optString("ServerName").ifEmpty { null }
        } catch (e: Exception) {
            false to (e.message ?: "Connection failed")
        }
    }

    /** Jellyfin's API is keyed per-user; this app just uses the first account on the server. */
    suspend fun firstUserId(url: String, apiKey: String): String? = withContext(Dispatchers.IO) {
        val arr = JSONArray(get("${base(url)}/Users", apiKey))
        if (arr.length() == 0) null else arr.getJSONObject(0).getString("Id")
    }

    /** The Jellyfin library is usually much bigger than one page, so this pages through the full
     * audio catalog (not just "recently added") via StartIndex/Limit until it's all been fetched. */
    suspend fun allAudio(url: String, apiKey: String, userId: String, pageSize: Int = 500): List<Track> = withContext(Dispatchers.IO) {
        val out = ArrayList<Track>()
        var startIndex = 0
        while (true) {
            val endpoint = "${base(url)}/Users/$userId/Items?IncludeItemTypes=Audio&Recursive=true&SortBy=SortName&Fields=Path&StartIndex=$startIndex&Limit=$pageSize"
            val json = JSONObject(get(endpoint, apiKey))
            val arr = json.optJSONArray("Items") ?: JSONArray()
            out += tracksFrom(url, arr)
            if (arr.length() == 0) break
            startIndex += arr.length()
            val total = json.optInt("TotalRecordCount", startIndex)
            if (startIndex >= total) break
        }
        out
    }

    suspend fun search(url: String, apiKey: String, userId: String, term: String, limit: Int = 50): List<Track> = withContext(Dispatchers.IO) {
        if (term.isBlank()) return@withContext emptyList()
        val q = java.net.URLEncoder.encode(term, "UTF-8")
        val json = JSONObject(get("${base(url)}/Users/$userId/Items?IncludeItemTypes=Audio&Recursive=true&SearchTerm=$q&Limit=$limit", apiKey))
        tracksFrom(url, json.optJSONArray("Items") ?: JSONArray())
    }

    private fun tracksFrom(url: String, arr: JSONArray): List<Track> {
        fun str(o: JSONObject, key: String) = if (o.isNull(key)) "" else fixMojibake(o.optString(key))
        return List(arr.length()) { i ->
            val o = arr.getJSONObject(i)
            val id = o.getString("Id")
            val artist = fixMojibake(if (!o.isNull("AlbumArtist") && o.optString("AlbumArtist").isNotEmpty()) o.optString("AlbumArtist")
                else o.optJSONArray("Artists")?.takeIf { it.length() > 0 }?.getString(0) ?: "")
            Track(
                path = "${base(url)}/Audio/$id/stream?static=true",
                title = str(o, "Name").ifEmpty { "(untitled)" },
                artist = artist,
                album = str(o, "Album"),
                albumArtist = artist,
                genre = "",
                trackNo = 0,
                discNo = 0,
                durationMs = o.optLong("RunTimeTicks") / 10_000L,
                year = 0,
                isMusic = true,
                artKey = artKeyOf(o),
                mtime = 0L,
                size = 0L,
                source = TrackSource.JELLYFIN,
                filePath = str(o, "Path"),
            )
        }
    }

    /** Scans just this artist's folder instead of the whole library, so a freshly-downloaded track
     * shows up as fast as possible: finds the artist's existing item (if Jellyfin already knows
     * about them from other albums) and asks for a recursive refresh of just that item's path via
     * /Library/Media/Updated. A brand-new artist with no existing Jellyfin item has no known path
     * to target, so that case falls back to a full library scan instead. */
    suspend fun scanArtistFolder(url: String, apiKey: String, artist: String): Unit = withContext(Dispatchers.IO) {
        val userId = firstUserId(url, apiKey) ?: return@withContext
        val q = java.net.URLEncoder.encode(artist, "UTF-8")
        val json = JSONObject(get("${base(url)}/Users/$userId/Items?IncludeItemTypes=MusicArtist&Recursive=true&SearchTerm=$q&Limit=1", apiKey))
        val items = json.optJSONArray("Items")
        val path = items?.takeIf { it.length() > 0 }?.getJSONObject(0)?.optString("Path")?.takeIf { it.isNotEmpty() }
        if (path == null) {
            post("${base(url)}/Library/Refresh", apiKey, null)
            return@withContext
        }
        val body = JSONObject().put("Updates", JSONArray().put(JSONObject().put("Path", path).put("UpdateType", "Modified")))
        post("${base(url)}/Library/Media/Updated", apiKey, body)
    }

    private fun base(url: String) = url.trimEnd('/')

    private fun get(url: String, apiKey: String): String {
        val conn = URL(url).openConnection() as HttpURLConnection
        conn.connectTimeout = 5000
        conn.readTimeout = 15000
        conn.requestMethod = "GET"
        conn.setRequestProperty("X-Emby-Token", apiKey)
        try {
            if (conn.responseCode !in 200..299) throw IOException("HTTP ${conn.responseCode}")
            return conn.inputStream.bufferedReader().use { it.readText() }
        } finally {
            conn.disconnect()
        }
    }

    private fun post(url: String, apiKey: String, body: JSONObject?) {
        val conn = URL(url).openConnection() as HttpURLConnection
        conn.connectTimeout = 5000
        conn.readTimeout = 15000
        conn.requestMethod = "POST"
        conn.setRequestProperty("X-Emby-Token", apiKey)
        if (body != null) {
            conn.doOutput = true
            conn.setRequestProperty("Content-Type", "application/json")
            conn.outputStream.use { it.write(body.toString().toByteArray(Charsets.UTF_8)) }
        }
        try {
            if (conn.responseCode !in 200..299) throw IOException("HTTP ${conn.responseCode}")
            conn.inputStream.close()
        } finally {
            conn.disconnect()
        }
    }
}

/** "jf<id>" of the item whose Primary image is the cover: the album's when it has one, else the track's own. */
internal fun artKeyOf(o: JSONObject): String? {
    val albumId = o.optString("AlbumId")
    if (albumId.isNotEmpty() && !o.isNull("AlbumPrimaryImageTag")) return "jf$albumId"
    if (o.optJSONObject("ImageTags")?.has("Primary") == true) return "jf" + o.getString("Id")
    return if (albumId.isNotEmpty()) "jf$albumId" else null
}
