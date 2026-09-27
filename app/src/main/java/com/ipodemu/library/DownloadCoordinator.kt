package com.ipodemu.library

import com.ipodemu.Prefs
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.withTimeoutOrNull

enum class DownloadStage { REQUESTED, SEARCHING, DOWNLOADING, IMPORTING, SCANNING, DONE, FAILED }

data class DownloadStatus(val stage: DownloadStage, val message: String, val source: String? = null)

/**
 * Orchestrates "download this missing track": races an on-demand Soulseek search (via slskd, if
 * configured) against Lidarr's indexer-based search -- Soulseek only wins if a peer already has the
 * exact file online right now, so Lidarr always runs too rather than waiting to see if Soulseek
 * pans out first. Whichever source produces a file gets it imported through Lidarr (for proper
 * tagging/filing), then asks Jellyfin to scan just that artist's folder so the merged library picks
 * it up as fast as possible -- Library's own 45s auto-refresh (see Library.startAutoRefresh) is what
 * actually surfaces it in the UI without the user pulling to refresh.
 *
 * Lidarr's queue/import status strings are matched loosely (contains, not exact enum) since this
 * hasn't been exercised against a live Lidarr queue transition yet -- see the app's setup notes.
 */
class DownloadCoordinator(private val prefs: Prefs) {
    private val lidarr = LidarrClient()
    private val slskd = SlskdClient()
    private val jellyfin = JellyfinDirectClient()

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
            val hit = slskd.searchForTrack(url, key, artist, title) ?: return false
            onUpdate(DownloadStatus(DownloadStage.DOWNLOADING, "Downloading from a peer via Soulseek...", "soulseek"))
            slskd.download(url, key, hit)
            val succeeded = withTimeoutOrNull(25_000L) {
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
            if (!succeeded) return false
            onUpdate(DownloadStatus(DownloadStage.IMPORTING, "Handing off to Lidarr for import...", "soulseek"))
            val folder = prefs.slskdDownloadPath
            val candidates = lidarr.manualImportCandidatesForFolder(prefs.lidarrUrl, prefs.lidarrApiKey, folder)
            if (candidates.length() == 0) return false
            lidarr.triggerManualImport(prefs.lidarrUrl, prefs.lidarrApiKey, candidates)
            finishWithJellyfinScan(artist, onUpdate, "soulseek", title)
            true
        } catch (_: Exception) {
            false
        }
    }

    private suspend fun tryLidarr(artist: String, title: String, album: String, onUpdate: (DownloadStatus) -> Unit): Boolean {
        val url = prefs.lidarrUrl; val key = prefs.lidarrApiKey
        return try {
            onUpdate(DownloadStatus(DownloadStage.SEARCHING, "Looking up \"$artist\" on Lidarr...", "lidarr"))
            val best = lidarr.lookupArtist(url, key, artist).firstOrNull() ?: return false
            var artistId = lidarr.existingArtistId(url, key, best.foreignArtistId)
            if (artistId == null) {
                onUpdate(DownloadStatus(DownloadStage.SEARCHING, "Adding \"${best.artistName}\" to Lidarr...", "lidarr"))
                artistId = lidarr.addArtist(url, key, best)
                // addArtist already asked Lidarr to search for missing albums -- give it a moment
                // before also firing an explicit album search below, so we don't double-trigger.
                delay(2000)
            }
            val albums = lidarr.albumsFor(url, key, artistId)
            val albumMatch = albums.firstOrNull { it.second.contains(album, ignoreCase = true) || album.contains(it.second, ignoreCase = true) }
            onUpdate(DownloadStatus(DownloadStage.SEARCHING, "Searching indexers...", "lidarr"))
            if (albumMatch != null) lidarr.triggerAlbumSearch(url, key, albumMatch.first) else lidarr.triggerArtistSearch(url, key, artistId)

            onUpdate(DownloadStatus(DownloadStage.DOWNLOADING, "Waiting for a grab...", "lidarr"))
            val watchArtistId = artistId
            var lastDownloadId: String? = null
            var seenInQueue = false
            val outcome = withTimeoutOrNull(120_000L) {
                while (true) {
                    val items = lidarr.queue(url, key)
                    val item = items.firstOrNull { it.artistId == watchArtistId }
                    if (item != null) {
                        seenInQueue = true
                        lastDownloadId = item.downloadId
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
