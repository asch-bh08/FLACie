package com.ipodemu.playback

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** The DJ's captions, cue sound, voice equaliser and the new switches (Captions, Cue sound, the lower "Music under the DJ" default). */
class DjCaptionsTest {
    @Test fun `a short line is one caption for the clip's length and a little after`() {
        val p = DjCaptions.plan("That was Blur. Here's Pulp.", 3000)
        assertEquals(1, p.size)
        assertEquals(0, p[0].atMs)
        assertEquals(3000 + DjCaptions.LINGER_MS, p[0].untilMs)
    }

    @Test fun `a long line is two or three phrases that cover the clip end to end`() {
        val text = "That was Parklife by Blur, from nineteen ninety four, a proper Britpop classic. Coming up next, something a little calmer from Pulp, to ease you into the evening."
        val p = DjCaptions.plan(text, 9000)
        assertTrue(p.size in 2..3)
        for (i in 1 until p.size) assertEquals(p[i - 1].untilMs, p[i].atMs)
        assertEquals(9000 + DjCaptions.LINGER_MS, p.last().untilMs)
        assertEquals(text, p.joinToString(" ") { it.text })
    }

    @Test fun `the cue sound is under 300 ms and never clips`() {
        val pcm = DjCaptions.swoosh(44_100)
        assertTrue(pcm.size * 1000 / 44_100 < 300)
        assertTrue(pcm.any { it.toInt() != 0 })
        assertTrue(pcm.all { it.toInt() in -32767..32767 })
    }

    @Test fun `the voice equaliser cuts the rumble and lifts the band nearest 3 kHz`() {
        val centres = intArrayOf(60_000, 230_000, 910_000, 3_600_000, 14_000_000)
        val l = DjCaptions.voiceEq(centres, (-1500).toShort(), 1500.toShort())
        assertEquals(-600, l[0].toInt()); assertEquals(0, l[1].toInt()); assertEquals(300, l[3].toInt()); assertEquals(0, l[4].toInt())
        // a device with a narrow range gets what it allows
        assertEquals(-200, DjCaptions.voiceEq(centres, (-200).toShort(), 200.toShort())[0].toInt())
    }

    @Test fun `captions and the cue default on, the music sits lower, and both switches round-trip`() {
        val d = DjSettings()
        assertTrue(d.captions); assertTrue(d.cue); assertEquals(20, d.duck)
        val off = DjSettings.parse(d.copy(captions = false, cue = false).toJson().toString())
        assertFalse(off.captions); assertFalse(off.cue)
        // an older server that sends neither: both on
        val old = DjSettings.parse("""{"enabled":true,"duck":30}""")
        assertTrue(old.captions); assertTrue(old.cue); assertEquals(30, old.duck)
    }

    @Test fun `a phrase shows for the clip position it belongs to, not for when the text arrived`() {
        val line = "Coming up, a song that is older than most of us. It is Blue Monday by New Order, and it still sounds like tomorrow."
        val plan = DjCaptions.plan(line, 8_000)
        assertTrue(plan.size >= 2)
        // text that arrived long before the audio plays shows nothing until the clip is at the phrase
        assertEquals(plan[0].text, DjCaptions.at(plan, 0)?.text)
        assertEquals(plan[1].text, DjCaptions.at(plan, plan[1].atMs.toLong())?.text)
        assertEquals(plan[0].text, DjCaptions.at(plan, plan[1].atMs - 1L)?.text)
        // a seek or a stall moves the caption with the audio, and after the end (and the linger) it is gone
        assertEquals(plan.last().text, DjCaptions.at(plan, 7_999)?.text)
        assertNull(DjCaptions.at(plan, plan.last().untilMs.toLong()))
        assertNull(DjCaptions.at(emptyList(), 100))
    }

    @Test fun `the clip is looked at often enough for a phrase to be within 150 ms of its audio`() {
        assertTrue(DjCaptions.POLL_MS <= 100)
    }
}
