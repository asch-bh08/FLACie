package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Test

/** Text on a cover colour: near-black on a light colour, white on a dark one, so it always reads (WCAG AA), as the web's accentFg. */
class ReadableOnTest {
    @Test fun dark_text_on_a_light_colour() = assertEquals(0xFF1A120C.toInt(), DesignColour.readableOn(0xFFE8774F.toInt()))
    @Test fun white_text_on_a_dark_colour() = assertEquals(0xFFFFFFFF.toInt(), DesignColour.readableOn(0xFF2F3A9A.toInt()))
    @Test fun white_on_a_mid_blue() = assertEquals(0xFFFFFFFF.toInt(), DesignColour.readableOn(0xFF5F5FF2.toInt()))
}
