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
        /** Timers by their delay: the conversation's short steps (250, 300 ms) and the limits (seconds). */
        val later = ArrayList<Pair<Long, () -> Unit>>()
        val said = ArrayList<String>()
        var micAllowed = true
        var finishes = 0
        var cancels = 0
        val held = ArrayList<Boolean>()
        val shown = ArrayList<String?>()
        val mic = CarDjMic(
            ears = object : CarDjMic.Ears {
                override fun listen(waitMs: Long, onHeard: (String?) -> Unit): Boolean { listens++; waits.add(waitMs); heard = onHeard; return true }
                override fun stop() { stops++; heard = null }
                override fun finish() { finishes++ }
            },
            music = object : CarDjMic.Music {
                override val playing: Boolean get() = this@Rig.playing
                override fun pause() { this@Rig.playing = false }
                override fun resume() { this@Rig.playing = true }
            },
            ask = { text, done -> asked.add(text); answer = done },
            djTalking = { talking },
            later = { ms, run -> later.add(ms to run) },
            say = { said.add(it) },
            micAllowed = { micAllowed },
            cancelAsk = { cancels++ },
            hold = { held.add(it) },
            show = { shown.add(it) },
        )
        /** The conversation's next short step (what is pending now with a delay under a second). */
        fun runLater() { val l = later.filter { it.first < 1000 }; later.removeAll(l.toSet()); l.forEach { it.second() } }
        /** A limit of [ms] passes: the timers set for it run. */
        fun pass(ms: Long) { val l = later.filter { it.first == ms }; later.removeAll(l.toSet()); l.forEach { it.second() } }
        val line get() = shown.lastOrNull()
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

    // owner, 25 Nov: "it listens forever, then the song starts playing again, then it hears the song and/or doesn't do anything, and there's no way of
    // knowing if the DJ heard you"

    @Test fun listeningHasAHardLimitThenTakesWhatItHeard() {
        val r = Rig()
        r.mic.press()
        r.pass(MicTurn.LISTEN_MAX_MS)
        assertEquals(1, r.finishes); assertEquals(CarDjMic.State.LISTENING, r.mic.state)
        r.heard!!("play maps")                      // the recogniser hands over what it had
        assertEquals(listOf("play maps"), r.asked)
    }

    @Test fun nothingBackAfterTheLimitEndsTheTurnAndSaysSo() {
        val r = Rig()
        r.mic.press()
        r.pass(MicTurn.LISTEN_MAX_MS); r.pass(MicTurn.RESULT_GRACE_MS)
        assertFalse(r.mic.conversing); assertEquals(CarDjMic.State.IDLE, r.mic.state)
        assertTrue(r.playing)
        assertEquals(listOf(MicTurn.NOT_CAUGHT), r.said)
        assertEquals(MicTurn.NOT_CAUGHT, r.line)
    }

    @Test fun anAnswerThatNeverComesLetsGo() {
        val r = Rig()
        r.mic.press(); r.heard!!("play maps")
        r.pass(MicTurn.THINK_MAX_MS)
        assertFalse(r.mic.conversing); assertTrue(r.playing); assertEquals(1, r.cancels)
        assertEquals(MicTurn.NO_ANSWER, r.line)
        // the answer turning up afterwards changes nothing here
        r.answer!!(DjAskAnswer(DjAskDo.NONE, "It's Firestarter."))
        assertFalse(r.mic.conversing); assertEquals(1, r.listens)
    }

    @Test fun aPressWhileWorkingCancelsTheRequest() {
        val r = Rig()
        r.mic.press(); r.heard!!("play maps")
        r.mic.press()
        assertEquals(1, r.cancels); assertFalse(r.mic.conversing); assertTrue(r.playing)
    }

    @Test fun theMusicIsHeldQuietForTheWholeTurn() {
        val r = Rig()
        r.mic.press()
        assertEquals(listOf(true), r.held)
        r.heard!!("play maps")
        assertEquals(listOf(true), r.held)          // still held while it works: the mic never hears the song
        r.answer!!(DjAskAnswer(DjAskDo.PLAY, "Maps, by Yeah Yeah Yeahs."))
        assertEquals(listOf(true, false), r.held)
    }

    @Test fun theCarShowsEachStage() {
        val r = Rig()
        r.mic.press()
        assertEquals(MicTurn.LISTENING, r.line)
        r.heard!!("play maps")
        assertEquals("Heard: “play maps” · Working…", r.line)
        r.answer!!(DjAskAnswer(DjAskDo.PLAY, "Maps, by Yeah Yeah Yeahs."))
        assertEquals("Maps, by Yeah Yeah Yeahs.", r.line)
        r.pass(MicTurn.RESULT_SHOW_MS)
        assertEquals(null, r.line)
    }

    @Test fun nothingHeardOnTheFirstTurnIsShown() {
        val r = Rig()
        r.mic.press(); r.heard!!(null)
        assertEquals(MicTurn.NOTHING_HEARD, r.line)
    }
}
