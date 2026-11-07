package com.ipodemu.playback

import com.ipodemu.library.Track
import com.ipodemu.library.baseTitleKey
import com.ipodemu.library.matchKey
import com.ipodemu.library.primaryArtist

/**
 * Another copy of the same song in the library, for a song whose file is not there any more (it was swapped for a Hi-Res copy, or moved): the same title
 * and artist, not a clip, Jellyfin's before a download's own address, the longest first (owner, 6 Nov: Rhythm of the Night said streaming and never played).
 */
object OtherCopy {
    fun of(gone: Track, songs: List<Track>): Track? = best(gone, songs) { it.matchKey == gone.matchKey }
        // none with the same title: the same song without its version note ("Freed From Desire - Radio Edit" swapped for a Hi-Res "Freed From Desire"),
        // by the same lead artist; a remix or a live take keeps its note, so it is never taken (owner's log, 6 Nov)
        ?: baseTitleKey(gone.title).let { base -> val by = primaryArtist(gone.artist); best(gone, songs) { baseTitleKey(it.title) == base && primaryArtist(it.artist) == by } }

    private fun best(gone: Track, songs: List<Track>, same: (Track) -> Boolean): Track? = songs.asSequence()
        .filter { it.path != gone.path && same(it) && (it.durationMs == 0L || it.durationMs >= 45_000) }
        .sortedWith(compareBy<Track> { it.path.contains("/file?path=") }.thenByDescending { it.durationMs })
        .firstOrNull()
}
