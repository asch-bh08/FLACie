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
        var stops = 0
        val asked = ArrayList<String>()
        var answer: ((DjAskAnswer) -> Unit)? = null
        var talking = false
        val later = ArrayList<() -> Unit>()
        val said = ArrayList<String>()
        var micAllowed = true
        val mic = CarDjMic(
            ears = object : CarDjMic.Ears {
                override fun listen(onHeard: (String?) -> Unit): Boolean { listens++; heard = onHeard; return true }
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

    @Test fun aQuestionAnsweredEndsItAndThePausedSongCarriesOn() {
        val r = Rig()
        r.mic.press(); r.heard!!("what's this song")
        r.answer!!(DjAskAnswer(DjAskDo.NONE, "It's Firestarter."))
        assertFalse(r.mic.conversing); assertTrue(r.playing); assertEquals(1, r.listens)
    }

    @Test fun whenTheDjAsksBackItListensAgainOnceItHasFinishedTalking() {
        val r = Rig()
        r.mic.press(); r.heard!!("play my favs")
        r.talking = true
        r.answer!!(DjAskAnswer(DjAskDo.NONE, "Your hearted songs, or the ones you play most?", asked = true))
        assertTrue(r.mic.conversing); assertEquals(1, r.listens)
        r.runLater(); assertEquals(1, r.listens)          // still talking: waits
        r.talking = false
        r.runLater(); r.runLater()
        assertEquals(2, r.listens); assertEquals(CarDjMic.State.LISTENING, r.mic.state)
    }

    @Test fun nothingSaidOrAGoodbyeEndsItQuietly() {
        val r = Rig()
        r.mic.press(); r.heard!!(null)
        assertFalse(r.mic.conversing); assertTrue(r.playing); assertTrue(r.asked.isEmpty())
        r.mic.press(); r.heard!!("thanks")
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
