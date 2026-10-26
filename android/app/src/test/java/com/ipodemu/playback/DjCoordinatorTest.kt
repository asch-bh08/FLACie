package com.ipodemu.playback

import com.ipodemu.library.Track
import com.ipodemu.library.TrackSource
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class DjCoordinatorTest {
    private val cue = DjCue("Next up, Adele", "Next up, Adele", "/api/dj/clip/a.mp3", false)
    private fun song(n: Int) = DjSong("Song $n", "Artist", "Album", "Artist", 2015, "Pop", 200_000, "/m/$n.flac", null, n, 1)

    /** A coordinator that has asked about [next] and been answered with [answer]. */
    private fun holding(next: String, answer: DjCue? = cue): DjCoordinator {
        val c = DjCoordinator(); val ticket = c.ask("cur\n$next")
        assertTrue(c.accept(ticket, next, answer))
        return c
    }

    @Test fun theRequestCarriesEveryFieldOfTheContract() {
        val c = DjCoordinator()
        val o = c.request(DjSurface.AUTO, song(2), song(1), "jfPlaylist", 21, 5)
        assertEquals(setOf("surface", "next", "prev", "prevPlayedOut", "skippedRecently", "playlistId", "queueDj", "localHour", "weekday", "why"), o.keys().asSequence().toSet())
        assertEquals("auto", o.getString("surface")); assertEquals("jfPlaylist", o.getString("playlistId"))
        assertEquals(21, o.getInt("localHour")); assertEquals(5, o.getInt("weekday"))
        assertTrue("asked as the previous song starts: it is expected to play out", o.getBoolean("prevPlayedOut"))
        assertFalse(o.getBoolean("skippedRecently")); assertTrue("untouched", o.isNull("queueDj")); assertTrue(o.isNull("why"))
        val next = o.getJSONObject("next")
        assertEquals(setOf("title", "artist", "album", "albumArtist", "year", "genre", "durationMs", "path", "jellyfinId", "trackNo", "discNo"), next.keys().asSequence().toSet())
        assertEquals("Song 2", next.getString("title")); assertEquals(2015, next.getInt("year")); assertEquals(200_000L, next.getLong("durationMs")); assertEquals(2, next.getInt("trackNo"))
        assertTrue(next.isNull("jellyfinId")); assertEquals("Song 1", o.getJSONObject("prev").getString("title"))
        assertEquals("phone", c.request(DjSurface.PHONE, song(2), null, null, 0, 0).getString("surface"))
    }

    @Test fun theRequestSaysNullsAsNulls() {
        val c = DjCoordinator()
        val o = c.request(DjSurface.PHONE, song(1), null, "", 8, 0)
        assertTrue(o.isNull("prev")); assertTrue("an empty id is no playlist", o.isNull("playlistId"))
        c.flip(effectiveNow = false)
        assertTrue("the queue switch once touched", c.request(DjSurface.PHONE, song(1), null, null, 8, 0).getBoolean("queueDj"))
    }

    @Test fun aSongIsSentByWhatTheServerKnowsItBy() {
        val jf = Track(path = "http://jf:8096/Audio/0123456789abcdef0123456789abcdef/stream?static=true", title = "Hello", artist = "Adele", album = "25", albumArtist = "Adele",
            genre = "Pop", trackNo = 1, discNo = 1, durationMs = 295_000, year = 2015, isMusic = true, artKey = null, mtime = 0, size = 0, source = TrackSource.JELLYFIN,
            filePath = "/media/music/Adele/25/01 Hello.flac")
        val s = DjSong.of(jf)
        assertEquals("/media/music/Adele/25/01 Hello.flac", s.path); assertEquals("0123456789abcdef0123456789abcdef", s.jellyfinId)
        assertEquals(DjSong("Hello", "Adele", "25", "Adele", 2015, "Pop", 295_000, "/media/music/Adele/25/01 Hello.flac", "0123456789abcdef0123456789abcdef", 1, 1), s)
        val local = DjSong.of(jf.copy(path = "/sdcard/Music/hello.mp3", filePath = "", source = TrackSource.LOCAL))
        assertEquals("/sdcard/Music/hello.mp3", local.path); assertNull(local.jellyfinId)
    }

    @Test fun theCueIsSaidOnlyWhenTheSongStartsByItself() {
        assertEquals(cue, holding("/b").changed(DjCoordinator.Change.AUTO, "/b"))
        assertNull("skipped to it", holding("/b").changed(DjCoordinator.Change.SKIP, "/b", 120_000))
        assertNull("a new queue that starts with it", holding("/b").changed(DjCoordinator.Change.NEW_QUEUE, "/b"))
        assertNull("Repeat one", holding("/b").changed(DjCoordinator.Change.REPEAT, "/b"))
    }

    @Test fun theCueIsSaidOnlyForTheSongItWasFetchedFor() {
        assertNull("another song came next (the queue was changed)", holding("/b").changed(DjCoordinator.Change.AUTO, "/c"))
        assertNull(holding("/b").changed(DjCoordinator.Change.AUTO, null))
        assertNull("the server had nothing to say", holding("/b", answer = null).changed(DjCoordinator.Change.AUTO, "/b"))
    }

    @Test fun aCueIsUsedOnceAndAnySongChangeUsesItUp() {
        val c = holding("/b")
        assertEquals(cue, c.changed(DjCoordinator.Change.AUTO, "/b"))
        assertNull("not again", c.changed(DjCoordinator.Change.AUTO, "/b"))
        val d = holding("/b")
        assertNull(d.changed(DjCoordinator.Change.SKIP, "/x", 50_000))
        assertNull("the skip dropped it", d.changed(DjCoordinator.Change.AUTO, "/b"))
    }

    @Test fun theSameQuestionIsNotAskedTwiceAndALateAnswerIsDropped() {
        val c = DjCoordinator()
        val first = c.ask("a\nb")
        assertTrue(first > 0); assertEquals("asked already", -1, c.ask("a\nb"))
        val second = c.ask("a\nc")
        assertTrue(second > 0)
        assertFalse("the answer about b came after the question about c", c.accept(first, "b", cue)); assertNull(c.pending)
        assertTrue(c.accept(second, "c", cue)); assertEquals("c", c.pendingPath)
        assertNull("a new question drops the old song's cue", c.also { it.ask("a\nd") }.pending)
    }

    @Test fun aChangeOfSongLetsTheNextQuestionGoOut() {
        val c = DjCoordinator(); c.ask("a\nb")
        c.changed(DjCoordinator.Change.AUTO, "b")
        assertTrue(c.ask("a\nb") > 0)
    }

    @Test fun aQuickSkipInTheLastTwoChangesIsRemembered() {
        val c = DjCoordinator()
        c.changed(DjCoordinator.Change.SKIP, "/2", 4_000)
        assertTrue("skipped after 4 s", c.skippedRecently)
        assertTrue(c.request(DjSurface.PHONE, song(3), song(2), null, 0, 0).getBoolean("skippedRecently"))
        c.changed(DjCoordinator.Change.AUTO, "/3"); assertTrue("one change ago", c.skippedRecently)
        c.changed(DjCoordinator.Change.AUTO, "/4"); assertFalse("two songs played out since", c.skippedRecently)
    }

    @Test fun aSkipAfterListeningIsNotAQuickSkip() {
        val c = DjCoordinator()
        c.changed(DjCoordinator.Change.SKIP, "/2", 30_000); assertFalse(c.skippedRecently)
        c.changed(DjCoordinator.Change.SKIP, "/3", DjCoordinator.QUICK_SKIP_MS); assertFalse("10 s is listened to", c.skippedRecently)
        c.changed(DjCoordinator.Change.SKIP, "/4", -1L); assertFalse("not known", c.skippedRecently)
        c.changed(DjCoordinator.Change.SKIP, "/5", 9_999); assertTrue(c.skippedRecently)
        c.changed(DjCoordinator.Change.NEW_QUEUE, "/6"); assertTrue("a new queue is not a change that counts", c.skippedRecently)
        c.reset(); assertFalse("Clear forgets it", c.skippedRecently)
    }

    @Test fun theQueueSwitchFlipsWhatIsInForceAndANewQueueResetsIt() {
        val c = DjCoordinator()
        assertNull(c.queueDj)
        assertTrue(c.flip(effectiveNow = false)); assertEquals(true, c.queueDj)
        assertFalse(c.flip(effectiveNow = true)); assertEquals(false, c.queueDj)
        c.newQueue(); assertNull("play, a hand-over, the queue of last time", c.queueDj)
        c.flip(effectiveNow = true); c.reset(); assertNull("Clear", c.queueDj)
    }

    @Test fun flippingDropsTheCueAndAsksAgain() {
        val c = holding("/b")
        c.flip(effectiveNow = true)
        assertNull(c.pending); assertTrue("asked again with the switch in the request", c.ask("cur\n/b") > 0)
        val d = holding("/b"); d.newQueue(); assertNull(d.changed(DjCoordinator.Change.AUTO, "/b"))
    }

    @Test fun weekdaysAreCountedLikeDotNet() {
        assertEquals(0, DjCoordinator.weekday(java.time.DayOfWeek.SUNDAY))
        assertEquals(1, DjCoordinator.weekday(java.time.DayOfWeek.MONDAY))
        assertEquals(6, DjCoordinator.weekday(java.time.DayOfWeek.SATURDAY))
    }
}

class DuckRampTest {
    @Test fun downTo30PercentIn400MsAndBackIn600() {
        val r = DuckRamp()
        assertEquals(1f, r.level(0), 0f); assertTrue(r.settled(0)); assertFalse(r.ducking)
        r.duck(1_000)
        assertEquals(1f, r.level(1_000), 0.001f); assertEquals(0.65f, r.level(1_200), 0.001f); assertEquals(0.3f, r.level(1_400), 0.001f)
        assertTrue(r.settled(1_400)); assertFalse(r.settled(1_399)); assertTrue(r.ducking)
        assertEquals("held down while the DJ speaks", 0.3f, r.level(5_000), 0.001f)
        r.release(5_000)
        assertEquals(0.3f, r.level(5_000), 0.001f); assertEquals(0.65f, r.level(5_300), 0.001f); assertEquals(1f, r.level(5_600), 0.001f)
        assertTrue(r.settled(5_600)); assertFalse(r.ducking)
    }

    @Test fun turningBackHalfwayStartsFromWhereItIs() {
        val r = DuckRamp()
        r.duck(0); val mid = r.level(200)
        r.release(200)
        assertEquals("no jump", mid, r.level(200), 0.001f)
        // half the distance back up takes half of the 600 ms
        assertEquals(1f, r.level(500), 0.001f); assertTrue(r.settled(500)); assertTrue(r.level(350) in mid..1f)
    }

    @Test fun releasingWhenNotDuckedChangesNothing() {
        val r = DuckRamp()
        r.release(0); assertEquals(1f, r.level(0), 0f); assertTrue(r.settled(0))
        r.duck(0); r.reset(); assertEquals(1f, r.level(100), 0f); assertTrue(r.settled(100))
    }

    @Test fun neverOutsideTheRange() {
        val r = DuckRamp()
        r.duck(0); r.release(170); r.duck(400); r.release(650)
        for (t in -100L..2_000L step 13L) { val g = r.level(t); assertTrue("$t -> $g", g in DuckRamp.LOW..1f) }
    }
}

class DjLineTest {
    // 13 words: 5 s at 2.6 words a second
    private val text = "Up next it's Adele with Hello, from the album 25, out in 2015"
    private val clip = DjCue(text, text, "/api/dj/clip/a.mp3", backup = true)

    @Test fun aClipMustStartWithinOneAndAHalfSeconds() {
        val l = DjLine.start(clip, 10_000, clipFailed = false)!!
        assertFalse(l.phoneVoice); assertFalse(l.heard); assertEquals(11_500L, l.stopAt)
    }

    @Test fun aClipThatFailsBeforeItIsHeardIsSaidByThePhoneWhileTheSongIsYoung() {
        val l = DjLine.start(clip, 10_000, clipFailed = false)!!
        assertTrue("a 404 half a second in", l.clipFailed(10_500))
        assertTrue(l.phoneVoice); assertEquals("the phone's voice must be sounding 6 s into the song", 16_000L, l.stopAt)
        assertFalse("only once", l.clipFailed(10_600))
    }

    @Test fun noBackupLineAfterSixSecondsOrWithoutBackupVoice() {
        assertFalse(DjLine.start(clip, 0, false)!!.clipFailed(DjLine.BACKUP_WITHIN_MS))
        assertTrue(DjLine.start(clip, 0, false)!!.clipFailed(DjLine.BACKUP_WITHIN_MS - 1))
        assertFalse("Backup voice off", DjLine.start(clip.copy(backup = false), 0, false)!!.clipFailed(100))
        assertFalse("nothing to say", DjLine.start(clip.copy(text = " "), 0, false)!!.clipFailed(100))
        val heard = DjLine.start(clip, 0, false)!!.also { it.heard(800) }
        assertFalse("it had started: a clip failing halfway just stops", heard.clipFailed(1_000))
    }

    @Test fun aClipThatFailedWhileLoadingAheadGoesStraightToThePhonesVoice() {
        val l = DjLine.start(clip, 0, clipFailed = true)!!
        assertTrue(l.phoneVoice); assertEquals(DjLine.BACKUP_WITHIN_MS, l.stopAt)
        val retry = DjLine.start(clip.copy(backup = false), 0, clipFailed = true)!!
        assertFalse("no Backup voice: the clip is tried once more", retry.phoneVoice); assertEquals(DjLine.START_WITHIN_MS, retry.stopAt)
    }

    @Test fun aCueWithoutAClipIsThePhonesVoiceOrNothing() {
        val l = DjLine.start(clip.copy(clip = null), 0, false)!!
        assertTrue(l.phoneVoice); assertEquals(DjLine.START_WITHIN_MS, l.stopAt)
        assertNull(DjLine.start(clip.copy(clip = null, backup = false), 0, false))
        assertNull(DjLine.start(clip.copy(clip = null, text = ""), 0, false))
    }

    @Test fun onceHeardALineThatNeverEndsIsStoppedAnyway() {
        val c = DjLine.start(clip, 0, false)!!
        c.heard(700); assertTrue(c.heard); assertEquals("a clip that stalls: 20 s", 20_700L, c.stopAt)
        val v = DjLine.start(clip.copy(clip = null), 0, false)!!
        v.heard(300); assertEquals("13 words at 2.6 a second, plus 3 s", 300L + 5_000L + 3_000L, v.stopAt)
        assertEquals(DjLine.SPEECH_SLACK_MS, DjLine.speechMs(""))
        assertEquals("3 words", 1_154L + 3_000L, DjLine.speechMs("Next up, Adele"))
        assertEquals("words, not spaces", DjLine.speechMs("Next up, Adele"), DjLine.speechMs("  Next\nup,\t Adele  "))
    }
}

class SeekOriginTest {
    @Test fun theListenersSeekStopsTheLine() { assertTrue(SeekOrigin().seeked()) }

    @Test fun thePlayersOwnSeekDoesNotAndUsesItsMarkUp() {
        val s = SeekOrigin()
        s.self(); assertFalse("a long item's place put back", s.seeked())
        assertTrue("the next one is the listener's again", s.seeked())
    }

    @Test fun aMarkLeftOverNeverSwallowsTheListenersSeek() {
        val s = SeekOrigin()
        s.self(); s.clear(); assertTrue("cleared by the listener's seek, or at the end of the loop turn", s.seeked())
    }
}
