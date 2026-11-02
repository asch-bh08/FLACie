package com.ipodemu.player

/**
 * The maths of a level bar (the DJ's volume, the music under the DJ): 0 to 100 in steps of 5, with the knob's travel inset by its radius so the finger and the
 * knob agree at both ends, and only the latest save's answer believed. Pure, unit-tested. (Owner, 2 Nov: "how hard is it to make a working bar".)
 */
object LevelBar {
    /** The level for a finger at [x] on a bar [width] wide whose knob has radius [inset]. */
    fun at(x: Float, width: Float, inset: Float): Int {
        val travel = width - 2 * inset
        if (travel <= 0f) return 0
        return Math.round(((x - inset) / travel).coerceIn(0f, 1f) * 20) * 5
    }

    /** Where the knob's centre is drawn for [level]. */
    fun knobX(level: Int, width: Float, inset: Float): Float = inset + (width - 2 * inset).coerceAtLeast(0f) * (level.coerceIn(0, 100) / 100f)

    /** Numbers the saves, so an answer that comes back after a newer save was sent is not shown. */
    class Saves {
        private var last = 0
        fun next(): Int = ++last
        fun isLatest(n: Int): Boolean = n == last
    }
}
