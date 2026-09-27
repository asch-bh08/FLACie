package com.ipodemu.library

import android.content.Context
import android.os.Handler
import android.os.Looper
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
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

    /** Each top-level folder under Music/ is a playlist ("pop(FLAC)" -> "pop"); .m3u files add more. */
    private fun buildPlaylists(music: List<Track>): List<Group> {
        val byFolder = LinkedHashMap<String, MutableList<Track>>()
        for (t in music) {
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
    }

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
        if (source == Source.LOCAL) { tracks = localTracks; m3uPlaylists = localM3u; derive() }
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
