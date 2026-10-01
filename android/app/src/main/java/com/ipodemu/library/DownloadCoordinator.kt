package com.ipodemu.library

import com.ipodemu.Prefs
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withPermit
import kotlinx.coroutines.withTimeoutOrNull
import kotlinx.coroutines.withContext
import java.net.URLEncoder

enum class DownloadStage { REQUESTED, SEARCHING, DOWNLOADING, IMPORTING, SCANNING, DONE, FAILED }

/** [newTracks] are set on a Soulseek win's DONE status: Tracks the caller can add straight to the library, playable at
 * once over the file mover's own HTTP endpoint (no Jellyfin scan wait). Lidarr wins arrive through the Jellyfin scan. */
data class DownloadStatus(val stage: DownloadStage, val message: String, val source: String? = null, val newTrack: Track? = null,
                          val newTracks: List<Track> = listOfNotNull(newTrack))

/**
 * "Download this": Soulseek (slskd) first, since a live peer usually has a popular song right now, with Lidarr's indexer
 * search as the fallback. A Soulseek file is moved by the file mover into the folder Jellyfin scans
 * (`Artist/Artist - Title.ext`, or `Artist/Album/NN - Title.ext` for an album) because Lidarr's manual import refuses a
 * lone track. Without the file mover a Soulseek win can't be filed, so it falls through to Lidarr.
 *
 * Speed: the best two peers are raced (the second starts only if the first hasn't sent anything within a few seconds),
 * a peer that stalls is dropped for the next, and an album comes as one folder from one peer where possible, with any
 * missing songs fetched one by one from other peers, three at a time.
 */
class DownloadCoordinator(private val prefs: Prefs) {
    private val lidarr = LidarrClient()
    private val slskd = SlskdClient()
    private val jellyfin = JellyfinDirectClient()
    private val fileMover = FileMoverClient()

    private val soulseekReady get() = prefs.slskdUrl.isNotBlank() && prefs.slskdApiKey.isNotBlank() && prefs.fileMoverUrl.isNotBlank() && prefs.fileMoverApiKey.isNotBlank()
    private val lidarrReady get() = prefs.lidarrUrl.isNotBlank() && prefs.lidarrApiKey.isNotBlank()

    /** One song. [durationMs] (from the catalog) rules out peers' files of a different length (a live take, a cover). */
    suspend fun download(artist: String, title: String, album: String, durationMs: Long = 0, onUpdate: (DownloadStatus) -> Unit) {
        onUpdate(DownloadStatus(DownloadStage.REQUESTED, "Requested \"${title.ifBlank { album.ifBlank { artist } }}\""))
        if (title.isBlank()) { // an album/artist with no tracklist: Lidarr alone
            if (!lidarrReady) onUpdate(DownloadStatus(DownloadStage.FAILED, "Lidarr isn't configured (Settings > Lidarr)"))
            else if (!tryLidarr(artist, title, album, onUpdate)) onUpdate(DownloadStatus(DownloadStage.FAILED, "Not found on Lidarr"))
            return
        }
        if (soulseekReady && trySoulseek(artist, title, album, (durationMs / 1000).toInt(), onUpdate)) return
        if (!lidarrReady) { onUpdate(DownloadStatus(DownloadStage.FAILED, if (soulseekReady) "Not found on Soulseek" else "Downloads aren't set up (Settings > Lidarr)")); return }
        if (!tryLidarr(artist, title, album, onUpdate)) onUpdate(DownloadStatus(DownloadStage.FAILED, "Not found on Soulseek or Lidarr"))
    }

    /** A whole album or EP: one peer's folder where possible, missing songs one by one; Lidarr if Soulseek finds nothing. */
    suspend fun downloadAlbum(album: WebCatalog.Album, onUpdate: (DownloadStatus) -> Unit) {
        val name = album.cleanTitle
        onUpdate(DownloadStatus(DownloadStage.REQUESTED, "Requested \"$name\""))
        val list = withContext(Dispatchers.IO) { WebCatalog.tracks(album) }
        if (soulseekReady && list.isNotEmpty()) {
            val tracks = trySoulseekAlbum(album, list, onUpdate)
            if (tracks.isNotEmpty()) {
                val msg = if (tracks.size == list.size) "\"$name\" is ready to play" else "\"$name\": ${tracks.size} of ${list.size} songs ready"
                onUpdate(DownloadStatus(DownloadStage.DONE, msg, "soulseek", newTracks = tracks))
                return
            }
        }
        if (!lidarrReady) { onUpdate(DownloadStatus(DownloadStage.FAILED, "\"$name\" wasn't found on Soulseek")); return }
        if (!tryLidarr(album.artist, "", name, onUpdate)) onUpdate(DownloadStatus(DownloadStage.FAILED, "\"$name\" wasn't found"))
    }

    private suspend fun trySoulseek(artist: String, title: String, album: String, durationSec: Int, onUpdate: (DownloadStatus) -> Unit): Boolean = try {
        onUpdate(DownloadStatus(DownloadStage.SEARCHING, "Searching Soulseek...", "soulseek"))
        val cands = slskd.rankSong(slskd.search(prefs.slskdUrl, prefs.slskdApiKey, "${primaryArtist(artist)} ${WebCatalog.baseTitle(title)}"), artist, title, durationSec)
        val hit = if (cands.isEmpty()) null else fetchFirst(cands) { onUpdate(DownloadStatus(DownloadStage.DOWNLOADING, it, "soulseek")) }
        if (hit == null) false else {
            onUpdate(DownloadStatus(DownloadStage.IMPORTING, "Filing into the library...", "soulseek"))
            // the file itself may be untagged: the album comes from the catalog so it groups and gets a cover
            val alb = album.ifBlank { withContext(Dispatchers.IO) { CoverLookup.song(artist, title) }?.album.orEmpty() }
            val track = file(hit, artist, title, alb, 0, 0, durationSec * 1000L, "${clean(artist)} - ${clean(title)}", null)
            if (track == null) false else { finishWithJellyfinScan(artist, onUpdate, "soulseek", title, track); true }
        }
    } catch (_: Exception) { false }

    private suspend fun trySoulseekAlbum(album: WebCatalog.Album, list: List<WebCatalog.Song>, onUpdate: (DownloadStatus) -> Unit): List<Track> {
        val url = prefs.slskdUrl; val key = prefs.slskdApiKey
        val name = album.cleanTitle
        val got = arrayOfNulls<SlskdClient.FileResult>(list.size)
        fun status(msg: String) = onUpdate(DownloadStatus(DownloadStage.DOWNLOADING, msg, "soulseek"))
        try {
            onUpdate(DownloadStatus(DownloadStage.SEARCHING, "Searching Soulseek for \"$name\"...", "soulseek"))
            val files = slskd.search(url, key, "${primaryArtist(album.artist)} ${name.replace(Regex("""\s*[(\[][^)\]]*[)\]]"""), "")}", enough = 80)
            for (folder in slskd.rankAlbumFolders(files, album.artist, name, list.map { it.title }).take(3)) {
                val want = folder.indices.filter { got[it] == null && folder[it] != null }
                if (want.isEmpty()) continue
                val ok = fetchBatch(want.map { folder[it]!! }) { status("Downloading \"$name\": $it") }
                for (i in want) if (folder[i] in ok) got[i] = folder[i]
                if (got.all { it != null }) break
            }
            // songs no folder had: each from its own best peer, three at a time
            // at most 8 single-song searches per album, so one album can't set off Soulseek's search limit
            val missing = list.indices.filter { got[it] == null }.take(8)
            if (missing.isNotEmpty()) {
                status("Finding ${missing.size} more song${if (missing.size == 1) "" else "s"}...")
                val gate = Semaphore(3)
                coroutineScope {
                    missing.map { i ->
                        async {
                            gate.withPermit {
                                val s = list[i]
                                val cands = slskd.rankSong(slskd.search(url, key, "${primaryArtist(s.artist)} ${WebCatalog.baseTitle(s.title)}", timeoutMs = 12_000),
                                    s.artist, s.title, (s.durationMs / 1000).toInt())
                                if (cands.isNotEmpty()) got[i] = fetchFirst(cands) {}
                            }
                        }
                    }.awaitAll()
                }
            }
        } catch (_: Exception) {}
        if (got.all { it == null }) return emptyList()
        onUpdate(DownloadStatus(DownloadStage.IMPORTING, "Filing \"$name\" into the library...", "soulseek"))
        val multiDisc = list.any { it.discNo > 1 }
        return list.indices.mapNotNull { i ->
            val hit = got[i] ?: return@mapNotNull null
            val s = list[i]
            val no = (if (multiDisc) "${s.discNo}-" else "") + s.trackNo.toString().padStart(2, '0')
            file(hit, album.artist, s.title, name, s.trackNo, s.discNo, s.durationMs, "$no - ${clean(s.title)}", clean(name), trackArtist = s.artist)
        }
    }

    /**
     * Downloads one song from the best of [cands]: starts the top peer, adds the next if it hasn't sent anything after 6s,
     * drops a peer that stalls (nothing for 20s) or fails, and cancels the rest once one finishes. Null if none delivered.
     */
    private suspend fun fetchFirst(cands: List<SlskdClient.FileResult>, progress: (String) -> Unit): SlskdClient.FileResult? {
        val url = prefs.slskdUrl; val key = prefs.slskdApiKey
        class Active(val f: SlskdClient.FileResult, val started: Long) { var bytes = 0L; var moved = started; var id: String? = null }
        val queue = ArrayDeque(cands.take(8))
        val active = ArrayList<Active>()
        suspend fun drop(a: Active) { active.remove(a); a.id?.let { slskd.cancel(url, key, a.f.username, it) } }
        suspend fun startNext() {
            while (queue.isNotEmpty()) {
                val f = queue.removeFirst()
                try { slskd.download(url, key, f); active.add(Active(f, System.currentTimeMillis())); return } catch (_: Exception) {}
            }
        }
        startNext()
        val deadline = System.currentTimeMillis() + 8 * 60_000
        while (active.isNotEmpty() && System.currentTimeMillis() < deadline) {
            delay(1000)
            val now = System.currentTimeMillis()
            for (a in active.toList()) {
                val t = slskd.transfers(url, key, a.f.username)[a.f.filename]
                if (t != null) a.id = t.id
                when {
                    t?.done == true -> { active.filter { it !== a }.forEach { drop(it) }; return a.f }
                    t?.done == false -> drop(a)
                    t != null && t.bytes > a.bytes -> { a.bytes = t.bytes; a.moved = now }
                    // a silent peer is swapped only when there is someone else to try; the last one keeps its chance
                    now - a.moved > 20_000 && (queue.isNotEmpty() || active.size > 1) -> drop(a)
                }
            }
            // race a second peer while the first is still queued remotely or silent; replace a dropped one
            if (queue.isNotEmpty() && (active.isEmpty() || (active.size == 1 && active[0].bytes == 0L && now - active[0].started > 6_000))) startNext()
            active.maxByOrNull { it.bytes }?.let { a ->
                progress(if (a.bytes == 0L) "Waiting for a peer..." else "Downloading ${(a.bytes * 100 / a.f.size.coerceAtLeast(1)).coerceAtMost(99)}%...")
            }
        }
        active.toList().forEach { drop(it) }
        return null
    }

    /** Downloads several files from one peer at once; returns the ones that arrived. Gives up on the rest after 30s with
     * no progress at all. */
    private suspend fun fetchBatch(files: List<SlskdClient.FileResult>, progress: (String) -> Unit): Set<SlskdClient.FileResult> {
        val url = prefs.slskdUrl; val key = prefs.slskdApiKey
        val user = files.first().username
        try { slskd.download(url, key, files) } catch (_: Exception) { return emptySet() }
        val total = files.sumOf { it.size }.coerceAtLeast(1)
        var last = 0L; var moved = System.currentTimeMillis()
        val deadline = System.currentTimeMillis() + 20 * 60_000
        while (System.currentTimeMillis() < deadline) {
            delay(1500)
            val ts = slskd.transfers(url, key, user)
            val mine = files.map { ts[it.filename] }
            val bytes = files.indices.sumOf { i -> if (mine[i]?.done == true) files[i].size else mine[i]?.bytes ?: 0L }
            if (bytes > last) { last = bytes; moved = System.currentTimeMillis() }
            progress("${mine.count { it?.done == true }} of ${files.size} songs, ${(bytes * 100 / total).coerceAtMost(99)}%")
            if (mine.all { it?.done != null }) break
            if (System.currentTimeMillis() - moved > 30_000) break
        }
        val ts = slskd.transfers(url, key, user)
        files.forEach { f -> ts[f.filename]?.takeIf { it.done == null }?.let { slskd.cancel(url, key, user, it.id) } }
        return files.filter { ts[it.filename]?.done == true }.toSet()
    }

    private fun clean(s: String) = s.replace(Regex("[\\\\/:*?\"<>|]"), "_").trim().trimEnd('.')

    /**
     * Moves a finished file from slskd's inbox into the folder Jellyfin scans, `Artist/[Album/]name.ext`, via the file
     * mover, and returns a Track that plays from the file mover's `/file?path=` URL (works over the internet, before
     * Jellyfin has scanned it). slskd keeps only the peer's immediate parent folder, which is how the inbox path is rebuilt.
     */
    private suspend fun file(hit: SlskdClient.FileResult, artist: String, title: String, album: String, trackNo: Int, discNo: Int,
                             durationMs: Long, name: String, albumFolder: String?, trackArtist: String = artist): Track? {
        val parts = hit.filename.replace('\\', '/').split('/').filter { it.isNotBlank() }
        val inbox = prefs.slskdDownloadPath.trim('/')
        val source = if (parts.size >= 2) "$inbox/${parts[parts.size - 2]}/${parts.last()}" else "$inbox/${parts.last()}"
        val ext = parts.last().substringAfterLast('.', "mp3")
        val dest = listOfNotNull(prefs.nasFolder.trim('/').ifBlank { null }, clean(artist), albumFolder).joinToString("/") + "/$name.$ext"
        return try {
            fileMover.move(prefs.fileMoverUrl, prefs.fileMoverApiKey, source, dest)
            Track(
                path = "${prefs.fileMoverUrl.trimEnd('/')}/file?path=${URLEncoder.encode(dest, "UTF-8")}", title = title, artist = trackArtist,
                album = album, albumArtist = artist, genre = "", trackNo = trackNo, discNo = discNo, durationMs = durationMs, year = 0,
                isMusic = true, artKey = CoverLookup.key(artist, album, if (albumFolder != null) "" else title),
                mtime = System.currentTimeMillis(), size = hit.size, source = TrackSource.CLOUD,
            )
        } catch (_: Exception) { null }
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
