package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** Owner, 13 Nov: Voodoo People showed "Their Law: The Singles" though it is also on Music for the Jilted Generation; the album copy wins (as on the web). */
class OriginalAlbumFirstTest {
    private fun t(album: String, albumArtist: String = "The Prodigy") = Track(path = "/m/$album/Voodoo People.flac", title = "Voodoo People", artist = "The Prodigy",
        album = album, albumArtist = albumArtist, genre = "", trackNo = 1, discNo = 1, durationMs = 300_000, year = 1994, isMusic = true, artKey = null, mtime = 0, size = 0,
        source = TrackSource.JELLYFIN)

    @Test fun compilationsAreKnown() {
        assertTrue(isCompilation(t("Their Law: The Singles 1990-2005 [CD1]")))
        assertTrue(isCompilation(t("Greatest Hits", "Queen")))
        assertTrue(isCompilation(t("Dance Anthems", "Various Artists")))
        assertFalse(isCompilation(t("Music for the Jilted Generation")))
        assertFalse(isCompilation(t("Experience")))
    }

    @Test fun oneCopyPerSongIsTheAlbumCopyWhicheverCameFirst() {
        val best = t("Their Law: The Singles 1990-2005 [CD1]"); val album = t("Music for the Jilted Generation")
        assertEquals(album, listOf(best, album).oneCopyBy { it.matchKey }[album.matchKey])
        assertEquals(album, listOf(album, best).oneCopyBy { it.matchKey }[album.matchKey])
    }
}
