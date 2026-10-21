package com.ipodemu.library

import com.ipodemu.Prefs
import org.json.JSONArray
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL

/**
 * Sharing a song or a playlist as a link: the FLACie server this phone is signed in to makes a share (POST /api/share, its songs by title and
 * artist) and answers with its link (/s/{id}). Whoever opens the link signs in to FLACie as themselves and plays the songs from their own library.
 * Null when there is no FLACie server to ask (the app then shares the song's name as text).
 */
object ShareLinks {
    fun body(kind: String, name: String, songs: List<Track>): String = JSONObject().apply {
        put("kind", kind); put("name", name)
        put("entries", JSONArray().apply { for (t in songs) put(JSONObject().put("p", t.path).put("t", t.title).put("a", t.artist)) })
    }.toString()

    /** Blocking: call it off the main thread. */
    fun create(prefs: Prefs, kind: String, name: String, songs: List<Track>): String? {
        if (prefs.flacieWebUrl.isBlank() || !prefs.hasJellyfinAccount || songs.isEmpty()) return null
        return try {
            val c = URL(prefs.flacieWebUrl.trimEnd('/') + "/api/share").openConnection() as HttpURLConnection
            c.connectTimeout = 6_000; c.readTimeout = 8_000; c.requestMethod = "POST"; c.doOutput = true
            c.setRequestProperty("X-Emby-Token", prefs.accountToken)
            c.setRequestProperty("X-Jellyfin-Server", prefs.accountServer.trimEnd('/'))
            c.setRequestProperty("Content-Type", "application/json")
            val b = body(kind, name, songs).toByteArray()
            c.setFixedLengthStreamingMode(b.size)
            try {
                c.outputStream.use { it.write(b) }
                if (c.responseCode != 200) null else JSONObject(c.inputStream.bufferedReader().use { it.readText() }).optString("url").takeIf { it.startsWith("http") }
            } finally { c.disconnect() }
        } catch (_: Exception) { null }
    }
}
