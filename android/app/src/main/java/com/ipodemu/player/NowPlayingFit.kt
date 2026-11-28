package com.ipodemu.player

import kotlin.math.ceil
import kotlin.math.max
import kotlin.math.min

/**
 * How the phone's Now Playing fits its screen with nothing to scroll (owner, 10 Nov: on earlier versions his dad had to scroll down to reach the
 * controls and the graphs). Play / pause, previous / next, the seek (the frame of bars round the cover, the visualiser) are always on screen; when
 * the room is short, in this order:
 *  1. the cover shrinks (down to [MIN_COVER]);
 *  2. the frame's bars get a shorter band ([COMPACT_BAND] instead of 22-36 dp), same bar width and spacing, so the graph keeps its detail;
 *  3. the secondary things step aside: the first-run tip, the big title (smaller, then one line), the format and source chips and the
 *     download line, then previous / play / next step down from their big size, then the Lyrics / More / Info / Add row (words only, then
 *     just More beside the heart, whose sheet then lists Lyrics, Up next and Info).
 * Big by default (owner, 26 Nov: "I LOVE big buttons ... be smart"): previous / play / next are car-mode big until the screen is too short.
 * Shuffle and repeat sit either side of the time, so the transport row is just the three and the room left and right of the time is used.
 * A phone on its side puts the cover beside the controls ([Layout.SIDE]); a tablet or an unfolded Fold keeps the player and the side panel
 * ([Layout.WIDE]). All sizes are dp; text is measured as its size in sp times the font scale (Android 14 scales big text less, so this errs safe).
 * Pure Kotlin so it is unit tested (NowPlayingFitTest); LivingSleeve reads the [Plan] in a BoxWithConstraints.
 */
object NowPlayingFit {
    enum class Layout { STACKED, SIDE, WIDE }
    enum class Actions { FULL, COMPACT, MORE_ONLY }

    data class Plan(
        val layout: Layout,
        /** the frame (cover and its bars) as laid out: a square of this side */
        val side: Float,
        /** the band of bars round the cover (the graph's height) */
        val band: Float,
        val compactBand: Boolean,
        val titleSp: Float,
        val titleLines: Int,
        /** the format and source chips and the download line under the artist */
        val chips: Boolean,
        /** the first-run "swipe sideways" tip */
        val tip: Boolean,
        val actions: Actions,
        /** previous / play / next at their big size ([PLAY_H]) or a step down ([PLAY_H_TIGHT]) */
        val bigControls: Boolean,
        /** everything but the frame, top to bottom (in [Layout.SIDE], the taller of the two columns) */
        val fixed: Float,
        val height: Float,
    ) {
        val cover: Float get() = side - 2 * band
        /** the controls and the frame are all on screen */
        val fits: Boolean get() = fixed + (if (layout == Layout.SIDE) 0f else side) <= height + .5f && side >= MIN_SIDE
        val actionsHeight: Float get() = when (actions) { Actions.FULL -> SLOT_H; Actions.COMPACT -> 44f; Actions.MORE_ONLY -> 0f }
    }

    const val MIN_COVER = 180f
    const val COMPACT_COVER = 140f
    const val MIN_SIDE = 96f
    const val COMPACT_BAND = 16f
    const val WIDE_MIN_W = 600f
    const val WIDE_MIN_H = 480f

    /** The band of bars for a frame of this side, as the frame has always drawn it. */
    fun normalBand(side: Float): Float = if (side < 300f) 22f else if (side < 420f) 28f else 36f

    /** The gap between the cover and its bars: a compact band keeps the bars as long as it can. */
    fun gap(compact: Boolean): Float = if (compact) 5f else 7f

    private data class Step(val compactBand: Boolean, val tip: Boolean, val titleSp: Float, val titleLines: Int, val chips: Boolean, val actions: Actions, val big: Boolean, val minCover: Float)

    // each step is tried in turn; the first whose cover is at least its minCover is the plan
    private val steps = listOf(
        Step(false, true, 52f, 2, true, Actions.FULL, true, MIN_COVER),
        Step(true, true, 52f, 2, true, Actions.FULL, true, COMPACT_COVER),
        Step(true, false, 52f, 2, true, Actions.FULL, true, COMPACT_COVER),
        Step(true, false, 40f, 2, true, Actions.FULL, true, COMPACT_COVER),
        Step(true, false, 32f, 1, true, Actions.FULL, true, COMPACT_COVER),
        Step(true, false, 32f, 1, false, Actions.FULL, true, COMPACT_COVER),
        Step(true, false, 32f, 1, false, Actions.FULL, false, COMPACT_COVER),
        Step(true, false, 28f, 1, false, Actions.COMPACT, false, COMPACT_COVER),
        Step(true, false, 26f, 1, false, Actions.MORE_ONLY, false, 0f),
    )

    /**
     * [width] and [height] are the whole Now Playing screen (it draws under the system bars), [top] the status bar / cutout inset and [bottom]
     * the navigation bar inset.
     */
    fun plan(width: Float, height: Float, fontScale: Float = 1f, top: Float = 24f, bottom: Float = 24f): Plan {
        val fs = fontScale.coerceAtLeast(.85f)
        val layout = when {
            width >= WIDE_MIN_W && height >= WIDE_MIN_H -> Layout.WIDE
            width > height -> Layout.SIDE
            else -> Layout.STACKED
        }
        var last: Plan? = null
        for (s in steps) {
            val p = when (layout) {
                Layout.WIDE -> wide(width, height, fs, top, bottom, s)
                Layout.SIDE -> side(width, height, fs, top, bottom, s)
                Layout.STACKED -> stacked(width, height, fs, top, bottom, s)
            }
            last = p
            // on its side the cover is as big as the height allows whatever the right column holds, so only the column has to fit
            if ((layout == Layout.SIDE || p.cover >= s.minCover) && p.fits) return p
        }
        // nothing fits (a split-screen sliver): the most compact plan; the player lets only this case scroll, as a last resort
        return last!!
    }

    // ---- the parts' heights ----

    private fun line(sp: Float, fs: Float, k: Float = 1.35f) = sp * k * fs

    /** The kit's big slot (Kit.sizes(false).slot): the row is big by default too. */
    const val SLOT_H = 60f

    /** The bottom strip on a phone: the row of Lyrics / More / Info / Add, above the gesture bar; at [Actions.MORE_ONLY] no row (More is beside
     *  the heart), only the margin over the gesture bar. */
    fun strip(actions: Actions, bottom: Float): Float =
        (if (actions == Actions.MORE_ONLY) 0f else 6f + (if (actions == Actions.FULL) SLOT_H else 44f)) + max(bottom + 10f, 18f)

    fun tipHeight(fs: Float): Float = 2 * line(12.5f, fs) + 16f

    /** The title, the artist (with its chips) and the download line beside the 52 dp favourite button. */
    fun titleBlock(width: Float, fs: Float, sp: Float, lines: Int, chips: Boolean): Float {
        var col = lines * sp * .98f * fs
        val artist = line(17f, fs)
        col += 8f + if (chips) {
            val chip = line(12f, fs) + 8f
            // the artist and two chips wrap onto more rows when they do not fit beside each other
            val room = max(1f, width - 44f - 64f)
            val rows = min(3f, ceil((140f + 110f + 90f) * fs / room)).coerceAtLeast(1f)
            rows * max(artist, chip) + (rows - 1) * 6f + 8f + line(13f, fs)
        } else artist
        return max(col, 52f)
    }

    fun timeHeight(fs: Float): Float = 2f + line(15f, fs, 1.4f)

    /** The time with shuffle and repeat either side of it: as tall as the taller of the two. */
    fun timeRowHeight(fs: Float): Float = max(timeHeight(fs), ROUND)

    fun controlsHeight(width: Float, big: Boolean = true): Float = (if (big) PLAY_H else PLAY_H_TIGHT) * controlsScale(width - 28f, big)

    // previous / play / next, big by default (car mode's feel at app scale; the web's .ls-play / .ls-pillbtn are the same), a step down when tight;
    // play is the biggest. Shuffle and repeat are 48 dp rounded squares beside the time
    const val PLAY_W = 112f; const val PLAY_H = 72f; const val PILL_W = 84f; const val PILL_H = 64f
    const val PLAY_W_TIGHT = 88f; const val PLAY_H_TIGHT = 60f; const val PILL_W_TIGHT = 68f; const val PILL_H_TIGHT = 56f
    const val ROUND = 48f
    /** The cover's largest side: an unfolded Fold or a tablet gives it the room (1g-2: it was held to 560 dp, small with space round it). */
    const val COVER_MAX = 760f
    const val GAP = 14f

    /** Previous | play | next with their two gaps, end to end. */
    fun transportW(big: Boolean): Float = if (big) 2 * PILL_W + PLAY_W + 2 * GAP else 2 * PILL_W_TIGHT + PLAY_W_TIGHT + 2 * GAP

    /** How much previous / play / next shrink in a row [room] dp wide: 1 where the three fit, less on a very narrow screen, never under .8. */
    /** On a tall screen the play row and the row of slots grow with the height instead of leaving gaps (owner, 28 Nov): 1 up to 780 dp, rising to
     *  1.25 by 960 dp. Only used where the plan already fits (never on a short or sideways phone). */
    fun heightGrow(screenH: Float): Float = 1f + .25f * ((screenH - 780f) / 180f).coerceIn(0f, 1f)

    fun controlsScale(room: Float, big: Boolean = true): Float = (min(room, 520f) / transportW(big)).coerceIn(.8f, 1f)

    private fun gaps(stageH: Float) = if (stageH < 700f) 8f + 10f else 14f + 16f

    private fun frameSide(width: Float, room: Float) = min(min(width - 16f, room), 560f).coerceAtLeast(0f)

    private fun make(layout: Layout, side: Float, s: Step, fixed: Float, height: Float): Plan {
        val band = if (s.compactBand) COMPACT_BAND else normalBand(side)
        return Plan(layout, side, band, s.compactBand, s.titleSp, s.titleLines, s.chips, s.tip, s.actions, s.big, fixed, height)
    }

    /** A phone upright: top bar, the frame, the time, the title, the controls; the strip below. */
    private fun stacked(width: Float, height: Float, fs: Float, top: Float, bottom: Float, s: Step): Plan {
        val strip = strip(s.actions, bottom) + if (s.tip) tipHeight(fs) else 0f
        val stageH = height - strip
        // (an upright phone draws its title at most PHONE_TITLE sp: SleeveTitle)
        val stage = top + 60f + 4f + timeRowHeight(fs) + gaps(stageH) + titleBlock(width, fs, min(s.titleSp, PHONE_TITLE), s.titleLines, s.chips) + controlsHeight(width, s.big) + 12f + 8f
        val fixed = strip + stage
        return make(Layout.STACKED, frameSide(width, height - fixed), s, fixed, height)
    }

    /** The biggest title an upright phone draws (SleeveTitle caps it there; a tablet has the full 52). */
    const val PHONE_TITLE = 34f

    /** A tablet or an unfolded Fold: the player in the left half (up to 520 dp), its own row of actions, and a margin over the gesture bar. */
    private fun wide(width: Float, height: Float, fs: Float, top: Float, bottom: Float, s: Step): Plan {
        val w = min(width / 2f, 520f)
        val actions = (if (height < 700f) 8f else 14f) + (if (s.actions == Actions.FULL) SLOT_H else 44f)
        val fixed = top + 60f + 4f + timeRowHeight(fs) + gaps(height) + titleBlock(w, fs, s.titleSp, s.titleLines, s.chips) + controlsHeight(w, s.big) + 12f + actions + max(92f, bottom + 24f)
        return make(Layout.WIDE, frameSide(w, height - fixed), s, fixed, height)
    }

    /**
     * A phone on its side: the frame on the left, as tall as the screen allows (up to 45% of its width); on the right the top bar, title, time,
     * controls and, at its foot, the Lyrics / More / Info / Add row; at [Actions.MORE_ONLY] that row goes and More sits beside the heart.
     * The frame's size does not depend on the right column, so its band is compact only when the cover itself is short.
     */
    private fun side(width: Float, height: Float, fs: Float, top: Float, bottom: Float, s: Step): Plan {
        val side = frameSide(width * .45f + 16f, height - top - 8f)
        val colW = width - side - 24f
        val more = s.actions == Actions.MORE_ONLY
        val time = timeRowHeight(fs)
        val foot = if (more) max(bottom, 8f) else strip(s.actions, bottom)
        val right = 60f + titleBlock(colW, fs, s.titleSp, s.titleLines, s.chips) + 8f + time + 8f + controlsHeight(colW, s.big) + 8f + foot
        val compact = side - 2 * normalBand(side) < MIN_COVER
        return make(Layout.SIDE, side, s.copy(compactBand = compact), top + right, height)
    }

    /**
     * The Song info graph's height in a panel [panelH] tall: as before (a third of it, 150 to 230 dp, 280 for Hi-Res) when the whole graph card fits,
     * else as much as leaves its tabs, its name and the graph on screen together, not under 96 dp. The graph draws the same bins at any height.
     */
    fun infoGraphHeight(panelH: Float, fontScale: Float = 1f, hiRes: Boolean = false): Float {
        val hi = if (hiRes) 280f else 230f
        val want = (panelH * .34f).coerceIn(150f, hi)
        // the card's padding, the tabs, the gaps and the title row (serif 28 sp or the 44 dp full-screen button)
        val overhead = 28f + 48f + 10f + max(44f, 28f * 1.3f * fontScale) + 10f
        return if (want + overhead <= panelH) want else (panelH - overhead).coerceIn(96f, want)
    }
}

/** Now Playing's row of quiet actions (ONE APP 1g-2, layout B "mode slots", chosen from the mock-ups on docs/parity/np-1g2.html): Shuffle and
 *  Repeat are two-state slots beside Lyrics and Info, so previous / play / next keep their big row alone and the time (or the seek line) has its
 *  own; Add to playlist is in More's sheet. Only when the row shrinks to More do Shuffle and Repeat go back beside the time. Pure, unit-tested. */
object NowPlayingSlots {
    fun of(actions: NowPlayingFit.Actions): List<String> =
        if (actions == NowPlayingFit.Actions.MORE_ONLY) listOf("more") else listOf("shuffle", "repeat", "lyrics", "more")   // Info is in More (owner, 28 Nov)

    fun togglesBesideTime(actions: NowPlayingFit.Actions): Boolean = actions == NowPlayingFit.Actions.MORE_ONLY
}
