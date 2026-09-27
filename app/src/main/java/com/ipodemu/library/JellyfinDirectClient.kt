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
            val endpoint = "${base(url)}/Users/$userId/Items?IncludeItemTypes=Audio&Recursive=true&SortBy=SortName&StartIndex=$startIndex&Limit=$pageSize"
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
        fun str(o: JSONObject, key: String) = if (o.isNull(key)) "" else o.optString(key)
        return List(arr.length()) { i ->
            val o = arr.getJSONObject(i)
            val id = o.getString("Id")
            val artist = if (!o.isNull("AlbumArtist") && o.optString("AlbumArtist").isNotEmpty()) o.optString("AlbumArtist")
                else o.optJSONArray("Artists")?.takeIf { it.length() > 0 }?.getString(0) ?: ""
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
                artKey = null,
                mtime = 0L,
                size = 0L,
                source = TrackSource.JELLYFIN,
            )
        }
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
}
