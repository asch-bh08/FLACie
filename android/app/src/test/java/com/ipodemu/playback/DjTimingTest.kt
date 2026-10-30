package com.ipodemu.playback

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** The DJ's timing rule, with the same cases as DjTimingTests.cs on the server (the web player uses that one). */
class DjTimingTest {
    @Test fun unknownIntroOrClipTalksAsBefore() {
        assertEquals(DjTimingPlan.NOW, DjTiming.plan(null, 4_000))
        assertEquals(DjTimingPlan.NOW, DjTiming.plan(8_000, null))
    }

    // owner, 30 Oct: waiting for a long intro to run down sounded like the DJ "talks randomly 10 seconds in"; it talks as the song starts
    @Test fun aClipThatFitsTheIntroStartsWithTheSong() {
        assertEquals(DjTimingPlan(0, 0), DjTiming.plan(12_000, 4_000))
        assertEquals(DjTimingPlan(0, 0), DjTiming.plan(30_000, 4_000))
        assertEquals(DjTimingPlan(0, 0), DjTiming.plan(4_500, 4_000))
    }

    // a line starting once the song is already heard (after a skip, a slow clip) never holds it back: that silenced it and started it again from the top
    @Test fun aSongAlreadyUnderWayIsNeverHeldBack() {
        assertEquals(DjTimingPlan(0, 1_500), DjTiming.started(DjTimingPlan(0, 1_500), 0))
        assertEquals(DjTimingPlan(0, 1_500), DjTiming.started(DjTimingPlan(0, 1_500), DjTiming.HOLD_WITHIN_MS.toLong()))
        assertEquals(DjTimingPlan(0, 0), DjTiming.started(DjTimingPlan(0, 1_500), DjTiming.HOLD_WITHIN_MS + 1L))
        assertEquals(DjTimingPlan(0, 0), DjTiming.started(DjTiming.before(6_000), 2_000))
    }

    @Test fun aLongerClipLeadsTheSongInNeverMoreThanOneAndAHalfSeconds() {
        assertEquals(DjTimingPlan(0, 1_000), DjTiming.plan(4_000, 4_500))
        assertEquals(DjTimingPlan(0, 1_500), DjTiming.plan(0, 6_000))
        for (intro in listOf(0, 1_000, 3_000, 6_000, 20_000)) for (clip in listOf(500, 2_000, 5_000, 9_000, 15_000)) {
            val p = DjTiming.plan(intro, clip)
            assertTrue(p.songDelayMs in 0..DjTiming.MAX_SONG_DELAY_MS)
            assertTrue(p.clipAtMs >= 0)
            if (p.clipAtMs > 0) assertTrue(p.clipAtMs + clip <= intro - DjTiming.MARGIN_MS)
        }
    }

    @Test fun aClipsLengthIsGuessedFromItsWordsUntilKnown() {
        assertEquals(DjTiming.CLIP_PAD_MS + 5_000, DjTiming.estimateMs("one two three four five six seven eight nine ten eleven twelve thirteen"))
    }

    @Test fun theIntroComesInTheServersAnswer() {
        assertEquals(7_200, DjCue.parse("""{"speak":true,"text":"Hi","caption":"Hi","clip":"/api/dj/clip/x.mp3","backup":true,"introMs":7200}""")?.introMs)
        assertNull(DjCue.parse("""{"speak":true,"text":"Hi","caption":"Hi","clip":"/api/dj/clip/x.mp3","backup":true,"introMs":null}""")?.introMs)
        assertNull(DjCue.parse("""{"speak":true,"text":"Hi","caption":"Hi","clip":"/api/dj/clip/x.mp3","backup":true}""")?.introMs)
    }

    // "Talk before the song" (owner, 27 Oct): the song waits for the whole line, however long; the same as DjTiming.Before on the server
    @Test fun beforeTheSongHoldsItBackForTheWholeClip() {
        assertEquals(DjTimingPlan(0, 4_000 + DjTiming.MARGIN_MS), DjTiming.before(4_000))
        assertEquals(DjTimingPlan(0, 9_000 + DjTiming.MARGIN_MS), DjTiming.before(9_000))
    }

    @Test fun beforeComesInTheSettingsAndIsOffUnlessChosen() {
        assertEquals(false, DjSettings().before)
        assertEquals(true, DjSettings.parse("""{"before":true}""").before)
        assertEquals(true, DjSettings.parse(DjSettings(before = true).toJson().toString()).before)
    }
}
