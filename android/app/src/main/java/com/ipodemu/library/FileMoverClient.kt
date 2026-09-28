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

    private fun postForCode(url: String, apiKey: String, body: JSONObject): Int {
        val conn = URL("${url.trimEnd('/')}/move").openConnection() as HttpURLConnection
        conn.connectTimeout = 5000
        conn.readTimeout = 30000
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
