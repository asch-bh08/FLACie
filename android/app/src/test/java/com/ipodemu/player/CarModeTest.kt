package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class CarModeTest {
    // owner, 12 Nov: every graph in car mode, the Visualizer first; a tap or a swipe goes through them and round
    @Test fun carModeHasEveryLiveGraphTheVisualizerFirst() {
        assertEquals("leds", CarVisuals.first().first)
        assertEquals(infoGraphs(320, 2).map { it.first }.filter { it != "rate" }.toSet(), CarVisuals.map { it.first }.toSet())
        assertTrue(CarVisuals.size >= 11)
    }

    @Test fun nextAndPreviousGoRound() {
        assertEquals(CarVisuals[1].first, nextCarVisual(CarVisuals[0].first))
        assertEquals(CarVisuals.first().first, nextCarVisual(CarVisuals.last().first))
        assertEquals(CarVisuals.last().first, prevCarVisual(CarVisuals.first().first))
        assertEquals("leds", nextCarVisual("unknown"))
    }
}
