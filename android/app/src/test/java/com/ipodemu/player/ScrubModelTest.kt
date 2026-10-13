package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class ScrubModelTest {
    @Test fun ticksOnEveryTenthInBothDirections() {
        assertTrue(ScrubModel.tickCrossed(0.09f, 0.11f)); assertTrue(ScrubModel.tickCrossed(0.31f, 0.29f))
        assertFalse(ScrubModel.tickCrossed(0.11f, 0.19f)); assertFalse(ScrubModel.tickCrossed(0.5f, 0.5f))
        assertTrue("jumping several tenths is still one tick", ScrubModel.tickCrossed(0.05f, 0.95f))
        assertFalse(ScrubModel.tickCrossed(-1f, 0.01f)); assertTrue("reaching the end is a tick", ScrubModel.tickCrossed(0.99f, 5f)); assertFalse(ScrubModel.tickCrossed(1f, 5f))
    }

    @Test fun bubbleAndRemainingText() {
        assertEquals("1:37", ScrubModel.bubbleText(97f / 302f, 302_000)); assertEquals("0:00", ScrubModel.bubbleText(-1f, 302_000)); assertEquals("5:02", ScrubModel.bubbleText(2f, 302_000))
        assertEquals("0:00", ScrubModel.bubbleText(0.5f, 0))
        assertEquals("-2:19", ScrubModel.remainingText(100_000, 239_000)); assertEquals("-0:00", ScrubModel.remainingText(300_000, 239_000))
    }

    @Test fun spokenTimes() {
        assertEquals("1 minute 37 seconds", ScrubModel.spoken(97_000)); assertEquals("5 minutes 2 seconds", ScrubModel.spoken(302_000))
        assertEquals("45 seconds", ScrubModel.spoken(45_000)); assertEquals("0 seconds", ScrubModel.spoken(0)); assertEquals("1 second", ScrubModel.spoken(1_000))
        assertEquals("1 hour 2 minutes", ScrubModel.spoken(3_720_000)); assertEquals("2 hours", ScrubModel.spoken(7_200_000)); assertEquals("1 minute", ScrubModel.spoken(60_000))
    }

    @Test fun stateDescriptionForTheSlider() {
        assertEquals("1 minute 37 seconds of 5 minutes 2 seconds", ScrubModel.stateDescription(97_000, 302_000))
        assertEquals("10 seconds", ScrubModel.stateDescription(10_000, 0))
    }

    @Test fun bubbleStaysInsideTheBar() {
        assertEquals(0f, ScrubModel.bubbleLeft(0f, 12f, 300f, 60f, 324f), 0f)               // would hang off the left
        assertEquals(264f, ScrubModel.bubbleLeft(1f, 12f, 300f, 60f, 324f), 0f)             // would hang off the right
        assertEquals(132f, ScrubModel.bubbleLeft(0.5f, 12f, 300f, 60f, 324f), 0.01f)        // centred under the knob
        assertEquals(0f, ScrubModel.bubbleLeft(0.5f, 0f, 10f, 60f, 50f), 0f)                // a bubble wider than the bar
    }
}
