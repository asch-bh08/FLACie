package com.ipodemu.library

import android.content.Context
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.setValue
import org.json.JSONArray
import org.json.JSONObject
import java.io.File

/** How long a removal from a playlist page can be undone: a little longer than the Undo message stays on screen. */
const val UNDO_MS = 6_500L

/** Where a pending removal really is now: the recorded [index] if it still holds [path], else the first copy of it, else -1. */
fun removalIndex(paths: List<String>, index: Int, path: String): Int = if (paths.getOrNull(index) == path) index else paths.indexOf(path)

/** [mtime] = last edit (newest wins when two devices' copies are merged); [jfId] = the mirrored Jellyfin playlist. */
class UserPlaylist(val id: String, var name: String, val paths: MutableList<String>, var mtime: Long = System.currentTimeMillis(), var jfId: String? = null)

/** Favourites, hand-made playlists and play history. Persisted as JSON; [rev] lets Compose observe changes. */
class UserData(dir: File) {
    constructor(ctx: Context) : this(ctx.filesDir)
    private val file = File(dir, "userdata.json")
    val favorites = LinkedHashSet<String>()
    /** When each favourite was made (ms), and the favourites taken away with when: other devices follow by the latest change (FavoriteSync). */
    val favAt = HashMap<String, Long>()
    val favGone = ArrayList<FavoriteSync.Fav>()
    val playlists = ArrayList<UserPlaylist>()
    val recents = ArrayList<String>() // newest first
    /** When each recent song was last started (ms): the history shared with FLACie Web merges by it (HistorySync). */
    val recentAt = HashMap<String, Long>()
    /** Finished downloads, newest first: what was asked for and, for a Soulseek file, the path it plays from. */
    val downloads = ArrayList<DownloadEntry>()
    /** How many times each track has been started; feeds the recommendations. */
    val plays = HashMap<String, Int>()
    /** Title + artist for paths that came from another device's playlists/favourites, so they can be matched to this
     * device's copy of the same song (see Library.resolve). */
    val meta = HashMap<String, Pair<String, String>>()
    /** Ids of playlists deleted here, so a sync doesn't bring them back; and their Jellyfin copies still to delete. */
    val deletedPlaylists = LinkedHashSet<String>()
    val deletedPlaylistJf = java.util.Collections.synchronizedSet(LinkedHashSet<String>())
    /** Called after a change worth syncing to the account (not play counts/history). */
    var onChanged: (() -> Unit)? = null

    var rev by mutableIntStateOf(0)
        private set

    /** Star ratings (see [RatingStore]); [onRated] lets the app write a rating to Jellyfin. Made before load() runs: declared below it, it was
     * still null there, load() gave up at its first line, and nothing saved came back at a restart (13 to 25 Oct). */
    val ratings = RatingStore()

    init { load() }

    fun isFavorite(path: String) = path in favorites

    var onRated: ((Track, Int) -> Unit)? = null
    fun rating(t: Track): Int { @Suppress("UNUSED_EXPRESSION") rev; return ratings.get(t) }
    fun setRating(t: Track, stars: Int) { ratings.set(t, stars); changed(); onRated?.invoke(t, RatingStore.clamp(stars)) }
    fun mergeRatings(incoming: List<RatingEntry>) { if (ratings.merge(incoming)) changed(sync = false) }

    private val favStates = HashMap<String, androidx.compose.runtime.MutableState<Boolean>>()
    /** Per-track observable flag: toggling one favourite recomposes only that row, not every row. */
    fun favState(path: String): androidx.compose.runtime.State<Boolean> = favStates.getOrPut(path) { androidx.compose.runtime.mutableStateOf(path in favorites) }

    /** The same for a song: also on when it was made a favourite on another device (kept under that device's path, see [FavoriteMatch]). */
    fun favState(t: Track): androidx.compose.runtime.State<Boolean> {
        trackKeys[t.path] = t.matchKey
        return favStates.getOrPut(t.path) { androidx.compose.runtime.mutableStateOf(storedFavorite(t) != null) }
    }
    private val trackKeys = HashMap<String, String>()
    fun isFavorite(t: Track) = storedFavorite(t) != null
    private fun storedFavorite(t: Track) = FavoriteMatch.storedFor(t.path, t.matchKey, favorites, meta)
    /** A song's heart: takes away the favourite it stands for (whichever device's path it is kept under), else adds this one. */
    fun toggleFavorite(t: Track) { toggleFavorite(storedFavorite(t) ?: t.path) }
    /** Every heart on screen follows a change, including songs matched to another device's favourite by title and artist. */
    private fun refreshFavStates() {
        for ((p, s) in favStates) s.value = FavoriteMatch.storedFor(p, trackKeys[p] ?: "", favorites, meta) != null
    }

    /** Called after a favourite is added or removed (the offline library follows it). */
    var onFavoritesChanged: (() -> Unit)? = null

    fun toggleFavorite(path: String) {
        val now = System.currentTimeMillis()
        if (favorites.remove(path)) {
            favAt.remove(path)
            val m = meta[path]
            favGone.add(0, FavoriteSync.Fav(path, m?.first ?: "", m?.second ?: "", now))
            while (favGone.size > FavoriteSync.KEEP_REMOVALS) favGone.removeAt(favGone.size - 1)
        } else { favorites.add(path); favAt[path] = now }
        refreshFavStates()
        changed()
        onFavoritesChanged?.invoke()
    }

    fun recordDownload(e: DownloadEntry) {
        downloads.removeAll { it.title.equals(e.title, true) && it.artist.equals(e.artist, true) }
        downloads.add(0, e)
        if (e.path.isNotEmpty() && e.path !in meta) meta[e.path] = e.title to e.artist
        while (downloads.size > 200) downloads.removeAt(downloads.lastIndex)
        changed(sync = false)
    }

    fun recordPlay(path: String) {
        plays[path] = (plays[path] ?: 0) + 1
        recents.remove(path); recents.add(0, path); recentAt[path] = System.currentTimeMillis()
        while (recents.size > 120) recents.removeAt(recents.lastIndex)
        changed(sync = false)
    }

    fun createPlaylist(name: String, first: String? = null): UserPlaylist {
        val p = UserPlaylist("u" + System.currentTimeMillis().toString(36), name.trim().ifEmpty { "New Playlist" }, ArrayList())
        if (first != null) p.paths.add(first)
        playlists.add(p); changed(); return p
    }

    fun addToPlaylist(id: String, path: String) {
        playlists.firstOrNull { it.id == id }?.let { if (path !in it.paths) it.paths.add(path); it.mtime = System.currentTimeMillis(); changed() }
    }

    /** A song taken out of a playlist page that is not gone yet: it stays pending for [UNDO_MS] so Undo brings it back untouched, then the removal is applied. */
    class PendingRemoval(val playlistId: String, val path: String, val index: Int)
    var pending by androidx.compose.runtime.mutableStateOf<PendingRemoval?>(null)
        private set
    private val pendingHandler by lazy { android.os.Handler(android.os.Looper.getMainLooper()) }

    /** Starts a removal that Undo can cancel (see [PendingRemoval]); a removal already pending is applied first. Returns the song, or null. */
    fun removeLater(id: String, index: Int): String? {
        commitPending()
        val path = playlists.firstOrNull { it.id == id }?.paths?.getOrNull(index) ?: return null
        val pr = PendingRemoval(id, path, index)
        pending = pr; rev++
        pendingHandler.postDelayed({ if (pending === pr) commitPending() }, UNDO_MS)
        return path
    }
    /** Undo: the song was never taken out. */
    fun cancelRemoval() { if (pending != null) { pending = null; rev++ } }
    /** Applies the pending removal now (the time is up, or another edit is about to happen). */
    fun commitPending() {
        val pr = pending ?: return
        pending = null
        playlists.firstOrNull { it.id == pr.playlistId }?.let { p ->
            val i = removalIndex(p.paths, pr.index, pr.path)
            if (i >= 0) { p.paths.removeAt(i); p.mtime = System.currentTimeMillis(); changed() } else rev++
        }
    }
    /** The songs of a playlist as the page shows them: the one with a pending removal is already out of sight. */
    fun visiblePaths(id: String): List<String> {
        val p = playlists.firstOrNull { it.id == id } ?: return emptyList()
        val pr = pending?.takeIf { it.playlistId == id } ?: return p.paths.toList()
        val i = removalIndex(p.paths, pr.index, pr.path)
        return if (i < 0) p.paths.toList() else p.paths.filterIndexed { n, _ -> n != i }
    }

    /** Takes the song at [index] out (the Undo puts it back with [insertAt]). */
    fun removeAt(id: String, index: Int): String? = playlists.firstOrNull { it.id == id }?.let { p ->
        commitPending()
        if (index !in p.paths.indices) null else p.paths.removeAt(index).also { p.mtime = System.currentTimeMillis(); changed() }
    }

    fun insertAt(id: String, index: Int, path: String) {
        commitPending()
        playlists.firstOrNull { it.id == id }?.let { it.paths.add(index.coerceIn(0, it.paths.size), path); it.mtime = System.currentTimeMillis(); changed() }
    }

    /** Moves the song at [from] to [to] (up or down in the list). */
    fun move(id: String, from: Int, to: Int) {
        commitPending()
        playlists.firstOrNull { it.id == id }?.let { p ->
            if (from !in p.paths.indices || to !in p.paths.indices || from == to) return
            p.paths.add(to, p.paths.removeAt(from)); p.mtime = System.currentTimeMillis(); changed()
        }
    }

    /** Drops later copies of a song already in the playlist ([keyOf] says what counts as the same song); returns how many went. */
    fun removeDuplicates(id: String, keyOf: (String) -> String): Int {
        commitPending()
        val p = playlists.firstOrNull { it.id == id } ?: return 0
        val seen = HashSet<String>(); val keep = ArrayList<String>()
        for (path in p.paths) if (seen.add(keyOf(path))) keep.add(path)
        val removed = p.paths.size - keep.size
        if (removed > 0) { p.paths.clear(); p.paths.addAll(keep); p.mtime = System.currentTimeMillis(); changed() }
        return removed
    }

    fun removeFromPlaylist(id: String, path: String) {
        commitPending()
        playlists.firstOrNull { it.id == id }?.let { it.paths.remove(path); it.mtime = System.currentTimeMillis(); changed() }
    }

    fun deletePlaylist(id: String) {
        commitPending()
        playlists.firstOrNull { it.id == id }?.jfId?.let { deletedPlaylistJf.add(it) }
        playlists.removeAll { it.id == id }; deletedPlaylists.add(id); changed()
    }

    fun rename(id: String, name: String) { playlists.firstOrNull { it.id == id }?.let { it.name = name.trim().ifEmpty { "Playlist" }; it.mtime = System.currentTimeMillis() }; changed() }

    /** Saves a change made by the sync itself (e.g. a new Jellyfin playlist id) without triggering another push. */
    fun touchedExternally() { android.os.Handler(android.os.Looper.getMainLooper()).post { changed(sync = false) } }

    /** Folds in the account's copy (see AccountSync): favourites union, playlists by id with the newest edit winning,
     * playlists deleted on another device removed here. Entries carry title + artist for cross-device matching. */
    /** Takes away every favourite (each recorded as removed, so other devices follow); the paths come back for Undo. */
    fun clearFavorites(): List<String> {
        val was = favorites.toList(); val now = System.currentTimeMillis()
        for (p in was) { val m = meta[p]; favGone.add(0, FavoriteSync.Fav(p, m?.first ?: "", m?.second ?: "", now)); favStates[p]?.value = false }
        while (favGone.size > FavoriteSync.KEEP_REMOVALS) favGone.removeAt(favGone.size - 1)
        favorites.clear(); favAt.clear()
        changed(); onFavoritesChanged?.invoke()
        return was
    }

    /** Undo for [clearFavorites]: the same songs favourited again, in the same order. */
    fun restoreFavorites(paths: List<String>) {
        val now = System.currentTimeMillis()
        for (p in paths) { favorites.add(p); favAt[p] = now; favStates[p]?.value = true }
        changed(); onFavoritesChanged?.invoke()
    }

    /** [favs] and [gone]: the account's favourites and removals; [info] gives a local favourite's title and artist (from the library). */
    fun mergeRemote(favs: List<FavoriteSync.Fav>, gone: List<FavoriteSync.Fav>, lists: List<AccountSync.RemotePlaylist>, deleted: List<String>, info: (String) -> Pair<String, String> = { meta[it] ?: ("" to "") }) {
        fun note(p: String, t: String, a: String) { if (t.isNotEmpty() && p !in meta) meta[p] = t to a }
        favs.forEach { note(it.p, it.t, it.a) }
        // favourites: per song the latest of favouriting and removing wins, here and on the account (FavoriteSync)
        val local = favorites.map { p -> val (t, a) = info(p); FavoriteSync.Fav(p, t, a, favAt[p] ?: 0L) }
        val (keep, removed) = FavoriteSync.merge(local, favGone.toList(), favs, gone)
        val now = keep.mapTo(LinkedHashSet()) { it.p }
        for (p in favorites) if (p !in now) favStates[p]?.value = false
        for (p in now) if (p !in favorites) favStates[p]?.value = true
        favorites.clear(); favorites.addAll(now); refreshFavStates()
        favAt.clear(); keep.forEach { if (it.w > 0) favAt[it.p] = it.w }
        favGone.clear(); favGone.addAll(removed)
        for (id in deleted) if (id !in deletedPlaylists) { deletedPlaylists.add(id); playlists.removeAll { it.id == id } }
        for (r in lists) {
            if (r.id in deletedPlaylists) continue
            r.tracks.forEach { (p, t, a) -> note(p, t, a) }
            val mine = playlists.firstOrNull { it.id == r.id }
            if (mine == null) playlists.add(UserPlaylist(r.id, r.name, r.tracks.mapTo(ArrayList()) { it.first }, r.mtime, r.jfId))
            else if (r.mtime > mine.mtime) { mine.name = r.name; mine.paths.clear(); mine.paths.addAll(r.tracks.map { it.first }); mine.mtime = r.mtime; mine.jfId = r.jfId ?: mine.jfId }
            else if (mine.jfId == null) mine.jfId = r.jfId
        }
        changed(sync = false)
    }


    /** The account's own Jellyfin playlists (see AccountSync.pullJellyfinPlaylists): new ones are added, and ones not edited here
     * since the last sync take the server's name and tracks; ones deleted on the server go, unless edited here since. */
    fun mergeJellyfin(server: List<Triple<String, String, List<Pair<String, Pair<String, String>>>>>, syncedAt: Long) {
        val ids = server.mapTo(HashSet()) { it.first }
        playlists.removeAll { it.jfId != null && it.jfId !in ids && it.mtime <= syncedAt }
        for ((jf, name, tracks) in server) {
            tracks.forEach { (p, ta) -> if (ta.first.isNotEmpty() && p !in meta) meta[p] = ta }
            val paths = tracks.mapTo(ArrayList()) { it.first }
            val mine = playlists.firstOrNull { it.jfId == jf }
            if (mine == null) playlists.add(UserPlaylist("jf$jf", name, paths, syncedAt.coerceAtLeast(1), jf))
            // the account profile is the master copy of a playlist it already holds (FLACie Web edits it without touching the Jellyfin playlist, which the next push brings up to date); the server only fills one that is empty here
            else if (mine.paths.isEmpty()) { mine.name = name; mine.paths.addAll(paths) }
        }
        changed(sync = false)
    }
    /**
     * Forgets everything that belongs to the person signed in (favourites, playlists, history, play counts, ratings, the download log): at sign-out, and
     * when a different account signs in on this device. It is all kept in that person's account and comes back when they sign in again; without this the
     * next person's sign-in merged it into their own account.
     */
    fun clearPersonal() {
        favorites.clear(); favAt.clear(); favGone.clear(); playlists.clear(); recents.clear(); recentAt.clear(); downloads.clear(); plays.clear(); meta.clear()
        deletedPlaylists.clear(); deletedPlaylistJf.clear(); ratings.load(null)
        favStates.values.forEach { it.value = false }
        changed(sync = false)
    }

    /** The phone's plays, with titles and when, for merging with the account's history. A play from before times were kept counts as just older than the oldest timed one. */
    fun history(): List<HistorySync.Entry> {
        val oldest = recentAt.values.minOrNull() ?: 0L
        return recents.toList().mapIndexed { i, p -> val m = meta[p]; HistorySync.Entry(p, m?.first.orEmpty(), m?.second.orEmpty(), recentAt[p] ?: (oldest - 1 - i)) }
    }

    /** Recently Played becomes the merged history (newest first); the titles are kept so a song from the web resolves here. */
    fun setHistory(merged: List<HistorySync.Entry>) {
        recents.clear(); recentAt.clear()
        for (e in merged.take(120)) { recents.add(e.p); recentAt[e.p] = e.w; if (e.t.isNotEmpty() && e.p !in meta) meta[e.p] = e.t to e.a }
        changed(sync = false)
    }

    private fun changed(sync: Boolean = true) { rev++; save(); if (sync) onChanged?.invoke() }

    private fun load() {
        try {
            if (!file.exists()) return
            val o = JSONObject(file.readText())
            ratings.load(o.optJSONArray("ratings"))
            o.optJSONArray("fav")?.let { a -> for (i in 0 until a.length()) favorites.add(a.getString(i)) }
            o.optJSONObject("favat")?.let { m -> m.keys().forEach { k -> favAt[k] = m.optLong(k) } }
            o.optJSONArray("favgone")?.let { a -> for (i in 0 until a.length()) a.optJSONObject(i)?.let { g -> favGone.add(FavoriteSync.Fav(g.optString("p"), g.optString("t"), g.optString("a"), g.optLong("w"))) } }
            o.optJSONObject("plays")?.let { p -> p.keys().forEach { k -> plays[k] = p.optInt(k) } }
            o.optJSONArray("recent")?.let { a -> for (i in 0 until a.length()) recents.add(a.getString(i)) }
            o.optJSONObject("recentat")?.let { m -> m.keys().forEach { k -> recentAt[k] = m.optLong(k) } }
            o.optJSONArray("dl")?.let { a -> for (i in 0 until a.length()) a.getJSONObject(i).let { d -> downloads.add(DownloadEntry(d.optString("a"), d.optString("t"), d.optString("al"), d.optString("p"), d.optLong("w"), d.optString("s"))) } }
            o.optJSONObject("meta")?.let { m -> m.keys().forEach { k -> m.optJSONArray(k)?.let { v -> meta[k] = v.optString(0) to v.optString(1) } } }
            o.optJSONArray("deleted")?.let { a -> for (i in 0 until a.length()) deletedPlaylists.add(a.getString(i)) }
            o.optJSONArray("deljf")?.let { a -> for (i in 0 until a.length()) deletedPlaylistJf.add(a.getString(i)) }
            o.optJSONArray("lists")?.let { a ->
                for (i in 0 until a.length()) {
                    val p = a.getJSONObject(i)
                    val paths = ArrayList<String>()
                    p.getJSONArray("p").let { pa -> for (j in 0 until pa.length()) paths.add(pa.getString(j)) }
                    playlists.add(UserPlaylist(p.getString("id"), p.getString("n"), paths, p.optLong("m", 0L), p.optString("jf").ifBlank { null }))
                }
            }
        } catch (_: Exception) {}
    }

    // Saving used to serialise and write the whole file on the main thread at every track start (a visible hitch).
    // Now: snapshot the data here (cheap), write it on a background thread, and coalesce bursts of changes.
    private val ioThread = java.util.concurrent.Executors.newSingleThreadExecutor()
    private val main by lazy { android.os.Handler(android.os.Looper.getMainLooper()) }
    private var savePending = false
    private fun save() {
        if (savePending) return
        savePending = true
        main.postDelayed(Runnable {
            savePending = false
            val o = try {
                JSONObject().also { o ->
                    o.put("fav", JSONArray(favorites.toList()))
                    o.put("favat", JSONObject().also { m -> favAt.toMap().forEach { (k, v) -> m.put(k, v) } })
                    o.put("favgone", JSONArray().also { a -> favGone.toList().forEach { g -> a.put(JSONObject().put("p", g.p).put("t", g.t).put("a", g.a).put("w", g.w)) } })
                    o.put("ratings", ratings.toJson())
                    o.put("recent", JSONArray(recents.toList()))
                    o.put("recentat", JSONObject().also { m -> recentAt.toMap().forEach { (k, v) -> m.put(k, v) } })
                    o.put("dl", JSONArray().also { a -> downloads.toList().forEach { d -> a.put(JSONObject().put("a", d.artist).put("t", d.title).put("al", d.album).put("p", d.path).put("w", d.time).put("s", d.source)) } })
                    o.put("plays", JSONObject().also { p -> plays.toMap().forEach { (k, v) -> p.put(k, v) } })
                    o.put("lists", JSONArray().also { a -> playlists.forEach { a.put(JSONObject().put("id", it.id).put("n", it.name).put("p", JSONArray(it.paths.toList())).put("m", it.mtime).put("jf", it.jfId ?: "")) } })
                    o.put("meta", JSONObject().also { m -> meta.toMap().forEach { (k, v) -> m.put(k, JSONArray().put(v.first).put(v.second)) } })
                    o.put("deleted", JSONArray(deletedPlaylists.toList()))
                    o.put("deljf", JSONArray(deletedPlaylistJf.toList()))
                }
            } catch (_: Exception) { return@Runnable }
            ioThread.execute { try { file.writeText(o.toString()) } catch (_: Exception) {} }
        }, 800)
    }
}

/** One finished download (see UserData.downloads); [path] is empty for a Lidarr grab, which arrives through Jellyfin later. */
data class DownloadEntry(val artist: String, val title: String, val album: String, val path: String, val time: Long, val source: String)
