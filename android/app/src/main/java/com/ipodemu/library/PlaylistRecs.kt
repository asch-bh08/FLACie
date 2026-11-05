package com.ipodemu.library

import org.json.JSONObject

/**
 * "Recommended songs" under one of the person's own playlists (as on the web, FLACie.Core PlaylistRecs): FLACie Web works out about ten songs from the
 * library that go with the playlist and are not in it (`GET /api/recommend/playlist`); the phone finds its own copy of each and adds one to the end of
 * the playlist with one tap. Without FLACie Web, or when it fails, the section is not shown.
 */
object PlaylistRecs {
    const val N = 10

    class Pick(val path: String, val title: String, val artist: String)

    /** The server's answer: `{ picks: [ { path, title, artist, ... } ] }`. */
    fun parse(json: String): List<Pick> {
        val a = JSONObject(json).optJSONArray("picks") ?: return emptyList()
        return List(a.length()) { a.getJSONObject(it) }.map { Pick(it.optString("path"), it.optString("title"), it.optString("artist")) }
            .filter { it.title.isNotBlank() || it.path.isNotBlank() }
    }

    /**
     * The picks as this phone's songs (the server's path, else the same title and artist here), leaving out songs the phone can't find, songs already in
     * the playlist (by [inList], the playlist's song keys: the phone's copy may be newer than the server's) and the same song twice.
     */
    fun onPhone(picks: List<Pick>, resolve: (Pick) -> Track?, inList: Set<String>): List<Track> {
        val seen = HashSet<String>()
        return picks.mapNotNull(resolve).filter { it.matchKey !in inList && seen.add(it.matchKey) }
    }

    /** One tap on "+": the song goes to the end of the playlist (synced like any other playlist edit). False when it was already there. */
    fun add(ud: UserData, playlistId: String, t: Track): Boolean {
        val p = ud.playlists.firstOrNull { it.id == playlistId } ?: return false
        if (t.path in p.paths) return false
        ud.addToPlaylist(playlistId, t.path)
        return true
    }
}
