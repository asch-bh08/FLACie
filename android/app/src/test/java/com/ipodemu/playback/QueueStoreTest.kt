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
}
