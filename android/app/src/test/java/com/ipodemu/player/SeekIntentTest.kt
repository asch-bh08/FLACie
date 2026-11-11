package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Test

class SeekIntentTest {
    private val slop = 18f

    @Test fun aQuickTapSeeks() = assertEquals(SeekIntent.Kind.TAP, SeekIntent.decide(90, 2f, slop, released = true))

    @Test fun aFingerThatMovesAwayQuicklyIsASwipeNotASeek() {
        assertEquals(SeekIntent.Kind.SWIPE, SeekIntent.decide(60, 40f, slop, released = false))
        assertEquals(SeekIntent.Kind.SWIPE, SeekIntent.decide(120, 200f, slop, released = true))
    }

    @Test fun waitingBeforeTheHoldDecidesNothing() = assertEquals(SeekIntent.Kind.WAIT, SeekIntent.decide(80, 4f, slop, released = false))

    @Test fun aRestingFingerThenScrubs() {
        assertEquals(SeekIntent.Kind.SCRUB, SeekIntent.decide(SeekIntent.HOLD_MS, 3f, slop, released = false))
        assertEquals(SeekIntent.Kind.SCRUB, SeekIntent.decide(400, 120f, slop, released = false))   // after the hold, moving is scrubbing
    }
}
