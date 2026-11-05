package com.ipodemu.library

/**
 * The DJ's playlist steps (F9) on the phone's own playlists, by name. They go through [UserData] like a tap on the screen, so they reach the account and
 * FLACie Web the same way. A name is matched ignoring case; a playlist that is missing is made when songs are added to it.
 */
class DjPlaylistEdits(private val ud: UserData) {
    private fun find(name: String): UserPlaylist? = ud.playlists.firstOrNull { it.name.trim().equals(name.trim(), ignoreCase = true) }

    private fun findOrMake(name: String): UserPlaylist = find(name) ?: ud.createPlaylist(name)

    /** Puts [paths] at the end of [name] (made if missing), skipping ones already in it. */
    fun add(name: String, paths: List<String>) {
        if (name.isBlank()) return
        val p = findOrMake(name)
        for (path in paths) ud.addToPlaylist(p.id, path)
    }

    /** Takes [paths] out of [name]; nothing happens when there is no such playlist. */
    fun remove(name: String, paths: List<String>) {
        val p = find(name) ?: return
        for (path in paths) ud.removeFromPlaylist(p.id, path)
    }

    /** Makes [name] with [paths], or fills the one of that name. */
    fun create(name: String, paths: List<String>) = add(name, paths)

    /** Renames [name] to [to]; nothing happens when there is no such playlist or no new name. */
    fun rename(name: String, to: String) {
        if (to.isBlank()) return
        find(name)?.let { ud.rename(it.id, to) }
    }
}
