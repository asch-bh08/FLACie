package com.ipodemu.playback

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * Owner, 25 Nov: "the waveform graph is very laggy" on the phone. The player hands the decoded sound to the graphs' tap in bursts (a FLAC block of 4,096
 * frames is 93 ms at 44.1 kHz), and the graphs read the newest 2,048 frames: the picture changed about 11 times a second, in jumps. The read point now
 * moves on at the song's own rate between bursts, a little behind the newest frame.
 */
class SmoothHeadTest {
    private val rate = 44_100
    private val ms = 1_000_000L

    /** Writes of [chunk] frames as the player makes them (one every chunk's worth of time), reads every [frameMs], returns the read points. */
    private fun run(chunk: Int, frameMs: Long, seconds: Int): List<Long> {
        val h = SmoothHead()
        val chunkNs = chunk * 1_000_000_000L / rate
        var written = 0L; var nextWrite = 0L; val reads = ArrayList<Long>()
        var t = 0L
        while (t < seconds * 1_000L * ms) {
            while (nextWrite <= t) { written += chunk; h.wrote(written, nextWrite); nextWrite += chunkNs }
            reads += h.readEnd(t, rate)
            t += frameMs * ms
        }
        return reads
    }

    @Test fun theReadPointMovesOnEveryFrameNotOnlyWhenABurstArrives() {
        val reads = run(chunk = 4096, frameMs = 16, seconds = 3).drop(60)   // (after the first second: settled)
        val steps = reads.zipWithNext { a, b -> b - a }
        // a frame of 16 ms is about 706 frames of sound: every step is near that, none stands still and none jumps a whole burst
        assertTrue("stood still ${steps.count { it <= 0 }} times", steps.all { it > 0 })
        assertTrue("jumped: ${steps.max()}", steps.all { it in 400L..1_100L })
    }

    @Test fun itNeverReadsPastWhatHasArrivedAndStaysCloseBehindIt() {
        val h = SmoothHead()
        val chunkNs = 4096 * 1_000_000_000L / rate
        var written = 0L; var nextWrite = 0L; var t = 0L
        while (t < 3_000 * ms) {
            while (nextWrite <= t) { written += 4096; h.wrote(written, nextWrite); nextWrite += chunkNs }
            val r = h.readEnd(t, rate)
            assertTrue("read $r past written $written", r <= written)
            if (t > 1_000 * ms) assertTrue("${written - r} frames behind", written - r <= 3 * 4096)
            t += 16 * ms
        }
    }

    @Test fun aSeekOrANewSongStartsFromTheNewSoundAtOnce() {
        val h = SmoothHead()
        h.wrote(4096, 0); h.readEnd(10 * ms, rate)
        h.reset()
        h.wrote(4096, 20 * ms)
        assertTrue(h.readEnd(21 * ms, rate) <= 4096)
        // a long gap (paused, buffering): the read point waits for the sound instead of running ahead of it
        h.wrote(8192, 30 * ms)
        val r = h.readEnd(5_000 * ms, rate)
        assertTrue("$r", r <= 8192)
    }

    @Test fun nothingWrittenNothingToRead() = assertEquals(0L, SmoothHead().readEnd(1_000 * ms, rate))
}
