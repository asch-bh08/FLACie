package com.ipodemu.library

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL

/**
 * Talks to a Lidarr instance directly to request a track this app doesn't otherwise have, when a
 * search comes up empty. Lidarr manages music at Artist -> Album granularity (there's no
 * track-level search in its own model), so "download this track" here means: find/add the artist,
 * find the specific album if we can match it (a tighter, faster search than the whole artist's
 * catalog), and fall back to an artist-wide search otherwise. Auth is the API key from Lidarr's own
 * Settings > General page, sent as the X-Api-Key header on every request.
 */
class LidarrClient {
    data class ArtistLookup(val foreignArtistId: String, val artistName: String, val overview: String?)
    data class QueueItem(
        val id: Long, val title: String, val status: String, val trackedDownloadStatus: String?,
        val trackedDownloadState: String?, val artistId: Int?, val albumId: Int?,
        val sizeleft: Double, val size: Double, val downloadId: String?, val errorMessage: String?,
    )

    suspend fun testConnection(url: String, apiKey: String): Pair<Boolean, String?> = withContext(Dispatchers.IO) {
        try {
            val json = JSONObject(get("${base(url)}/api/v1/system/status", apiKey))
            true to json.optString("instanceName").ifEmpty { "Lidarr" }
        } catch (e: Exception) {
            false to (e.message ?: "Connection failed")
        }
    }

    /** MusicBrainz-backed artist search (Lidarr's own /artist/lookup), used to find the right artist
     * whether or not it's already in the library. */
    suspend fun lookupArtist(url: String, apiKey: String, term: String): List<ArtistLookup> = withContext(Dispatchers.IO) {
        if (term.isBlank()) return@withContext emptyList()
        val q = java.net.URLEncoder.encode(term, "UTF-8")
        val arr = JSONArray(get("${base(url)}/api/v1/artist/lookup?term=$q", apiKey))
        List(arr.length()) { i ->
            val o = arr.getJSONObject(i)
            ArtistLookup(o.getString("foreignArtistId"), o.optString("artistName"), o.optString("overview").ifEmpty { null })
        }
    }

    /** Artists already in the Lidarr library, so we don't re-add one that's already there. */
    suspend fun existingArtistId(url: String, apiKey: String, foreignArtistId: String): Int? = withContext(Dispatchers.IO) {
        val arr = JSONArray(get("${base(url)}/api/v1/artist", apiKey))
        for (i in 0 until arr.length()) {
            val o = arr.getJSONObject(i)
            if (o.optString("foreignArtistId") == foreignArtistId) return@withContext o.getInt("id")
        }
        null
    }

    /** Prefers a profile literally named "Lossless" -- this app's setup notes have that one edited
     * to exclude the 24-bit/96kHz "hi-res" quality tiers (needlessly huge for pop/rock catalog with
     * no audible benefit over standard FLAC), falling back to whatever profile comes first if a
     * Lidarr instance doesn't have one by that name. */
    private suspend fun qualityProfileId(url: String, apiKey: String): Int = withContext(Dispatchers.IO) {
        val arr = JSONArray(get("${base(url)}/api/v1/qualityprofile", apiKey))
        for (i in 0 until arr.length()) {
            val o = arr.getJSONObject(i)
            if (o.optString("name").equals("Lossless", ignoreCase = true)) return@withContext o.getInt("id")
        }
        arr.getJSONObject(0).getInt("id")
    }

    private suspend fun firstRootFolderPath(url: String, apiKey: String): String = withContext(Dispatchers.IO) {
        JSONArray(get("${base(url)}/api/v1/rootfolder", apiKey)).getJSONObject(0).getString("path")
    }

    /** Adds the artist as monitored, but does NOT ask Lidarr to search all of its missing albums --
     * that would search every album the artist has, not just the one with the track we actually
     * want. The caller matches and triggers a search for that one specific album instead. */
    suspend fun addArtist(url: String, apiKey: String, lookup: ArtistLookup): Int = withContext(Dispatchers.IO) {
        val profileId = qualityProfileId(url, apiKey)
        val rootFolder = firstRootFolderPath(url, apiKey)
        val body = JSONObject().apply {
            put("foreignArtistId", lookup.foreignArtistId)
            put("artistName", lookup.artistName)
            put("qualityProfileId", profileId)
            put("metadataProfileId", 1)
            put("rootFolderPath", rootFolder)
            put("monitored", true)
            put("addOptions", JSONObject().apply {
                put("monitor", "all")
                put("searchForMissingAlbums", false)
            })
        }
        JSONObject(post("${base(url)}/api/v1/artist", apiKey, body)).getInt("id")
    }

    /** This artist's albums, so a specific one matching the track's album name can be targeted. */
    suspend fun albumsFor(url: String, apiKey: String, artistId: Int): List<Pair<Int, String>> = withContext(Dispatchers.IO) {
        val arr = JSONArray(get("${base(url)}/api/v1/album?artistId=$artistId", apiKey))
        List(arr.length()) { i -> arr.getJSONObject(i).let { it.getInt("id") to it.optString("title") } }
    }

    /** Which album actually contains the requested track -- a search only gives us a song title, not
     * an album, so this is the real way to find the right one (matching by album name against an
     * empty/blank album string would just match whichever album happens to come first). */
    suspend fun albumIdForTrack(url: String, apiKey: String, artistId: Int, title: String): Int? = withContext(Dispatchers.IO) {
        val arr = JSONArray(get("${base(url)}/api/v1/track?artistId=$artistId", apiKey))
        val target = title.trim().lowercase()
        for (i in 0 until arr.length()) {
            val o = arr.getJSONObject(i)
            if (o.optString("title").trim().lowercase() == target) return@withContext o.optInt("albumId").takeIf { it != 0 }
        }
        for (i in 0 until arr.length()) {
            val o = arr.getJSONObject(i)
            val t = o.optString("title").trim().lowercase()
            if (t.contains(target) || target.contains(t)) return@withContext o.optInt("albumId").takeIf { it != 0 }
        }
        null
    }

    /** Tighter and faster than an artist-wide search when we can match the exact album. */
    suspend fun triggerAlbumSearch(url: String, apiKey: String, albumId: Int) = withContext(Dispatchers.IO) {
        post("${base(url)}/api/v1/command", apiKey, JSONObject().put("name", "AlbumSearch").put("albumIds", JSONArray().put(albumId)))
        Unit
    }

    suspend fun triggerArtistSearch(url: String, apiKey: String, artistId: Int) = withContext(Dispatchers.IO) {
        post("${base(url)}/api/v1/command", apiKey, JSONObject().put("name", "ArtistSearch").put("artistId", artistId))
        Unit
    }

    /** The live download queue -- polled tightly while a request is in flight so completion is
     * noticed within a few seconds, not on Lidarr's own slower default refresh cycle. */
    suspend fun queue(url: String, apiKey: String): List<QueueItem> = withContext(Dispatchers.IO) {
        val json = JSONObject(get("${base(url)}/api/v1/queue?pageSize=200", apiKey))
        val arr = json.optJSONArray("records") ?: JSONArray()
        List(arr.length()) { i ->
            val o = arr.getJSONObject(i)
            QueueItem(
                id = o.optLong("id"), title = o.optString("title"), status = o.optString("status"),
                trackedDownloadStatus = o.optString("trackedDownloadStatus").ifEmpty { null },
                trackedDownloadState = o.optString("trackedDownloadState").ifEmpty { null },
                artistId = if (o.has("artistId")) o.optInt("artistId") else null,
                albumId = if (o.has("albumId")) o.optInt("albumId") else null,
                sizeleft = o.optDouble("sizeleft", 0.0), size = o.optDouble("size", 0.0),
                downloadId = o.optString("downloadId").ifEmpty { null },
                errorMessage = o.optJSONArray("statusMessages")?.takeIf { it.length() > 0 }
                    ?.getJSONObject(0)?.optJSONArray("messages")?.takeIf { it.length() > 0 }?.getString(0),
            )
        }
    }

    /** Files Lidarr has downloaded but not yet filed into the library -- normally auto-imported, but
     * a manual trigger here is what actually gets it onto disk (and tagged) as fast as possible. */
    suspend fun manualImportCandidates(url: String, apiKey: String, downloadId: String): JSONArray = withContext(Dispatchers.IO) {
        JSONArray(get("${base(url)}/api/v1/manualimport?downloadId=$downloadId", apiKey))
    }

    /** For a file that landed outside Lidarr's own download client (e.g. a Soulseek download via
     * slskd) -- Lidarr can still match/tag it as long as it can see the folder, so this points it
     * at an arbitrary path instead of one of its own tracked downloads. */
    suspend fun manualImportCandidatesForFolder(url: String, apiKey: String, folder: String): JSONArray = withContext(Dispatchers.IO) {
        val q = java.net.URLEncoder.encode(folder, "UTF-8")
        JSONArray(get("${base(url)}/api/v1/manualimport?folder=$q", apiKey))
    }

    /** Applies Lidarr's own suggested match for each candidate file -- it already knows which
     * artist/album/track each file matches, we're just telling it to go ahead and file them. */
    suspend fun triggerManualImport(url: String, apiKey: String, candidates: JSONArray) = withContext(Dispatchers.IO) {
        if (candidates.length() == 0) return@withContext
        val files = JSONArray()
        for (i in 0 until candidates.length()) {
            val c = candidates.getJSONObject(i)
            files.put(JSONObject().apply {
                put("path", c.optString("path"))
                put("artistId", c.optJSONObject("artist")?.optInt("id") ?: JSONObject.NULL)
                put("albumId", c.optJSONObject("album")?.optInt("id") ?: JSONObject.NULL)
                put("albumReleaseId", c.optJSONObject("albumReleaseId") ?: JSONObject.NULL)
                put("quality", c.opt("quality") ?: JSONObject.NULL)
                put("importMode", "move")
            })
        }
        post("${base(url)}/api/v1/command", apiKey, JSONObject().put("name", "ManualImport").put("files", files))
        Unit
    }

    private fun base(url: String) = url.trimEnd('/')

    private fun get(url: String, apiKey: String): String {
        val conn = URL(url).openConnection() as HttpURLConnection
        conn.connectTimeout = 5000
        conn.readTimeout = 15000
        conn.requestMethod = "GET"
        conn.setRequestProperty("X-Api-Key", apiKey)
        try {
            if (conn.responseCode !in 200..299) throw IOException("HTTP ${conn.responseCode}")
            return conn.inputStream.bufferedReader().use { it.readText() }
        } finally {
            conn.disconnect()
        }
    }

    private fun post(url: String, apiKey: String, body: JSONObject): String {
        val conn = URL(url).openConnection() as HttpURLConnection
        conn.connectTimeout = 5000
        conn.readTimeout = 15000
        conn.requestMethod = "POST"
        conn.doOutput = true
        conn.setRequestProperty("X-Api-Key", apiKey)
        conn.setRequestProperty("Content-Type", "application/json")
        conn.outputStream.use { it.write(body.toString().toByteArray(Charsets.UTF_8)) }
        try {
            if (conn.responseCode !in 200..299) {
                val err = try { conn.errorStream?.bufferedReader()?.use { r -> r.readText() } } catch (_: Exception) { null }
                throw IOException("HTTP ${conn.responseCode}${err?.let { ": $it" } ?: ""}")
            }
            return conn.inputStream.bufferedReader().use { it.readText() }
        } finally {
            conn.disconnect()
        }
    }
}
