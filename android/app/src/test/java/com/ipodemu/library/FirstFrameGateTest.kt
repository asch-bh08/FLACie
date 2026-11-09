package com.ipodemu.library

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** Owner, 9 Nov: Home's covers should be there on the first frame "like Spotify", not load in after it. */
class FirstFrameGateTest {
    @Test fun waitsForTheCoversOnDisk() {
        val g = FirstFrameGate(startMs = 0, capMs = 400)
        g.began(); g.began()
        assertFalse(g.ready(50))
        g.ended()
        assertFalse(g.ready(60))
        g.ended()
        assertTrue(g.ready(70))
    }

    @Test fun neverWaitsLongerThanTheCap() {
        val g = FirstFrameGate(startMs = 1_000, capMs = 400)
        g.began()
        assertFalse(g.ready(1_399))
        assertTrue(g.ready(1_400))
    }

    @Test fun nothingToWarmIsReadyAtOnce() = assertTrue(FirstFrameGate(startMs = 0, capMs = 400).ready(0))

    @Test fun decodesOnTheSpotOnlyJustAfterStart() {
        val g = FirstFrameGate(startMs = 0, capMs = 400)
        assertTrue(g.decodeNow(2_999))
        assertFalse(g.decodeNow(3_000))
    }
}
