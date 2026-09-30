package com.ipodemu.library

import android.content.Context
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.setValue
import org.json.JSONArray
import org.json.JSONObject
import java.io.File

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

    private val favStates = HashMap<String, androidx.compose.runtime.MutableState<Boolean>>()
    /** Per-track observable flag: toggling one favourite recomposes only that row, not every row. */
    fun favState(path: String): androidx.compose.runtime.State<Boolean> = favStates.getOrPut(path) { androidx.compose.runtime.mutableStateOf(path in favorites) }

    fun toggleFavorite(path: String) {
        if (!favorites.remove(path)) favorites.add(path)
        favStates[path]?.value = path in favorites
        changed()
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

    fun removeFromPlaylist(id: String, path: String) {
        playlists.firstOrNull { it.id == id }?.let { it.paths.remove(path); it.mtime = System.currentTimeMillis(); changed() }
    }

    fun deletePlaylist(id: String) {
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
            else if (mine.mtime <= syncedAt) { mine.name = name; mine.paths.clear(); mine.paths.addAll(paths) }
        }
        changed(sync = false)
    }
    private fun changed(sync: Boolean = true) { rev++; save(); if (sync) onChanged?.invoke() }

    private fun load() {
        try {
            if (!file.exists()) return
            val o = JSONObject(file.readText())
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
