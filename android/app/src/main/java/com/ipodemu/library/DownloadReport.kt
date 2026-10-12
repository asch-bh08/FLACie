package com.ipodemu.library

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.delay
import org.json.JSONObject

/**
 * Telling FLACie Web about a finished download (`POST /api/downloaded`, same auth as `/api/played`): the phone's downloads are done by the
 * phone's own coordinator, so the web server never hears about them unless the phone says so. Optional (only when a FLACie Web host is
 * configured), fire and forget, one retry. Everything here is pure so it can be unit tested with a fake sender; the HTTP call itself is
 * [FlacieWebClient.reportDownloaded].
 */
object DownloadReport {
    /** One song to report. [path] is the track path exactly as the phone has it (the file mover's `/file?path=` URL); null when there is none (a Lidarr grab). */
    class Item(val artist: String, val title: String, val album: String, val durationMs: Long, val path: String?, val source: String?, val finishedAtMs: Long)

    /** The sources FLACie Web knows; anything else is left out of the report rather than guessed. */
    private val sources = setOf("soulseek", "lidarr", "ytdl", "archive", "audius", "jamendo")
    /** A whole album is reported song by song, but never more than this many per finished download (the server takes 200 reports an hour). */
    const val MAX_PER_DOWNLOAD = 40

    fun sourceId(raw: String?): String? = raw?.lowercase()?.takeIf { it in sources }

    /** The request body: `{ artist, title, album, durationMs, path, source, finishedAtMs }`; optional fields are left out when empty. */
    fun json(i: Item): String {
        val o = JSONObject().put("artist", i.artist).put("title", i.title).put("album", i.album).put("finishedAtMs", i.finishedAtMs)
        if (i.durationMs > 0) o.put("durationMs", i.durationMs)
        i.path?.takeIf { it.isNotBlank() }?.let { o.put("path", it) }
        sourceId(i.source)?.let { o.put("source", it) }
        return o.toString()
    }

    /**
     * What to report when a download finished: each song the file mover filed (with its playable path), or, for a grab that arrives through
     * Jellyfin later (Lidarr), the song that was asked for, without a path. Nothing without both an artist and a title (the server answers 400).
     */
    fun itemsFor(artist: String, title: String, album: String, durationMs: Long, status: DownloadStatus, nowMs: Long): List<Item> {
        val src = status.source
        val tracks = status.newTracks
        val items = if (tracks.isNotEmpty())
            tracks.map { Item(it.artist, it.title, it.album, it.durationMs, it.path, src, nowMs) }
        else listOf(Item(artist, title, album, durationMs, null, src, nowMs))
        return items.filter { it.artist.isNotBlank() && it.title.isNotBlank() }.take(MAX_PER_DOWNLOAD)
    }

    /**
     * Sends [item] with [attempt] (it returns the HTTP status and throws when the server cannot be reached). Success is any 2xx. Network errors
     * and 5xx are tried once more after [retryDelayMs]; a refusal (400, 401, 403, 404, 429) is final. Returns whether the server took it.
     * Never throws except for coroutine cancellation.
     */
    suspend fun deliver(item: Item, attempt: suspend (Item) -> Int, retryDelayMs: Long = 4_000L): Boolean {
        for (n in 0..1) {
            val code = try { attempt(item) } catch (e: CancellationException) { throw e } catch (_: Exception) { -1 }
            if (code in 200..299) return true
            val refused = code in 400..499
            if (refused || n == 1) return false
            delay(retryDelayMs)
        }
        return false
    }
}
