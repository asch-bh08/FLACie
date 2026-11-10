package com.ipodemu.library

import org.json.JSONArray
import org.json.JSONObject
import java.io.File

/**
 * What the first screen needs, kept on disk: Home's shelves exactly as they were last drawn (a few dozen songs and album covers), so a cold start draws
 * Home from this small file instead of waiting for the whole library to load and for the shelves to be worked out from it (see [Library], HomeScreen).
 * An album card only needs its name, cover and first song, so that is all that is kept of it.
 */
object HomeSnapshot {
    class Data(val shelves: List<HomeShelf>, val moods: List<String>)

    private const val VERSION = 1

    fun toJson(data: Data): String {
        val shelves = JSONArray()
        for (s in data.shelves) shelves.put(JSONObject().put("id", s.id).put("title", s.title).put("sub", s.subtitle)
            .put("tracks", JSONArray().also { a -> s.tracks.forEach { a.put(TrackJson.toObj(it)) } })
            .put("albums", JSONArray().also { a -> s.albums.forEach { g -> g.tracks.firstOrNull()?.let { first -> a.put(JSONObject().put("n", g.name).put("k", g.artKey ?: "").put("t", TrackJson.toObj(first))) } } }))
        return JSONObject().put("v", VERSION).put("moods", JSONArray(data.moods)).put("shelves", shelves).toString()
    }

    /** The saved shelves, or null when [raw] is not a usable snapshot. Never throws. */
    fun fromJson(raw: String): Data? = try {
        val o = JSONObject(raw)
        if (o.optInt("v") != VERSION) null else {
            val shelves = o.getJSONArray("shelves").let { a -> List(a.length()) { i -> shelf(a.getJSONObject(i)) } }
            val moods = o.optJSONArray("moods")?.let { a -> List(a.length()) { a.getString(it) } } ?: emptyList()
            if (shelves.isEmpty()) null else Data(shelves, moods)
        }
    } catch (_: Exception) { null }

    private fun shelf(o: JSONObject): HomeShelf {
        val ts = o.optJSONArray("tracks")?.let { a -> List(a.length()) { TrackJson.fromObj(a.getJSONObject(it)) } } ?: emptyList()
        val gs = o.optJSONArray("albums")?.let { a -> List(a.length()) { i -> a.getJSONObject(i).let { g -> Group(g.optString("n"), listOf(TrackJson.fromObj(g.getJSONObject("t"))), g.optString("k").ifEmpty { null }) } } } ?: emptyList()
        return HomeShelf(o.optString("id"), o.optString("title"), o.optString("sub"), ts, gs)
    }

    fun write(file: File, data: Data) {
        val tmp = File(file.path + ".tmp")
        tmp.writeText(toJson(data)); file.delete(); tmp.renameTo(file)
    }

    fun read(file: File): Data? = try { if (file.exists()) fromJson(file.readText()) else null } catch (_: Exception) { null }

    /** The cover keys of the first screens, in the order they are drawn (shelf by shelf, left to right), with whether the small (row) size is what is drawn. */
    fun coverKeys(data: Data, limit: Int = 48): List<Pair<String, Boolean>> {
        val out = LinkedHashMap<String, Boolean>()
        for (s in data.shelves) {
            for (t in s.tracks) t.artKey?.let { out.putIfAbsent(it, true) }
            for (g in s.albums) g.artKey?.let { out.putIfAbsent(it, false) }
            if (out.size >= limit) break
        }
        return out.entries.take(limit).map { it.key to it.value }
    }
}
