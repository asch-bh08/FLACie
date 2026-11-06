package com.ipodemu.playback

import com.ipodemu.library.Track
import com.ipodemu.library.matchKey

/**
 * Another copy of the same song in the library, for a song whose file is not there any more (it was swapped for a Hi-Res copy, or moved): the same title
 * and artist, not a clip, Jellyfin's before a download's own address, the longest first (owner, 6 Nov: Rhythm of the Night said streaming and never played).
 */
object OtherCopy {
    fun of(gone: Track, songs: List<Track>): Track? = songs.asSequence()
        .filter { it.path != gone.path && it.matchKey == gone.matchKey && (it.durationMs == 0L || it.durationMs >= 45_000) }
        .sortedWith(compareBy<Track> { it.path.contains("/file?path=") }.thenByDescending { it.durationMs })
        .firstOrNull()
}
