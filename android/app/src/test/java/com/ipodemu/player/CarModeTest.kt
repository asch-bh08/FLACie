package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class CarModeTest {
    // owner, 12 Nov: "car mode should have all the graphs, just scrollable": every live graph of the Info card, in its order, not the bit rate
    @Test fun carModeSwipesThroughEveryLiveGraph() {
        val graphs = carGraphs().map { it.first }
        assertEquals(infoGraphs(320, 2).map { it.first }.filter { it != "rate" }, graphs)
        assertTrue(graphs.size >= 11)
    }

    @Test fun theControlsAreBiggerInTheCar() = assertTrue(CarScale > 1.2f)
}
