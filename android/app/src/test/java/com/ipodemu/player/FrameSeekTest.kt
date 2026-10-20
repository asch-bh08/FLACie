package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** The frame seek bar round the cover: its geometry (the same as the web's frameGeo) and where a touch lands in the song. */
class FrameSeekTest {
    private val f = FrameSeek(side = 300f, corner = 20f, gap = 8f)

    private fun assertPoint(t: Float, x: Float, y: Float, nx: Float, ny: Float) {
        val p = f.walk(t)
        assertEquals("x at $t", x, p[0], .01f); assertEquals("y at $t", y, p[1], .01f)
        assertEquals("nx at $t", nx, p[2], .01f); assertEquals("ny at $t", ny, p[3], .01f)
    }

    @Test fun theWalkStartsAtTwelveOClockPointingUp() = assertPoint(0f, 0f, -158f, 0f, -1f)
    @Test fun aQuarterOfTheWayIsThreeOClock() = assertPoint(.25f, 158f, 0f, 1f, 0f)
    @Test fun halfWayIsSixOClock() = assertPoint(.5f, 0f, 158f, 0f, 1f)
    @Test fun threeQuartersIsNineOClock() = assertPoint(.75f, -158f, 0f, -1f, 0f)
    @Test fun theWalkWrapsRound() = assertPoint(1.25f, 158f, 0f, 1f, 0f)

    @Test fun thePerimeterIsFourSidesAndFourQuarterCircles() {
        val rr = 28f; val straight = 2 * 158f - 2 * rr
        assertEquals(4 * straight + 4 * (Math.PI / 2 * rr).toFloat(), f.perimeter, .01f)
    }

    @Test fun theCornersAreRoundAndTheirNormalsPointOutDiagonally() {
        // the middle of the top-right corner arc
        val tMid = (f.perimeter / 8f) / f.perimeter
        val p = f.walk(tMid)
        assertEquals(.7071f, p[2], .02f); assertEquals(-.7071f, p[3], .02f)
    }

    @Test fun aTouchOnThePictureIsNeverASeek() {
        assertNull(f.fraction(0f, 0f))
        assertNull(f.fraction(140f, -140f))
    }

    @Test fun aTouchOnTheFrameSeeksToWhereItIs() {
        assertEquals(0f, f.fraction(0f, -170f)!!, .005f)
        assertEquals(.25f, f.fraction(170f, 0f)!!, .005f)
        assertEquals(.5f, f.fraction(0f, 165f)!!, .005f)
        assertEquals(.75f, f.fraction(-165f, 0f)!!, .005f)
    }

    @Test fun aDragKeepsSeekingEvenOverThePicture() {
        assertNotNull(f.nearest(10f, 0f))
        assertEquals(.25f, f.nearest(150f, 0f), .005f)
    }

    @Test fun theServersShapeIsReadFromJson() {
        val a = WaveCache.parseJson((0 until 200).joinToString(",", "[", "]") { (it % 256).toString() })!!
        assertEquals(200, a.size); assertEquals(199, a[199])
        assertNull(WaveCache.parseJson("[1,2,3]"))
        assertNull(WaveCache.parseJson("{\"error\":1}"))
        assertNull(WaveCache.parseJson((0 until 200).joinToString(",", "[", "]") { "300" }))
    }

    @Test fun theBarCountFollowsTheWebRule() {
        assertEquals(Math.round(f.perimeter / (3f * 2.3f)), f.bars(3f))
        assertTrue(FrameSeek(40f, 4f, 2f).bars(3f) >= 60)
    }
}
