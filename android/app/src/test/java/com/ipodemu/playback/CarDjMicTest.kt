package com.ipodemu.playback

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** "Talk to the DJ" from the car's screen: the same turns as the phone's mic button (DjConversation), with a pretend mic, music and DJ. */
class CarDjMicTest {
    private class Rig {
        var playing = true
        var heard: ((String?) -> Unit)? = null
        var listens = 0
        val waits = ArrayList<Long>()
        var stops = 0
        val asked = ArrayList<String>()
        var answer: ((DjAskAnswer) -> Unit)? = null
        var talking = false
        val later = ArrayList<() -> Unit>()
        val said = ArrayList<String>()
        var micAllowed = true
        val mic = CarDjMic(
            ears = object : CarDjMic.Ears {
                override fun listen(waitMs: Long, onHeard: (String?) -> Unit): Boolean { listens++; waits.add(waitMs); heard = onHeard; return true }
                override fun stop() { stops++; heard = null }
            },
            music = object : CarDjMic.Music {
                override val playing: Boolean get() = this@Rig.playing
                override fun pause() { this@Rig.playing = false }
                override fun resume() { this@Rig.playing = true }
            },
            ask = { text, done -> asked.add(text); answer = done },
            djTalking = { talking },
            later = { _, run -> later.add(run) },
            say = { said.add(it) },
            micAllowed = { micAllowed },
        )
        fun runLater() { val l = later.toList(); later.clear(); l.forEach { it() } }
    }

    @Test fun aPressPausesTheMusicAndListens() {
        val r = Rig()
        r.mic.press()
        assertFalse(r.playing); assertEquals(1, r.listens); assertEquals(CarDjMic.State.LISTENING, r.mic.state)
    }

    @Test fun aRequestThatPlaysSomethingEndsItAndLeavesTheMusicAsChosen() {
        val r = Rig()
        r.mic.press(); r.heard!!("play some Prodigy")
        assertEquals(listOf("play some Prodigy"), r.asked); assertEquals(CarDjMic.State.THINKING, r.mic.state)
        r.answer!!(DjAskAnswer(DjAskDo.PLAY, "Here's The Prodigy."))
        assertEquals(CarDjMic.State.IDLE, r.mic.state); assertFalse(r.mic.conversing)
        assertFalse(r.playing)   // (the play itself starts the new music; the paused song is not resumed over it)
    }

    @Test fun aPlainControlEndsItAtOnceAndTheSongCarriesOn() {
        val r = Rig()
        r.mic.press(); r.heard!!("turn it up")
        r.answer!!(DjAskAnswer(DjAskDo.VOLUMEUP, ""))
        assertFalse(r.mic.conversing); assertTrue(r.playing); assertEquals(1, r.listens)
    }

    @Test fun aNormalAnswerListensAgainForAShortWhileThenEndsQuietlyIfNobodySpeaks() {
        val r = Rig()
        r.mic.press(); r.heard!!("what's this song")
        r.answer!!(DjAskAnswer(DjAskDo.NONE, "It's Firestarter."))
        assertTrue(r.mic.conversing); assertFalse(r.playing)       // the music stays paused while it waits for a follow-up
        r.runLater(); r.runLater()
        assertEquals(2, r.listens); assertEquals(listOf(0L, DjConversation.FOLLOW_UP_MS), r.waits)
        r.heard!!(null)                                             // nobody spoke: it ends quietly and the song carries on
        assertFalse(r.mic.conversing); assertTrue(r.playing); assertEquals(1, r.asked.size)
    }

    @Test fun aFollowUpIsAnsweredLikeTheFirstRequest() {
        val r = Rig()
        r.mic.press(); r.heard!!("what's this song")
        r.answer!!(DjAskAnswer(DjAskDo.NONE, "It's Firestarter."))
        r.runLater(); r.runLater(); r.heard!!("who sings it")
        assertEquals(listOf("what's this song", "who sings it"), r.asked)
        r.answer!!(DjAskAnswer(DjAskDo.NONE, "The Prodigy.", intent = "WhoSings"))
        assertTrue(r.mic.conversing)
    }

    @Test fun theCapStopsListeningAgain() {
        val r = Rig()
        r.mic.press()
        for (i in 1..DjConversation.MAX_TURNS) {
            r.heard!!("what's this song"); r.answer!!(DjAskAnswer(DjAskDo.NONE, "It's Firestarter."))
            if (i < DjConversation.MAX_TURNS) { r.runLater(); r.runLater() }
        }
        assertFalse(r.mic.conversing); assertTrue(r.playing)
    }

    @Test fun aGoodbyeIsAnsweredAnytimeAndEndsItWithTheMusicBack() {
        val r = Rig()
        r.mic.press(); r.heard!!("thanks")
        assertEquals(listOf("thanks"), r.asked)                      // the server answers it with "Anytime." (made ahead in the voice)
        r.answer!!(DjAskAnswer(DjAskDo.NONE, "Anytime.", intent = "Goodbye"))
        assertFalse(r.mic.conversing); assertTrue(r.playing)
    }

    @Test fun whenTheDjAsksBackItListensAgainOnceItHasFinishedTalking() {
        val r = Rig()
        r.mic.press(); r.heard!!("play my favs")
        r.talking = true
        r.answer!!(DjAskAnswer(DjAskDo.NONE, "Your hearted songs, or the ones you play most?", asked = true, intent = "Clarify"))
        assertTrue(r.mic.conversing); assertEquals(1, r.listens)
        r.runLater(); assertEquals(1, r.listens)          // still talking: waits
        r.talking = false
        r.runLater(); r.runLater()
        assertEquals(2, r.listens); assertEquals(CarDjMic.State.LISTENING, r.mic.state)
        assertEquals(DjConversation.ANSWER_WINDOW_MS, r.waits[1])   // a question back is given longer to be answered
    }

    @Test fun nothingSaidEndsItQuietly() {
        val r = Rig()
        r.mic.press(); r.heard!!(null)
        assertFalse(r.mic.conversing); assertTrue(r.playing); assertTrue(r.asked.isEmpty())
    }

    @Test fun aPressWhileListeningStopsItAndTheSongCarriesOn() {
        val r = Rig()
        r.mic.press(); r.mic.press()
        assertEquals(1, r.stops); assertFalse(r.mic.conversing); assertTrue(r.playing)
    }

    @Test fun withoutTheMicrophoneAllowedItSaysHowAndTouchesNothing() {
        val r = Rig().apply { micAllowed = false }
        r.mic.press()
        assertEquals(0, r.listens); assertTrue(r.playing); assertEquals(1, r.said.size)
    }
}
