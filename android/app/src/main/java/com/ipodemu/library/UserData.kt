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
class UserData(ctx: Context) {
    private val file = File(ctx.filesDir, "userdata.json")
    val favorites = LinkedHashSet<String>()
    val playlists = ArrayList<UserPlaylist>()
    val recents = ArrayList<String>() // newest first
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

    init { load() }

    fun isFavorite(path: String) = path in favorites

    /** Star ratings (see [RatingStore]); [onRated] lets the app write a rating to Jellyfin. */
    val ratings = RatingStore()
    var onRated: ((Track, Int) -> Unit)? = null
    fun rating(t: Track): Int { @Suppress("UNUSED_EXPRESSION") rev; return ratings.get(t) }
    fun setRating(t: Track, stars: Int) { ratings.set(t, stars); changed(); onRated?.invoke(t, RatingStore.clamp(stars)) }
    fun mergeRatings(incoming: List<RatingEntry>) { if (ratings.merge(incoming)) changed(sync = false) }

    private val favStates = HashMap<String, androidx.compose.runtime.MutableState<Boolean>>()
    /** Per-track observable flag: toggling one favourite recomposes only that row, not every row. */
    fun favState(path: String): androidx.compose.runtime.State<Boolean> = favStates.getOrPut(path) { androidx.compose.runtime.mutableStateOf(path in favorites) }

    /** Called after a favourite is added or removed (the offline library follows it). */
    var onFavoritesChanged: (() -> Unit)? = null

    fun toggleFavorite(path: String) {
        if (!favorites.remove(path)) favorites.add(path)
        favStates[path]?.value = path in favorites
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
        recents.remove(path); recents.add(0, path)
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
    private val pendingHandler = android.os.Handler(android.os.Looper.getMainLooper())

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
    fun mergeRemote(favs: List<Triple<String, String, String>>, lists: List<AccountSync.RemotePlaylist>, deleted: List<String>) {
        fun note(p: String, t: String, a: String) { if (t.isNotEmpty() && p !in meta) meta[p] = t to a }
        favs.forEach { (p, t, a) -> note(p, t, a); if (favorites.add(p)) favStates[p]?.value = true }
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
    private fun changed(sync: Boolean = true) { rev++; save(); if (sync) onChanged?.invoke() }

    private fun load() {
        try {
            if (!file.exists()) return
            val o = JSONObject(file.readText())
            ratings.load(o.optJSONArray("ratings"))
            o.optJSONArray("fav")?.let { a -> for (i in 0 until a.length()) favorites.add(a.getString(i)) }
            o.optJSONObject("plays")?.let { p -> p.keys().forEach { k -> plays[k] = p.optInt(k) } }
            o.optJSONArray("recent")?.let { a -> for (i in 0 until a.length()) recents.add(a.getString(i)) }
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
    private val main = android.os.Handler(android.os.Looper.getMainLooper())
    private var savePending = false
    private fun save() {
        if (savePending) return
        savePending = true
        main.postDelayed(Runnable {
            savePending = false
            val o = try {
                JSONObject().also { o ->
                    o.put("fav", JSONArray(favorites.toList()))
                    o.put("ratings", ratings.toJson())
                    o.put("recent", JSONArray(recents.toList()))
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
