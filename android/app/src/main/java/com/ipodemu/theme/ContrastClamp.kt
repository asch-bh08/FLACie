package com.ipodemu.theme

import kotlin.math.pow

/** WCAG contrast arithmetic on ARGB ints (pure, unit-tested): used to keep drawn labels readable on whatever colour they sit on. */
object ContrastClamp {
    private fun lin(c: Int): Double { val v = c / 255.0; return if (v <= 0.03928) v / 12.92 else ((v + 0.055) / 1.055).pow(2.4) }

    fun luminance(argb: Int): Double = 0.2126 * lin((argb shr 16) and 0xFF) + 0.7152 * lin((argb shr 8) and 0xFF) + 0.0722 * lin(argb and 0xFF)

    /** The WCAG contrast ratio, 1 (none) to 21 (black on white). */
    fun ratio(a: Int, b: Int): Double {
        val la = luminance(a); val lb = luminance(b)
        return (maxOf(la, lb) + 0.05) / (minOf(la, lb) + 0.05)
    }

    private fun mix(from: Int, to: Int, t: Double): Int {
        fun ch(shift: Int) = (((from shr shift) and 0xFF) * (1 - t) + ((to shr shift) and 0xFF) * t).toInt().coerceIn(0, 255)
        return (from and 0xFF000000.toInt()) or (ch(16) shl 16) or (ch(8) shl 8) or ch(0)
    }

    /** [fg] as it is when it already reads on [bg] at [min]:1, else moved towards black or white (whichever the background is further from) until it does. */
    fun ensure(fg: Int, bg: Int, min: Double = 4.5): Int {
        if (ratio(fg, bg) >= min) return fg
        val target = if (luminance(bg) > 0.4) 0xFF000000.toInt() else 0xFFFFFFFF.toInt()
        var t = 0.0
        while (t < 1.0) { t += 0.05; val c = mix(fg, target, t); if (ratio(c, bg) >= min) return c }
        return target
    }
}
