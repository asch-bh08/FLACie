package com.ipodemu.library

import android.content.Context
import android.os.Handler
import android.os.Looper
import com.ipodemu.Prefs
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import org.json.JSONArray
import org.json.JSONObject
import java.io.File

class Group(val name: String, val tracks: List<Track>, val artKey: String?)

/** A search hit that isn't in the library yet -- from Lidarr's MusicBrainz-backed catalog (albums)
 * or a live Soulseek peer search (tracks), not from anything already owned. Search shows these in
 * their own section, distinct from owned results, each with a Download action. */
data class CatalogAlbum(val title: String, val artist: String, val imageUrl: String?)
data class CatalogTrack(val artist: String, val title: String, val imageUrl: String?)
data class CatalogResults(val albums: List<CatalogAlbum>, val tracks: List<CatalogTrack>) {
    companion object { val EMPTY = CatalogResults(emptyList(), emptyList()) }
}

class Library(ctx: Context, val art: ArtCache) {
    /** Where [tracks]/[playlists] currently come from. Switching does not forget the other side:
     * the local scan result stays cached in memory so flipping back to LOCAL is instant. */
    enum class Source { LOCAL, SYNC }
    @Volatile var source: Source = Source.LOCAL; private set
    /** Set while [loadSync] is in flight; cleared (with an error left in [syncError] on failure) when it settles. */
    @Volatile var syncing = false; private set
    @Volatile var syncError: String? = null; private set
    /** Which device/host Sync mode is currently showing, for display in Settings. */
    @Volatile var syncDeviceLabel: String? = null; private set
    /** Result of the last [findSyncDevices] call, and the host it searched (so a clean empty result
     * can be told apart from "never searched"). */
    @Volatile var syncDevices: List<SyncDevice> = emptyList(); private set
    @Volatile var discoveringDevices = false; private set
    @Volatile var lastSyncSearch: String? = null; private set

    /** Jellyfin/Plex/NAS connection state, for their Settings screens' own connect/status UI
     * (separate from [mergeJellyfin]/[mergePlex]/[mergeNas]'s silent background attempts on every rescan). */
    @Volatile var jellyfinConnecting = false; private set
    @Volatile var jellyfinConnected = false; private set
    @Volatile var jellyfinStatus: String? = null; private set

    @Volatile var plexConnecting = false; private set
    @Volatile var plexConnected = false; private set
    @Volatile var plexStatus: String? = null; private set

    @Volatile var nasConnecting = false; private set
    @Volatile var nasConnected = false; private set
    @Volatile var nasStatus: String? = null; private set

    @Volatile var lidarrConnecting = false; private set
    @Volatile var lidarrConnected = false; private set
    @Volatile var lidarrStatus: String? = null; private set

    @Volatile var slskdConnecting = false; private set
    @Volatile var slskdConnected = false; private set
    @Volatile var slskdStatus: String? = null; private set

    @Volatile var fileMoverConnecting = false; private set
    @Volatile var fileMoverConnected = false; private set
    @Volatile var fileMoverStatus: String? = null; private set

    @Volatile var tracks: List<Track> = emptyList(); private set
    /** Playlists read from .m3u/.m3u8 files: name -> track paths. Local-source only. */
    @Volatile var m3uPlaylists: Map<String, List<String>> = emptyMap(); private set
    /** Playlists as reported by ipodsync (real named playlists, not folder-derived). Sync-source only. */
    @Volatile private var syncGroups: List<Group> = emptyList()
    @Volatile private var localTracks: List<Track> = emptyList()
    @Volatile private var localM3u: Map<String, List<String>> = emptyMap()

    @Volatile var scanning = false; private set
    @Volatile var scanCount = 0; private set
    @Volatile var loaded = false; private set
    var onChange: (() -> Unit)? = null
    private val observers = java.util.concurrent.CopyOnWriteArrayList<() -> Unit>()
    /** Extra listeners (Compose UI); returns a function that removes the listener. */
    fun observe(fn: () -> Unit): () -> Unit { observers.add(fn); return { observers.remove(fn) } }

    private val main = Handler(Looper.getMainLooper())
    private val scope = CoroutineScope(Dispatchers.Default)
    private val cacheFile = File(ctx.filesDir, "library.json")
    private var job: Job? = null
    private val scanner = Scanner(ctx, art)
    private val sync = SyncClient()
    private val jellyfin = JellyfinDirectClient()
    @Volatile private var jellyfinUserId: String? = null
    private val plex = PlexDirectClient()
    @Volatile private var plexSectionKey: String? = null
    private val nas = NasDirectClient()
    private val lidarr = LidarrClient()
    private val slskd = SlskdClient()
    private val fileMover = FileMoverClient()
    private val prefs = Prefs(ctx)
    private val downloader = DownloadCoordinator(prefs)
    /** Jellyfin/Plex/NAS tracks -- each both folded into [tracks] (deduped against whatever else is
     * showing) and exposed here on its own, for a dedicated library section (like Playlists/Artists/
     * etc) separate from the blended view. Kept cached so re-merging (after a rescan, or switching
     * Sync devices) doesn't need a fresh network round trip. */
    @Volatile var jellyfinTracks: List<Track> = emptyList(); private set
    @Volatile var plexTracks: List<Track> = emptyList(); private set
    @Volatile var nasTracks: List<Track> = emptyList(); private set

    /** Status of the in-flight "download this missing track" request, if any -- Settings > Lidarr's
     * search screen binds to this to show progress. Null once nothing has been requested this session. */
    @Volatile var downloadStatus: DownloadStatus? = null; private set

    /** Search's "Download" action: races Soulseek against Lidarr (see DownloadCoordinator), then
     * schedules a couple of re-merges over the next 45s so the new track surfaces without the user
     * having to manually pull to refresh once Jellyfin's finished indexing it. */
    fun requestDownload(artist: String, title: String, album: String) {
        scope.launch {
            downloader.download(artist, title, album) { status ->
                downloadStatus = status
                notifyChange()
                if (status.stage == DownloadStage.DONE) refreshAfterDownload()
            }
        }
    }

    private fun refreshAfterDownload() {
        scope.launch { kotlinx.coroutines.delay(15_000L); mergeJellyfin(); mergePlex(); mergeNas() }
        scope.launch { kotlinx.coroutines.delay(40_000L); mergeJellyfin(); mergePlex(); mergeNas() }
    }

    /** Any track by file path (playlists and favourites store paths). */
    fun byPath(): Map<String, Track> = derive().byPath

    /** Albums with the newest files first. */
    fun recentAlbums(limit: Int = 24): List<Group> = derive().albums.sortedByDescending { g -> g.tracks.maxOf { it.mtime } }.take(limit)

    val memos: List<Track> get() = tracks.filter { !it.isMusic }.sortedByDescending { it.mtime }

    // Derived views are rebuilt (off the UI thread) whenever the track list changes.
    private class Derived(
        val src: List<Track>, val songs: List<Track>, val artists: List<Group>, val albums: List<Group>,
        val genres: List<Group>, val playlists: List<Group>, val byPath: Map<String, Track> = emptyMap(),
    )
    @Volatile private var derived = Derived(emptyList(), emptyList(), emptyList(), emptyList(), emptyList(), emptyList())

    private fun derive(): Derived {
        val src = tracks
        val d0 = derived
        if (d0.src === src) return d0
        val m = src.filter { it.isMusic }
        val songs = m.sortedBy { sortKey(it.title) }
        val artists = m.groupBy { it.albumArtist.ifEmpty { it.artist } }.map { (k, v) ->
            Group(k, v.sortedWith(albumOrder), v.firstNotNullOfOrNull { it.artKey })
        }.sortedBy { sortKey(it.name) }
        val albums = m.groupBy { it.albumKey }.map { (_, v) ->
            Group(v[0].album, v.sortedWith(trackOrder), v.firstNotNullOfOrNull { it.artKey })
        }.sortedBy { sortKey(it.name) }
        // Genres differing only by case ("HoodTrap" / "hoodtrap") are one genre.
        val genres = m.filter { it.genre.isNotEmpty() }.groupBy { it.genre.lowercase() }.map { (_, v) ->
            Group(v.groupingBy { it.genre }.eachCount().maxByOrNull { it.value }!!.key, v.sortedWith(albumOrder), v.firstNotNullOfOrNull { it.artKey })
        }.sortedBy { sortKey(it.name) }
        val d = Derived(src, songs, artists, albums, genres, buildPlaylists(m), src.associateBy { it.path })
        derived = d
        return d
    }

    /** Each top-level folder under Music/ is a playlist ("pop(FLAC)" -> "pop"); .m3u files add more.
     * Remote tracks (Jellyfin, folded in by [mergeJellyfin]) have no real folder structure in their
     * path, so they sit out of folder-derived playlists entirely -- they still show up in Songs/
     * Albums/Artists like any other track, just not grouped into a bogus one-track "playlist" here. */
    private fun buildPlaylists(music: List<Track>): List<Group> {
        val byFolder = LinkedHashMap<String, MutableList<Track>>()
        for (t in music) {
            if (t.path.startsWith("http:") || t.path.startsWith("https:")) continue
            val dirs = t.path.split('/').dropLast(1)
            val mi = dirs.indexOfLast { it.equals("Music", ignoreCase = true) }
            val folder = (if (mi >= 0) dirs.getOrNull(mi + 1) else dirs.lastOrNull()) ?: continue
            val name = folder.replace(QUALITY_TAG, "").trim().ifEmpty { folder }
            byFolder.getOrPut(name.lowercase() + "" + name) { ArrayList() }.add(t)
        }
        val out = ArrayList<Group>()
        val used = HashSet<String>()
        for ((k, v) in byFolder) {
            val name = k.substringAfter("")
            used.add(name.lowercase())
            val sorted = v.sortedWith(compareBy(String.CASE_INSENSITIVE_ORDER) { it.path })
            out.add(Group(name, sorted, sorted.firstNotNullOfOrNull { it.artKey }))
        }
        val by = music.associateBy { it.path }
        for ((name, paths) in m3uPlaylists) {
            val ts = paths.mapNotNull { by[it] }
            if (ts.isEmpty()) continue
            out.add(Group(if (name.lowercase() in used) "$name (playlist)" else name, ts, ts.firstNotNullOfOrNull { it.artKey }))
        }
        return out.sortedBy { sortKey(it.name) }
    }

    fun songs(): List<Track> = derive().songs
    fun artists(): List<Group> = derive().artists
    fun albums(): List<Group> = derive().albums
    fun genres(): List<Group> = derive().genres
    /** ipodsync's own named playlists in Sync mode; folder-/m3u-derived groups otherwise. */
    fun playlists(): List<Group> = if (source == Source.SYNC) syncGroups else derive().playlists

    /** Live search against what's NOT already owned -- Lidarr's MusicBrainz-backed catalog for
     * artists/albums (works without adding anything first), and a live Soulseek peer search for the
     * specific track if the query looks like "Artist - Title". Each source fails silently (empty
     * result) if not configured or unreachable; a slow/offline Lidarr or Soulseek just means this
     * section of Search stays empty, not an error. Owned hits are filtered out so this only ever
     * shows things Search's own local sections don't already have. */
    suspend fun catalogSearch(query: String): CatalogResults {
        if (query.isBlank() || prefs.lidarrUrl.isBlank() || prefs.lidarrApiKey.isBlank()) return CatalogResults.EMPTY
        val ownedAlbumKeys = albums().map { g -> "${g.tracks.firstOrNull()?.artist.orEmpty().trim().lowercase()}|${g.name.trim().lowercase()}" }.toSet()
        val catalogAlbums = try {
            val qLower = query.trim().lowercase()
            lidarr.lookupAlbum(prefs.lidarrUrl, prefs.lidarrApiKey, query)
                .distinctBy { "${it.artistName.trim().lowercase()}|${it.title.trim().lowercase()}" }
                .filter { "${it.artistName.trim().lowercase()}|${it.title.trim().lowercase()}" !in ownedAlbumKeys }
                // A free-text album search returns every same-titled release by anyone (tribute
                // albums, unknown-artist covers, megamixes...) -- there's no real popularity signal
                // in Lidarr's own API, so this is a best-effort proxy: an exact title match with real
                // cover art is far more likely to be the release someone actually meant than a loose
                // match with no artwork at all. Just the single best guess, not the whole noisy list.
                .sortedWith(compareBy({ it.title.trim().lowercase() != qLower }, { it.imageUrl == null }))
                .take(1)
                .map { CatalogAlbum(it.title, it.artistName, it.imageUrl) }
        } catch (_: Exception) { emptyList() }
        val catalogTracks = if (query.contains(" - ") && prefs.slskdUrl.isNotBlank() && prefs.slskdApiKey.isNotBlank()) {
            try {
                val parts = query.split(" - ", limit = 2)
                val artist = parts[0].trim(); val title = parts[1].trim()
                val ownedSong = songs().any { it.artist.trim().equals(artist, true) && it.title.trim().equals(title, true) }
                if (ownedSong) emptyList()
                else {
                    val hit = slskd.searchCandidates(prefs.slskdUrl, prefs.slskdApiKey, artist, title, timeoutMs = 6000).take(1)
                    if (hit.isEmpty()) emptyList() else {
                        // Best-effort cover: reuse the album search above if it already has this
                        // artist, else one extra lookup by artist name alone -- Soulseek's own search
                        // results never carry artwork.
                        val cover = catalogAlbums.firstOrNull { it.artist.trim().equals(artist, true) }?.imageUrl
                            ?: try {
                                lidarr.lookupAlbum(prefs.lidarrUrl, prefs.lidarrApiKey, artist)
                                    .firstOrNull { it.artistName.trim().equals(artist, true) }?.imageUrl
                            } catch (_: Exception) { null }
                        listOf(CatalogTrack(artist, title, cover))
                    }
                }
            } catch (_: Exception) { emptyList() }
        } else emptyList()
        return CatalogResults(catalogAlbums, catalogTracks)
    }

    /** Load the cached local library, then rescan. Runs regardless of [source] (so switching back to
     * LOCAL later is instant/fresh); only mirrors into the visible [tracks] while LOCAL is active. */
    fun start() {
        if (scanning) return
        scanning = true; scanCount = 0
        job = scope.launch {
            try {
                if (!loaded) { load(); derive(); loaded = true; notifyChange() }
                runScan()
            } catch (_: Exception) {
            } finally {
                scanning = false; notifyChange()
            }
        }
    }

    fun rescan() = start()

    /** Look for a real iPod on the PC running ipodsync at `host` (e.g. "192.168.1.50:5070"). */
    fun findSyncDevices(host: String) {
        if (discoveringDevices) return
        discoveringDevices = true; syncError = null; notifyChange()
        scope.launch {
            try { syncDevices = sync.devices(host); lastSyncSearch = host }
            catch (e: Exception) { syncDevices = emptyList(); syncError = e.message ?: "Connection failed"; lastSyncSearch = null }
            finally { discoveringDevices = false; notifyChange() }
        }
    }

    /** Switch to Sync mode: fetch a real iPod's tracks and real playlists from ipodsync. Read-only for now. */
    fun loadSync(host: String, deviceRoot: String) {
        if (syncing) return
        syncing = true; syncError = null; notifyChange()
        scope.launch {
            try {
                val lib = sync.library(host, deviceRoot)
                tracks = lib.tracks; syncGroups = lib.playlists; m3uPlaylists = emptyMap()
                source = Source.SYNC
                syncDeviceLabel = "$deviceRoot  ($host)"
                derive()
                mergeJellyfin(); mergePlex(); mergeNas(); checkLidarr(); checkSlskd(); checkFileMover()
            } catch (e: Exception) {
                syncError = e.message ?: "Connection failed"
            } finally {
                syncing = false; notifyChange()
            }
        }
    }

    /** Back to whatever the last local scan found -- instant, no rescan needed. */
    fun useLocal() {
        if (source == Source.LOCAL) return
        tracks = localTracks; m3uPlaylists = localM3u
        syncGroups = emptyList(); syncDeviceLabel = null; syncError = null
        source = Source.LOCAL
        derive(); notifyChange()
        mergeJellyfin(); mergePlex(); mergeNas(); checkLidarr(); checkSlskd(); checkFileMover()
    }

    /**
     * The "General Music Player" behaviour: whatever's on screen (local files, or a Sync-mode
     * iPod) quietly also gets Jellyfin's tracks folded in when a Jellyfin server is configured --
     * same song in both places (by normalized title+artist) shows once, preferring the copy that
     * doesn't need network. Talks to Jellyfin directly (no PC/ipodsync in the loop at all), so
     * this works whether or not anything else is turned on. If Jellyfin can't be reached right
     * now (not configured, no network, server down), this fails silently and whatever was already
     * showing just stays as-is -- no error, no retry loop.
     */
    private fun mergeJellyfin() {
        val url = prefs.jellyfinUrl
        val key = prefs.jellyfinApiKey
        if (url.isBlank() || key.isBlank()) return
        scope.launch {
            try {
                val userId = jellyfinUserId ?: jellyfin.firstUserId(url, key)?.also { jellyfinUserId = it } ?: return@launch
                jellyfinTracks = jellyfin.allAudio(url, key, userId)
                jellyfinConnected = true
                applyJellyfinMerge()
            } catch (_: Exception) { /* offline or unreachable -- keep showing what's already there */ }
        }
    }

    /** Settings > Jellyfin's "Connect" button: tests the server, saves it, and does the first
     * fetch+merge right away rather than waiting for the next background attempt. */
    fun connectJellyfin(url: String, apiKey: String) {
        if (jellyfinConnecting) return
        jellyfinConnecting = true; jellyfinStatus = null; notifyChange()
        scope.launch {
            try {
                val (ok, info) = jellyfin.testConnection(url, apiKey)
                if (!ok) { jellyfinStatus = "Could not connect: $info"; jellyfinConnected = false; return@launch }
                val userId = jellyfin.firstUserId(url, apiKey)
                if (userId == null) { jellyfinStatus = "Connected, but this server has no users."; jellyfinConnected = false; return@launch }
                jellyfinUserId = userId
                prefs.jellyfinUrl = url; prefs.jellyfinApiKey = apiKey
                jellyfinTracks = jellyfin.allAudio(url, apiKey, userId)
                jellyfinConnected = true
                jellyfinStatus = info?.let { "Connected to $it" } ?: "Connected"
                applyJellyfinMerge()
            } catch (e: Exception) {
                jellyfinStatus = "Could not connect: ${e.message}"; jellyfinConnected = false
            } finally {
                jellyfinConnecting = false; notifyChange()
            }
        }
    }

    private fun applyJellyfinMerge() = mergeExtra(jellyfinTracks)

    /** Same direct-connect pattern as Jellyfin, against a Plex Media Server's own REST API. */
    private fun mergePlex() {
        val url = prefs.plexUrl
        val token = prefs.plexToken
        if (url.isBlank() || token.isBlank()) return
        scope.launch {
            try {
                val key = plexSectionKey ?: plex.firstMusicSectionKey(url, token)?.also { plexSectionKey = it } ?: return@launch
                plexTracks = plex.allAudio(url, token, key)
                plexConnected = true
                applyPlexMerge()
            } catch (_: Exception) { /* offline or unreachable -- keep showing what's already there */ }
        }
    }

    /** Settings > Plex's "Connect" button: tests the server, saves it, and does the first fetch+merge right away. */
    fun connectPlex(url: String, token: String) {
        if (plexConnecting) return
        plexConnecting = true; plexStatus = null; notifyChange()
        scope.launch {
            try {
                val (ok, info) = plex.testConnection(url, token)
                if (!ok) { plexStatus = "Could not connect: $info"; plexConnected = false; return@launch }
                val key = plex.firstMusicSectionKey(url, token)
                if (key == null) { plexStatus = "Connected, but this server has no music library."; plexConnected = false; return@launch }
                plexSectionKey = key
                prefs.plexUrl = url; prefs.plexToken = token
                plexTracks = plex.allAudio(url, token, key)
                plexConnected = true
                plexStatus = info?.let { "Connected to $it" } ?: "Connected"
                applyPlexMerge()
            } catch (e: Exception) {
                plexStatus = "Could not connect: ${e.message}"; plexConnected = false
            } finally {
                plexConnecting = false; notifyChange()
            }
        }
    }

    private fun applyPlexMerge() = mergeExtra(plexTracks)

    /** Same idea again, but for a plain SMB/CIFS network share instead of a media server's API --
     * see [NasDirectClient] for how titles/artists/albums get inferred with no metadata service. */
    private fun mergeNas() {
        val host = prefs.nasHost
        val share = prefs.nasShare
        if (host.isBlank() || share.isBlank()) return
        scope.launch {
            try {
                nasTracks = nas.scanTracks(host, share, prefs.nasFolder, prefs.nasUsername, prefs.nasPassword, prefs.nasDomain)
                nasConnected = true
                applyNasMerge()
            } catch (_: Exception) { /* offline or unreachable -- keep showing what's already there */ }
        }
    }

    /** Settings > NAS's "Connect" button: tests the share, saves it, and does the first scan+merge right away. */
    fun connectNas(host: String, share: String, folder: String, username: String, password: String, domain: String) {
        if (nasConnecting) return
        nasConnecting = true; nasStatus = null; notifyChange()
        scope.launch {
            try {
                val (ok, info) = nas.testConnection(host, share, folder, username, password, domain)
                if (!ok) { nasStatus = "Could not connect: $info"; nasConnected = false; return@launch }
                val found = nas.scanTracks(host, share, folder, username, password, domain)
                prefs.nasHost = host; prefs.nasShare = share; prefs.nasFolder = folder
                prefs.nasUsername = username; prefs.nasPassword = password; prefs.nasDomain = domain
                nasTracks = found
                nasConnected = true
                nasStatus = "Connected -- ${found.size} tracks found"
                applyNasMerge()
            } catch (e: Exception) {
                nasStatus = "Could not connect: ${e.message}"; nasConnected = false
            } finally {
                nasConnecting = false; notifyChange()
            }
        }
    }

    /** Lidarr/slskd aren't music sources to merge in -- just download targets -- so unlike
     * Jellyfin/Plex/NAS there's nothing to fetch on a rescan, just a lightweight reachability check
     * so Settings shows "Connected" again without the user re-entering anything. */
    private fun checkLidarr() {
        val url = prefs.lidarrUrl; val key = prefs.lidarrApiKey
        if (url.isBlank() || key.isBlank()) return
        scope.launch {
            try {
                val (ok, info) = lidarr.testConnection(url, key)
                lidarrConnected = ok
                if (ok) { lidarrStatus = info?.let { "Connected to $it" } ?: "Connected"; notifyChange() }
            } catch (_: Exception) { /* offline or unreachable -- keep showing what's already there */ }
        }
    }

    fun connectLidarr(url: String, apiKey: String) {
        if (lidarrConnecting) return
        lidarrConnecting = true; lidarrStatus = null; notifyChange()
        scope.launch {
            try {
                val (ok, info) = lidarr.testConnection(url, apiKey)
                if (!ok) { lidarrStatus = "Could not connect: $info"; lidarrConnected = false; return@launch }
                prefs.lidarrUrl = url; prefs.lidarrApiKey = apiKey
                lidarrConnected = true
                lidarrStatus = info?.let { "Connected to $it" } ?: "Connected"
            } catch (e: Exception) {
                lidarrStatus = "Could not connect: ${e.message}"; lidarrConnected = false
            } finally {
                lidarrConnecting = false; notifyChange()
            }
        }
    }

    private fun checkSlskd() {
        val url = prefs.slskdUrl; val key = prefs.slskdApiKey
        if (url.isBlank() || key.isBlank()) return
        scope.launch {
            try {
                val (ok, info) = slskd.testConnection(url, key)
                slskdConnected = ok
                if (ok) { slskdStatus = info?.let { "Connected (slskd $it)" } ?: "Connected"; notifyChange() }
            } catch (_: Exception) { /* offline or unreachable -- keep showing what's already there */ }
        }
    }

    private fun checkFileMover() {
        val url = prefs.fileMoverUrl; val key = prefs.fileMoverApiKey
        if (url.isBlank() || key.isBlank()) return
        scope.launch {
            try {
                val (ok, _) = fileMover.testConnection(url, key)
                fileMoverConnected = ok
                if (ok) { fileMoverStatus = "Connected"; notifyChange() }
            } catch (_: Exception) { /* offline or unreachable -- keep showing what's already there */ }
        }
    }

    /** Soulseek is optional (Lidarr alone is a complete, working fallback), so this saves a blank
     * downloadPath as "not configured" rather than erroring. */
    fun connectSlskd(url: String, apiKey: String, downloadPath: String) {
        if (slskdConnecting) return
        slskdConnecting = true; slskdStatus = null; notifyChange()
        scope.launch {
            try {
                val (ok, info) = slskd.testConnection(url, apiKey)
                if (!ok) { slskdStatus = "Could not connect: $info"; slskdConnected = false; return@launch }
                prefs.slskdUrl = url; prefs.slskdApiKey = apiKey
                if (downloadPath.isNotBlank()) prefs.slskdDownloadPath = downloadPath
                slskdConnected = true
                slskdStatus = info?.let { "Connected (slskd $it)" } ?: "Connected"
            } catch (e: Exception) {
                slskdStatus = "Could not connect: ${e.message}"; slskdConnected = false
            } finally {
                slskdConnecting = false; notifyChange()
            }
        }
    }

    /** Fallback for filing a Soulseek download when NAS/SMB isn't reachable (e.g. away from home) --
     * optional, same "leave it blank" pattern as the other integrations. */
    fun connectFileMover(url: String, apiKey: String) {
        if (fileMoverConnecting) return
        fileMoverConnecting = true; fileMoverStatus = null; notifyChange()
        scope.launch {
            try {
                val (ok, info) = fileMover.testConnection(url, apiKey)
                if (!ok) { fileMoverStatus = "Could not connect: $info"; fileMoverConnected = false; return@launch }
                prefs.fileMoverUrl = url; prefs.fileMoverApiKey = apiKey
                fileMoverConnected = true
                fileMoverStatus = "Connected"
            } catch (e: Exception) {
                fileMoverStatus = "Could not connect: ${e.message}"; fileMoverConnected = false
            } finally {
                fileMoverConnecting = false; notifyChange()
            }
        }
    }

    private fun applyNasMerge() = mergeExtra(nasTracks)

    /** Folds [extraSource] into [tracks], dropping anything that's a title+artist match for a track
     * already showing -- whichever source got merged first (local/sync, then Jellyfin, then Plex,
     * then NAS, per the call order in [runScan]/[useLocal]) wins the dedup, same cross-source-only
     * rule ipodsync's ListenLibrary.Merge uses. */
    private fun mergeExtra(extraSource: List<Track>) {
        if (extraSource.isEmpty()) return
        val seen = tracks.mapTo(HashSet()) { dedupKey(it) }
        val extra = extraSource.filter { dedupKey(it) !in seen }
        if (extra.isEmpty()) return
        tracks = tracks + extra
        derive(); notifyChange()
    }

    private fun dedupKey(t: Track) = "${t.title.trim().lowercase()}|${t.artist.trim().lowercase()}"

    private suspend fun runScan() {
        val existing = localTracks.associateBy { it.path }
        val res = scanner.scan(existing) { n, partial ->
            scanCount = n
            if (localTracks.isEmpty()) {
                localTracks = partial
                if (source == Source.LOCAL) { tracks = partial; derive() }
            }
            notifyChange()
        }
        localTracks = res.tracks; localM3u = res.playlists
        if (source == Source.LOCAL) { tracks = localTracks; m3uPlaylists = localM3u; derive(); mergeJellyfin(); mergePlex(); mergeNas(); checkLidarr(); checkSlskd(); checkFileMover() }
        art.forgetMisses()
        save()
    }

    private fun load() {
        try {
            if (!cacheFile.exists()) return
            val root = JSONObject(cacheFile.readText())
            localTracks = root.getJSONArray("t").let { a -> List(a.length()) { fromJson(a.getJSONObject(it)) } }
            val pl = root.optJSONObject("p")
            localM3u = pl?.keys()?.asSequence()?.associateWith { k ->
                pl.getJSONArray(k).let { a -> List(a.length()) { a.getString(it) } }
            } ?: emptyMap()
            if (source == Source.LOCAL) { tracks = localTracks; m3uPlaylists = localM3u }
        } catch (_: Exception) { localTracks = emptyList() }
    }

    private fun notifyChange() { main.post { onChange?.invoke(); for (o in observers) o() } }

    private fun save() {
        val root = JSONObject()
        root.put("t", JSONArray().also { a -> localTracks.forEach { a.put(toJson(it)) } })
        root.put("p", JSONObject().also { o -> localM3u.forEach { (k, v) -> o.put(k, JSONArray(v)) } })
        cacheFile.writeText(root.toString())
    }

    private fun toJson(t: Track) = JSONObject().put("p", t.path).put("ti", t.title).put("ar", t.artist)
        .put("al", t.album).put("aa", t.albumArtist).put("g", t.genre).put("tn", t.trackNo).put("dn", t.discNo)
        .put("d", t.durationMs).put("y", t.year).put("m", t.isMusic).put("k", t.artKey ?: "")
        .put("mt", t.mtime).put("s", t.size)

    private fun fromJson(o: JSONObject) = Track(
        o.getString("p"), o.getString("ti"), o.getString("ar"), o.getString("al"), o.getString("aa"),
        o.getString("g"), o.getInt("tn"), o.getInt("dn"), o.getLong("d"), o.getInt("y"), o.getBoolean("m"),
        o.getString("k").ifEmpty { null }, o.getLong("mt"), o.getLong("s"),
    )

    companion object {
        val trackOrder = compareBy<Track>({ it.discNo }, { it.trackNo }, { sortKey(it.title) })
        val albumOrder = compareBy<Track>({ sortKey(it.album) }, { it.discNo }, { it.trackNo }, { sortKey(it.title) })
        private val QUALITY_TAG = Regex("\\s*[(\\[]\\s*(flac|mp3|wav|m4a|aac|ogg|opus|alac|lossless)\\s*[)\\]]\\s*$", RegexOption.IGNORE_CASE)
    }
}
