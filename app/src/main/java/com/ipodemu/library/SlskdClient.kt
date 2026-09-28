package com.ipodemu.library

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull
import org.json.JSONArray
import org.json.JSONObject
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL
import java.util.UUID

/**
 * Talks to slskd (a Soulseek daemon with a REST API) directly, as a faster first attempt before
 * falling back to Lidarr's indexer-based search: if a peer already has the exact file online right
 * now, Soulseek can be much quicker than waiting on a torrent/usenet release. Unlike Lidarr this is
 * a live, on-demand peer search -- there's no guarantee anyone has the track available at all, so
 * every call here is timeout-bounded and callers are expected to fall back to Lidarr when it comes
 * up empty. Auth is an API key (slskd's own `web.authentication.api_keys` config), sent as the
 * X-API-Key header, same secrets-in-Prefs pattern as the other direct-connect sources.
 *
 * NOTE: written against slskd's documented v0 API shape but not yet exercised against a live
 * instance -- see the app's setup notes for why (no slskd container has been deployed yet).
 */
class SlskdClient {
    data class FileResult(val username: String, val filename: String, val size: Long, val hasFreeUploadSlot: Boolean, val bitRate: Int?)

    suspend fun testConnection(url: String, apiKey: String): Pair<Boolean, String?> = withContext(Dispatchers.IO) {
        try {
            val json = JSONObject(get("${base(url)}/api/v0/application", apiKey))
            true to json.optJSONObject("version")?.optString("current")
        } catch (e: Exception) {
            false to (e.message ?: "Connection failed")
        }
    }

    /** Fires a live peer search and waits up to [timeoutMs], returning several ranked candidates
     * (free-upload-slot peers first, decent quality only -- lossless or >= 256kbps) rather than just
     * the single best one, so a caller can fall through to the next peer if the first turns out to
     * be slow or rejects the transfer, instead of giving up on Soulseek after one bad peer. */
    suspend fun searchCandidates(url: String, apiKey: String, artist: String, title: String, timeoutMs: Long = 25_000): List<FileResult> =
        withContext(Dispatchers.IO) {
            val searchId = UUID.randomUUID().toString()
            val query = "$artist $title".trim()
            post("${base(url)}/api/v0/searches", apiKey, JSONObject().apply {
                put("id", searchId)
                put("searchText", query)
            })
            withTimeoutOrNull(timeoutMs) {
                while (true) {
                    // A single poll's own HTTP call failing (one dropped packet over a phone's
                    // connection, a transient 502 from the Funnel, whatever) used to propagate
                    // straight out of this whole function and get swallowed by the caller's
                    // catch-all as "no results" -- killing an otherwise-winning search over one
                    // blip in what's ~8 polls in a 6s window. Confirmed live: an identical search
                    // (same searchText) run via curl against the same slskd instance moments later
                    // found 250 responses in ~4s, while the in-app search came back completely
                    // empty. A failed poll now just counts as "nothing yet" and the loop keeps
                    // trying until the real timeout.
                    val results = try { rankedResultsSoFar(url, apiKey, searchId) } catch (_: Exception) { emptyList() }
                    if (results.isNotEmpty()) return@withTimeoutOrNull results
                    delay(750)
                }
                @Suppress("UNREACHABLE_CODE") emptyList()
            } ?: emptyList()
        }

    private fun rankedResultsSoFar(url: String, apiKey: String, searchId: String): List<FileResult> {
        // slskd's search-detail endpoint omits the actual file listings unless explicitly asked for
        // (responseCount is present either way, but responses is just [] without this) -- confirmed
        // live against a real search: same endpoint, 250 responses reported, 0 returned without this.
        val json = JSONObject(get("${base(url)}/api/v0/searches/$searchId?includeResponses=true", apiKey))
        val responses = json.optJSONArray("responses") ?: return emptyList()
        val candidates = ArrayList<FileResult>()
        for (i in 0 until responses.length()) {
            val r = responses.getJSONObject(i)
            val username = r.optString("username")
            val freeSlot = r.optBoolean("hasFreeUploadSlot", false)
            val files = r.optJSONArray("files") ?: continue
            for (j in 0 until files.length()) {
                val f = files.getJSONObject(j)
                val name = f.optString("filename")
                if (!NasSmb.isAudio(name.substringAfterLast('\\').substringAfterLast('/'))) continue
                val bitRate = if (f.has("bitRate") && !f.isNull("bitRate")) f.optInt("bitRate") else null
                // "decent quality": lossless (no bitrate field, e.g. flac) or >= 256kbps lossy
                val decent = bitRate == null || bitRate >= 256
                if (!decent) continue
                candidates.add(FileResult(username, name, f.optLong("size"), freeSlot, bitRate))
            }
        }
        // Free-upload-slot peers first (won't queue behind someone else's transfer), then the rest.
        return candidates.sortedByDescending { it.hasFreeUploadSlot }
    }

    /** Enqueues the download; slskd writes it into its own configured downloads directory once complete. */
    suspend fun download(url: String, apiKey: String, file: FileResult) = withContext(Dispatchers.IO) {
        val body = JSONArray().put(JSONObject().apply { put("filename", file.filename); put("size", file.size) })
        post("${base(url)}/api/v0/transfers/downloads/${enc(file.username)}", apiKey, body)
        Unit
    }

    /** null while queued/in-progress, true on success, false on a failed/errored transfer. */
    suspend fun downloadSucceeded(url: String, apiKey: String, file: FileResult): Boolean? = withContext(Dispatchers.IO) {
        val json = JSONObject(get("${base(url)}/api/v0/transfers/downloads/${enc(file.username)}", apiKey))
        val dirs = json.optJSONArray("directories") ?: return@withContext null
        for (i in 0 until dirs.length()) {
            val files = dirs.getJSONObject(i).optJSONArray("files") ?: continue
            for (j in 0 until files.length()) {
                val f = files.getJSONObject(j)
                if (f.optString("filename") != file.filename) continue
                val state = f.optString("state")
                return@withContext when {
                    state.contains("Succeeded", true) -> true
                    state.contains("Completed", true) && state.contains("Succeeded", true) -> true
                    state.contains("Errored", true) || state.contains("Cancelled", true) || state.contains("Rejected", true) -> false
                    else -> null
                }
            }
        }
        null
    }

    private fun enc(s: String) = java.net.URLEncoder.encode(s, "UTF-8")
    private fun base(url: String) = url.trimEnd('/')

    private fun get(url: String, apiKey: String): String {
        val conn = URL(url).openConnection() as HttpURLConnection
        conn.connectTimeout = 4000
        conn.readTimeout = 8000
        conn.requestMethod = "GET"
        conn.setRequestProperty("X-API-Key", apiKey)
        try {
            if (conn.responseCode !in 200..299) throw IOException("HTTP ${conn.responseCode}")
            return conn.inputStream.bufferedReader().use { it.readText() }
        } finally {
            conn.disconnect()
        }
    }

    private fun post(url: String, apiKey: String, body: Any) {
        val conn = URL(url).openConnection() as HttpURLConnection
        conn.connectTimeout = 4000
        conn.readTimeout = 8000
        conn.requestMethod = "POST"
        conn.doOutput = true
        conn.setRequestProperty("X-API-Key", apiKey)
        conn.setRequestProperty("Content-Type", "application/json")
        conn.outputStream.use { it.write(body.toString().toByteArray(Charsets.UTF_8)) }
        try {
            if (conn.responseCode !in 200..299) throw IOException("HTTP ${conn.responseCode}")
            conn.inputStream.close()
        } finally {
            conn.disconnect()
        }
    }
}
