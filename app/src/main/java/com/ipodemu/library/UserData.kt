package com.ipodemu.library

import android.content.Context
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.setValue
import org.json.JSONArray
import org.json.JSONObject
import java.io.File

class UserPlaylist(val id: String, var name: String, val paths: MutableList<String>)

/** Favourites, hand-made playlists and play history. Persisted as JSON; [rev] lets Compose observe changes. */
class UserData(ctx: Context) {
    private val file = File(ctx.filesDir, "userdata.json")
    val favorites = LinkedHashSet<String>()
    val playlists = ArrayList<UserPlaylist>()
    val recents = ArrayList<String>() // newest first
    /** How many times each track has been started; feeds the recommendations. */
    val plays = HashMap<String, Int>()

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

    fun recordPlay(path: String) {
        plays[path] = (plays[path] ?: 0) + 1
        recents.remove(path); recents.add(0, path)
        while (recents.size > 120) recents.removeAt(recents.lastIndex)
        changed()
    }

    fun createPlaylist(name: String, first: String? = null): UserPlaylist {
        val p = UserPlaylist("u" + System.currentTimeMillis().toString(36), name.trim().ifEmpty { "New Playlist" }, ArrayList())
        if (first != null) p.paths.add(first)
        playlists.add(p); changed(); return p
    }

    fun addToPlaylist(id: String, path: String) {
        playlists.firstOrNull { it.id == id }?.let { if (path !in it.paths) it.paths.add(path); changed() }
    }

    fun removeFromPlaylist(id: String, path: String) {
        playlists.firstOrNull { it.id == id }?.let { it.paths.remove(path); changed() }
    }

    fun deletePlaylist(id: String) { playlists.removeAll { it.id == id }; changed() }

    fun rename(id: String, name: String) { playlists.firstOrNull { it.id == id }?.name = name.trim().ifEmpty { "Playlist" }; changed() }

    private fun changed() { rev++; save() }

    private fun load() {
        try {
            if (!file.exists()) return
            val o = JSONObject(file.readText())
            o.optJSONArray("fav")?.let { a -> for (i in 0 until a.length()) favorites.add(a.getString(i)) }
            o.optJSONObject("plays")?.let { p -> p.keys().forEach { k -> plays[k] = p.optInt(k) } }
            o.optJSONArray("recent")?.let { a -> for (i in 0 until a.length()) recents.add(a.getString(i)) }
            o.optJSONArray("lists")?.let { a ->
                for (i in 0 until a.length()) {
                    val p = a.getJSONObject(i)
                    val paths = ArrayList<String>()
                    p.getJSONArray("p").let { pa -> for (j in 0 until pa.length()) paths.add(pa.getString(j)) }
                    playlists.add(UserPlaylist(p.getString("id"), p.getString("n"), paths))
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
                    o.put("plays", JSONObject().also { p -> plays.toMap().forEach { (k, v) -> p.put(k, v) } })
                    o.put("lists", JSONArray().also { a -> playlists.forEach { a.put(JSONObject().put("id", it.id).put("n", it.name).put("p", JSONArray(it.paths.toList()))) } })
                }
            } catch (_: Exception) { return@Runnable }
            ioThread.execute { try { file.writeText(o.toString()) } catch (_: Exception) {} }
        }, 800)
    }
}
