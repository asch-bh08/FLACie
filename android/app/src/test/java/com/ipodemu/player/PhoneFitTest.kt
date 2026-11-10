package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** The phone's screens fit 320x568 and 360x640, font scale 1.5 and a phone on its side (PhoneFit). */
class PhoneFitTest {
    private val scales = listOf(1f, 1.3f, 1.5f)

    // ---- classic Now Playing ----

    @Test fun classicUprightKeepsACoverOnSmallPhonesAtEveryScale() {
        // the room inside the bars: 24 dp status bar, 48 dp three-button navigation
        for ((w, h) in listOf(320f to 568f - 72f, 360f to 640f - 72f)) for (fs in scales) for (b in listOf(4, 6)) {
            val p = ClassicFit.plan(w, h, fs, b)
            val at = "${w}x$h at $fs ($b buttons): $p"
            assertFalse(at, p.twoCol)
            assertTrue(at, p.art >= ClassicFit.MIN_ART)
            assertFalse(at, p.scroll)
        }
    }

    @Test fun classicUprightOnARoomyPhoneDropsNothing() {
        val p = ClassicFit.plan(412f, 915f - 72f, 1f)
        assertTrue(p.album); assertTrue(p.tag); assertEquals(24f, p.titleSp); assertFalse(p.tight)
    }

    @Test fun classicUprightAtBigTextStepsAsideInOrder() {
        val p = ClassicFit.plan(320f, 496f, 1.5f)
        // the album line goes before the download line, which goes before the title shrinks
        assertFalse(p.album)
        if (p.titleSp < 24f) assertFalse(p.tag)
    }

    @Test fun classicOnItsSideFitsWithoutScrollingAtBigText() {
        for ((w, h) in listOf(568f to 296f, 640f to 336f, 732f to 388f, 800f to 336f)) for (fs in scales) for (b in listOf(4, 6)) {
            val p = ClassicFit.plan(w, h, fs, b)
            val at = "${w}x$h at $fs ($b buttons): $p"
            assertTrue(at, p.twoCol)
            assertFalse(at, p.scroll)
            assertTrue(at, p.art >= 180f)
        }
    }

    @Test fun classicOnItsSideMovesTheHeaderOverTheCoverOnlyWhenNeeded() {
        assertFalse(ClassicFit.plan(800f, 410f, 1f).headerOverArt)
        assertTrue(ClassicFit.plan(568f, 296f, 1.5f).headerOverArt)
    }

    @Test fun classicExtrasFitTheColumn() {
        // six round buttons in a 251 dp column (a 568 dp phone on its side)
        val s = ClassicFit.extrasSize(251f, 6)
        assertTrue(6 * s + 7 * 6f <= 251f + .5f || s == 36f)
        assertEquals(44f, ClassicFit.extrasSize(320f, 4))
        assertTrue(ClassicFit.extrasSize(200f, 6) >= 36f)
    }
}
