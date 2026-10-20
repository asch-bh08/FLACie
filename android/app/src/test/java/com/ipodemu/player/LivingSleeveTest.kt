package com.ipodemu.player

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

/** The pure parts of the Living Sleeve player: the cover's two colours, the song's loudness shape and its cache, the bars, and when the background may drift. */
class LivingSleeveTest {
    private fun argb(r: Int, g: Int, b: Int) = (0xFF shl 24) or (r shl 16) or (g shl 8) or b
    private fun r(c: Int) = (c shr 16) and 255
    private fun g(c: Int) = (c shr 8) and 255
    private fun b(c: Int) = c and 255
    private fun light(c: Int) = (maxOf(r(c), g(c), b(c)) + minOf(r(c), g(c), b(c))) / 510.0

    // ---- colours -------------------------------------------------------------------------------------------------

    @Test fun aRedCoverGivesALightRedMainColour() {
        val c = SleeveColours.extract(IntArray(24 * 24) { argb(200, 20, 20) })
        assertTrue(c.fromCover)
        assertTrue("red leads", r(c.main) > g(c.main) + 40 && r(c.main) > b(c.main) + 40)
        // the main colour is lifted to about 66% lightness so dark text on it reads
        assertEquals(0.66, light(c.main), 0.03)
    }

    @Test fun theSecondColourIsTheNextHueAndMuchDarker() {
        // two thirds green, one third blue: green is the main hue, blue the second
        val c = SleeveColours.extract(IntArray(24 * 24) { i -> if (i % 3 == 0) argb(20, 30, 220) else argb(20, 220, 40) })
        assertTrue("main is green", g(c.main) > r(c.main) && g(c.main) > b(c.main))
        assertTrue("second is blue", b(c.second) > g(c.second) && b(c.second) > r(c.second))
        assertEquals(0.32, light(c.second), 0.03)
    }

    @Test fun aVividPatchBeatsALargerGreyArea() {
        // weights follow saturation cubed: a small saturated orange patch outweighs plenty of dull grey-blue
        val c = SleeveColours.extract(IntArray(24 * 24) { i -> if (i < 60) argb(255, 120, 0) else argb(120, 128, 140) })
        assertTrue("orange wins", r(c.main) > b(c.main) + 60)
    }

    @Test fun nearBlackAndNearWhitePixelsAreIgnoredAndAnEmptyCoverFallsBack() {
        val c = SleeveColours.extract(IntArray(100) { if (it % 2 == 0) argb(0, 0, 0) else argb(255, 255, 255) })
        assertFalse(c.fromCover)
        assertEquals(SleeveColours.DEFAULT, c)
        assertEquals(SleeveColours.DEFAULT, SleeveColours.extract(IntArray(0)))
    }

    @Test fun oneHueOnlyUsesItForBothFields() {
        val c = SleeveColours.extract(IntArray(50) { argb(30, 40, 230) })
        assertTrue(b(c.main) > r(c.main) && b(c.second) > r(c.second))
        assertTrue("the second is the darker one", light(c.second) < light(c.main))
    }

    // ---- loudness slices -------------------------------------------------------------------------------------------

    @Test fun rmsOfEachSliceIsNormalisedToTheLoudest() {
        // 4 slices over 4 seconds of mono audio at constant amplitudes 1000, 2000, 4000, 0
        val s = WaveSlicer(slices = 4, durationUs = 4_000_000L)
        for ((i, amp) in listOf(1000, 2000, 4000, 0).withIndex()) {
            val block = ShortArray(1000) { amp.toShort() }
            s.add(i * 1_000_000L, block, block.size, channels = 1)
        }
        assertArrayEquals(intArrayOf(64, 128, 255, 0), s.result())   // 255 * 1000 / 4000 = 63.75
    }

    @Test fun onlyTheFirstChannelCounts() {
        val s = WaveSlicer(2, 2_000_000L)
        // interleaved L,R: the right channel is the opposite of the left and must be ignored
        s.add(0L, ShortArray(200) { if (it % 2 == 0) 3000 else 1 }, 200, channels = 2)
        s.add(1_000_000L, ShortArray(200) { if (it % 2 == 0) 1500 else 30000 }, 200, channels = 2)
        assertArrayEquals(intArrayOf(255, 128), s.result())
    }

    @Test fun aBlockPastTheEndLandsInTheLastSliceAndSilenceStaysZero() {
        val s = WaveSlicer(3, 3_000_000L)
        s.add(9_000_000L, ShortArray(10) { 500 }, 10, 1)
        s.add(-5L, ShortArray(10) { 0 }, 10, 1)
        assertArrayEquals(intArrayOf(0, 0, 255), s.result())
        assertArrayEquals(IntArray(3), WaveSlicer(3, 3_000_000L).result())   // nothing decoded: all zero, never a division by zero
    }

    @Test fun anUnknownLengthIsRefused() {
        assertNull(WaveSlicer.orNull(slices = 200, durationUs = 0L))
        assertNull(WaveSlicer.orNull(slices = 200, durationUs = -1L))
    }

    // ---- cache ---------------------------------------------------------------------------------------------------

    @Test fun cacheKeyIsStableHexAndDiffersPerPath() {
        val a = WaveCache.key("content://media/external/audio/media/42")
        assertEquals(a, WaveCache.key("content://media/external/audio/media/42"))
        assertNotEquals(a, WaveCache.key("content://media/external/audio/media/43"))
        assertEquals(40, a.length); assertTrue(a.all { it in "0123456789abcdef" })
    }

    @Test fun aShapeSurvivesTheDiskAndABrokenFileIsIgnored() {
        val dir = File.createTempFile("waves", "").apply { delete(); mkdirs() }
        try {
            val shape = IntArray(200) { it }
            WaveCache.write(dir, "/a/b.flac", shape)
            assertArrayEquals(shape, WaveCache.read(dir, "/a/b.flac"))
            assertNull(WaveCache.read(dir, "/other.flac"))
            File(dir, WaveCache.key("/short.flac") + ".wave").writeBytes(ByteArray(7))
            assertNull("a file of the wrong length is not a shape", WaveCache.read(dir, "/short.flac"))
        } finally { dir.deleteRecursively() }
    }

    // ---- bars ----------------------------------------------------------------------------------------------------

    @Test fun levelBarsWhileThereIsNoShape() {
        val h = WaveBars.heights(null, 5)
        assertEquals(5, h.size); assertTrue(h.all { it == 0.4f })
    }

    @Test fun eachBarTakesTheLoudestSliceUnderIt() {
        // a ramp from 0 to 199: the 3rd percentile is 6 and the 97th is 194
        val shape = IntArray(200) { it }
        val h = WaveBars.heights(shape, 2)
        assertEquals(0.12f + 0.88f * (99 - 6) / 188f, h[0], 0.001f)
        assertEquals(1.0f, h[1], 0.001f)
        assertEquals("more bars than slices still gives every bar a height", 400, WaveBars.heights(shape, 400).size)
    }

    @Test fun aLoudlyMasteredSongIsStretchedSoItsShapeShows() {
        // everything between 130 and 180, as a brick-walled master: the quiet part reads low and the loud part high
        val shape = IntArray(200) { if (it < 100) 130 + it % 3 else 178 + it % 3 }
        val h = WaveBars.heights(shape, 2)
        assertTrue("quiet half low: ${h[0]}", h[0] < .2f)
        assertTrue("loud half high: ${h[1]}", h[1] > .95f)
    }

    @Test fun aFlatShapeDrawsLevelBars() = assertTrue(WaveBars.heights(IntArray(200) { 150 }, 8).all { it == 0.4f })

    @Test fun barCountFollowsTheWidthInThreeAndTwo() {
        assertEquals(8, WaveBars.count(10f, 3f, 2f))      // never fewer than 8
        assertEquals(60, WaveBars.count(300f, 3f, 2f))    // (300 + 2) / 5
    }

    // ---- motion --------------------------------------------------------------------------------------------------

    @Test fun theBackgroundDriftsOnlyWhilePlayingWithMotionOn() {
        assertTrue(SleeveMotion.drifts(playing = true, motionPref = true, animatorScale = 1f))
        assertFalse(SleeveMotion.drifts(false, true, 1f))
        assertFalse(SleeveMotion.drifts(true, false, 1f))
        assertFalse("system animations off", SleeveMotion.drifts(true, true, 0f))
    }

    // ---- design I: ground and tone, the title, the pill, the ring ------------------------------------------------

    @Test fun theGroundIsDarkAndTheToneSitsBetweenItAndTheText() {
        val c = SleeveColours.extract(IntArray(24 * 24) { if (it % 2 == 0) argb(200, 20, 20) else argb(20, 40, 200) })
        assertEquals(0.16, light(c.ground), 0.03)
        assertEquals(0.26, light(c.tone), 0.03)
        assertTrue("text on the tone is light", light(c.toneFg) > .85)
    }

    @Test fun theDefaultGroundMatchesTheWeb() {
        assertEquals(argb(22, 58, 63), SleeveColours.DEFAULT.ground)
        assertEquals(argb(36, 82, 90), SleeveColours.DEFAULT.tone)
    }

    @Test fun theLastWordOfTheTitleIsSplitOffForTheItalics() {
        assertEquals("Hey " to "Jude", SleeveText.split("Hey Jude"))
        assertEquals("Here Comes the " to "Sun", SleeveText.split("Here Comes the Sun  "))
        assertEquals("" to "Yesterday", SleeveText.split("Yesterday"))
        assertEquals("" to "", SleeveText.split(""))
    }

    @Test fun thePillNamesTheAlbumAndTrackElseWhereItPlaysFrom() {
        assertEquals("ABBEY ROAD · TRACK 7", SleeveText.where("Abbey Road", 7, "Jellyfin"))
        assertEquals("JELLYFIN", SleeveText.where("Abbey Road", 0, "Jellyfin"))
        assertEquals("JELLYFIN", SleeveText.where(" ", 3, "Jellyfin"))
    }

    @Test fun theRingStartsAtTwelveAndGoesClockwise() {
        val w = 200f
        assertEquals(0f, RingSeek.fraction(100f, 5f, w, w)!!, 0.001f)        // 12 o'clock
        assertEquals(.25f, RingSeek.fraction(195f, 100f, w, w)!!, 0.001f)    // 3 o'clock
        assertEquals(.5f, RingSeek.fraction(100f, 195f, w, w)!!, 0.001f)     // 6 o'clock
        assertEquals(.75f, RingSeek.fraction(5f, 100f, w, w)!!, 0.001f)      // 9 o'clock
    }

    @Test fun aTouchOnThePlayButtonOrOutsideTheRingIsNoSeek() {
        assertNull(RingSeek.fraction(100f, 100f, 200f, 200f))
        assertNull(RingSeek.fraction(100f, 60f, 200f, 200f))   // 40% of the radius: the play button
        assertNull(RingSeek.fraction(100f, -20f, 200f, 200f))  // well outside
    }

    @Test fun barsArePlayedUpToTheirMiddle() {
        assertTrue(RingSeek.isPlayed(0, 80, .5f / 80))
        assertFalse(RingSeek.isPlayed(1, 80, .5f / 80))
        assertFalse(RingSeek.isPlayed(0, 80, 0f))
        assertTrue(RingSeek.isPlayed(79, 80, 1f))
    }
}
