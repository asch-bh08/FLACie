package com.ipodemu.library

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL
import java.net.URLEncoder

/** A real iPod ipodsync sees plugged into the PC ("root" is its drive/mount path, e.g. "G:/"). */
class SyncDevice(val rootPath: String, val volumeLabel: String?, val hasDatabase: Boolean, val needsUserAction: Boolean = false)

class SyncLibrary(val tracks: List<Track>, val playlists: List<Group>)

/**
 * Talks to ipodsync's read-only HTTP API (see EDIT-PROTOCOL.md / ipodsync's Program.cs) over the
 * LAN or Tailscale. No auth, no writes yet -- this is the Sync-mode browse-only step.
 */
class SyncClient {
    suspend fun devices(host: String): List<SyncDevice> = withContext(Dispatchers.IO) {
        val arr = JSONArray(get("http://$host/api/devices"))
        List(arr.length()) { i ->
            val o = arr.getJSONObject(i)
            SyncDevice(o.getString("rootPath"), o.optString("volumeLabel").ifEmpty { null }, o.optBoolean("hasDatabase"), o.optBoolean("needsUserAction"))
        }
    }

    suspend fun library(host: String, deviceRoot: String): SyncLibrary = withContext(Dispatchers.IO) {
        val enc = URLEncoder.encode(deviceRoot, "UTF-8")
        val root = JSONObject(get("http://$host/api/library?root=$enc"))
        val rawTracks = root.getJSONArray("tracks")
        val byId = LinkedHashMap<Long, Track>()
        for (i in 0 until rawTracks.length()) {
            val t = rawTracks.getJSONObject(i)
            val id = t.getLong("id")
            byId[id] = Track(
                path = "sync://$host/${deviceRoot.hashCode()}/$id",
                title = t.optString("title").ifEmpty { "(untitled)" },
                artist = t.optString("artist"),
                album = t.optString("album"),
                albumArtist = t.optString("albumArtist").ifEmpty { t.optString("artist") },
                genre = t.optString("genre"),
                trackNo = t.optInt("trackNumber"),
                discNo = t.optInt("discNumber"),
                durationMs = t.optLong("lengthMs"),
                year = t.optInt("year"),
                isMusic = true,
                artKey = null,
                mtime = 0L,
                size = t.optLong("sizeBytes"),
                source = TrackSource.IPOD,
            )
        }
        val rawPlaylists = root.getJSONArray("playlists")
        val groups = ArrayList<Group>()
        for (i in 0 until rawPlaylists.length()) {
            val p = rawPlaylists.getJSONObject(i)
            if (p.optBoolean("isMaster")) continue
            val ids = p.getJSONArray("trackIds")
            val tracks = List(ids.length()) { byId[ids.getLong(it)] }.filterNotNull()
            if (tracks.isEmpty()) continue
            groups += Group(p.optString("name").ifEmpty { "(untitled playlist)" }, tracks, null)
        }
        SyncLibrary(byId.values.toList(), groups)
    }

    private fun get(url: String): String {
        val conn = URL(url).openConnection() as HttpURLConnection
        conn.connectTimeout = 5000
        conn.readTimeout = 15000
        conn.requestMethod = "GET"
        try {
            if (conn.responseCode !in 200..299) throw java.io.IOException("HTTP ${conn.responseCode}")
            return conn.inputStream.bufferedReader().use { it.readText() }
        } finally {
            conn.disconnect()
        }
    }
}
