package com.ipodemu.library

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL

/**
 * Talks to the small file-mover service on the homelab -- moves a file within its shared root (the
 * same one slskd downloads onto and Jellyfin's music library lives under), used to file a completed
 * Soulseek download when a direct SMB move (NasSmb) isn't reachable, e.g. away from home over the
 * Tailscale Funnel, which can't carry raw SMB. Auth is an API key sent as X-Api-Key, same pattern as
 * the other direct-connect sources.
 */
class FileMoverClient {
    /** Deliberately asks it to move a file that doesn't exist -- a 400 "not found" response proves
     * the key is valid and the service is actually reachable, without touching real files. */
    suspend fun testConnection(url: String, apiKey: String): Pair<Boolean, String?> = withContext(Dispatchers.IO) {
        try {
            val code = postForCode(url, apiKey, JSONObject().put("from", "__test__/__missing__").put("to", "__test__/__missing__"))
            if (code == 400) true to null else false to "Unexpected response (HTTP $code)"
        } catch (e: Exception) {
            false to (e.message ?: "Connection failed")
        }
    }

    suspend fun move(url: String, apiKey: String, from: String, to: String) = withContext(Dispatchers.IO) {
        val code = postForCode(url, apiKey, JSONObject().put("from", from).put("to", to))
        if (code != 200) throw IOException("move failed (HTTP $code)")
        Unit
    }

    /** Asks the service to download a public https [source] file straight into its root at [to] (the open sources: Internet Archive, Jamendo, Audius). */
    suspend fun fetch(url: String, apiKey: String, source: String, to: String) = withContext(Dispatchers.IO) {
        val code = postForCode(url, apiKey, JSONObject().put("url", source).put("to", to), "fetch", readTimeoutMs = 180_000)
        if (code != 200) throw IOException("fetch failed (HTTP $code)")
        Unit
    }

    /** The last-resort yt-dlp source behind the file mover: finds the song and files it at [to] + ".ext". Returns (extension, seconds) or null when it had none. */
    suspend fun ytdl(url: String, apiKey: String, artist: String, title: String, durationSec: Int, to: String): Pair<String, Int>? = withContext(Dispatchers.IO) {
        val conn = URL("${url.trimEnd('/')}/ytdl").openConnection() as HttpURLConnection
        conn.connectTimeout = 5000; conn.readTimeout = 200_000
        conn.requestMethod = "POST"; conn.doOutput = true
        conn.setRequestProperty("X-Api-Key", apiKey); conn.setRequestProperty("Content-Type", "application/json")
        conn.outputStream.use { it.write(JSONObject().put("artist", artist).put("title", title).put("durationSec", durationSec).put("to", to).toString().toByteArray(Charsets.UTF_8)) }
        try {
            val code = conn.responseCode
            if (code == 404) null   // the search really found no usable upload
            else if (code != 200) {
                val said = try { JSONObject((conn.errorStream ?: conn.inputStream).bufferedReader().readText()).optString("error") } catch (_: Exception) { "" }
                throw IOException(if (said.isNotBlank()) said else "HTTP $code")
            } else JSONObject(conn.inputStream.bufferedReader().readText()).let { j -> if (j.optBoolean("ok")) j.optString("ext").takeIf { it.isNotBlank() }?.let { it to j.optInt("durationSec") } else null }
        } finally { conn.disconnect() }
    }

    private fun postForCode(url: String, apiKey: String, body: JSONObject, path: String = "move", readTimeoutMs: Int = 30000): Int {
        val conn = URL("${url.trimEnd('/')}/$path").openConnection() as HttpURLConnection
        conn.connectTimeout = 5000
        conn.readTimeout = readTimeoutMs
        conn.requestMethod = "POST"
        conn.doOutput = true
        conn.setRequestProperty("X-Api-Key", apiKey)
        conn.setRequestProperty("Content-Type", "application/json")
        conn.outputStream.use { it.write(body.toString().toByteArray(Charsets.UTF_8)) }
        try {
            return conn.responseCode
        } finally {
            conn.disconnect()
        }
    }
}
