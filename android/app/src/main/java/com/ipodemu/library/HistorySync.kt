package com.ipodemu.library

/**
 * The play history shared with FLACie Web (the account profile's "history": path, title, artist, when). The phone's Recently Played was its own plays
 * only, so someone listening on the web saw 0 there and nothing under Recently played in the car. The two lists merge: newest first, one row per song
 * (by address, or by title and artist when the web and the phone spell the address differently), at most [MAX].
 */
object HistorySync {
    data class Entry(val p: String, val t: String, val a: String, val w: Long)
    const val MAX = 200

    fun merge(local: List<Entry>, remote: List<Entry>): List<Entry> {
        val seenPaths = HashSet<String>(); val seenSongs = HashSet<String>()
        val out = ArrayList<Entry>()
        for (e in (local + remote).sortedByDescending { it.w }) {
            val song = if (e.t.isNotBlank()) matchKey(e.t, e.a) else null
            if (e.p in seenPaths || (song != null && song in seenSongs)) continue
            seenPaths += e.p; song?.let { seenSongs += it }
            out += e
            if (out.size == MAX) break
        }
        return out
    }
}
