package com.ipodemu.library

import android.content.Context
import android.os.Handler
import android.os.Looper
import com.ipodemu.Prefs
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import kotlinx.coroutines.async
import org.json.JSONArray
import org.json.JSONObject
import java.io.File

class Group(val name: String, val tracks: List<Track>, val artKey: String?)

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

    init {
        // covers for streamed tracks are fetched the first time they are shown, then kept on disk like scanned ones
        art.fetcher = { key ->
            when {
                key.startsWith("jf") && prefs.jellyfinUrl.isNotBlank() ->
                    CoverLookup.bytes("${prefs.jellyfinUrl.trimEnd('/')}/Items/${key.substring(2)}/Images/Primary?maxWidth=640&quality=90",
                        mapOf("X-Emby-Token" to prefs.jellyfinApiKey))
                key.startsWith("px") && prefs.plexUrl.isNotBlank() ->
                    CoverLookup.bytes("${prefs.plexUrl.trimEnd('/')}/library/metadata/${key.substring(2)}/thumb", mapOf("X-Plex-Token" to prefs.plexToken))
                key.startsWith("it") -> CoverLookup.fetch(key)
                key.startsWith("nf") -> NasCover.fetch(key, prefs.nasUsername, prefs.nasPassword, prefs.nasDomain)
                else -> null
            }
        }
    }
    /** Jellyfin/Plex/NAS tracks -- each both folded into [tracks] (deduped against whatever else is
     * showing) and exposed here on its own, for a dedicated library section (like Playlists/Artists/
     * etc) separate from the blended view. Kept cached so re-merging (after a rescan, or switching
     * Sync devices) doesn't need a fresh network round trip. */
    @Volatile var jellyfinTracks: List<Track> = emptyList(); private set
    @Volatile var plexTracks: List<Track> = emptyList(); private set
    @Volatile var nasTracks: List<Track> = emptyList(); private set
    /** NAS files Jellyfin doesn't also serve -- what the NAS section lists, since the rest are the same files. */
    fun nasOnlyTracks(): List<Track> {
        val n = nasTracks; val j = jellyfinTracks
        nasOnlyCache?.let { if (it.first === n && it.second === j) return it.third }
        val files = j.mapNotNullTo(HashSet()) { it.fileKey }; val keys = SongIndex(j)
        return n.filter { it.fileKey !in files && !keys.has(it) }.also { nasOnlyCache = Triple(n, j, it) }
    }
    @Volatile private var nasOnlyCache: Triple<List<Track>, List<Track>, List<Track>>? = null

    /** Status of the in-flight "download this missing track" request, if any -- Settings > Lidarr's
     * search screen binds to this to show progress. Null once nothing has been requested this session. */
    @Volatile var downloadStatus: DownloadStatus? = null; private set

    /** Search's "Download" action: races Soulseek against Lidarr (see DownloadCoordinator). A
     * Soulseek win carries a ready-to-play Track straight in the DONE status -- added immediately,
     * no waiting on anything. A Lidarr win doesn't have that (Lidarr does its own import under its
     * own naming), so that path still relies on the scheduled re-merges noticing it via Jellyfin. */
    /** Called after a service connection is saved from its Settings screen, so the account copy gets updated. */
    var onServicesChanged: (() -> Unit)? = null

    /** Re-tries every configured service in the background (after the account restored connections). */
    fun reconnectAll() { jellyfinUserId = null; mergeJellyfin(); mergePlex(); mergeNas(); checkLidarr(); checkSlskd(); checkFileMover() }

    @Volatile private var keyCache: Pair<List<Track>, Map<String, Track>>? = null
    private fun byKey(): Map<String, Track> { val t = tracks; keyCache?.let { if (it.first === t) return it.second }; return t.associateBy { dedupKey(it) }.also { keyCache = t to it } }
    @Volatile private var jfKeyCache: Pair<List<Track>, Map<String, Track>>? = null
    fun jellyfinByKey(): Map<String, Track> { val t = jellyfinTracks; jfKeyCache?.let { if (it.first === t) return it.second }; return t.associateBy { dedupKey(it) }.also { jfKeyCache = t to it } }
    private val jfItemRe = Regex("/Audio/([0-9a-fA-F]{32})/")
    @Volatile private var jfIdCache: Pair<List<Track>, Map<String, Track>>? = null
    /** The Jellyfin song for an item id (remote control and Jams send item ids). */
    fun jellyfinTrack(id: String): Track? {
        val t = jellyfinTracks
        val m = jfIdCache?.takeIf { it.first === t }?.second ?: t.mapNotNull { x -> jfItemRe.find(x.path)?.let { it.groupValues[1].lowercase() to x } }.toMap().also { jfIdCache = t to it }
        return m[id.lowercase()]
    }
    /** This track as a Jellyfin item id: its own stream, or the Jellyfin copy of the same song; null if Jellyfin has none. */
    fun jellyfinIdOf(t: Track): String? = jfItemRe.find(t.path)?.groupValues?.get(1) ?: jellyfinByKey()[dedupKey(t)]?.let { jfItemRe.find(it.path)?.groupValues?.get(1) }

    /** A playlist/favourite entry on this device: the exact file, or -- for an entry synced from another device --
     * this library's copy of the same song by title + artist. */
    fun resolve(path: String, meta: Pair<String, String>?): Track? =
        byPath()[path] ?: meta?.let { byKey()[matchKey(it.first, it.second)] } ?: jellyfinByPath()[path] ?: meta?.let { findLoose(it.first, it.second) }
            // a Jellyfin stream the library hasn't listed (yet): still playable from what the playlist recorded
            ?: if (meta != null && path.startsWith("http") && "/Audio/" in path) Track(
                path = path, title = meta.first, artist = meta.second, album = "", albumArtist = meta.second, genre = "", trackNo = 0, discNo = 0,
                durationMs = 0, year = 0, isMusic = true, artKey = Regex("/Audio/([0-9a-fA-F]{32})/").find(path)?.let { "jf" + it.groupValues[1] },
                mtime = 0, size = 0, source = TrackSource.JELLYFIN,
            ) else null

    private fun jellyfinByPath(): Map<String, Track> { val t = jellyfinTracks; jfPathCache?.let { if (it.first === t) return it.second }; return t.associateBy { it.path }.also { jfPathCache = t to it } }
    @Volatile private var jfPathCache: Pair<List<Track>, Map<String, Track>>? = null

    /** The signed-in account's user when Jellyfin is the account's own server (its token can't list /Users). */
    private suspend fun jellyfinUser(url: String, key: String): String? =
        prefs.accountUserId.takeIf { it.isNotBlank() && prefs.accountServer.trimEnd('/') == url.trimEnd('/') } ?: jellyfin.firstUserId(url, key)

    /** Called on the main thread when a download finishes (App records it in UserData.downloads). */
    var onDownloaded: ((DownloadEntry) -> Unit)? = null

    /** Each request's latest status, by [songKey]/[albumKey], so every search row can show its own progress. */
    val downloads = java.util.concurrent.ConcurrentHashMap<String, DownloadStatus>()
    /** Downloads in progress on this phone (with their story so far) and the log of finished ones, for the Downloads screen. */
    val runningDownloads = java.util.concurrent.ConcurrentHashMap<String, RunningDownload>()
    val downloadLog = DownloadLog(ctx)
    fun songKey(artist: String, title: String) = "s|" + matchKey(title, artist)
    fun albumKey(a: WebCatalog.Album) = "a|${a.id}"

    /** Search's per-song Download. Several can run at once; each reports under its own key. */
    fun requestDownload(artist: String, title: String, album: String, durationMs: Long = 0) {
        val key = songKey(artist, title)
        if (downloads[key]?.stage.let { it != null && it != DownloadStage.DONE && it != DownloadStage.FAILED }) return
        val label = "${title.ifBlank { album }} · $artist"
        runningDownloads[key] = RunningDownload(java.util.UUID.randomUUID().toString(), label, "Search", CoverLookup.key(artist, album, title))
        scope.launch {
            downloader.download(artist, title, album, durationMs) { status -> onStatus(key, status, DownloadEntry(artist, title.ifBlank { album }, album, "", 0, "")) }
        }
    }

    /** A whole album or EP from search. */
    fun requestAlbum(a: WebCatalog.Album) {
        val key = albumKey(a)
        if (downloads[key]?.stage.let { it != null && it != DownloadStage.DONE && it != DownloadStage.FAILED }) return
        runningDownloads[key] = RunningDownload(java.util.UUID.randomUUID().toString(), "${a.cleanTitle} · ${a.artist}", "Search", null)
        // the entry's empty path makes Recently Downloaded list the album's songs
        scope.launch { downloader.downloadAlbum(a) { status -> onStatus(key, status, DownloadEntry(a.artist, a.cleanTitle, a.cleanTitle, "", 0, "")) } }
    }

    /** Keeps the download's story (each status, and what each source said) and writes it to the log when it ends. */
    private fun track(key: String, status: DownloadStatus) {
        val r = runningDownloads[key] ?: return
        if (!status.miss) { r.message = status.message; if (status.source != null) r.current = status.source }
        if (r.trail.lastOrNull()?.let { it.text == status.message && it.source == status.source } != true) r.trail += TrailStep(System.currentTimeMillis(), status.source, status.message, status.miss)
        if (status.stage == DownloadStage.DONE || status.stage == DownloadStage.FAILED) {
            runningDownloads.remove(key)
            val done = status.stage == DownloadStage.DONE
            val file = status.newTracks.firstOrNull()?.path?.substringAfter("/file?path=", "")?.takeIf { it.isNotEmpty() }?.let { java.net.URLDecoder.decode(it, "UTF-8") }
            downloadLog.add(DownloadRecord(r.id, r.label, r.kind, r.startedAt, System.currentTimeMillis(), done, if (done) status.source else null, status.message, file, r.artKey, r.trail.toList()))
        }
    }

    private fun onStatus(key: String, status: DownloadStatus, entry: DownloadEntry) {
        downloadStatus = status
        track(key, status)
        downloads[key] = status
        addDownloadedTracks(status.newTracks)
        notifyChange()
        if (status.stage == DownloadStage.DONE) {
            refreshAfterDownload()
            val t = status.newTracks.singleOrNull()
            val e = if (t != null) DownloadEntry(t.artist, t.title, t.album, t.path, System.currentTimeMillis(), status.source.orEmpty())
                else entry.copy(album = status.newTracks.firstOrNull()?.album ?: entry.album, time = System.currentTimeMillis(), source = status.source.orEmpty())
            main.post { onDownloaded?.invoke(e) }
        }
    }

    private fun refreshAfterDownload() {
        scope.launch { kotlinx.coroutines.delay(15_000L); mergeJellyfin(); mergePlex(); mergeNas() }
        scope.launch { kotlinx.coroutines.delay(40_000L); mergeJellyfin(); mergePlex(); mergeNas() }
    }

    @Volatile private var matchCache: Pair<List<Track>, Map<String, Track>>? = null
    /** This library's copy of a song by title and artist, whichever release it is on; null if it has none. */
    fun findSong(title: String, artist: String): Track? {
        val s = derive().songs
        val m = matchCache?.takeIf { it.first === s }?.second ?: HashMap<String, Track>().also { h -> s.forEach { h.putIfAbsent(it.matchKey, it) } }.also { matchCache = s to it }
        return m[matchKey(title, artist)]
    }

    /** Whether anything can download here: Soulseek with the file mover, or Lidarr. */
    fun downloadsConfigured() = (prefs.slskdUrl.isNotBlank() && prefs.fileMoverUrl.isNotBlank()) || prefs.lidarrUrl.isNotBlank() || (prefs.fileMoverUrl.isNotBlank() && prefs.fileMoverApiKey.isNotBlank())

    /** A song with this title whose credit is missing or the same artist spelled another way (a file with no artist tag), when the exact match finds nothing. */
    fun findLoose(title: String, artist: String): Track? {
        val nt = normTitle(title); val pa = primaryArtist(artist)
        return derive().songs.firstOrNull { t ->
            normTitle(t.title) == nt && primaryArtist(t.artist).let { a -> a.isEmpty() || a == "unknown artist" || a == "unknown" || (pa.isNotEmpty() && (a.contains(pa) || pa.contains(a))) }
        }
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
        val artists = m.groupBy { it.albumArtist.ifEmpty { it.artist }.ifBlank { "Unknown Artist" } }.map { (k, v) ->
            Group(k, v.sortedWith(albumOrder), v.firstNotNullOfOrNull { it.artKey })
        }.sortedBy { sortKey(it.name) }
        val albums = m.groupBy { it.albumKey }.map { (_, v) ->
            Group(v[0].album.ifBlank { "Unknown Album" }, v.sortedWith(trackOrder), v.firstNotNullOfOrNull { it.artKey })
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


    // ConcurrentHashMap rejects null values (a "no cover found" miss used to crash the app), so a miss is stored as ""
    private val albumArtUrlCache = java.util.concurrent.ConcurrentHashMap<String, String>()

    /** Best-effort cover art for an OWNED track that has no local [Track.artKey] -- NAS scans (no
     * embedded-tag reading) and Cloud/Soulseek injections never set one. Reuses the same Lidarr
     * catalog lookup the "not yet owned" rows already use, so a Top Result card can show real art
     * instead of the generic note-glyph placeholder. Cached per artist+album so repeat searches for
     * the same song don't re-hit Lidarr. */
    suspend fun albumArtUrl(artist: String, album: String): String? {
        if (prefs.lidarrUrl.isBlank() || prefs.lidarrApiKey.isBlank()) return null
        val key = "${artist.trim().lowercase()}|${album.trim().lowercase()}"
        albumArtUrlCache[key]?.let { return it.ifEmpty { null } }
        val term = album.ifBlank { artist }
        val url = try {
            lidarr.lookupAlbum(prefs.lidarrUrl, prefs.lidarrApiKey, term)
                .firstOrNull { it.artistName.trim().equals(artist.trim(), true) }?.imageUrl
        } catch (_: Exception) { null }
        albumArtUrlCache[key] = url ?: ""
        return url
    }

    /** Load the cached local library, then rescan. Runs regardless of [source] (so switching back to
     * LOCAL later is instant/fresh); only mirrors into the visible [tracks] while LOCAL is active. */
    fun start() {
        if (scanning) return
        scanning = true; scanCount = 0
        job = scope.launch {
            try {
                if (!loaded) { load(); showSavedLibrary(); loadRemote(); rebuild(); loaded = true }
                if (source == Source.LOCAL) { mergeJellyfin(); mergePlex(); mergeNas(); checkLidarr(); checkSlskd(); checkFileMover() }
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
                syncTracks = lib.tracks; syncGroups = lib.playlists; m3uPlaylists = emptyMap()
                source = Source.SYNC
                syncDeviceLabel = "$deviceRoot  ($host)"
                rebuild()
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
        m3uPlaylists = localM3u
        syncGroups = emptyList(); syncDeviceLabel = null; syncError = null
        source = Source.LOCAL
        rebuild()
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
                val userId = jellyfinUserId ?: jellyfinUser(url, key)?.also { jellyfinUserId = it } ?: return@launch
                jellyfinTracks = jellyfin.allAudio(url, key, userId)
                jellyfinConnected = true
                applyJellyfinMerge()
            } catch (_: Exception) { /* offline or unreachable -- keep showing what's already there */ }
        }
    }

    /** Settings > Jellyfin's "Connect" button: tests the server, saves it, and does the first
     * fetch+merge right away rather than waiting for the next background attempt. */
    /** Guest mode starts empty: forgets every server and download service on this device (not on the servers or in any
     * account) and their songs; this device's own music stays. */
    fun forgetAllServices() {
        prefs.jellyfinUrl = ""; prefs.jellyfinApiKey = ""; prefs.plexUrl = ""; prefs.plexToken = ""
        prefs.nasHost = ""; prefs.nasShare = ""; prefs.nasFolder = ""; prefs.nasUsername = ""; prefs.nasPassword = ""; prefs.nasDomain = ""
        prefs.lidarrUrl = ""; prefs.lidarrApiKey = ""; prefs.slskdUrl = ""; prefs.slskdApiKey = ""; prefs.fileMoverUrl = ""; prefs.fileMoverApiKey = ""
        jellyfinUserId = null; jellyfinConnected = false; plexConnected = false; nasConnected = false; lidarrConnected = false
        jellyfinStatus = null
        jellyfinTracks = emptyList(); plexTracks = emptyList(); nasTracks = emptyList(); downloaded = emptyList()
        rebuild(); saveRemote()
    }

    /** Forgets the Jellyfin server and its songs (the account copy follows on the next sync). */
    fun disconnectJellyfin() {
        prefs.jellyfinUrl = ""; prefs.jellyfinApiKey = ""
        jellyfinUserId = null; jellyfinConnected = false; jellyfinStatus = null; jellyfinTracks = emptyList()
        onServicesChanged?.invoke()
        rebuild(); saveRemote()
    }

    fun connectJellyfin(url: String, apiKey: String) {
        if (jellyfinConnecting) return
        jellyfinConnecting = true; jellyfinStatus = null; notifyChange()
        scope.launch {
            try {
                val (ok, info) = jellyfin.testConnection(url, apiKey)
                if (!ok) { jellyfinStatus = "Could not connect: $info"; jellyfinConnected = false; return@launch }
                val userId = jellyfinUser(url, apiKey)
                if (userId == null) { jellyfinStatus = "Connected, but this server has no users."; jellyfinConnected = false; return@launch }
                jellyfinUserId = userId
                prefs.jellyfinUrl = url; prefs.jellyfinApiKey = apiKey
                onServicesChanged?.invoke()
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

    private fun applyJellyfinMerge() { rebuild(); saveRemote() }

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
                onServicesChanged?.invoke()
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

    private fun applyPlexMerge() { rebuild(); saveRemote() }

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
                onServicesChanged?.invoke()
                nasTracks = found
                nasConnected = true
                nasStatus = "Connected: ${found.size} tracks found"
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
                onServicesChanged?.invoke()
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
                onServicesChanged?.invoke()
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
                onServicesChanged?.invoke()
                fileMoverConnected = true
                fileMoverStatus = "Connected"
            } catch (e: Exception) {
                fileMoverStatus = "Could not connect: ${e.message}"; fileMoverConnected = false
            } finally {
                fileMoverConnecting = false; notifyChange()
            }
        }
    }

    private fun applyNasMerge() { rebuild(); saveRemote() }

    /** Folds [extraSource] into [tracks], dropping anything that's a title+artist match for a track
     * already showing -- whichever source got merged first (local/sync, then Jellyfin, then Plex,
     * then NAS, per the call order in [runScan]/[useLocal]) wins the dedup, same cross-source-only
     * rule ipodsync's ListenLibrary.Merge uses. */
    /** Adds a track this app just downloaded and filed itself straight into the merged library --
     * instant, no waiting on Jellyfin's own scan/API at all. Reuses the same title+artist dedup as
     * every other merge source, so once Jellyfin or NAS eventually also notices the same file, it's
     * recognized as already-present rather than duplicated. */
    fun addDownloadedTrack(t: Track) { addDownloadedTracks(listOf(t)) }
    /** Kept in remote.json too, so a fresh download is still there after a restart even if Jellyfin hasn't scanned it yet. */
    fun addDownloadedTracks(ts: List<Track>) { if (ts.isEmpty()) return; downloaded = downloaded.filter { d -> ts.none { it.path == d.path } } + ts; rebuild(); saveRemote() }
    @Volatile private var downloaded: List<Track> = emptyList()
    @Volatile private var syncTracks: List<Track> = emptyList()

    /**
     * The shown library, rebuilt from scratch: this device's files (or the iPod in Sync mode), then Jellyfin, Plex, the
     * NAS and fresh downloads, in that order. Rebuilding (instead of adding to whatever is there) means the result never
     * depends on which fetch finished first -- a local scan finishing used to wipe a Jellyfin merge that had landed before
     * it, so Jellyfin songs came and went until the next fetch.
     */
    @Synchronized private fun rebuild() {
        // built aside and swapped in once: assigning the base list first let a screen read an empty library mid-rebuild
        var acc = if (source == Source.SYNC) syncTracks else localTracks
        // a download is also a NAS file the moment it lands: the download (playable anywhere) hides that NAS row, and
        // Jellyfin's copy, once scanned, hides the download (same file, see fileKey)
        val dlFiles = downloaded.mapNotNullTo(HashSet()) { it.fileKey }
        // NAS rows are checked against every Jellyfin file (nasOnlyTracks), not just the Jellyfin rows that survived its own
        // de-dup: a NAS FLAC whose Jellyfin twin lost to a stray .m4a copy of the same song used to show up as a NAS extra
        for (s in listOf(jellyfinTracks, plexTracks, nasOnlyTracks().filterNot { it.fileKey in dlFiles }, downloaded)) acc = merged(acc, s)
        tracks = acc
        derive(); notifyChange(); saveMergedSoon()
    }

    /** [base] plus the songs of [extraSource] it doesn't already have. */
    private fun merged(base: List<Track>, extraSource: List<Track>): List<Track> {
        if (extraSource.isEmpty()) return base
        var acc = base
        // Jellyfin and the NAS share are usually the same files (Jellyfin scans that share): the Jellyfin copy wins,
        // since it streams from anywhere while SMB only works at home -- drop NAS rows it duplicates before merging.
        if (extraSource.first().source == TrackSource.JELLYFIN) {
            val jf = extraSource.mapNotNullTo(HashSet()) { it.fileKey } to SongIndex(extraSource)
            acc = acc.filterNot { it.source == TrackSource.NAS && (it.fileKey in jf.first || jf.second.has(it)) }
        }
        val seen = SongIndex(acc)
        val seenFiles = acc.mapNotNullTo(HashSet()) { it.fileKey }
        // distinctBy first: two copies of one song inside the same batch (a NAS folder holding both a Soulseek file and a
        // Lidarr import) would otherwise both survive, since neither was "seen" when the other was checked.
        // The same song on two albums stays on both (an album page must not lose tracks); a copy of the same song on
        // the same album, however each source spells the credits and edition, collapses.
        val extra = extraSource.distinctBy { mergeKey(it) }.filter { !seen.has(it) && (it.fileKey == null || it.fileKey !in seenFiles) }
        return if (extra.isEmpty()) acc else acc + extra
    }

    /** Same song across sources, tolerant of how each one spells the credits (see [matchKey]). */
    private fun dedupKey(t: Track) = t.matchKey

    /** [matchKey] plus the album with editions and punctuation dropped ("Recovery [Deluxe Edition]" = "Recovery"). */
    private fun mergeKey(t: Track) = t.mergeKeyC
    private fun albumNorm(t: Track) = t.albumNormC

    /** Songs already present: the same song on the same album; a copy with no album tag counts as present when the song
     * is there on any album (a tagged album copy is never hidden by a loose untagged one, or album pages would lose it). */
    private inner class SongIndex(ts: List<Track>) {
        private val exact = HashSet<String>(); private val any = HashSet<String>()
        init { for (t in ts) { exact += mergeKey(t); any += t.matchKey } }
        fun has(t: Track) = mergeKey(t) in exact || (albumNorm(t).isEmpty() && t.matchKey in any)
    }

    private suspend fun runScan() {
        val existing = localTracks.associateBy { it.path }
        val res = scanner.scan(existing) { n, partial ->
            scanCount = n
            if (localTracks.isEmpty()) {
                localTracks = partial
                if (source == Source.LOCAL) rebuild()
            }
            notifyChange()
        }
        localTracks = res.tracks; localM3u = res.playlists
        if (source == Source.LOCAL) { m3uPlaylists = localM3u; rebuild() }
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
            if (source == Source.LOCAL) m3uPlaylists = localM3u
        } catch (_: Exception) { localTracks = emptyList() }
    }

    private val remoteFile = File(cacheFile.parentFile, "remote.json")

    /** Last fetched Jellyfin/Plex/NAS lists, so they show the moment the app opens instead of after a network round trip. */
    private suspend fun loadRemote() {
        try {
            if (!remoteFile.exists()) return
            // streamed straight into tracks: building a JSONObject tree for several MB of tracks took seconds on a phone
            val parsed = HashMap<String, List<Track>>()
            android.util.JsonReader(remoteFile.bufferedReader(Charsets.UTF_8, 1 shl 16)).use { r ->
                r.beginObject()
                while (r.hasNext()) {
                    val k = r.nextName()
                    if (k in REMOTE_KEYS) { val out = ArrayList<Track>(); r.beginArray(); while (r.hasNext()) out += readTrack(r); r.endArray(); parsed[k] = out } else r.skipValue()
                }
                r.endObject()
            }
            fun list(k: String) = parsed[k] ?: emptyList()
            if (jellyfinTracks.isEmpty()) jellyfinTracks = list("jf")
            if (plexTracks.isEmpty()) plexTracks = list("px")
            if (nasTracks.isEmpty()) nasTracks = list("nas")
            val week2 = System.currentTimeMillis() - 14L * 86_400_000
            if (downloaded.isEmpty()) downloaded = list("dl").filter { it.mtime > week2 }
        } catch (_: Exception) {}
    }

    @Synchronized private fun saveRemote() {
        try {
            fun arr(ts: List<Track>) = JSONArray().also { a -> ts.forEach { a.put(toJson(it)) } }
            val tmp = File(remoteFile.path + ".tmp")
            tmp.writeText(JSONObject().put("jf", arr(jellyfinTracks)).put("px", arr(plexTracks)).put("nas", arr(nasTracks)).put("dl", arr(downloaded)).toString())
            tmp.renameTo(remoteFile)
        } catch (_: Exception) {}
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
        .put("mt", t.mtime).put("s", t.size).put("src", t.source.name).put("fp", t.filePath)

    private fun fromJson(o: JSONObject) = Track(
        o.getString("p"), fixMojibake(o.getString("ti")), fixMojibake(o.getString("ar")), fixMojibake(o.getString("al")), fixMojibake(o.getString("aa")),
        o.getString("g"), o.getInt("tn"), o.getInt("dn"), o.getLong("d"), o.getInt("y"), o.getBoolean("m"),
        o.getString("k").ifEmpty { null }, o.getLong("mt"), o.getLong("s"),
        source = o.optString("src").let { s -> TrackSource.entries.firstOrNull { it.name == s } } ?: TrackSource.LOCAL,
        filePath = o.optString("fp"),
    )

    private val mergedFile = File(cacheFile.parentFile, "merged.json")
    private var mergedSave: Job? = null

    /** The blended library as it was last shown, so a cold start has songs on screen straight away; the full rebuild from the
     * per-source lists (and the network refresh) follows in the background and replaces it. */
    private fun showSavedLibrary() {
        try {
            if (source != Source.LOCAL || tracks.isNotEmpty() || !mergedFile.exists()) return
            val out = ArrayList<Track>()
            android.util.JsonReader(mergedFile.bufferedReader(Charsets.UTF_8, 1 shl 16)).use { r -> r.beginArray(); while (r.hasNext()) out += readTrack(r); r.endArray() }
            if (out.isEmpty()) return
            tracks = out
            notifyChange()
        } catch (_: Exception) {}
    }

    private fun saveMergedSoon() {
        mergedSave?.cancel()
        mergedSave = scope.launch {
            kotlinx.coroutines.delay(4000)
            val all = tracks
            if (all.isEmpty() || source != Source.LOCAL) return@launch
            try {
                val tmp = File(mergedFile.path + ".tmp")
                android.util.JsonWriter(tmp.bufferedWriter(Charsets.UTF_8, 1 shl 16)).use { w ->
                    w.beginArray()
                    for (t in all) {
                        w.beginObject().name("p").value(t.path).name("ti").value(t.title).name("ar").value(t.artist).name("al").value(t.album).name("aa").value(t.albumArtist)
                            .name("g").value(t.genre).name("tn").value(t.trackNo.toLong()).name("dn").value(t.discNo.toLong()).name("d").value(t.durationMs).name("y").value(t.year.toLong())
                            .name("m").value(t.isMusic).name("k").value(t.artKey ?: "").name("mt").value(t.mtime).name("s").value(t.size).name("src").value(t.source.name).name("fp").value(t.filePath).endObject()
                    }
                    w.endArray()
                }
                tmp.renameTo(mergedFile)
            } catch (_: Exception) {}
        }
    }

    private fun readTrack(r: android.util.JsonReader): Track {
        var p = ""; var ti = ""; var ar = ""; var al = ""; var aa = ""; var g = ""; var tn = 0; var dn = 0; var d = 0L; var y = 0; var m = false
        var k = ""; var mt = 0L; var s = 0L; var src = ""; var fp = ""
        r.beginObject()
        while (r.hasNext()) when (r.nextName()) {
            "p" -> p = r.nextString(); "ti" -> ti = r.nextString(); "ar" -> ar = r.nextString(); "al" -> al = r.nextString(); "aa" -> aa = r.nextString()
            "g" -> g = r.nextString(); "tn" -> tn = r.nextInt(); "dn" -> dn = r.nextInt(); "d" -> d = r.nextLong(); "y" -> y = r.nextInt(); "m" -> m = r.nextBoolean()
            "k" -> k = r.nextString(); "mt" -> mt = r.nextLong(); "s" -> s = r.nextLong(); "src" -> src = r.nextString(); "fp" -> fp = r.nextString()
            else -> r.skipValue()
        }
        r.endObject()
        return Track(p, fixMojibake(ti), fixMojibake(ar), fixMojibake(al), fixMojibake(aa), g, tn, dn, d, y, m, k.ifEmpty { null }, mt, s,
            source = TrackSource.entries.firstOrNull { it.name == src } ?: TrackSource.LOCAL, filePath = fp)
    }

    companion object {
        private val REMOTE_KEYS = setOf("jf", "px", "nas", "dl")
        val trackOrder = compareBy<Track>({ it.discNo }, { it.trackNo }, { sortKey(it.title) })
        val albumOrder = compareBy<Track>({ sortKey(it.album) }, { it.discNo }, { it.trackNo }, { sortKey(it.title) })
        private val QUALITY_TAG = Regex("\\s*[(\\[]\\s*(flac|mp3|wav|m4a|aac|ogg|opus|alac|lossless)\\s*[)\\]]\\s*$", RegexOption.IGNORE_CASE)
    }
}
