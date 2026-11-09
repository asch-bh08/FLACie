package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** Owner, 9 Nov: the mic answered, the music came back, and it still said "listening" and took no press. */
class DjMicLimitsTest {
    @Test fun pressTwiceInARowAfterAnAnswer() {
        val m = MicGate()
        assertEquals(MicPress.START, m.press(0)); m.listening(0)
        m.thinking(1_000)
        m.answered(2_000, listenAgain = false)
        assertEquals(MicState.IDLE, m.state)
        assertEquals(MicPress.START, m.press(3_000))
    }

    @Test fun anAnswerThatNeverComesLetsGo() {
        val m = MicGate()
        m.press(0); m.listening(0); m.thinking(1_000)
        assertEquals(MicPress.WAIT, m.press(5_000))
        assertTrue(m.tick(1_000 + DjMicLimits.THINK_MAX_MS, playing = false))
        assertEquals(MicState.IDLE, m.state)
        assertEquals(MicPress.START, m.press(40_000))
    }

    @Test fun listeningEndsAfterTheLimit() {
        val m = MicGate()
        m.press(0); m.listening(0)
        assertFalse(m.tick(DjMicLimits.LISTEN_MAX_MS - 1, playing = false))
        assertTrue(m.tick(DjMicLimits.LISTEN_MAX_MS, playing = false))
        assertEquals(MicState.IDLE, m.state)
        assertEquals(MicPress.START, m.press(DjMicLimits.LISTEN_MAX_MS + 10))
    }

    @Test fun theMusicComingBackEndsAFollowUpListen() {
        val m = MicGate()
        m.press(0); m.listening(0, playing = false); m.thinking(1_000)
        m.answered(2_000, listenAgain = true)
        m.listening(3_000, playing = false)
        assertFalse(m.tick(3_500, playing = false))
        assertTrue(m.tick(4_000, playing = true))
        assertEquals(MicState.IDLE, m.state)
        assertFalse(m.conversing)
        assertEquals(MicPress.START, m.press(5_000))
    }

    @Test fun theFirstTurnIgnoresThePausesOwnLateReport() {
        // the press pauses the song, but "playing" can still read true as the mic starts
        val m = MicGate()
        m.press(0); m.listening(0, playing = true)
        assertFalse(m.tick(300, playing = true))
        assertEquals(MicState.LISTENING, m.state)
    }

    @Test fun aPressWhileListeningStopsAndAPressBetweenTurnsEnds() {
        val m = MicGate()
        m.press(0); m.listening(0)
        assertEquals(MicPress.STOP, m.press(500))
        assertEquals(MicState.IDLE, m.state); assertFalse(m.conversing)
        m.press(1_000); m.listening(1_000); m.thinking(2_000); m.answered(3_000, listenAgain = true)
        assertEquals(MicPress.END, m.press(3_100))
        assertFalse(m.conversing)
        assertEquals(MicPress.START, m.press(3_200))
    }
}
