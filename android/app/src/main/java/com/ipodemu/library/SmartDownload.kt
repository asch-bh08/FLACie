package com.ipodemu.library

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import java.net.URLEncoder

/** Who does a song's download: FLACie Web ([SERVER]), this phone's own code ([PHONE]), or nobody ([OFF]: the person switched the phone's downloads off). */
enum class DownloadRoute { SERVER, PHONE, OFF }

/**
 * The phone's half of smart downloads (docs/superpowers/specs/2026-11-01-smart-downloads-design.md). FLACie Web runs the race between the sources, files the
 * song and upgrades it to Hi-Res later; the phone only asks, then shows what the server says as it goes (the same progress lines and source label as a
 * download the phone does itself). The phone's own [DownloadCoordinator] stays as the backup for when FLACie Web isn't set up or can't be reached.
 */
object SmartDownload {
    /** What the person sees when FLACie Web can't do it and they have switched the phone's own downloads off. */
    const val OFF_MESSAGE = "Downloads need FLACie Web"

    /**
     * FLACie Web when it is set up and answered; otherwise the phone itself, unless the quiet switch ("Download on this phone when FLACie Web can't") is off.
     * [reachable] is simply whether starting the download on the server worked just now.
     */
    fun choose(webSetUp: Boolean, reachable: Boolean, phoneToo: Boolean): DownloadRoute =
        if (webSetUp && reachable) DownloadRoute.SERVER else if (phoneToo) DownloadRoute.PHONE else DownloadRoute.OFF

    /** A song three or more places down the queue is not needed yet, so it goes straight for Hi-Res; one needed soon (or asked for by hand) takes the quickest copy. */
    fun modeFor(placesAhead: Int, hiRes: Boolean): String = if (hiRes || placesAhead >= 3) "hires" else "quick"

    class Ask(val artist: String, val title: String, val album: String, val durationMs: Long, val mode: String)

    /** The server's stage names in the phone's: anything it doesn't know yet reads as searching, which is the harmless one. */
    private fun stageOf(s: String): DownloadStage = s.lowercase().let { n ->
        when {
            "request" in n -> DownloadStage.REQUESTED
            "download" in n || "transfer" in n -> DownloadStage.DOWNLOADING
            "import" in n || "fil" in n || "mov" in n -> DownloadStage.IMPORTING
            "scan" in n || "add" in n || "index" in n -> DownloadStage.SCANNING
            else -> DownloadStage.SEARCHING
        }
    }

    fun statusOf(j: FlacieWebClient.JobStatus, track: Track?): DownloadStatus = when {
        j.failed -> DownloadStatus(DownloadStage.FAILED, j.message.ifBlank { "FLACie Web could not download this" }, j.source)
        j.done -> DownloadStatus(DownloadStage.DONE, j.message, j.source, track)
        else -> DownloadStatus(stageOf(j.stage), j.message, j.source)
    }

    /** The song as the phone plays it: straight from the file mover's own HTTP endpoint, like a phone download (no waiting for Jellyfin to scan it). */
    fun trackFor(fileBase: String, path: String, artist: String, title: String, album: String, durationMs: Long): Track = Track(
        path = if (path.startsWith("http")) path else "${fileBase.trimEnd('/')}/file?path=${URLEncoder.encode(path, "UTF-8")}", title = title, artist = artist,
        album = album, albumArtist = artist, genre = "", trackNo = 0, discNo = 0, durationMs = durationMs, year = 0,
        isMusic = true, artKey = CoverLookup.key(artist, album, title), mtime = System.currentTimeMillis(), size = 0, source = TrackSource.CLOUD,
    )

    /**
     * Starts [ask] on the server with [start] and follows the job with [status] about once a second, reporting to [onUpdate] only when something changed
     * (plus a repeat every [heartbeatMs], so the download guard can tell a long job from a dead one). Returns false, having told nobody, when the server could
     * not be reached to start (the caller then downloads on the phone if it may); true once the job has been reported to its end, including the server
     * saying no, which is shown as it was said rather than tried again on the phone. [fileBase] is the file mover's address, for playing the finished song;
     * without it the song is picked up by the library's next refresh instead. Albums are not sent here yet: they stay on the phone's own code.
     */
    suspend fun run(
        ask: Ask, fileBase: String, start: () -> String, status: (String) -> FlacieWebClient.JobStatus, onUpdate: (DownloadStatus) -> Unit,
        pollMs: Long = 1_000, heartbeatMs: Long = 20_000, maxMisses: Int = 15, maxMs: Long = 30 * 60_000L,
    ): Boolean {
        val id = try { withContext(Dispatchers.IO) { start() } }
            catch (e: CancellationException) { throw e }
            catch (e: FlacieWebClient.Refused) { onUpdate(DownloadStatus(DownloadStage.FAILED, e.message ?: "FLACie Web said no")); return true }
            catch (_: Exception) { return false }
        val began = System.currentTimeMillis()
        var shown: DownloadStatus? = null; var shownAt = 0L; var misses = 0
        while (true) {
            val j = try { withContext(Dispatchers.IO) { status(id) } }
                catch (e: CancellationException) { throw e }
                catch (e: FlacieWebClient.JobGone) { onUpdate(DownloadStatus(DownloadStage.FAILED, e.message ?: "FLACie Web lost track of this download", shown?.source)); return true }
                catch (_: Exception) {
                    if (++misses >= maxMisses) { onUpdate(DownloadStatus(DownloadStage.FAILED, "Lost touch with FLACie Web. The download may still finish there.", shown?.source)); return true }
                    delay(pollMs); null
                }
            if (j == null) continue
            misses = 0
            val track = if (j.done && !j.failed && !j.path.isNullOrBlank() && fileBase.isNotBlank()) trackFor(fileBase, j.path, ask.artist, ask.title, ask.album, ask.durationMs) else null
            val s = statusOf(j, track)
            val now = System.currentTimeMillis()
            val last = shown
            if (last == null || last.stage != s.stage || last.message != s.message || last.source != s.source || now - shownAt >= heartbeatMs || s.stage == DownloadStage.DONE || s.stage == DownloadStage.FAILED) {
                onUpdate(s); shown = s; shownAt = now
            }
            if (s.stage == DownloadStage.DONE || s.stage == DownloadStage.FAILED) return true
            if (now - began > maxMs) { onUpdate(DownloadStatus(DownloadStage.FAILED, "This is taking very long. It may still finish on FLACie Web.", s.source)); return true }
            delay(pollMs)
        }
    }
}
