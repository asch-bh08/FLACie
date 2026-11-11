package com.ipodemu.player

import com.ipodemu.library.Group
import com.ipodemu.library.Track
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class HomePickTest {
    private fun album(name: String, artist: String, n: Int = 5) = Group(name, (1..n).map { Track("/$artist/$name/$it.flac", "$name $it", artist, name, artist, "", it, 1, 0L, 0, true, null, 0L, 0L) }, null)

    @Test fun anArtistPlayedLatelyWinsAndTheSameDayGivesTheSamePick() {
        val albums = listOf(album("A", "Nobody"), album("B", "The Prodigy"), album("C", "Someone"))
        val p = HomePick.pick(albums, emptySet(), setOf("the prodigy"), day = 7)
        assertEquals("B", p?.name)
        assertEquals(p, HomePick.pick(albums, emptySet(), setOf("the prodigy"), day = 7))
    }

    @Test fun anAlbumHeardLatelyAndShortOnesAreLeftOut() {
        val heard = album("B", "The Prodigy")
        val albums = listOf(heard, album("Short", "The Prodigy", n = 2), album("C", "Someone"))
        assertEquals("C", HomePick.pick(albums, setOf(heard.tracks[0].albumKey), setOf("the prodigy"), day = 3)?.name)
    }

    @Test fun aLibraryWithoutWholeAlbumsStillGetsAPick() = assertEquals("Pair", HomePick.pick(listOf(album("Pair", "X", n = 2), album("Tiny", "Y", n = 1)), emptySet(), emptySet(), 1)?.name)

    @Test fun nothingToPickIsNull() = assertNull(HomePick.pick(listOf(album("Tiny", "X", n = 1)), emptySet(), emptySet(), 1))
}
