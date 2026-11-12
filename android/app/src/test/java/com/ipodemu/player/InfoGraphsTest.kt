package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Test

class InfoGraphsTest {
    // the web's order (TrackInfoPanel.razor Picks), with Orbit and Skyline (owner, 12 Nov: "many more graph options")
    @Test fun theGraphsInTheWebsOrder() = assertEquals(
        listOf("curve", "leds", "wall", "loud", "ring", "scope", "orbit", "skyline", "bars", "aura", "stereo", "rate"),
        infoGraphs(320, 2).map { it.first },
    )

    @Test fun noBitRateWhenTheFileDoesNotSay() = assertEquals("stereo", infoGraphs(0, 2).last().first)
}
