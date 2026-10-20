package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/** The seek frame closes as a loop: the bars either side of 12 o'clock meet at one height, and bars away from the seam keep the song's own shape. */
class SeamBlendTest {
    @Test
    fun the_first_and_last_bars_meet_at_one_height() {
        val h = FloatArray(80) { i -> if (i < 40) 0.15f else 1f }   // quiet start, loud end
        val out = WaveBars.blendSeam(h)
        assertEquals(out[0], out[79], 1e-4f)
        assertEquals((0.15f + 1f) / 2f, out[0], 1e-4f)
    }

    @Test
    fun the_blend_eases_and_leaves_the_middle_alone() {
        val h = FloatArray(80) { i -> if (i < 40) 0.15f else 1f }
        val out = WaveBars.blendSeam(h)
        // easing: each step away from the seam on the quiet side is no higher than the one before
        for (j in 1 until 6) assertTrue("bar $j", out[j] <= out[j - 1] + 1e-6f)
        // away from the seam nothing changes
        for (i in 10 until 70) assertEquals(h[i], out[i], 0f)
    }

    @Test
    fun a_level_frame_stays_level() {
        val out = WaveBars.blendSeam(FloatArray(80) { 0.4f })
        for (v in out) assertEquals(0.4f, v, 1e-6f)
    }
}
