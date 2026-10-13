package com.ipodemu.theme

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class ContrastClampTest {
    @Test fun knownRatios() {
        assertEquals(21.0, ContrastClamp.ratio(0xFF000000.toInt(), 0xFFFFFFFF.toInt()), 0.01)
        assertEquals(1.0, ContrastClamp.ratio(0xFF808080.toInt(), 0xFF808080.toInt()), 0.001)
        assertEquals(4.54, ContrastClamp.ratio(0xFF767676.toInt(), 0xFFFFFFFF.toInt()), 0.02)   // the classic "#767676 is the lightest grey that passes AA"
    }

    @Test fun aReadableColourIsLeftAlone() {
        assertEquals(0xFF222222.toInt(), ContrastClamp.ensure(0xFF222222.toInt(), 0xFFEEEEEE.toInt()))
    }

    @Test fun aFaintLabelIsDarkenedOnALightRingAndLightenedOnADarkOne() {
        val lightRing = 0xFFE8E8EA.toInt(); val faint = 0xFFB0B0B4.toInt()
        val fixed = ContrastClamp.ensure(faint, lightRing)
        assertTrue(ContrastClamp.ratio(fixed, lightRing) >= 4.5); assertTrue(ContrastClamp.luminance(fixed) < ContrastClamp.luminance(faint))
        val darkRing = 0xFF202024.toInt(); val dim = 0xFF55555A.toInt()
        val fixed2 = ContrastClamp.ensure(dim, darkRing)
        assertTrue(ContrastClamp.ratio(fixed2, darkRing) >= 4.5); assertTrue(ContrastClamp.luminance(fixed2) > ContrastClamp.luminance(dim))
    }

    @Test fun alphaIsKept() {
        assertEquals(0x80, (ContrastClamp.ensure(0x80B0B0B4.toInt(), 0xFFFFFFFF.toInt()) ushr 24))
    }
}
