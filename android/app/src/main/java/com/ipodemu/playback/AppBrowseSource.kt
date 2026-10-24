package com.ipodemu.playback

import com.ipodemu.App
import com.ipodemu.library.Group
import com.ipodemu.library.Track
import com.ipodemu.library.offline.OfflineState

/** The app's own lists, as [BrowseSource]. Called on a background thread by the media library service. */
class AppBrowseSource(private val app: App) : BrowseSource {
    private fun resolve(paths: Collection<String>): List<Track> = paths.mapNotNull { app.library.resolve(it, app.userData.meta[it]) }

    override fun recent(): List<Track> = resolve(app.userData.recents.toList().take(100))
    override fun favorites(): List<Track> = resolve(app.userData.favorites.toList().asReversed().take(BrowseTree.MAX_QUEUE))
    override fun downloaded(): List<Track> = app.offline.entries.filter { it.state == OfflineState.DONE }.map { it.toTrack() }
    override fun playlists(): List<Pair<String, String>> { val hidden = app.prefs.hiddenPlaylists; return app.userData.playlists.toList().filter { it.id !in hidden }.map { it.id to it.name } }
    override fun playlistTracks(id: String): List<Track> = app.userData.playlists.toList().firstOrNull { it.id == id }?.let { resolve(it.paths.toList()) } ?: emptyList()
    override fun playlistSummary(id: String): Pair<Int, String?> = app.userData.playlists.toList().firstOrNull { it.id == id }?.let { pl ->
        val paths = pl.paths.toList(); paths.size to paths.asSequence().take(5).firstNotNullOfOrNull { app.library.resolve(it, app.userData.meta[it])?.artKey }
    } ?: (0 to null)
    override fun allSongs(): List<Track> = app.library.songs()
    override fun albums(): List<Group> = app.library.albums()
    override fun artists(): List<Group> = app.library.artists()
    override fun search(query: String): List<Track> {
        val q = query.trim().lowercase(); if (q.isEmpty()) return emptyList()
        return app.library.songs().asSequence().filter { it.title.lowercase().contains(q) || it.artist.lowercase().contains(q) || it.album.lowercase().contains(q) }.take(50).toList()
    }
}
