package com.ipodemu.library.offline

import com.ipodemu.library.Track
import com.ipodemu.library.TrackSource
import org.json.JSONArray

/**
 * What the Download button of an album or a playlist says and does, from how many of its songs are on the phone. Only songs from the server can be kept offline;
 * the others (already on the phone, or not downloadable) are left out of the count. Pure, unit-tested.
 */
class KeepStatus(val kind: Kind, val label: String, val done: Int, val total: Int) {
    enum class Kind { NONE, BUSY, DONE, FAILED, UNAVAILABLE }

    companion object {
        fun of(tracks: List<Track>, entry: (String) -> OfflineEntry?): KeepStatus {
            val songs = tracks.filter { it.source == TrackSource.JELLYFIN }.distinctBy { OfflineMath.keyOf(it.path) }
            if (songs.isEmpty()) return KeepStatus(Kind.UNAVAILABLE, "Not available", 0, 0)
            val es = songs.map { entry(it.path)?.state }
            val done = es.count { it == OfflineState.DONE }; val failed = es.count { it == OfflineState.FAILED }
            val pending = es.count { it == OfflineState.QUEUED || it == OfflineState.DOWNLOADING }
            val missing = es.count { it == null }
            return when {
                // some songs are on their way only because another list wanted them: the rest are still to be asked for
                missing > 0 && done + pending + failed > 0 -> KeepStatus(Kind.NONE, "Download the other $missing", done, songs.size)
                pending > 0 -> KeepStatus(Kind.BUSY, "Downloading $done of ${songs.size}", done, songs.size)
                failed > 0 -> KeepStatus(Kind.FAILED, "$failed failed: try again", done, songs.size)
                done == songs.size -> KeepStatus(Kind.DONE, "Downloaded", done, songs.size)
                done > 0 -> KeepStatus(Kind.NONE, "Download the other ${songs.size - done}", done, songs.size)
                else -> KeepStatus(Kind.NONE, "Download", 0, songs.size)
            }
        }
    }
}

object KeepPlan {
    /** The songs to queue for a whole album, playlist or pick: server songs that are not offline yet (a failed one is tried again), each once. */
    fun toEnqueue(tracks: List<Track>, entry: (String) -> OfflineEntry?): List<Track> =
        tracks.filter { it.source == TrackSource.JELLYFIN }.distinctBy { OfflineMath.keyOf(it.path) }.filter { val e = entry(it.path); e == null || e.state == OfflineState.FAILED }
}

/** Playlists the person keeps offline: songs added to them later download too, songs that leave every such playlist go again. Pure. */
object PlaylistKeepPlan {
    /** [demoteKeys]: songs that leave their last kept playlist but are still favourites kept offline, so they carry on as favourite downloads. */
    class Plan(val add: List<Track>, val removeKeys: List<String>, val demoteKeys: List<String>)

    /** [lists]: the songs of each kept playlist now. [favoriteKeys]: the favourites that are kept offline (empty when that setting is off). */
    fun plan(lists: List<List<Track>>, favoriteKeys: Set<String>, index: OfflineIndex): Plan {
        val wanted = lists.flatten().filter { it.source == TrackSource.JELLYFIN }.associateBy { OfflineMath.keyOf(it.path) }
        val add = wanted.values.filter { !index.has(OfflineMath.keyOf(it.path)) }
        val gone = index.entries.filter { it.reason == OfflineReason.PLAYLIST && it.key !in wanted }
        return Plan(add, gone.filter { it.key !in favoriteKeys }.map { it.key }, gone.filter { it.key in favoriteKeys }.map { it.key })
    }
}

/** The ids of the playlists kept offline, saved in the settings as one JSON text. */
object KeptLists {
    fun encode(ids: Set<String>): String = JSONArray(ids.toList()).toString()
    fun decode(s: String?): Set<String> {
        if (s.isNullOrBlank()) return emptySet()
        return try { val a = JSONArray(s); (0 until a.length()).map { a.optString(it) }.filter { it.isNotEmpty() }.toSet() } catch (_: Exception) { emptySet() }
    }
}
