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

    var rev by mutableIntStateOf(0)
        private set

    init { load() }

    fun isFavorite(path: String) = path in favorites

    fun toggleFavorite(path: String) {
        if (!favorites.remove(path)) favorites.add(path)
        changed()
    }

    fun recordPlay(path: String) {
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

    private fun save() {
        try {
            val o = JSONObject()
            o.put("fav", JSONArray(favorites.toList()))
            o.put("recent", JSONArray(recents))
            o.put("lists", JSONArray().also { a -> playlists.forEach { a.put(JSONObject().put("id", it.id).put("n", it.name).put("p", JSONArray(it.paths))) } })
            file.writeText(o.toString())
        } catch (_: Exception) {}
    }
}
