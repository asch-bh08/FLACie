package com.ipodemu.library

import com.ipodemu.Prefs
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.withTimeoutOrNull
import java.net.URLEncoder

enum class DownloadStage { REQUESTED, SEARCHING, DOWNLOADING, IMPORTING, SCANNING, DONE, FAILED }

/** [newTrack] is set only on a Soulseek win's DONE status -- a fully-formed Track the caller can add
 * straight to the library, playable immediately over the file-mover's own HTTP endpoint (WAN-capable,
 * no Jellyfin/NAS scan wait). Lidarr's own wins don't have this: Lidarr does its own import with its
 * own naming, so the library still finds out about those via the normal Jellyfin scan + merge. */
data class DownloadStatus(val stage: DownloadStage, val message: String, val source: String? = null, val newTrack: Track? = null)

/**
 * Orchestrates "download this missing track": races an on-demand Soulseek search (via slskd, if
 * configured) against Lidarr's indexer-based search. Lidarr always runs too rather than waiting to
 * see if Soulseek pans out first, but with a live peer search this usually wins -- a popular track
 * easily has dozens of peers online, so it's normally the faster, primary path.
 *
 * The two sources are filed differently: Lidarr's own grabs go through Lidarr's own import (it
 * already knows the artist/album it searched for). A Soulseek win is just a single file, and
 * Lidarr's manual-import flatly refuses a lone track when it's expecting a whole album -- confirmed
 * live, this isn't a timing issue a retry fixes -- so instead it's moved directly into the folder
 * Jellyfin scans, named `Artist/Artist - Title.ext`, via the file-mover HTTP service (Settings >
 * Lidarr > File mover). This needs that configured; without it a Soulseek win can't be filed
 * anywhere and the request falls through to Lidarr's slower but self-sufficient path. (An SMB move
 * on the same share was tried first here, but never completed successfully across three separate
 * bug fixes in live testing -- dropped rather than sink more time into it.)
 */
class DownloadCoordinator(private val prefs: Prefs) {
    private val lidarr = LidarrClient()
    private val slskd = SlskdClient()
    private val jellyfin = JellyfinDirectClient()
    private val fileMover = FileMoverClient()

    /** [soulseekFirst]: a Soulseek search result the user picked -- try Soulseek alone, Lidarr only if that fails. */
    suspend fun download(artist: String, title: String, album: String, soulseekFirst: Boolean = false, onUpdate: (DownloadStatus) -> Unit) {
        onUpdate(DownloadStatus(DownloadStage.REQUESTED, "Requested \"${title.ifBlank { album.ifBlank { artist } }}\""))
        if (soulseekFirst && title.isNotBlank() && prefs.slskdUrl.isNotBlank() && prefs.slskdApiKey.isNotBlank()) {
            if (trySoulseek(artist, title, onUpdate)) return
            if (prefs.lidarrUrl.isBlank() || prefs.lidarrApiKey.isBlank()) { onUpdate(DownloadStatus(DownloadStage.FAILED, "Not found on Soulseek")); return }
            if (!tryLidarr(artist, title, album, onUpdate)) onUpdate(DownloadStatus(DownloadStage.FAILED, "Not found on Soulseek or Lidarr"))
            return
        }
        if (prefs.lidarrUrl.isBlank() || prefs.lidarrApiKey.isBlank()) {
            onUpdate(DownloadStatus(DownloadStage.FAILED, "Lidarr isn't configured (Settings > Lidarr)"))
            return
        }
        coroutineScope {
            // Soulseek only makes sense for a single track -- an album/artist-only request (blank
            // title, from a catalog search result rather than a track search) goes to Lidarr alone,
            // since there's no reasonable way to raced-search a whole album's worth of files over
            // Soulseek's single-file download flow.
            val soulseekConfigured = title.isNotBlank() && prefs.slskdUrl.isNotBlank() && prefs.slskdApiKey.isNotBlank()
            val soulseekDeferred = if (soulseekConfigured) async { trySoulseek(artist, title, onUpdate) } else null
            val lidarrDeferred = async { tryLidarr(artist, title, album, onUpdate) }

            val soulseekWon = soulseekDeferred?.await() ?: false
            if (soulseekWon) {
                lidarrDeferred.cancel()
                return@coroutineScope
            }
            if (!lidarrDeferred.await()) {
                onUpdate(DownloadStatus(DownloadStage.FAILED, "Not found on Lidarr's indexers${if (soulseekConfigured) " or Soulseek" else ""}"))
            }
        }
    }

    private suspend fun trySoulseek(artist: String, title: String, onUpdate: (DownloadStatus) -> Unit): Boolean {
        val url = prefs.slskdUrl; val key = prefs.slskdApiKey
        return try {
            onUpdate(DownloadStatus(DownloadStage.SEARCHING, "Searching Soulseek...", "soulseek"))
            // A popular track easily has dozens of peers online -- try several ranked candidates in
            // turn instead of giving up on Soulseek entirely because the single best-ranked peer was
            // slow or rejected the request.
            val candidates = slskd.searchCandidates(url, key, artist, title)
            for ((index, hit) in candidates.take(5).withIndex()) {
                onUpdate(DownloadStatus(
                    DownloadStage.DOWNLOADING,
                    "Downloading from a peer via Soulseek" + (if (index > 0) " (peer ${index + 1})..." else "..."),
                    "soulseek",
                ))
                try {
                    slskd.download(url, key, hit)
                } catch (_: Exception) {
                    continue   // this peer refused the request outright -- try the next one
                }
                val succeeded = withTimeoutOrNull(60_000L) {
                    var result = false
                    while (true) {
                        when (slskd.downloadSucceeded(url, key, hit)) {
                            true -> { result = true; break }
                            false -> break
                            null -> delay(1500)
                        }
                    }
                    result
                } ?: false
                if (!succeeded) continue
                onUpdate(DownloadStatus(DownloadStage.IMPORTING, "Filing into the library...", "soulseek"))
                val track = fileIntoLibrary(hit, artist, title) ?: return false
                finishWithJellyfinScan(artist, onUpdate, "soulseek", title, track)
                return true
            }
            false
        } catch (_: Exception) {
            false
        }
    }

    /** Moves the just-downloaded file from slskd's inbox straight into the folder Jellyfin scans, as
     * `Artist/Artist - Title.ext` -- via the HTTP file-mover service (see the class doc for why this
     * doesn't go through Lidarr). An earlier same-network SMB move (NasSmb.moveFile) was tried first
     * here, but jcifs-ng misbehaved on this exact operation across three different bug fixes and never
     * once completed successfully in live testing -- not worth more time on it, so this goes straight
     * to the file-mover now. Requires it to be configured; returns null without it, since there's
     * nowhere else to safely file a bare, untagged single track. slskd flattens a peer's own folder
     * structure down to just the immediate parent folder when it writes the finished file to disk
     * (confirmed against several live downloads), which is what `hit.filename`'s last two path
     * segments are used to reconstruct here.
     *
     * Returns a ready-to-play Track on success -- its `path` is the file-mover's own `/file?path=`
     * URL (the same service, same auth, that just moved it), not the on-disk path, so playback works
     * over WAN exactly like Jellyfin/Plex, without waiting on either of them to notice the file. */
    private suspend fun fileIntoLibrary(hit: SlskdClient.FileResult, artist: String, title: String): Track? {
        if (prefs.fileMoverUrl.isBlank() || prefs.fileMoverApiKey.isBlank()) return null
        val remoteParts = hit.filename.replace('\\', '/').split('/').filter { it.isNotBlank() }
        val sourceLeaf = remoteParts.last()
        val sourceParentFolder = if (remoteParts.size >= 2) remoteParts[remoteParts.size - 2] else ""
        val inboxFolder = prefs.slskdDownloadPath.trim('/')
        val sourceFolder = if (sourceParentFolder.isNotBlank()) "$inboxFolder/$sourceParentFolder" else inboxFolder
        val ext = sourceLeaf.substringAfterLast('.', "mp3")
        fun clean(s: String) = s.replace(Regex("[\\\\/:*?\"<>|]"), "_").trim()
        val destFolder = "${prefs.nasFolder.trim('/')}/${clean(artist)}"
        val destName = "${clean(artist)} - ${clean(title)}.$ext"
        val destRelPath = "$destFolder/$destName"
        return try {
            fileMover.move(prefs.fileMoverUrl, prefs.fileMoverApiKey, "$sourceFolder/$sourceLeaf", destRelPath)
            val playUrl = "${prefs.fileMoverUrl.trimEnd('/')}/file?path=${URLEncoder.encode(destRelPath, "UTF-8")}"
            Track(
                path = playUrl, title = title, artist = artist, album = "", albumArtist = artist, genre = "",
                trackNo = 0, discNo = 0, durationMs = 0, year = 0, isMusic = true, artKey = null,
                mtime = System.currentTimeMillis(), size = hit.size, source = TrackSource.CLOUD,
            )
        } catch (_: Exception) {
            null
        }
    }

    private suspend fun tryLidarr(artist: String, title: String, album: String, onUpdate: (DownloadStatus) -> Unit): Boolean {
        val url = prefs.lidarrUrl; val key = prefs.lidarrApiKey
        return try {
            onUpdate(DownloadStatus(DownloadStage.SEARCHING, "Looking up \"$artist\" on Lidarr...", "lidarr"))
            val best = lidarr.lookupArtist(url, key, artist).firstOrNull() ?: return false
            var artistId = lidarr.existingArtistId(url, key, best.foreignArtistId)
            val justAdded = artistId == null
            if (artistId == null) {
                onUpdate(DownloadStatus(DownloadStage.SEARCHING, "Adding \"${best.artistName}\" to Lidarr...", "lidarr"))
                artistId = lidarr.addArtist(url, key, best)
            }
            val watchArtistId = artistId
            // A track search only gives us a song title, not an album -- find which album actually has
            // it (falling back to the caller's album name, then an artist-wide search) so only that one
            // album gets searched, instead of the whole artist's catalog. For a freshly-added artist,
            // Lidarr syncs its album/track metadata asynchronously (slower for a big catalog), so poll
            // for a bit rather than guessing a fixed delay -- a wrong guess is what silently fell back
            // to an artist-wide search here before. A blank title means this is an album- or
            // artist-only request (from a catalog search result, not a track search) -- skip straight
            // to matching by album name instead of matching an empty string against every track.
            var targetAlbumId: Int? = null
            if (title.isNotBlank()) {
                targetAlbumId = lidarr.albumIdForTrack(url, key, watchArtistId, title)
                if (targetAlbumId == null && justAdded) {
                    withTimeoutOrNull(15_000L) {
                        while (targetAlbumId == null) {
                            delay(1500)
                            targetAlbumId = lidarr.albumIdForTrack(url, key, watchArtistId, title)
                        }
                    }
                }
            }
            if (targetAlbumId == null && album.isNotBlank()) {
                if (justAdded) withTimeoutOrNull(15_000L) { while (lidarr.albumsFor(url, key, watchArtistId).isEmpty()) delay(1500) }
                val albums = lidarr.albumsFor(url, key, watchArtistId)
                targetAlbumId = albums.firstOrNull { it.second.contains(album, ignoreCase = true) || album.contains(it.second, ignoreCase = true) }?.first
            }
            val albumId = targetAlbumId
            onUpdate(DownloadStatus(DownloadStage.SEARCHING, "Searching indexers...", "lidarr"))
            if (albumId != null) lidarr.triggerAlbumSearch(url, key, albumId) else lidarr.triggerArtistSearch(url, key, watchArtistId)

            onUpdate(DownloadStatus(DownloadStage.DOWNLOADING, "Waiting for a grab...", "lidarr"))
            var seenInQueue = false
            val outcome = withTimeoutOrNull(120_000L) {
                while (true) {
                    val items = lidarr.queue(url, key)
                    // By artist, not the specific album: Lidarr's indexer search can legitimately grab
                    // a different release than the one targeted above (e.g. a "Best Of" compilation
                    // that happens to contain the same track) and file it under a different album --
                    // scoping this to the exact target album would wait forever for a grab that already
                    // happened. We only trigger one album's search above (not a blanket multi-album
                    // one), so there's normally just one relevant item here regardless.
                    val item = items.firstOrNull { it.artistId == watchArtistId }
                    if (item != null) {
                        seenInQueue = true
                        if (item.errorMessage != null) return@withTimeoutOrNull false
                        val stuckAwaitingImport = item.trackedDownloadState?.contains("import", true) == true ||
                            item.trackedDownloadState?.contains("warning", true) == true
                        if (stuckAwaitingImport && item.downloadId != null) {
                            onUpdate(DownloadStatus(DownloadStage.IMPORTING, "Importing...", "lidarr"))
                            val candidates = lidarr.manualImportCandidates(url, key, item.downloadId)
                            if (candidates.length() > 0) lidarr.triggerManualImport(url, key, candidates)
                        }
                    } else if (seenInQueue) {
                        // it was queued and has now disappeared -- Lidarr's own auto-import already ran
                        return@withTimeoutOrNull true
                    }
                    delay(3000)
                }
                @Suppress("UNREACHABLE_CODE") false
            }
            if (outcome != true) return false
            finishWithJellyfinScan(artist, onUpdate, "lidarr", title)
            true
        } catch (_: Exception) {
            false
        }
    }

    private suspend fun finishWithJellyfinScan(artist: String, onUpdate: (DownloadStatus) -> Unit, source: String, title: String, newTrack: Track? = null) {
        onUpdate(DownloadStatus(DownloadStage.SCANNING, if (newTrack != null) "Adding to your library..." else "Refreshing Jellyfin...", source))
        // A Soulseek win (newTrack != null) is already visible via addDownloadedTrack -- Jellyfin's
        // own scan is no longer needed to surface it in this app, and triggering it anyway caused a
        // real, confirmed-live duplicate: this app's merge sometimes read Jellyfin's title back
        // mid-scan, before Jellyfin's own async metadata pass replaced a temporary filename-derived
        // stub with the real embedded-tag title, leaving a stale second entry with the wrong name.
        // Skipping the trigger here removes that race entirely; Jellyfin will still pick the file up
        // on its own regular schedule for anyone using its other clients, just not on this timeline.
        if (newTrack == null) {
            try {
                val jfUrl = prefs.jellyfinUrl; val jfKey = prefs.jellyfinApiKey
                if (jfUrl.isNotBlank() && jfKey.isNotBlank()) jellyfin.scanArtistFolder(jfUrl, jfKey, artist)
            } catch (_: Exception) { /* a nice-to-have speed boost, not required for correctness -- the
                library's own periodic auto-refresh will pick the track up regardless */ }
        }
        val message = if (newTrack != null) "\"$title\" is ready to play" else "\"$title\" should appear within 45s"
        onUpdate(DownloadStatus(DownloadStage.DONE, message, source, newTrack))
    }
}
