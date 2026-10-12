package com.ipodemu.library

import com.ipodemu.player.SeekHold
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** Beta 57: the seek bar holds where the finger lifted, covers are warmed around the current song, a song with no server picture gets one catalog key per album. */
class Beta57RegressionTest {
    // ---- seek bar ---------------------------------------------------------------------------------------------------------------
    @Test fun knobFollowsFingerThenHoldThenPlayback() {
        assertEquals(.7f, SeekHold.shown(.7f, .5f, .1f), 0f)       // finger down wins
        assertEquals(.5f, SeekHold.shown(null, .5f, .1f), 0f)      // lifted: stays where it was let go, no snap back to .1
        assertEquals(.1f, SeekHold.shown(null, null, .1f), 0f)     // nothing held: playback
        assertEquals(1f, SeekHold.shown(2f, null, 0f), 0f)         // clamped
        assertEquals(0f, SeekHold.shown(null, null, -3f), 0f)
    }

    @Test fun holdEndsOnlyWhenPlaybackArrives() {
        assertFalse("old position still reported", SeekHold.settled(.5f, .1f))
        assertTrue(SeekHold.settled(.5f, .505f))
        assertTrue(SeekHold.settled(.5f, .495f))
        assertFalse(SeekHold.settled(.5f, .52f))
        assertFalse("nothing held", SeekHold.settled(null, .5f))
        assertTrue(SeekHold.GIVE_UP_MS in 500..5000)
    }

    // ---- warm plan --------------------------------------------------------------------------------------------------------------
    private val keys = listOf("a", "b", "c", "d", "e", "f", "g", "h")

    @Test fun warmPlanOrderAndRange() {
        assertEquals(listOf("b", "c", "d", "e", "f", "g"), WarmPlan.rows(keys, 2))    // previous + next four, in play order
        assertEquals(listOf("b", "c", "d", "e"), WarmPlan.large(keys, 2))             // previous + next two
    }

    @Test fun warmPlanAtEdgesAndWithBlanks() {
        assertEquals(listOf("a", "b", "c", "d", "e"), WarmPlan.rows(keys, 0))
        assertEquals(listOf("g", "h"), WarmPlan.large(keys, 7))
        assertEquals(listOf("a", "c"), WarmPlan.large(listOf("a", null, "c"), 1))     // a song with no cover is skipped, not fatal
        assertTrue(WarmPlan.rows(emptyList(), 0).isEmpty())
    }

    // ---- catalog key ------------------------------------------------------------------------------------------------------------
    private fun song(title: String, album: String, artist: String = "The Prodigy", albumArtist: String = "") = Track(
        path = "http://jf/Audio/%032x/stream?static=true".format(title.hashCode()), title = title, artist = artist, album = album, albumArtist = albumArtist, genre = "", trackNo = 1, discNo = 1,
        durationMs = 1000, year = 2000, isMusic = true, artKey = null, mtime = 0, size = 0, source = TrackSource.JELLYFIN)

    @Test fun songsOfOneAlbumShareOneKey() {
        val a = CoverVetting.catalogKey(song("Out of Space", "Experience"))
        val b = CoverVetting.catalogKey(song("Charly", "Experience"))
        assertNotNull(a); assertEquals(a, b)
        assertTrue(a!!.startsWith("it"))
    }

    @Test fun albumArtistWinsAndDifferentAlbumsDiffer() {
        assertEquals(CoverVetting.catalogKey(song("X", "Album", "Guest", "Host")), CoverVetting.catalogKey(song("Y", "Album", "Other", "Host")))
        assertTrue(CoverVetting.catalogKey(song("X", "One")) != CoverVetting.catalogKey(song("X", "Two")))
    }

    @Test fun noAlbumNameFallsBackToTitleAndUnknownGivesNoKeyWithoutArtist() {
        val a = CoverVetting.catalogKey(song("Alone", "Unknown Album"))
        val b = CoverVetting.catalogKey(song("Other", "Unknown Album"))
        assertNotNull(a); assertTrue("by title when no album", a != b)
        assertNull(CoverVetting.catalogKey(song("T", "Album", artist = "")))
    }
}
