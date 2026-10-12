package com.ipodemu.playback

import com.ipodemu.library.Track
import com.ipodemu.library.TrackSource
import org.json.JSONArray
import org.json.JSONObject

/**
 * The play queue kept on disk, so the app comes back after being killed with the same song and position (paused) instead of nothing.
 * Pure (no Android classes besides org.json), so it is unit-tested on the JVM.
 */
object QueueStore {
    /** At most this many songs are kept (a "play all" of ten thousand is not written out): a window around the current song. */
    const val MAX = 300
    private const val BEHIND = 20
    const val VERSION = 1

    /** [shuffleOrder]: the play order (indices into [tracks]) of a shuffled queue, or null when none was saved or it no longer fits. */
    class Saved(val tracks: List<Track>, val index: Int, val positionMs: Long, val shuffleOrder: IntArray? = null)

    /** [shuffleOrder] (queue indices in play order, whole queue) is kept for the window that is written, so a reordered shuffle survives a kill. */
    fun encode(tracks: List<Track>, index: Int, positionMs: Long, shuffleOrder: IntArray? = null): String {
        val i = index.coerceIn(0, (tracks.size - 1).coerceAtLeast(0))
        val from = (i - BEHIND).coerceAtLeast(0).coerceAtMost((tracks.size - MAX).coerceAtLeast(0))
        val window = tracks.subList(from, minOf(tracks.size, from + MAX))
        val a = JSONArray()
        for (t in window) a.put(JSONObject()
            .put("p", t.path).put("ti", t.title).put("ar", t.artist).put("al", t.album).put("aa", t.albumArtist).put("g", t.genre)
            .put("tn", t.trackNo).put("dn", t.discNo).put("d", t.durationMs).put("y", t.year).put("m", t.isMusic)
            .put("k", t.artKey ?: "").put("mt", t.mtime).put("s", t.size).put("src", t.source.name).put("fp", t.filePath).put("ot", t.origTitle))
        val root = JSONObject().put("v", VERSION).put("i", i - from).put("pos", positionMs.coerceAtLeast(0)).put("t", a)
        if (shuffleOrder != null && shuffleOrder.size == tracks.size) {
            val so = JSONArray()
            for (v in shuffleOrder) if (v >= from && v < from + window.size) so.put(v - from)
            root.put("so", so)
        }
        return root.toString()
    }

    /**
     * Reads a saved queue. A song for which [keep] says no (its file is gone, its source was removed) is dropped; if the current song is one of
     * those, playback comes back at the start of the song that took its place. Returns null for a broken file or an empty queue.
     */
    fun decode(json: String, keep: (Track) -> Boolean = { true }): Saved? {
        val root = try { JSONObject(json) } catch (_: Exception) { return null }
        if (root.optInt("v", 0) != VERSION) return null
        val arr = root.optJSONArray("t") ?: return null
        val saved = root.optInt("i", 0)
        val out = ArrayList<Track>(); var index = -1; var currentKept = false
        val newIndex = IntArray(arr.length()) { -1 }   // saved position -> position after dropping what is gone (-1: dropped)
        for (n in 0 until arr.length()) {
            val o = arr.optJSONObject(n) ?: continue
            val t = try { track(o) } catch (_: Exception) { continue }
            val ok = keep(t)
            if (n == saved) currentKept = ok
            if (ok) { if (n >= saved && index < 0) index = out.size; newIndex[n] = out.size; out.add(t) }
        }
        if (out.isEmpty()) return null
        if (index < 0) index = out.lastIndex   // everything from the current song on is gone: the last song left
        val pos = if (currentKept) root.optLong("pos", 0L).coerceAtLeast(0L) else 0L
        // the saved play order, minus the songs that were dropped; only used when it is still a permutation of what is left
        val so = root.optJSONArray("so")
        var order: IntArray? = null
        if (so != null && so.length() == arr.length()) {
            val mapped = ArrayList<Int>(out.size)
            for (k in 0 until so.length()) { val v = so.optInt(k, -1); if (v in newIndex.indices && newIndex[v] >= 0) mapped.add(newIndex[v]) }
            val a = mapped.toIntArray()
            if (QueueEdit.isPermutation(a, out.size)) order = a
        }
        return Saved(out, index, pos, order)
    }

    private fun track(o: JSONObject) = Track(
        path = o.getString("p"), title = o.optString("ti"), artist = o.optString("ar"), album = o.optString("al"), albumArtist = o.optString("aa"),
        genre = o.optString("g"), trackNo = o.optInt("tn"), discNo = o.optInt("dn"), durationMs = o.optLong("d"), year = o.optInt("y"),
        isMusic = o.optBoolean("m", true), artKey = o.optString("k").ifEmpty { null }, mtime = o.optLong("mt"), size = o.optLong("s"),
        source = try { TrackSource.valueOf(o.optString("src", "LOCAL")) } catch (_: Exception) { TrackSource.LOCAL },
        filePath = o.optString("fp"), origTitle = o.optString("ot"),
    )

    /** A local file that is gone is stale; streamed and content: songs cannot be checked here and are kept (the player reports it if one fails). */
    fun stillThere(t: Track, fileExists: (String) -> Boolean): Boolean {
        val p = t.path
        return if (p.startsWith("http:") || p.startsWith("https:") || p.startsWith("smb:") || p.startsWith("content:")) true
        else fileExists(if (p.startsWith("file:")) p.removePrefix("file://").removePrefix("file:") else p)
    }
}
