package com.ipodemu.library

import com.ipodemu.Prefs
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull

enum class DownloadStage { REQUESTED, SEARCHING, DOWNLOADING, IMPORTING, SCANNING, DONE, FAILED }

data class DownloadStatus(val stage: DownloadStage, val message: String, val source: String? = null)

/**
 * Orchestrates "download this missing track": races an on-demand Soulseek search (via slskd, if
 * configured) against Lidarr's indexer-based search. Lidarr always runs too rather than waiting to
 * see if Soulseek pans out first, but with a live peer search this usually wins -- a popular track
 * easily has dozens of peers online, so it's normally the faster, primary path.
 *
 * The two sources are filed differently: Lidarr's own grabs go through Lidarr's own import (it
 * already knows the artist/album it searched for). A Soulseek win is just a single file, and
 * Lidarr's manual-import flatly refuses a lone track when it's expecting a whole album -- confirmed
 * live, this isn't a timing issue a retry fixes -- so instead it's moved directly (via SMB, same
 * share slskd downloads onto) into the folder Jellyfin scans, named `Artist/Artist - Title.ext`.
 * This needs NAS configured (Settings > NAS) pointing at that share; without it a Soulseek win can't
 * be filed anywhere and the request falls through to Lidarr's slower but self-sufficient path.
 */
class DownloadCoordinator(private val prefs: Prefs) {
    private val lidarr = LidarrClient()
    private val slskd = SlskdClient()
    private val jellyfin = JellyfinDirectClient()
    private val fileMover = FileMoverClient()

    suspend fun download(artist: String, title: String, album: String, onUpdate: (DownloadStatus) -> Unit) {
        onUpdate(DownloadStatus(DownloadStage.REQUESTED, "Requested \"$title\""))
        if (prefs.lidarrUrl.isBlank() || prefs.lidarrApiKey.isBlank()) {
            onUpdate(DownloadStatus(DownloadStage.FAILED, "Lidarr isn't configured (Settings > Lidarr)"))
            return
        }
        coroutineScope {
            val soulseekConfigured = prefs.slskdUrl.isNotBlank() && prefs.slskdApiKey.isNotBlank()
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
                if (!fileIntoLibrary(hit, artist, title)) return false
                finishWithJellyfinScan(artist, onUpdate, "soulseek", title)
                return true
            }
            false
        } catch (_: Exception) {
            false
        }
    }

    /** Moves the just-downloaded file from slskd's inbox straight into the folder Jellyfin scans, as
     * `Artist/Artist - Title.ext` -- an SMB move on the same share slskd's downloads land on (see the
     * class doc for why this doesn't go through Lidarr). Requires NAS to be configured; returns false
     * without it, since there's nowhere else to safely file a bare, untagged single track. slskd
     * flattens a peer's own folder structure down to just the immediate parent folder when it writes
     * the finished file to disk (confirmed against two live downloads), which is what `hit.filename`'s
     * last two path segments are used to reconstruct here. */
    private suspend fun fileIntoLibrary(hit: SlskdClient.FileResult, artist: String, title: String): Boolean {
        val remoteParts = hit.filename.replace('\\', '/').split('/').filter { it.isNotBlank() }
        val sourceLeaf = remoteParts.last()
        val sourceParentFolder = if (remoteParts.size >= 2) remoteParts[remoteParts.size - 2] else ""
        val inboxFolder = prefs.slskdDownloadPath.trim('/')
        val sourceFolder = if (sourceParentFolder.isNotBlank()) "$inboxFolder/$sourceParentFolder" else inboxFolder
        val ext = sourceLeaf.substringAfterLast('.', "mp3")
        fun clean(s: String) = s.replace(Regex("[\\\\/:*?\"<>|]"), "_").trim()
        val destFolder = "${prefs.nasFolder.trim('/')}/${clean(artist)}"
        val destName = "${clean(artist)} - ${clean(title)}.$ext"

        // Same-network SMB move first -- no extra network hop through the homelab's own server, and
        // works even if the file-mover service isn't set up. Falls back to the HTTP file-mover (which
        // works from anywhere, unlike SMB over the Funnel) when that fails or isn't configured.
        if (prefs.nasHost.isNotBlank() && prefs.nasShare.isNotBlank()) {
            try {
                withContext(Dispatchers.IO) {
                    NasSmb.moveFile(
                        prefs.nasUsername, prefs.nasPassword, prefs.nasDomain,
                        prefs.nasHost, prefs.nasShare, sourceFolder, sourceLeaf, destFolder, destName,
                    )
                }
                return true
            } catch (_: Exception) { /* try the file-mover fallback below */ }
        }
        if (prefs.fileMoverUrl.isNotBlank() && prefs.fileMoverApiKey.isNotBlank()) {
            try {
                fileMover.move(prefs.fileMoverUrl, prefs.fileMoverApiKey, "$sourceFolder/$sourceLeaf", "$destFolder/$destName")
                return true
            } catch (_: Exception) { return false }
        }
        return false
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
            // A search only gives us a song title, not an album -- find which album actually has it
            // (falling back to the caller's album name, then an artist-wide search) so only that one
            // album gets searched, instead of the whole artist's catalog. For a freshly-added artist,
            // Lidarr syncs its album/track metadata asynchronously (slower for a big catalog), so poll
            // for a bit rather than guessing a fixed delay -- a wrong guess is what silently fell back
            // to an artist-wide search here before.
            var targetAlbumId = lidarr.albumIdForTrack(url, key, watchArtistId, title)
            if (targetAlbumId == null && justAdded) {
                withTimeoutOrNull(15_000L) {
                    while (targetAlbumId == null) {
                        delay(1500)
                        targetAlbumId = lidarr.albumIdForTrack(url, key, watchArtistId, title)
                    }
                }
            }
            if (targetAlbumId == null && album.isNotBlank()) {
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

    private suspend fun finishWithJellyfinScan(artist: String, onUpdate: (DownloadStatus) -> Unit, source: String, title: String) {
        onUpdate(DownloadStatus(DownloadStage.SCANNING, "Refreshing Jellyfin...", source))
        try {
            val jfUrl = prefs.jellyfinUrl; val jfKey = prefs.jellyfinApiKey
            if (jfUrl.isNotBlank() && jfKey.isNotBlank()) jellyfin.scanArtistFolder(jfUrl, jfKey, artist)
        } catch (_: Exception) { /* a nice-to-have speed boost, not required for correctness -- the
            library's own periodic auto-refresh will pick the track up regardless */ }
        onUpdate(DownloadStatus(DownloadStage.DONE, "\"$title\" should appear within 45s", source))
    }
}
