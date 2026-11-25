package com.ipodemu.playback

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** A spoken request is answered once, at once or not at all: late and superseded answers are dropped (owner, 25 Nov: "it plays the song 2 mins later"). */
class DjAskGateTest {
    private var now = 0L
    private val gate = DjAskGate { now }

    @Test fun theFirstAnswerForTheLatestRequestIsTaken() {
        val t = gate.begin()
        now = 900
        assertTrue(gate.answer(t))
    }

    @Test fun aRequestIsAnsweredOnlyOnce() {
        val t = gate.begin()
        assertTrue(gate.answer(t))
        // the server's own answer after the phone already answered offline at the deadline: dropped
        assertFalse(gate.answer(t))
    }

    @Test fun anAnswerToAnOlderRequestIsDropped() {
        val first = gate.begin()
        val second = gate.begin()
        assertFalse(gate.answer(first))
        assertTrue(gate.answer(second))
    }

    @Test fun anAnswerAfterThePersonMovedOnIsDropped() {
        val t = gate.begin()
        gate.cancel()
        assertFalse(gate.answer(t))
    }

    @Test fun anAnswerLaterThanTheStaleLimitIsDropped() {
        val t = gate.begin()
        now = DjAskGate.STALE_MS + 1
        assertFalse(gate.answer(t))
    }

    @Test fun theDeadlineAnswersARequestTheServerHasNotAnsweredYet() {
        val t = gate.begin()
        now = DjAskGate.ASK_DEADLINE_MS
        assertTrue(gate.timedOut(t))
        assertFalse(gate.answer(t))
    }

    @Test fun theDeadlineDoesNothingOnceAnswered() {
        val t = gate.begin()
        assertTrue(gate.answer(t))
        assertFalse(gate.timedOut(t))
    }

    @Test fun theDeadlineIsShortEnoughForACar() {
        assertTrue(DjAskGate.ASK_DEADLINE_MS <= 7_000L)
        assertTrue(DjAskGate.CONNECT_MS < DjAskGate.ASK_DEADLINE_MS)
    }

    @Test fun aFetchedSongThatLandsSoonPlaysNow() {
        val t = gate.begin(); gate.answer(t)
        now = 30_000
        assertEquals(DjAskGate.Landed.PLAY_NOW, gate.landed(t))
    }

    @Test fun aFetchedSongThatLandsLateGoesUpNextInsteadOfCuttingIn() {
        val t = gate.begin(); gate.answer(t)
        now = DjAskGate.LANDED_NOW_MS + 1
        assertEquals(DjAskGate.Landed.UP_NEXT, gate.landed(t))
    }

    @Test fun aFetchedSongIsNotPlayedOnceSomethingElseWasAsked() {
        val t = gate.begin(); gate.answer(t)
        gate.begin()
        now = 10_000
        assertEquals(DjAskGate.Landed.DROP, gate.landed(t))
    }

    @Test fun aFetchedSongIsNotPlayedOncePlayWasStartedByHand() {
        val t = gate.begin(); gate.answer(t)
        gate.cancel()
        assertEquals(DjAskGate.Landed.DROP, gate.landed(t))
    }

    @Test fun howLongARequestTook() {
        val t = gate.begin()
        now = 1234
        assertEquals(1234L, gate.age(t))
        assertNull(gate.age(t + 5))
    }
}
