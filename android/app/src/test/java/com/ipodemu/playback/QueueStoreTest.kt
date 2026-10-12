package com.ipodemu.playback

import com.ipodemu.library.Track
import com.ipodemu.library.TrackSource
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class QueueStoreTest {
    private fun t(n: Int, path: String = "/music/$n.flac", src: TrackSource = TrackSource.LOCAL) = Track(
        path = path, title = "Title $n \"quoted\" é", artist = "Artist", album = "Album", albumArtist = "AA", genre = "Rock", trackNo = n, discNo = 1,
        durationMs = 180_000L + n, year = 1999, isMusic = true, artKey = if (n % 2 == 0) "jf$n" else null, mtime = 5L, size = 7L, source = src,
        filePath = "/srv/$n.flac", origTitle = "",
    )

    @Test fun roundTripKeepsTracksIndexAndPosition() {
        val tracks = (1..5).map { t(it) }
        val s = QueueStore.decode(QueueStore.encode(tracks, 3, 61_500L))!!
        assertEquals(tracks, s.tracks)
        assertEquals(3, s.index)
        assertEquals(61_500L, s.positionMs)
    }

    @Test fun sourceAndNullArtSurvive() {
        val s = QueueStore.decode(QueueStore.encode(listOf(t(1, "http://x/Audio/abc/stream", TrackSource.JELLYFIN), t(2, src = TrackSource.NAS)), 0, 0))!!
        assertEquals(TrackSource.JELLYFIN, s.tracks[0].source)
        assertNull(s.tracks[0].artKey)
        assertEquals("jf2", s.tracks[1].artKey)
    }

    @Test fun hugeQueueIsWindowedAroundTheCurrentSong() {
        val tracks = (0 until 5000).map { t(it) }
        val s = QueueStore.decode(QueueStore.encode(tracks, 2500, 1000L))!!
        assertEquals(QueueStore.MAX, s.tracks.size)
        assertEquals("/music/2500.flac", s.tracks[s.index].path)
    }

    @Test fun windowAtTheEndStillHoldsTheCurrentSong() {
        val tracks = (0 until 1000).map { t(it) }
        val s = QueueStore.decode(QueueStore.encode(tracks, 999, 0L))!!
        assertEquals("/music/999.flac", s.tracks[s.index].path)
        assertEquals(QueueStore.MAX, s.tracks.size)
    }

    @Test fun removedTracksAreDroppedAndIndexFollows() {
        val tracks = (1..5).map { t(it) }
        val s = QueueStore.decode(QueueStore.encode(tracks, 3, 40_000L)) { it.trackNo != 2 }!!
        assertEquals(listOf(1, 3, 4, 5), s.tracks.map { it.trackNo })
        assertEquals(2, s.index)               // still song 4
        assertEquals(40_000L, s.positionMs)
    }

    @Test fun currentSongGoneStartsTheNextOneFromTheTop() {
        val tracks = (1..5).map { t(it) }
        val s = QueueStore.decode(QueueStore.encode(tracks, 2, 40_000L)) { it.trackNo != 3 }!!
        assertEquals(4, s.tracks[s.index].trackNo)
        assertEquals(0L, s.positionMs)
    }

    @Test fun everythingAfterTheCurrentSongGoneFallsBackToTheLastOne() {
        val tracks = (1..4).map { t(it) }
        val s = QueueStore.decode(QueueStore.encode(tracks, 3, 9L)) { it.trackNo < 3 }!!
        assertEquals(2, s.tracks[s.index].trackNo)
        assertEquals(0L, s.positionMs)
    }

    @Test fun nothingLeftOrBrokenFileGivesNull() {
        assertNull(QueueStore.decode(QueueStore.encode(listOf(t(1)), 0, 0)) { false })
        assertNull(QueueStore.decode("not json"))
        assertNull(QueueStore.decode("""{"v":99,"t":[]}"""))
        assertNull(QueueStore.decode("""{"v":1}"""))
    }

    @Test fun aBadEntryIsSkippedNotFatal() {
        val json = """{"v":1,"i":1,"pos":5,"t":[{"x":1},{"p":"/a.flac","ti":"A"}]}"""
        val s = QueueStore.decode(json)
        assertNotNull(s)
        assertEquals("/a.flac", s!!.tracks.single().path)
        assertEquals(0, s.index)
    }

    @Test fun staleCheckKeepsStreamsAndChecksLocalFiles() {
        val exists = { p: String -> p == "/music/1.flac" }
        assertTrue(QueueStore.stillThere(t(1), exists))
        assertTrue(!QueueStore.stillThere(t(2), exists))
        assertTrue(QueueStore.stillThere(t(3, "https://h/Audio/x/stream"), exists))
        assertTrue(QueueStore.stillThere(t(4, "smb://nas/a.flac"), exists))
        assertTrue(QueueStore.stillThere(t(5, "content://media/1"), exists))
    }

    // ---- an edited queue comes back as it was left ----------------------------------------------------------------------------------

    @Test fun aReorderedQueueIsSavedAndRestoredInThatOrder() {
        // Up next after "move up" twice: the stored list order is what the player has
        val tracks = listOf(t(1), t(2), t(4), t(3), t(5))
        val s = QueueStore.decode(QueueStore.encode(tracks, 1, 12_000L))!!
        assertEquals(listOf(1, 2, 4, 3, 5), s.tracks.map { it.trackNo })
        assertEquals(1, s.index)
        assertNull(s.shuffleOrder)
    }

    @Test fun aShuffleOrderRoundTrips() {
        val tracks = (0 until 6).map { t(it) }
        val order = intArrayOf(3, 0, 5, 1, 4, 2)
        val s = QueueStore.decode(QueueStore.encode(tracks, 5, 0L, order))!!
        assertTrue(order.contentEquals(s.shuffleOrder))
        assertEquals(5, s.tracks[s.index].trackNo)
    }

    @Test fun theShuffleOrderFollowsTheWindowAndDroppedSongs() {
        val tracks = (0 until 5000).map { t(it) }
        val order = IntArray(5000) { 4999 - it }   // plays backwards
        val s = QueueStore.decode(QueueStore.encode(tracks, 2500, 0L, order))!!
        assertEquals(QueueStore.MAX, s.tracks.size)
        val o = s.shuffleOrder!!
        assertTrue(QueueEdit.isPermutation(o, s.tracks.size))
        // the window plays backwards too
        assertEquals(s.tracks.lastIndex, o.first()); assertEquals(0, o.last())

        val five = (0 until 5).map { t(it) }
        val dropped = QueueStore.decode(QueueStore.encode(five, 0, 0L, intArrayOf(4, 2, 0, 3, 1))) { it.trackNo != 2 }!!
        assertTrue(intArrayOf(3, 0, 2, 1).contentEquals(dropped.shuffleOrder))   // 4,0,3,1 renumbered after the drop of 2
    }

    @Test fun aBrokenShuffleOrderIsIgnoredNotFatal() {
        val tracks = (0 until 3).map { t(it) }
        val json = QueueStore.encode(tracks, 0, 0L, intArrayOf(2, 1, 0)).replace("\"so\":[2,1,0]", "\"so\":[1,1,0]")
        val s = QueueStore.decode(json)!!
        assertNull(s.shuffleOrder)
        assertEquals(3, s.tracks.size)
        assertNull(QueueStore.decode(QueueStore.encode(tracks, 0, 0L, intArrayOf(0, 1)))!!.shuffleOrder)   // wrong length is not written
    }
}
