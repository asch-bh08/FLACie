package com.ipodemu.playback

import com.ipodemu.library.Track
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class FadeBetweenTest {
    private fun t(album: String, no: Int, disc: Int = 1, artist: String = "A", dur: Long = 200_000) = Track(
        path = "p$album$no$disc", title = "t$no", artist = artist, album = album, albumArtist = artist, genre = "", trackNo = no, discNo = disc,
        durationMs = dur, year = 0, isMusic = true, artKey = null, mtime = 0, size = 0)

    @Test fun nextTrackOfTheSameAlbumIsContinuous() {
        assertTrue(FadeBetween.continuous(t("X", 3), t("X", 4)))
        assertTrue("the first track of the next disc", FadeBetween.continuous(t("X", 12, disc = 1), t("X", 1, disc = 2)))
        assertFalse("a skipped track is not a sequence", FadeBetween.continuous(t("X", 3), t("X", 5)))
        assertFalse("another album", FadeBetween.continuous(t("X", 3), t("Y", 4)))
        assertFalse("another artist with the same album name", FadeBetween.continuous(t("X", 3, artist = "A"), t("X", 4, artist = "B")))
        assertFalse(FadeBetween.continuous(null, t("X", 1))); assertFalse(FadeBetween.continuous(t("X", 1), null)); assertFalse("no album", FadeBetween.continuous(t("", 1), t("", 2)))
    }

    @Test fun songsWithoutTrackNumbersOfOneAlbumAreASequence() {
        assertTrue(FadeBetween.continuous(t("X", 0), t("X", 0)))
    }

    @Test fun flagsSayWhichEndsFade() {
        val a = t("X", 1); val b = t("X", 2); val c = t("Y", 1)
        val mid = FadeBetween.flags(a, b, c)
        assertFalse(mid.fadeIn); assertTrue(mid.fadeOut)
        val first = FadeBetween.flags(null, a, b)
        assertTrue(first.fadeIn); assertFalse(first.fadeOut)
        val alone = FadeBetween.flags(null, a, null)
        assertTrue(alone.fadeIn); assertTrue(alone.fadeOut)
    }

    @Test fun gainIsOneInTheMiddleAndFallsAtFadedEnds() {
        val f = FadeBetween.Flags(true, true)
        assertEquals(1f, FadeBetween.gain(100_000, 200_000, 4_000, f), 0f)
        assertEquals(0f, FadeBetween.gain(0, 200_000, 4_000, f), 0f)
        assertEquals(0.5f, FadeBetween.gain(2_000, 200_000, 4_000, f), 0.001f)
        assertEquals(0f, FadeBetween.gain(200_000, 200_000, 4_000, f), 0f)
        assertEquals(0.5f, FadeBetween.gain(198_000, 200_000, 4_000, f), 0.001f)
    }

    @Test fun anEndWithoutAFlagIsLeftAlone() {
        assertEquals(1f, FadeBetween.gain(0, 200_000, 4_000, FadeBetween.Flags(false, true)), 0f)
        assertEquals(1f, FadeBetween.gain(199_999, 200_000, 4_000, FadeBetween.Flags(true, false)), 0f)
        assertEquals(1f, FadeBetween.gain(0, 200_000, 4_000, FadeBetween.Flags(false, false)), 0f)
    }

    @Test fun offOrUnknownLengthChangesNothingAndShortSongsKeepAMiddle() {
        val f = FadeBetween.Flags(true, true)
        assertEquals(1f, FadeBetween.gain(0, 200_000, 0, f), 0f); assertEquals(1f, FadeBetween.gain(0, 0, 4_000, f), 0f)
        // a 6 s song with a 12 s fade: each side fades over 2 s, the middle 2 s is full
        assertEquals(1f, FadeBetween.gain(3_000, 6_000, 12_000, f), 0f); assertTrue(FadeBetween.gain(1_000, 6_000, 12_000, f) < 1f)
    }

    @Test fun neverOutsideZeroToOne() {
        val f = FadeBetween.Flags(true, true)
        for (p in -5_000L..205_000L step 777L) { val g = FadeBetween.gain(p, 200_000, 8_000, f); assertTrue("$p -> $g", g in 0f..1f) }
    }
}
