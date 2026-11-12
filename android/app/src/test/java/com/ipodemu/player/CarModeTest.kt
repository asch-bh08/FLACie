package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Test

class CarModeTest {
    @Test fun tapGoesToTheNextVisualAndRoundToTheFirst() {
        assertEquals("ring", nextCarVisual("leds"))
        assertEquals("leds", nextCarVisual(CarVisuals.last().first))
    }

    @Test fun anUnknownVisualStartsAtTheFirst() = assertEquals("leds", nextCarVisual("wall"))
}
