package com.ipodemu.playback

import com.ipodemu.library.Track
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class SleepAndSpeedTest {
    private fun t(album: String, artist: String = "A", albumArtist: String = "", dur: Long = 180_000, genre: String = "") = Track(
        path = "http://x/Audio/${"%032x".format((album + dur + artist).hashCode())}/stream", title = "t", artist = artist, album = album, albumArtist = albumArtist, genre = genre,
        trackNo = 1, discNo = 1, durationMs = dur, year = 0, isMusic = true, artKey = null, mtime = 0, size = 0)

    // ---- fade ----
    @Test fun fadeIsFullThenFallsToSilence() {
        assertEquals(1f, SleepPlan.fadeGain(60_000), 0f)
        assertEquals(1f, SleepPlan.fadeGain(30_000), 0f)
        assertEquals(0f, SleepPlan.fadeGain(0), 0f)
        assertEquals(0f, SleepPlan.fadeGain(-5), 0f)
        assertEquals(0.5f, SleepPlan.fadeGain(15_000), 0.001f)
    }

    @Test fun fadeNeverRisesAsTimeRunsOut() {
        var prev = 2f
        for (ms in 30_000L downTo 0L step 500L) { val g = SleepPlan.fadeGain(ms); assertTrue(g <= prev + 1e-6f); assertTrue(g in 0f..1f); prev = g }
    }

    // ---- end of track / album ----
    @Test fun trackRemaining() {
        assertEquals(60_000L, SleepPlan.trackRemaining(120_000, 180_000))
        assertEquals(0L, SleepPlan.trackRemaining(200_000, 180_000))
        assertNull(SleepPlan.trackRemaining(10, 0))
    }

    @Test fun albumEndsWhereTheNextSongIsAnotherAlbum() {
        val q = listOf(t("One"), t("One"), t("Two"), t("Two"), t("Two"))
        assertFalse(SleepPlan.albumEndsAfter(q, 0)); assertTrue(SleepPlan.albumEndsAfter(q, 1))
        assertFalse(SleepPlan.albumEndsAfter(q, 2)); assertTrue(SleepPlan.albumEndsAfter(q, 4))
        assertTrue("past the queue", SleepPlan.albumEndsAfter(q, 9))
    }

    @Test fun sameAlbumNameByAnotherArtistIsAnotherAlbum() {
        assertTrue(SleepPlan.albumEndsAfter(listOf(t("Hits", artist = "X"), t("Hits", artist = "Y")), 0))
        assertFalse("album artist decides", SleepPlan.albumEndsAfter(listOf(t("Hits", artist = "X", albumArtist = "VA"), t("hits ", artist = "Y", albumArtist = "va")), 0))
    }

    @Test fun songsWithoutAnAlbumEachEndTheirOwn() {
        assertTrue(SleepPlan.albumEndsAfter(listOf(t(""), t("")), 0))
    }

    @Test fun albumRemainingAddsTheRestOfTheAlbum() {
        val q = listOf(t("One", dur = 100_000), t("One", dur = 200_000), t("One", dur = 300_000), t("Two", dur = 50_000))
        assertEquals(40_000L + 200_000 + 300_000, SleepPlan.albumRemaining(q, 0, 60_000))
        assertEquals(300_000L - 100_000, SleepPlan.albumRemaining(q, 2, 100_000))
        assertNull("unknown length", SleepPlan.albumRemaining(listOf(t("One", dur = 100_000), t("One", dur = 0)), 0, 0))
    }

    // ---- extend ----
    @Test fun extendAddsToWhatIsLeftOrStartsFromNow() {
        assertEquals(1_000_000L + 15 * 60_000L, SleepPlan.extend(1_000_000L, 900_000L))
        assertEquals(900_000L + 15 * 60_000L, SleepPlan.extend(100_000L, 900_000L))
        assertEquals(1_000_000L + 5 * 60_000L, SleepPlan.extend(1_000_000L, 0L, 5))
    }

    @Test fun extendOnlyInTheLastStretchOfARunningTimer() {
        assertTrue(SleepPlan.canExtend(SleepMode.MINUTES, 60_000)); assertTrue(SleepPlan.canExtend(SleepMode.END_OF_TRACK, 0))
        assertFalse(SleepPlan.canExtend(SleepMode.MINUTES, 10 * 60_000)); assertFalse(SleepPlan.canExtend(SleepMode.OFF, 1000)); assertFalse(SleepPlan.canExtend(SleepMode.MINUTES, null))
    }

    // ---- shake ----
    @Test fun threeJoltsCountAsOneShake() {
        var n = 0; val d = ShakeDetector { n++ }
        d.onSample(0f, 0f, 9.8f, 0)                // at rest: nothing
        d.onSample(30f, 0f, 9.8f, 100); d.onSample(-30f, 0f, 9.8f, 300); d.onSample(30f, 0f, 9.8f, 500)
        assertEquals(1, n)
        d.onSample(30f, 0f, 9.8f, 700); d.onSample(-30f, 0f, 9.8f, 900); d.onSample(30f, 0f, 9.8f, 1100)
        assertEquals("cooldown: the same shake counts once", 1, n)
        d.onSample(30f, 0f, 0f, 5000); d.onSample(-30f, 0f, 0f, 5200); d.onSample(30f, 0f, 0f, 5400)
        assertEquals(2, n)
    }

    @Test fun aSingleBumpOrSlowWalkIsNotAShake() {
        var n = 0; val d = ShakeDetector { n++ }
        d.onSample(30f, 0f, 0f, 0)
        d.onSample(30f, 0f, 0f, 3000); d.onSample(30f, 0f, 0f, 6000)      // too far apart
        d.onSample(12f, 0f, 9.8f, 6100); d.onSample(12f, 0f, 9.8f, 6300)   // gentle: under 2.2 g
        assertEquals(0, n)
    }

    // ---- speed ----
    @Test fun speedStepsClampAndWrapNowhere() {
        assertEquals(1.25f, SpeedPolicy.step(1.0f, true), 0f); assertEquals(0.75f, SpeedPolicy.step(1.0f, false), 0f)
        assertEquals(3.0f, SpeedPolicy.step(3.0f, true), 0f); assertEquals(0.5f, SpeedPolicy.step(0.5f, false), 0f)
        assertEquals(1.5f, SpeedPolicy.step(1.3f, true), 0f)
        assertEquals(3f, SpeedPolicy.clamp(9f), 0f); assertEquals(0.5f, SpeedPolicy.clamp(0.1f), 0f); assertEquals(1f, SpeedPolicy.clamp(Float.NaN), 0f)
    }

    @Test fun speedLabels() {
        assertEquals("1x", SpeedPolicy.label(1f)); assertEquals("1.25x", SpeedPolicy.label(1.25f)); assertEquals("0.5x", SpeedPolicy.label(0.5f)); assertEquals("3x", SpeedPolicy.label(3f)); assertEquals("1.5x", SpeedPolicy.label(1.5f))
    }

    @Test fun spokenWordByGenre() {
        for (g in listOf("Audiobook", "Podcast", "Spoken Word", "Hörbuch", "Speech", "Lecture")) assertTrue(g, SpeedPolicy.isSpoken(g))
        for (g in listOf("Rock", "Jazz", "Electronic", "Classical", "Soundtrack", "")) assertFalse(g, SpeedPolicy.isSpoken(g))
    }

    @Test fun longItemControlsShowForSpokenLongOrChangedSpeed() {
        assertFalse(SpeedPolicy.showLongItemControls("Rock", 200_000, 1f))
        assertTrue(SpeedPolicy.showLongItemControls("Audiobook", 200_000, 1f))
        assertTrue(SpeedPolicy.showLongItemControls("Rock", 25 * 60_000L, 1f))
        assertTrue(SpeedPolicy.showLongItemControls("Rock", 200_000, 1.5f))
    }

    @Test fun itemKeyIsTheJellyfinIdWhenThereIsOne() {
        val id = "a1b2c3d4e5f60718293a4b5c6d7e8f90"
        assertEquals(id, SpeedPolicy.keyOf("https://srv/Audio/${id.uppercase()}/stream?static=true&api_key=zzz"))
        assertEquals(SpeedPolicy.keyOf("http://one/Audio/$id/stream"), SpeedPolicy.keyOf("http://two:8096/Audio/$id/universal"))
        assertEquals("/music/a.flac", SpeedPolicy.keyOf("/music/a.flac"))
    }

    @Test fun resumePointRules() {
        val book = 3 * 3_600_000L
        assertEquals(0L, SpeedPolicy.resumeAt(null, book))
        assertEquals(0L, SpeedPolicy.resumeAt(2_000, book))
        assertEquals(600_000L, SpeedPolicy.resumeAt(600_000, book))
        assertEquals("finished", 0L, SpeedPolicy.resumeAt(book - 5_000, book))
        assertEquals("short songs always start at 0", 0L, SpeedPolicy.resumeAt(100_000, 200_000))
        assertTrue(SpeedPolicy.shouldSavePlace(600_000, book)); assertFalse(SpeedPolicy.shouldSavePlace(1_000, book))
        assertFalse(SpeedPolicy.shouldSavePlace(book - 1_000, book)); assertFalse(SpeedPolicy.shouldSavePlace(100_000, 200_000))
    }

    // ---- memory ----
    @Test fun memoryKeepsTheNewestAndForgetsTheDefault() {
        val m = ItemMemory(cap = 3, defaultValue = 1.0)
        m.set("a", 1.5); m.set("b", 2.0); m.set("c", 0.75); m.set("d", 1.25)
        assertNull("oldest dropped", m.get("a")); assertEquals(1.25, m.get("d")!!, 0.0); assertEquals(3, m.size)
        m.set("b", 1.0)
        assertNull("1x is the default: not stored", m.get("b")); assertEquals(2, m.size)
        m.set("c", null); assertNull(m.get("c"))
    }

    @Test fun memorySurvivesAnEncodeDecodeRoundTrip() {
        val m = ItemMemory(defaultValue = 1.0)
        m.set("id=with=equals", 1.75); m.set("plain", 2.5); m.set("/music/a b.flac", 0.5)
        val back = ItemMemory.decode(m.encode(), defaultValue = 1.0)
        assertEquals(1.75, back.get("id=with=equals")!!, 0.0); assertEquals(2.5, back.get("plain")!!, 0.0); assertEquals(0.5, back.get("/music/a b.flac")!!, 0.0)
        assertEquals(3, back.size)
        assertEquals(0, ItemMemory.decode(null).size); assertEquals(0, ItemMemory.decode("garbage\n=\nx=notanumber").size)
    }

    @Test fun updatingAnItemMovesItToTheNewest() {
        val m = ItemMemory(cap = 2)
        m.set("a", 1.5); m.set("b", 1.5); m.set("a", 2.0); m.set("c", 1.5)
        assertEquals(2.0, m.get("a")!!, 0.0); assertNull(m.get("b"))
    }
}
