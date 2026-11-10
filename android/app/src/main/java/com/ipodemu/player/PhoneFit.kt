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

/**
 * The mini player above the tabs. Its words (title, artist, download line) must fit the bar's height: at big text the download line goes, then the
 * title is smaller, then the bar grows a little (not past [GROW] more). Across, the title keeps at least [MIN_TITLE] dp (more at big text): the mic
 * goes first, then the Devices button, then the DJ (both are on Now Playing too). The format chip beside the artist only shows at ordinary text sizes.
 */
object MiniFit {
    data class Plan(val height: Float, val titleSp: Float, val tag: Boolean, val chip: Boolean, val dj: Boolean, val mic: Boolean, val devices: Boolean)

    const val MIN_TITLE = 96f
    const val GROW = 16f

    private fun need(fs: Float) = MIN_TITLE * fs.coerceIn(1f, 1.5f)

    /** The Modern bar: [width] is the bar's row (the screen, less a rail), [short] a screen under 500 dp tall. */
    fun modern(width: Float, fontScale: Float, short: Boolean, dj: Boolean, devices: Boolean): Plan {
        val fs = fontScale.coerceAtLeast(.85f)
        val base = if (short) 58f else 66f
        var titleSp = 20f
        var tag = true
        fun text() = titleSp * 1.05f * fs + fitLine(12.5f, fs) + (if (tag) fitLine(11f, fs) else 0f)
        if (text() > base - 8f) tag = false
        if (text() > base - 8f) titleSp = 17f
        val height = max(base, min(text() + 10f, base + GROW))
        // the row: outer and inner padding, the cover, the column's padding, play and next
        val room = width - 24f - 14f - (if (short) 44f else 50f) - 24f - 48f - 48f
        var showDj = dj; var showMic = dj && width >= 380f; var showDev = devices
        fun used() = (if (showDj) 40f else 0f) + (if (showMic) 40f else 0f) + (if (showDev) 48f else 0f)
        if (room - used() < need(fs)) showMic = false
        if (room - used() < need(fs)) showDev = false
        if (room - used() < need(fs)) showDj = false
        return Plan(height, titleSp, tag, !short && fs <= 1.15f && room - used() >= need(fs) + 40f, showDj, showMic, showDev)
    }

    /** The iPod skins' bar: 56 dp, growing for big text; play, next and the DJ keep 48 dp touch targets. */
    fun classic(width: Float, fontScale: Float, dj: Boolean): Plan {
        val fs = fontScale.coerceAtLeast(.85f)
        var tag = true
        fun text() = fitLine(14f, fs) + fitLine(12f, fs) + (if (tag) fitLine(10.5f, fs) else 0f)
        if (text() > 56f - 6f) tag = false
        val height = max(56f, min(text() + 4f, 56f + GROW))
        val room = width - 16f - 40f - 20f - 48f - 48f
        var showDj = dj; var showMic = dj && width >= 380f
        fun used() = (if (showDj) 48f else 0f) + (if (showMic) 48f else 0f)
        if (room - used() < need(fs)) showMic = false
        if (room - used() < need(fs)) showDj = false
        return Plan(height, 14f, tag, false, showDj, showMic, false)
    }
}

/**
 * Home's hero (the last cover under the search pill, the greeting and the serif headline). Upright it is 300 dp; on its side it is at most 60% of
 * the height, so the chips and the first shelf show under it. The headline (40 sp) is smaller when its two lines would not fit the hero over the
 * search pill and the greeting, and is kept under 52 sp as drawn at big text sizes.
 */
object HomeFit {
    data class Hero(val height: Float, val headlineSp: Float)

    const val TOP = 6f + 48f

    fun hero(width: Float, height: Float, fontScale: Float = 1f): Hero {
        val fs = fontScale.coerceAtLeast(.85f)
        val h = if (width > height) (height * .6f).coerceIn(180f, 300f) else 300f
        var sp = 40f
        fun need() = TOP + fitLine(11f, fs) + 6f + 2 * sp * 1.05f * fs + 8f
        while (sp > 24f && (need() > h || sp * fs > 52f)) sp -= 2f
        return Hero(h, sp)
    }
}

/**
 * The song lists (Songs, an album's or a playlist's songs, search's library rows, Up next). A row's trailing cluster (downloaded mark, heart, time,
 * the ⋯ button) took about 140 dp at font scale 1.5, leaving a 320 dp phone's title six letters; the time goes when the width per unit of text is
 * short (it is in the song's menu and on Now Playing).
 */
object ListFit {
    /** The width, in dp, divided by the font scale, under which the row's time steps aside. */
    const val TIME_MIN = 260f

    fun showTime(width: Float, fontScale: Float = 1f): Boolean = width / fontScale.coerceAtLeast(.85f) >= TIME_MIN
}

/**
 * Album, artist and playlist pages. Upright the cover is as before (up to 300 dp); on its side it is at most 45% of the height (the 300 dp cover
 * filled a landscape phone, Play and Shuffle under the fold). Two panes (the header beside the songs) from 560 dp wide when the screen is clearly
 * landscape, the header column up to 300 dp and scrolling. The serif title (42 sp, 34 for a long one) is kept under 50 sp as drawn.
 */
object DetailFit {
    fun coverCap(width: Float, height: Float, max: Float = 300f): Float = if (width > height) min(max, max(140f, height * .45f)) else max

    fun twoPane(width: Float, height: Float): Boolean = width >= 560f && width > height * 1.25f

    fun sideWidth(width: Float): Float = min(300f, width * .42f)

    fun titleSp(long: Boolean, fontScale: Float = 1f): Float {
        var sp = if (long) 34f else 42f
        while (sp > 26f && sp * fontScale > 50f) sp -= 2f
        return sp
    }
}

/**
 * Search's catalogue rows: the cover, the title and its line, then the ⋯ menu and a Download (or Play) pill. On a 360 dp phone the pill and the menu
 * left the title about 100 dp (68 on a 320 dp one); when the title would get under 140 dp per unit of font scale the pill becomes a 48 dp icon.
 */
object SearchFit {
    fun compactActions(width: Float, fontScale: Float = 1f): Boolean {
        val fs = fontScale.coerceAtLeast(.85f)
        // row padding and the cover (104), the menu and the gaps (42), the pill: its padding and icon room, and "Download" as drawn
        val room = width - 104f - 42f - (46f + 66f * fs)
        return room < 140f * fs
    }
}

/**
 * A Settings row: the name and what it does on the left, a value and an action chip (or a chevron) on the right. At font scale 1.5 on a 320 dp phone
 * a value and a chip took about 260 dp of the 256 the row has, pressing the name to nothing; when the name would get under 110 dp per unit of font
 * scale the value and the chip go under it instead.
 */
object SettingsFit {
    fun trailing(fontScale: Float, valueChars: Int, actionChars: Int, chevron: Boolean): Float {
        val fs = fontScale.coerceAtLeast(.85f)
        val action = actionChars > 0
        val value = if (valueChars > 0) min(if (action) 120f else 150f, valueChars * (if (action) 13f else 15f) * .55f * fs) + 8f else 0f
        return value + (if (action) 32f + actionChars * 14f * .6f * fs else 0f) + (if (chevron) 24f else 0f)
    }

    /** [width]: the panel's row (the screen, up to 620 dp). */
    fun stack(width: Float, fontScale: Float, valueChars: Int, actionChars: Int, chevron: Boolean = false): Boolean {
        val room = width - 32f - 32f - 12f - trailing(fontScale, valueChars, actionChars, chevron)
        return room < 110f * fontScale.coerceAtLeast(.85f)
    }
}

/**
 * Up next's header: "Playing from" and the list's name beside the Save as playlist chip. At font scale 1.5 on a 320 dp phone the chip was about 240
 * dp wide and left the name 48; the chip says just "Save" when the name would get under 100 dp per unit of font scale.
 */
object QueueFit {
    const val LONG = "Save as playlist"
    const val SHORT = "Save"

    private fun chip(label: String, fs: Float) = 32f + 23f + label.length * 14f * .6f * fs

    /** [width]: the pane (the screen on a phone, the side panel on a tablet). */
    fun saveLabel(width: Float, fontScale: Float = 1f): String {
        val fs = fontScale.coerceAtLeast(.85f)
        val room = width - 36f - 24f - 8f
        return if (room - chip(LONG, fs) >= 100f * fs) LONG else SHORT
    }
}

/**
 * Lyrics' line size: 30 sp in the full pane, 24 sp timed and 20 sp plain in the card. Big text multiplied them to 45 sp, about eight letters a line
 * on a 320 dp phone, and a long line hit its six-line limit; the drawn size is kept under [PANE_MAX] in the pane and [CARD_MAX] in the card.
 */
object LyricsFit {
    const val PANE_MAX = 40f
    const val CARD_MAX = 32f

    fun sp(big: Boolean, synced: Boolean, fontScale: Float = 1f): Float {
        val base = if (big) 30f else if (synced) 24f else 20f
        val cap = if (big) PANE_MAX else CARD_MAX
        return min(base, cap / fontScale.coerceAtLeast(.85f))
    }
}

/**
 * Song info. The four number tiles (kbps, kHz, bit, channels) shared a 256 dp card on a 320 dp phone, and "1411" in 20 sp mono at font scale 1.5 was
 * cut to "1…": they go two to a row when four would not fit. The full-screen graph's hint (up to three lines under the controls) gives way to the
 * graph on a short screen: as many lines as leave the graph [MIN_GRAPH] dp.
 */
object InfoFit {
    const val MIN_GRAPH = 120f

    /** [width]: the card's inside (the panel less 28 dp of padding). */
    fun tileColumns(width: Float, fontScale: Float = 1f): Int = if (width >= 192f * fontScale.coerceAtLeast(.85f) + 104f) 4 else 2

    /** [height]: the full-screen graph's room inside the system bars. */
    fun hintLines(height: Float, fontScale: Float = 1f): Int {
        val fs = fontScale.coerceAtLeast(.85f)
        val overhead = 32f + max(48f, 32f * 1.3f * fs) + 44f + 58f + 3 * 12f
        for (n in 3 downTo 1) if (overhead + 12f + n * fitLine(12.5f, fs) + MIN_GRAPH <= height) return n
        return 0
    }
}

/**
 * Grids of covers (Albums, Playlists): as many columns of at least [min] dp as fit, but two on any phone at least 280 dp wide (a 320 dp phone got one
 * column of 296 dp covers from GridCells.Adaptive(150 dp)).
 */
object GridFit {
    fun columns(width: Float, min: Float = 150f, gap: Float = 8f, pad: Float = 24f): Int {
        val n = ((width - pad + gap) / (min + gap)).toInt()
        return if (n < 2 && width >= 280f) 2 else n.coerceAtLeast(1)
    }
}
