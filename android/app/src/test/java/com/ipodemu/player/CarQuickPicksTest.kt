package com.ipodemu.player

import com.ipodemu.library.HomeShelf
import com.ipodemu.library.HomeShelves
import com.ipodemu.library.RecPick
import com.ipodemu.library.Track
import com.ipodemu.library.TrackSource
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class CarQuickPicksTest {
    private fun t(n: Int) = Track(
        path = "http://jf/$n", title = "Song $n", artist = "Ar", album = "Al", albumArtist = "Ar", genre = "", trackNo = n, discNo = 1,
        durationMs = 1000, year = 0, isMusic = true, artKey = "k$n", mtime = 0, size = 0, source = TrackSource.JELLYFIN)

    // a song the server suggests that the phone does not have: the same placeholder Home shows as a "New" row
    private fun newSong(): Track = HomeShelves.fromServer(
        listOf(RecPick.Shelf("quick-picks", "Quick picks", "", listOf(RecPick(null, "Fresh", "Someone", "", 1000, true, null)), emptyList())), { null }, emptyList())!![0].tracks[0]

    @Test fun Car_quick_picks_drop_new_songs_and_thumbed_down_ones_and_keep_the_order() {
        val fresh = newSong(); assertTrue(HomeShelves.isNew(fresh))
        val shelves = listOf(HomeShelf("listen-again", "Listen again", "", listOf(t(9))), HomeShelf("quick-picks", "Quick picks", "", listOf(t(3), fresh, t(1), t(4), t(2))))
        val picks = carQuickPicks(shelves, listOf(com.ipodemu.library.matchKey("Song 4", "Ar")))
        assertEquals(listOf("Song 3", "Song 1", "Song 2"), picks.map { it.title })
    }

    @Test fun Car_quick_picks_are_empty_without_the_shelf() {
        assertTrue(carQuickPicks(null, emptyList()).isEmpty())
        assertTrue(carQuickPicks(listOf(HomeShelf("listen-again", "x", "", listOf(t(1)))), emptyList()).isEmpty())
    }
}
