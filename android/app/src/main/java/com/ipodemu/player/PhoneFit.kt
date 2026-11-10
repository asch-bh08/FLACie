package com.ipodemu.player

import kotlin.math.max
import kotlin.math.min

/**
 * How the phone's other screens fit small phones (320x568 and 360x640 dp), big text (font scale 1.3 and 1.5) and a phone on its side, as
 * [NowPlayingFit] does for the Modern player. Each object here is the decision one screen makes; the Compose code only reads it. All sizes are dp,
 * text is measured as its size in sp times 1.35 times the font scale (Android 14 scales big text less, so this errs safe). Pure Kotlin, unit tested
 * in PhoneFitTest.
 */
internal fun fitLine(sp: Float, fontScale: Float, k: Float = 1.35f) = sp * k * fontScale.coerceAtLeast(.85f)

/**
 * The classic iPod-skin Now Playing (NowPlayingUi.kt). Upright: the cover takes the room the header, the words, the seek bar, the transport and the
 * extras row leave; when that is under [MIN_ART], in turn the album line goes, then the download line, then the title is smaller, then the gaps
 * close. On its side (two columns): the words, seek, transport and extras must fit the right column's height; when they do not, the header moves
 * over the cover (left column), then the same steps; only a split-screen sliver scrolls the right column.
 */
object ClassicFit {
    data class Plan(
        val twoCol: Boolean,
        val titleSp: Float,
        val album: Boolean,
        val tag: Boolean,
        /** the small gaps between the rows close */
        val tight: Boolean,
        /** two columns: the header (Close, Now Playing, Up next) sits over the cover instead of at the top of the controls */
        val headerOverArt: Boolean,
        /** the transport row's scale (1 = 74 dp play button) */
        val transportK: Float,
        /** the extras row's buttons (favourite, EQ, sleep, DJ, mic, more) */
        val extras: Float,
        /** nothing else helped: the controls column scrolls */
        val scroll: Boolean,
        /** the cover's side */
        val art: Float,
    )

    const val MIN_ART = 140f
    const val HEADER = 44f

    private data class Step(val titleSp: Float, val album: Boolean, val tag: Boolean, val tight: Boolean)

    private val steps = listOf(
        Step(24f, true, true, false),
        Step(24f, false, true, false),
        Step(24f, false, false, false),
        Step(20f, false, false, false),
        Step(20f, false, false, true),
    )

    fun transportK(colW: Float): Float = (colW / 320f).coerceIn(.62f, 1f)

    /** The extras row's button size: 44 dp, smaller (not under 36) when [buttons] of them would not fit the column side by side. */
    fun extrasSize(colW: Float, buttons: Int): Float = ((colW - 6f * (buttons + 1)) / buttons.coerceAtLeast(1)).coerceIn(36f, 44f)

    private fun info(fs: Float, s: Step) =
        fitLine(s.titleSp, fs) + 2f + fitLine(17f, fs) + (if (s.tag) 2f + fitLine(12.5f, fs) else 0f) + (if (s.album) fitLine(14f, fs) else 0f)

    private fun seek(fs: Float) = 44f + fitLine(13f, fs)

    /** [width] x [height]: the room inside the system bars; [buttons]: how many extras show (4, or 6 with the DJ and its mic). */
    fun plan(width: Float, height: Float, fontScale: Float = 1f, buttons: Int = 4): Plan {
        val fs = fontScale.coerceAtLeast(.85f)
        val twoCol = width > height * .8f
        if (twoCol) {
            val colW = (width - 28f - 16f) * .48f
            val k = transportK(colW)
            val extras = extrasSize(colW, buttons)
            var last: Plan? = null
            for (over in listOf(false, true)) for (s in steps) {
                val col = 16f + (if (over) 0f else HEADER) + info(fs, s) + seek(fs) + 74f * k + extras + (if (s.tight) 0f else 8f)
                val artRoom = height - 16f - 8f - (if (over) HEADER else 0f)
                val art = min((width - 44f) * .52f, artRoom).coerceAtLeast(0f)
                val p = Plan(true, s.titleSp, s.album, s.tag, s.tight, over, k, extras, false, art)
                last = p
                if (col <= height + .5f) return p
            }
            return last!!.copy(scroll = true)
        }
        val colW = width - 44f
        val k = transportK(colW)
        val extras = extrasSize(colW, buttons)
        var last: Plan? = null
        for (s in steps) {
            val gaps = if (s.tight) 2f + 4f + 2f else 6f + 10f + 6f
            val fixed = 20f + HEADER + 20f + info(fs, s) + seek(fs) + 74f * k + extras + gaps
            val art = min(min(height - fixed, colW), 420f).coerceAtLeast(0f)
            val p = Plan(false, s.titleSp, s.album, s.tag, s.tight, false, k, extras, false, art)
            last = p
            if (art >= MIN_ART) return p
        }
        return last!!
    }
}
