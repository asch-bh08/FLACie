package com.ipodemu.library

/**
 * Remembers the playlists stamp FLACie Web last gave, so a change made on the web is noticed within one poll (owner, 5 Nov: a song added to a
 * playlist on the web should be on the phone at once). The first stamp seen is only remembered: the app syncs by itself when it starts.
 */
class PlaylistWatch {
    private var seen: String? = null

    /** True when [rev] differs from the last stamp that was taken in; a blank stamp (an old server) never counts. */
    @Synchronized fun changed(rev: String): Boolean {
        if (rev.isBlank()) return false
        val last = seen
        if (last == null) { seen = rev; return false }
        return rev != last
    }

    /** The playlists for [rev] have been fetched: it is the new baseline. */
    @Synchronized fun taken(rev: String) { if (rev.isNotBlank()) seen = rev }
}
