package com.ipodemu.player

import com.ipodemu.library.HomeShelf
import com.ipodemu.library.HomeShelves
import com.ipodemu.library.RecPick
import com.ipodemu.library.Track
import com.ipodemu.library.TrackSource
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/** Which song a tapped Home card starts with (the owner tapped Price Tag on "Listen again" and I Gotta Feeling played). */
class ShelfStartTest {
    private fun t(n: Int, title: String = "Song $n") = Track(
        path = "http://jf/Audio/$n/stream", title = title, artist = "Ar", album = "Al", albumArtist = "Ar", genre = "", trackNo = n, discNo = 1,
        durationMs = 1000, year = 0, isMusic = true, artKey = "k$n", mtime = 0, size = 0, source = TrackSource.JELLYFIN)

    private fun newSong(): Track = HomeShelves.fromServer(
        listOf(RecPick.Shelf("quick-picks", "Quick picks", "", listOf(RecPick(null, "Fresh", "Someone", "", 1000, true, null)), emptyList())), { null }, emptyList())!![0].tracks[0]

    @Test fun theTappedSongIsFirstWhateverTheListNowLooksLike() {
        val shelf = listOf(t(1, "Hello"), t(2, "Price Tag"), t(3, "I Gotta Feeling"))
        // the shelf was rebuilt between the draw and the tap: the card that was drawn at place 1 (Price Tag) is now at place 0 and a new song leads
        val rebuilt = listOf(t(2, "Price Tag"), t(3, "I Gotta Feeling"), t(1, "Hello"))
        val tapped = shelf[1]
        val s = HomeShelves.startOf(rebuilt, tapped)!!
        assertEquals("Price Tag", s.queue[s.index].title)
    }

    @Test fun newSongsLeaveTheQueueButTheIndexStillPointsAtTheTappedOne() {
        val fresh = newSong()
        val shelf = listOf(fresh, t(1), fresh, t(2), t(3))
        val s = HomeShelves.startOf(shelf, t(3))!!
        assertEquals(listOf("Song 1", "Song 2", "Song 3"), s.queue.map { it.title })
        assertEquals(2, s.index)
    }

    @Test fun aNewSongStartsNothing() {
        assertNull(HomeShelves.startOf(listOf(t(1), newSong()), newSong()))
    }

    @Test fun aTappedSongNoLongerOnTheShelfPlaysFirstOnItsOwnTerms() {
        val s = HomeShelves.startOf(listOf(t(1), t(2)), t(9))!!
        assertEquals(listOf("Song 9", "Song 1", "Song 2"), s.queue.map { it.title })
        assertEquals(0, s.index)
    }

    @Test fun twoCardsOfTheSameSongStartAtTheTappedOne() {
        val a = t(1); val b = t(2)
        val s = HomeShelves.startOf(listOf(a, b, a.copy(title = "Song 1 again")), b)!!
        assertEquals(1, s.index)
    }

    @Test fun byPositionStillWorksForAShelfThatHasNotChanged() {
        val shelf = HomeShelf("listen-again", "Listen again", "", listOf(t(1), t(2), t(3)))
        assertEquals(2, HomeShelves.startOf(shelf.tracks, shelf.tracks[2])!!.index)
    }
}
