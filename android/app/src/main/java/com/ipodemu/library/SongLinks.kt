package com.ipodemu.library

/**
 * One song at a link (owner, 8 Nov: "import single songs from a link, like SoundCloud remixes"): a SoundCloud track or a YouTube video, cleaned to the
 * address FLACie Web's yt-dlp service fetches. Sets, playlists, profiles and other sites are not one song. Twin of FLACie.Core `PlaylistLinks.SongLink`.
 */
object SongLinks {
    data class Song(val url: String, val service: String)

    private val soundCloud = Regex("""^https://(?:www\.|m\.)?soundcloud\.com/([A-Za-z0-9_-]+)/([A-Za-z0-9_-]+)/?(?:[?#].*)?$""", RegexOption.IGNORE_CASE)
    private val youTube = Regex("""^https://(?:(?:www\.|m\.|music\.)?youtube\.com/watch\?(?:[^#]*&)?v=|youtu\.be/)([A-Za-z0-9_-]{11})(?:[?&#].*)?$""", RegexOption.IGNORE_CASE)
    private val notTracks = setOf("sets", "tracks", "albums", "reposts", "likes", "followers", "following", "popular-tracks", "comments")
    private val notUsers = setOf("discover", "search", "you", "stream", "charts")

    fun of(text: String?): Song? {
        val u = text?.trim().orEmpty()
        if (u.isEmpty() || ' ' in u) return null
        soundCloud.matchEntire(u)?.let { m ->
            val (user, track) = m.destructured
            if (track.lowercase() !in notTracks && user.lowercase() !in notTracks && user.lowercase() !in notUsers) return Song("https://soundcloud.com/$user/$track", "SoundCloud")
        }
        youTube.matchEntire(u)?.let { m -> if (!u.contains("list=", ignoreCase = true)) return Song("https://www.youtube.com/watch?v=${m.groupValues[1]}", "YouTube") }
        return null
    }
}
