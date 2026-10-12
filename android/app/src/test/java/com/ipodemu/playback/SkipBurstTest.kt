package com.ipodemu.playback

import com.ipodemu.library.AudioFacts
import com.ipodemu.player.buildAudioInfo
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** Forty fast skips: the player is asked to open at most two streams, and every song the screen passes shows one badge, decided by that song's own facts. */
class SkipBurstTest {
    /** A queue of [n] songs and what a player would do with the decisions of [SkipBurst]: counts the stream opens. */
    private class Sim(val n: Int, start: Int = 0) {
        val burst = SkipBurst(400)
        var playing = start
        var opens = 1                    // the song that is playing was opened before the burst
        val shown = ArrayList<Int>()     // what the screen shows after each press (the target while a burst runs)
        fun press(now: Long, forward: Boolean) {
            val r = burst.press(now, playing) { from -> val to = if (forward) from + 1 else from - 1; if (to in 0 until n) to else -1 }
            when (r) {
                SkipBurst.Press.ACT_NOW -> { playing = (playing + if (forward) 1 else -1).coerceIn(0, n - 1); opens++ }
                else -> {}
            }
            shown += if (burst.target >= 0) burst.target else playing
        }
        fun settle() { val t = burst.settle(); if (t >= 0 && t != playing) { playing = t; opens++ } }
    }

    @Test fun fortyRapidSkipsOpenAtMostTwoStreams() {
        val s = Sim(100)
        var t = 10_000L
        repeat(40) { s.press(t, true); t += 60 }   // 60 ms apart: a held-down or hammered Next
        // the screen moved with every press...
        assertEquals((1..40).toList(), s.shown)
        // ...but the player has not been asked 40 times: one open for the first press
        assertEquals(2, s.opens)
        s.settle()
        assertEquals("the burst's end opens the song it ended on, once", 3, s.opens)
        assertEquals(40, s.playing)
    }

    @Test fun slowSkipsAreEachActedOnAtOnce() {
        val s = Sim(100)
        var t = 10_000L
        repeat(5) { s.press(t, true); t += 900 }
        assertEquals(6, s.opens)
        assertEquals(5, s.playing)
    }

    @Test fun backAndForthInABurstEndsWhereTheLastPressPointed() {
        val s = Sim(100, start = 50)
        var t = 0L
        val dirs = listOf(true, true, true, false, false, true, false, false, false, true)
        for (d in dirs) { s.press(t, d); t += 80 }
        s.settle()
        assertEquals(50 + dirs.sumOf { if (it) 1 else -1 as Int }, s.playing)
        assertTrue(s.opens <= 3)
    }

    @Test fun theEdgesOfTheQueueAreNotLeftOrOverrun() {
        val s = Sim(3, start = 1)
        var t = 0L
        repeat(10) { s.press(t, true); t += 50 }
        s.settle()
        assertEquals(2, s.playing)
        assertTrue(s.shown.all { it in 0..2 })
        assertFalse(s.burst.target >= 0)
    }

    // ---- the badge while the screen runs ahead of the player --------------------------------------------------------

    private fun fact(i: Int) = when (i % 4) {
        0 -> AudioFacts.Fact("flac", 96_000, 24, 3000)   // Hi-Res
        1 -> AudioFacts.Fact("flac", 44_100, 16, 1000)   // CD
        2 -> AudioFacts.Fact("mp3", 44_100, 0, 320)      // lossy
        else -> null                                      // not read yet
    }

    @Test fun everySongOnTheWayShowsExactlyOneBadgeFromItsOwnFacts() {
        val s = Sim(100)
        var t = 0L
        repeat(40) { s.press(t, true); t += 50 }
        // while a burst runs the player still decodes the song it was on: its format must not lend anything to the song the screen shows
        val staleDecoder = com.ipodemu.player.DecoderFormat("audio/flac", 24, 96_000, 2, 3000)
        for (index in s.shown) {
            val own = buildAudioInfo(fact(index), "flac", null, 96_000, 2, 0, 0)
            // asked again and again (the screen redraws) the answer is one value
            assertEquals(1, (1..20).map { buildAudioInfo(fact(index), "flac", null, 96_000, 2, 0, 0) }.toSet().size)
            val expectHiRes = index % 4 == 0
            assertEquals("song $index", expectHiRes, own.hiRes)
            // unknown facts: neutral codec, never Hi-Res, whatever the renderer last reported
            if (index % 4 == 3) { assertEquals("FLAC", own.codec); assertFalse(own.hiRes) }
            // a decoder is only passed for the song that really is current; with one, the facts of the file still decide
            val withDecoder = buildAudioInfo(fact(index), "flac", staleDecoder, 96_000, 2, 0, 0)
            if (fact(index) != null) assertEquals("song $index with a stale decoder", expectHiRes, withDecoder.hiRes)
        }
    }
}
