package com.ipodemu.player

import com.ipodemu.library.Track
import com.ipodemu.library.TrackSource
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** The tick boxes in a list: which rows are ticked, Select all, and what "Download N" does with a mixed pick (owned songs are kept offline, the rest are asked for). */
class PickSetTest {
    private fun t(n: Int, src: TrackSource = TrackSource.JELLYFIN) = Track(
        path = "http://jf/Audio/%032x/stream".format(n), title = "Song $n", artist = "A", album = "", albumArtist = "A", genre = "", trackNo = n, discNo = 1,
        durationMs = 1000, year = 0, isMusic = true, artKey = null, mtime = 0, size = 0, source = src)

    @Test fun tapTicksAndUntick() {
        val p = PickSet().toggle("a").toggle("b").toggle("a")
        assertEquals(1, p.count); assertTrue("b" in p); assertFalse("a" in p)
    }
    @Test fun selectAllTicksEverythingAndAgainClears() {
        val all = listOf("a", "b", "c")
        val p = PickSet().toggle("a").selectAllOrNone(all)
        assertEquals(3, p.count); assertTrue(p.allOf(all))
        assertEquals(0, p.selectAllOrNone(all).count)
    }
    @Test fun downloadLabelCountsThePick() {
        assertEquals("Download 1", PickSet.downloadLabel(1)); assertEquals("Download 12", PickSet.downloadLabel(12)); assertEquals("Download", PickSet.downloadLabel(0))
    }
    @Test fun mixedPickSplitsIntoOfflineAndFetch() {
        val owned = mapOf("x" to t(1), "y" to t(2))
        val s = PickSet.split(listOf("x", "z", "y", "w", "x")) { owned[it] }
        assertEquals(listOf("Song 1", "Song 2"), s.keep.map { it.title }); assertEquals(listOf("z", "w"), s.fetch)
    }
    @Test fun saysWhatHappened() {
        assertEquals("Keeping 2 songs on this phone and getting 3 more", PickSet.summary(2, 3))
        assertEquals("Keeping 1 song on this phone", PickSet.summary(1, 0))
        assertEquals("Getting 1 song", PickSet.summary(0, 1))
    }
}
