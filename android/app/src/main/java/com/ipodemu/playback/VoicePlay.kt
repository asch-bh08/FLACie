package com.ipodemu.playback

/**
 * "Hey Google, play ... on FLACie": Google opens the app with MEDIA_PLAY_FROM_SEARCH, the words heard and, when it worked them out, what they are (a
 * song, album, artist or playlist) with its artist, album and title. The app said it handled this but did nothing with it; now the words go to the DJ
 * as a play request, the same as the car's voice search. Null: just "play music", which carries on with the last queue (or shuffles the library).
 */
object VoicePlay {
    private const val SONG = "vnd.android.cursor.item/audio"
    private const val ALBUM = "vnd.android.cursor.item/album"
    private const val ARTIST = "vnd.android.cursor.item/artist"
    private const val PLAYLIST = "vnd.android.cursor.item/playlist"

    fun words(query: String?, focus: String?, artist: String?, album: String?, title: String?): String? {
        val q = query?.trim().orEmpty()
        val a = artist?.trim().orEmpty(); val al = album?.trim().orEmpty(); val t = title?.trim().orEmpty()
        when (focus) {
            SONG -> if (t.isNotEmpty()) return "play $t" + if (a.isNotEmpty()) " by $a" else ""
            ALBUM -> if (al.isNotEmpty()) return "play the album $al" + if (a.isNotEmpty()) " by $a" else ""
            ARTIST -> if (a.isNotEmpty()) return "play $a"
            PLAYLIST -> if (q.isNotEmpty()) return "play my ${q.replaceFirstChar { it.uppercase() }} playlist"
        }
        // Google says "music" for a bare "play music on FLACie"
        if (q.isEmpty() || q.equals("music", ignoreCase = true)) return null
        return if (q.startsWith("play ", ignoreCase = true)) q else "play $q"
    }
}
