package com.ipodemu.library

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL
import java.util.UUID

/**
 * Talks to slskd (a Soulseek daemon with a REST API, auth by its X-API-Key). Soulseek is a live peer search, so the two
 * things that decide how fast a download is are which peer is picked and how long the search waits:
 * - slskd only hands back responses once a search completes, and by default that takes its full timeout. A search is
 *   stopped as soon as enough peers have answered, which is a second or two for anything popular.
 * - Peers are ranked by match quality first (the typed artist in the path, no unrequested remix/live/cover, a length
 *   close to the real song), then by a free upload slot, short queue and fast upload speed.
 */
class SlskdClient {
    data class FileResult(
        val username: String, val filename: String, val size: Long, val hasFreeUploadSlot: Boolean, val bitRate: Int?,
        val uploadSpeed: Long = 0, val queueLength: Int = 0, val lengthSec: Int = 0,
    ) {
        val leaf: String get() = filename.substringAfterLast('\\').substringAfterLast('/')
        val folder: String get() = filename.replace('\\', '/').substringBeforeLast('/', "")
        val lossless: Boolean get() = leaf.substringAfterLast('.').lowercase() in setOf("flac", "alac", "wav", "aiff", "ape", "wv")
    }

    /** One file's transfer as slskd reports it. [done]: true succeeded, false failed, null still going. */
    data class Transfer(val id: String, val bytes: Long, val done: Boolean?, val remotelyQueued: Boolean)

    suspend fun testConnection(url: String, apiKey: String): Pair<Boolean, String?> = withContext(Dispatchers.IO) {
        try {
            val json = JSONObject(get("${base(url)}/api/v0/application", apiKey))
            true to json.optJSONObject("version")?.optString("current")
        } catch (e: Exception) {
            false to (e.message ?: "Connection failed")
        }
    }

    /** Every decent-quality audio file peers offer for [query] (lossless or >= 256 kbps). Stops the search once [enough]
     * peers answered and a short grace period passed, or at [timeoutMs]. */
    suspend fun search(url: String, apiKey: String, query: String, timeoutMs: Long = 15_000, enough: Int = 40): List<FileResult> = withContext(Dispatchers.IO) {
        val id = UUID.randomUUID().toString()
        val body = JSONObject().apply {
            put("id", id); put("searchText", query.replace(Regex("""[^\p{L}\p{N}' ]"""), " ").replace(Regex("\\s+"), " ").trim())
            // seconds, counted by slskd from the last response rather than the start
            put("searchTimeout", (timeoutMs / 1000).toInt().coerceAtLeast(5)); put("responseLimit", 200); put("fileLimit", 20_000)
        }
        // slskd takes one search request at a time (429 for another), which an album's three parallel fill-ins hit
        for (attempt in 0 until 8) {
            try { post("${base(url)}/api/v0/searches", apiKey, body); break }
            catch (e: IOException) { if (e.message != "HTTP 429" || attempt == 7) throw e; delay(400L + attempt * 200) }
        }
        val start = System.currentTimeMillis()
        var firstSeen = 0L
        while (System.currentTimeMillis() - start < timeoutMs + 2_000) {
            delay(500)
            // a failed poll (one dropped packet over the Funnel) is just "nothing yet", not the end of the search
            val s = try { JSONObject(get("${base(url)}/api/v0/searches/$id", apiKey)) } catch (_: Exception) { continue }
            if (s.optBoolean("isComplete") || s.optString("state").contains("Completed")) break
            val n = s.optInt("responseCount")
            if (n > 0 && firstSeen == 0L) firstSeen = System.currentTimeMillis()
            val since = if (firstSeen == 0L) 0 else System.currentTimeMillis() - firstSeen
            // enough answers, or a quieter search that has had a few seconds to collect them
            if (n >= enough || (n >= 5 && since > 2_500) || (n > 0 && since > 5_000)) {
                try { put("${base(url)}/api/v0/searches/$id", apiKey) } catch (_: Exception) {}
                waitComplete(url, apiKey, id)
                break
            }
        }
        val json = try { JSONObject(get("${base(url)}/api/v0/searches/$id?includeResponses=true", apiKey)) } catch (_: Exception) { return@withContext emptyList() }
        try { delete("${base(url)}/api/v0/searches/$id", apiKey) } catch (_: Exception) {}
        parse(json.optJSONArray("responses") ?: return@withContext emptyList())
    }

    private suspend fun waitComplete(url: String, apiKey: String, id: String) {
        // responses are saved only once the stopped search finalizes
        repeat(20) {
            val done = try { JSONObject(get("${base(url)}/api/v0/searches/$id", apiKey)).let { it.optBoolean("isComplete") || it.optString("state").contains("Completed") } } catch (_: Exception) { false }
            if (done) return
            delay(250)
        }
    }

    private fun parse(responses: JSONArray): List<FileResult> {
        val out = ArrayList<FileResult>()
        for (i in 0 until responses.length()) {
            val r = responses.getJSONObject(i)
            val files = r.optJSONArray("files") ?: continue
            for (j in 0 until files.length()) {
                val f = files.getJSONObject(j)
                val name = f.optString("filename")
                if (!NasSmb.isAudio(name.substringAfterLast('\\').substringAfterLast('/'))) continue
                val bitRate = if (f.has("bitRate") && !f.isNull("bitRate")) f.optInt("bitRate") else null
                if (bitRate != null && bitRate < 256) continue
                out.add(FileResult(r.optString("username"), name, f.optLong("size"), r.optBoolean("hasFreeUploadSlot"), bitRate,
                    r.optLong("uploadSpeed"), r.optInt("queueLength"), if (f.isNull("length")) 0 else f.optInt("length")))
            }
        }
        return out
    }

    /** The best files for one song, best first, at most one per peer (so falling through to the next tries someone else). */
    fun rankSong(files: List<FileResult>, artist: String, title: String, durationSec: Int = 0): List<FileResult> {
        val titleWords = tokens(WebCatalog.baseTitle(title))
        if (titleWords.isEmpty()) return emptyList()
        val artistWords = tokens(primaryArtist(artist))
        val wanted = tokens("$artist $title").toSet()
        val ok = files.filter { f ->
            val leaf = tokens(f.leaf.substringBeforeLast('.')).toSet()
            titleWords.all { it in leaf } && unrequestedVariant(leaf, wanted) == null &&
                (durationSec == 0 || f.lengthSec == 0 || kotlin.math.abs(f.lengthSec - durationSec) <= 12) && f.size > 1_000_000
        }
        // the typed artist somewhere in the path (file or folder) rules out the same title by somebody else
        val byArtist = ok.filter { f -> tokens(f.filename).toSet().containsAll(artistWords) }
        val matched = byArtist.ifEmpty { if (artistWords.isEmpty()) ok else emptyList() }
        // lossless whenever anyone has it, however slow; MP3/AAC (320 kbps) only when no peer has a lossless copy at all
        return matched.filter { it.lossless }.ifEmpty { matched.filter { (it.bitRate ?: 0) >= 320 } }
            .sortedByDescending { peerScore(it) }
            .distinctBy { it.username }
    }

    /** An album as whole folders from single peers: each candidate folder with, per tracklist entry, its best file. */
    fun rankAlbumFolders(files: List<FileResult>, artist: String, album: String, titles: List<String>): List<List<FileResult?>> {
        val artistWords = tokens(primaryArtist(artist)).toSet()
        val albumWords = tokens(album.replace(Regex("""\s*[(\[][^)\]]*[)\]]"""), "")).toSet()
        val folders = files.groupBy { it.username + "\u0000" + it.folder }.values.filter { fs ->
            val path = tokens(fs.first().filename).toSet()
            (artistWords.isNotEmpty() && path.containsAll(artistWords)) || (albumWords.isNotEmpty() && path.containsAll(albumWords))
        }
        val need = maxOf(1, (titles.size * 0.6).toInt())
        return folders.mapNotNull { fs ->
            // longest titles pick first, so "Love Song" takes its file before "Love" can; then the shortest matching name is
            // the closest ("Intro" over "Intro (Reprise)"), lossless before lossy when a folder has both
            val used = HashSet<FileResult>()
            val picks = arrayOfNulls<FileResult>(titles.size)
            for (i in titles.indices.sortedByDescending { tokens(WebCatalog.baseTitle(titles[it])).size }) {
                val tw = tokens(WebCatalog.baseTitle(titles[i]))
                if (tw.isEmpty()) continue
                picks[i] = fs.filter { f -> f !in used && tokens(f.leaf.substringBeforeLast('.')).toSet().containsAll(tw) }
                    .sortedWith(compareBy({ it.leaf.substringBeforeLast('.').length }, { if (it.lossless) 0 else 1 })).firstOrNull()?.also { used.add(it) }
            }
            val found = picks.count { it != null }
            // an all-lossless folder beats any lossy one, then the most songs, then the fastest peer
            val lossless = picks.all { it == null || it.lossless }
            if (found < need) null else picks.toList() to ((if (lossless) 100_000 else 0) + found * 100 + peerScore(fs.first()))
        }.sortedByDescending { it.second }.map { it.first }
    }

    /** Free upload slot (it starts now instead of queueing), short queue, fast uploader. */
    private fun peerScore(f: FileResult): Int =
        (if (f.hasFreeUploadSlot) 500 else 0) - f.queueLength.coerceAtMost(50) * 8 + (f.uploadSpeed / 50_000).toInt().coerceAtMost(400)

    private fun unrequestedVariant(leaf: Set<String>, wanted: Set<String>): String? =
        listOf("remix", "live", "instrumental", "karaoke", "acapella", "cover", "sped", "slowed", "nightcore", "8d", "reverb", "edit", "demo", "mix")
            .firstOrNull { it in leaf && it !in wanted }

    private fun tokens(s: String) = s.lowercase().replace(Regex("""[^\p{L}\p{N}]+"""), " ").split(' ').filter { it.isNotBlank() && it != "the" }

    /** Queues files from one peer in a single request; slskd writes each into its downloads directory once complete. */
    suspend fun download(url: String, apiKey: String, files: List<FileResult>) = withContext(Dispatchers.IO) {
        if (files.isEmpty()) return@withContext
        val body = JSONArray().also { a -> files.forEach { f -> a.put(JSONObject().put("filename", f.filename).put("size", f.size)) } }
        post("${base(url)}/api/v0/transfers/downloads/${enc(files.first().username)}", apiKey, body)
    }

    suspend fun download(url: String, apiKey: String, file: FileResult) = download(url, apiKey, listOf(file))

    /** Current state of each of a peer's transfers, by remote filename. */
    suspend fun transfers(url: String, apiKey: String, username: String): Map<String, Transfer> = withContext(Dispatchers.IO) {
        val out = HashMap<String, Transfer>()
        val json = try { JSONObject(get("${base(url)}/api/v0/transfers/downloads/${enc(username)}", apiKey)) } catch (_: Exception) { return@withContext out }
        val dirs = json.optJSONArray("directories") ?: return@withContext out
        for (i in 0 until dirs.length()) {
            val files = dirs.getJSONObject(i).optJSONArray("files") ?: continue
            for (j in 0 until files.length()) {
                val f = files.getJSONObject(j)
                val state = f.optString("state")
                val done = when {
                    state.contains("Succeeded", true) -> true
                    state.contains("Errored", true) || state.contains("Cancelled", true) || state.contains("Rejected", true) || state.contains("TimedOut", true) -> false
                    else -> null
                }
                out[f.optString("filename")] = Transfer(f.optString("id"), f.optLong("bytesTransferred"), done, state.contains("Remotely", true))
            }
        }
        out
    }

    /** Stops a transfer and clears it from slskd's list (a stalled peer is dropped for the next one). */
    suspend fun cancel(url: String, apiKey: String, username: String, id: String) = withContext(Dispatchers.IO) {
        try { delete("${base(url)}/api/v0/transfers/downloads/${enc(username)}/${enc(id)}?remove=true", apiKey) } catch (_: Exception) {}
    }

    private fun enc(s: String) = java.net.URLEncoder.encode(s, "UTF-8").replace("+", "%20")
    private fun base(url: String) = url.trimEnd('/')

    private fun open(url: String, apiKey: String, method: String): HttpURLConnection =
        (URL(url).openConnection() as HttpURLConnection).apply {
            connectTimeout = 5000; readTimeout = 10000; requestMethod = method; setRequestProperty("X-API-Key", apiKey)
        }

    private fun get(url: String, apiKey: String): String {
        val conn = open(url, apiKey, "GET")
        try {
            if (conn.responseCode !in 200..299) throw IOException("HTTP ${conn.responseCode}")
            return conn.inputStream.bufferedReader().use { it.readText() }
        } finally { conn.disconnect() }
    }

    private fun send(url: String, apiKey: String, method: String, body: Any?) {
        val conn = open(url, apiKey, method)
        if (body != null) {
            conn.doOutput = true
            conn.setRequestProperty("Content-Type", "application/json")
            conn.outputStream.use { it.write(body.toString().toByteArray(Charsets.UTF_8)) }
        }
        try {
            if (conn.responseCode !in 200..299) throw IOException("HTTP ${conn.responseCode}")
        } finally { conn.disconnect() }
    }

    private fun post(url: String, apiKey: String, body: Any) = send(url, apiKey, "POST", body)
    private fun put(url: String, apiKey: String) = send(url, apiKey, "PUT", null)
    private fun delete(url: String, apiKey: String) = send(url, apiKey, "DELETE", null)
}
