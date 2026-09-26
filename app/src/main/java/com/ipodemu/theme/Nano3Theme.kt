package com.ipodemu.theme

import android.graphics.Canvas
import android.graphics.Paint
import com.ipodemu.nav.Item
import com.ipodemu.nav.Kind
import com.ipodemu.nav.ListPage
import com.ipodemu.nav.MenuBuilder
import com.ipodemu.nav.NowPlayingPage
import com.ipodemu.nav.Page
import kotlin.math.ceil
import kotlin.math.floor

/** Colours for the nano-style UI; a dark skin is just another palette. */
class Palette(
    val bg: Int,
    val hdrTop: Int, val hdrBot: Int, val hdrLine: Int, val hdrInk: Int,
    val ink: Int, val sub: Int, val line: Int,
    val selTop: Int, val selBot: Int, val selEdge: Int, val selInk: Int, val selSub: Int,
    val chev: Int,
    val paneTop: Int, val paneBot: Int, val paneInk: Int, val paneAccent: Int, val paneDim: Int,
    val npTop: Int, val npBot: Int, val npTitle: Int, val npArtist: Int, val npAlbum: Int, val npDim: Int,
    val artFrame: Int, val artBg: Int, val artGlyph: Int,
    val barTop: Int, val barBot: Int, val barEdge: Int,
)

private fun c(v: Long) = v.toInt()

object Palettes {
    val nanoLight = Palette(
        bg = c(0xFFFFFFFF),
        hdrTop = c(0xFFF7F8FA), hdrBot = c(0xFFB9BEC8), hdrLine = c(0xFF7B808A), hdrInk = c(0xFF1C1E23),
        ink = c(0xFF000000), sub = c(0xFF858A94), line = c(0xFFE1E4EA),
        selTop = c(0xFF5DA6F5), selBot = c(0xFF1D6BDB), selEdge = c(0xFF8CC0FA), selInk = c(0xFFFFFFFF), selSub = c(0xFFDCE8FB),
        chev = c(0xFF8C919B),
        paneTop = c(0xFF2B2E36), paneBot = c(0xFF07080A), paneInk = c(0xFFFFFFFF), paneAccent = c(0xFF63A9FF), paneDim = c(0xFF9BA1AE),
        npTop = c(0xFFFFFFFF), npBot = c(0xFFE8EBF0), npTitle = c(0xFF000000), npArtist = c(0xFF3D4048), npAlbum = c(0xFF7A7F89), npDim = c(0xFF7F848E),
        artFrame = c(0xFFC9CDD4), artBg = c(0xFFE3E6EB), artGlyph = c(0xFFB0B5BF),
        barTop = c(0xFFA9AEB8), barBot = c(0xFFE6E9EE), barEdge = c(0xFF8A8F99),
    )
    val nanoDark = Palette(
        bg = c(0xFF111215),
        hdrTop = c(0xFF3C3F47), hdrBot = c(0xFF1A1C21), hdrLine = c(0xFF000000), hdrInk = c(0xFFF2F3F5),
        ink = c(0xFFF2F3F5), sub = c(0xFF8B909B), line = c(0xFF23252B),
        selTop = c(0xFF4A9BFF), selBot = c(0xFF1F62D6), selEdge = c(0xFF7DB7FF), selInk = c(0xFFFFFFFF), selSub = c(0xFFD7E6FF),
        chev = c(0xFF6C717C),
        paneTop = c(0xFF22242C), paneBot = c(0xFF040405), paneInk = c(0xFFFFFFFF), paneAccent = c(0xFF63A9FF), paneDim = c(0xFF9BA1AE),
        npTop = c(0xFF1C1E23), npBot = c(0xFF0A0B0D), npTitle = c(0xFFFFFFFF), npArtist = c(0xFFC7CAD1), npAlbum = c(0xFF8B909B), npDim = c(0xFF8B909B),
        artFrame = c(0xFF3A3D45), artBg = c(0xFF2A2D34), artGlyph = c(0xFF5A5F6B),
        barTop = c(0xFF2A2D34), barBot = c(0xFF3D4048), barEdge = c(0xFF555A66),
    )
}

/**
 * iPod nano 3rd generation ("fatty"): 320x240 display, split-screen menus with a preview pane, glossy blue selection.
 * The same UI ships in light/dark palettes and in flat or device-body skins; in fullscreen (controller deployed)
 * it switches to taller rows and a big-artwork layout.
 */
class Nano3Theme(
    override val id: String,
    override val displayName: String,
    private val pal: Palette,
    override val chrome: Chrome,
    override val device: DeviceSpec,
) : ListTheme() {
    override val headerH = 26f

    private val tall: Boolean get() = logicalH > 300f
    override val rowH: Float get() = if (tall) 38f else 27f

    private val W: Float get() = logicalW
    private val H: Float get() = logicalH
    private val PREVIEW_X: Float get() = W * 0.55f
    /** Portrait screens (nano 4th/5th gen) are too narrow for the split view. */
    private val narrow: Boolean get() = W < 280f

    override fun buildRoot(b: MenuBuilder): ListPage = b.rootPage {
        b.statusItems() + listOf(
            b.musicItem { musicMenu(b) },
            b.voiceMemosItem(),
            b.settingsItem(),
            b.shuffleItem(),
        ) + b.nowPlayingItems()
    }

    private fun musicMenu(b: MenuBuilder) = b.page("Music") {
        listOf(b.playlistsItem(), b.artistsItem(), b.albumsItem(), b.songsItem(), b.genresItem())
    }

    // ---- screen ------------------------------------------------------------

    override fun drawScreen(c: Canvas, page: Page, ctx: DrawCtx) {
        when (page) {
            is ListPage -> drawList(c, page, ctx)
            is NowPlayingPage -> drawNowPlaying(c, ctx)
        }
    }

    private fun header(c: Canvas, title: String, ctx: DrawCtx, right: String? = null) {
        Gfx.vgrad(c, 0f, 0f, W, headerH, pal.hdrTop, pal.hdrBot)
        Gfx.line(c, 0f, headerH - 0.5f, W, headerH - 0.5f, pal.hdrLine, 1f)
        Gfx.text(c, title, W / 2, 18f, 14f, pal.hdrInk, Gfx.SANS_BOLD, Paint.Align.CENTER, maxW = W * 0.6f)
        if (ctx.status.playing) Gfx.triangle(c, 9f, headerH / 2, 10f, pal.hdrInk)
        Gfx.battery(c, W - 7f, headerH / 2, ctx.status.battery, ctx.status.charging, pal.hdrInk, 0xFF56B14E.toInt())
        (ctx.status.clock ?: right)?.let { Gfx.text(c, it, W - 34f, 18f, 12f, pal.hdrInk, Gfx.SANS, Paint.Align.RIGHT) }
    }

    private fun drawList(c: Canvas, page: ListPage, ctx: DrawCtx) {
        Gfx.rect(c, 0f, 0f, W, H, pal.bg)
        val split = !narrow && (page.kind == Kind.MENU || hasArtPane(page))
        val listW = if (split) PREVIEW_X else W
        val rows = rowsVisible()
        val n = page.source.size

        c.save()
        c.clipRect(0f, headerH, listW, H)
        val first = floor(page.scroll).toInt().coerceAtLeast(0)
        val last = minOf(n - 1, ceil(page.scroll + rows).toInt())
        for (i in first..last) {
            val y = headerH + (i - page.scroll) * rowH
            drawRow(c, page, page.source[i], i == page.selected && !page.free, y, listW, ctx)
        }
        // Fullscreen menus: use the empty space under the list for what is coming up next.
        if (tall && page.kind == Kind.MENU) drawUpNext(c, ctx, headerH + (n - page.scroll) * rowH + 14f, listW)
        c.restore()

        if (n > rows) {
            val trackH = H - headerH - 4f
            val th = (trackH * rows / n).coerceAtLeast(14f)
            val ty = headerH + 2f + (trackH - th) * (page.scroll / (n - rows).toFloat().coerceAtLeast(1f))
            Gfx.rrect(c, listW - 5f, ty, listW - 2f, ty + th, 1.5f, 0x88555A66.toInt())
        }
        if (split) { if (page.kind == Kind.MENU) drawPreview(c, page, ctx) else drawArtPane(c, page, ctx) }
        header(c, page.title, ctx)
    }

    private fun drawUpNext(c: Canvas, ctx: DrawCtx, y0: Float, w: Float) {
        val q = ctx.upNext
        if (q.isEmpty() || H - y0 < 78f) return
        Gfx.line(c, 10f, y0, w - 10f, y0, pal.line, 1f)
        Gfx.text(c, "UP NEXT", 10f, y0 + 17f, 10.5f, pal.sub, Gfx.SANS_BOLD)
        val pitch = 34f
        val rows = ((H - y0 - 24f) / pitch).toInt().coerceIn(1, 4)
        q.take(rows).forEachIndexed { i, (_, t) ->
            val y = y0 + 26f + i * pitch
            Gfx.text(c, t.title, 10f, y + 12f, 13f, pal.ink, Gfx.SANS_MED, maxW = w - 20f)
            Gfx.text(c, t.artist.ifEmpty { t.album }, 10f, y + 26f, 10.5f, pal.sub, Gfx.SANS, maxW = w - 20f)
        }
    }

    private fun drawRow(c: Canvas, page: ListPage, item: Item, sel: Boolean, y: Float, w: Float, ctx: DrawCtx) {
        val fg = if (sel) pal.selInk else pal.ink
        val sub = if (sel) pal.selSub else pal.sub
        val fs = rowH * 0.555f
        if (sel) {
            Gfx.vgrad(c, 0f, y, w, y + rowH, pal.selTop, pal.selBot)
            Gfx.line(c, 0f, y + 0.5f, w, y + 0.5f, pal.selEdge, 1f)
        } else {
            Gfx.line(c, 0f, y + rowH - 0.5f, w, y + rowH - 0.5f, pal.line, 1f)
        }
        val base = y + rowH * 0.7f
        var x = 10f
        if (narrow && hasArtPane(page)) {
            Gfx.art(c, ctx.art.get(item.artKey, true), 6f, y + 3f, 6f + rowH - 6f, y + rowH - 3f, pal.artBg, pal.artGlyph)
            x = rowH + 6f
        }
        if (item.checked != null) { if (item.checked.invoke()) Gfx.check(c, 15f, y + rowH / 2, fs * 0.6f, fg, 2f); x = 28f }
        var right = w - 10f
        if (item.chevron) { Gfx.chevron(c, right - 3f, y + rowH / 2, fs * 0.73f, if (sel) fg else pal.chev, 2f); right -= fs * 0.95f }
        val v = item.value?.invoke() ?: if (page.kind == Kind.MEMOS) item.subtitle else null
        if (v != null) {
            val vw = Gfx.text(c, v, right, base - 0.5f, fs * 0.8f, sub, Gfx.SANS, Paint.Align.RIGHT, maxW = w * 0.45f)
            right -= vw + 8f
        }
        Gfx.text(c, item.title, x, base, fs, fg, Gfx.SANS_MED, maxW = right - x)
    }

    override fun listRight(page: ListPage): Float = if (!narrow && (page.kind == Kind.MENU || hasArtPane(page))) PREVIEW_X else W

    private fun hasArtPane(p: ListPage) = p.kind == Kind.SONGS || p.kind == Kind.ALBUMS || p.kind == Kind.ARTISTS

    /** ES-DE style split view: the selected entry's artwork and details on the right of the list. */
    private fun drawArtPane(c: Canvas, page: ListPage, ctx: DrawCtx) {
        val l = PREVIEW_X
        Gfx.vgrad(c, l, headerH, W, H, pal.paneTop, pal.paneBot)
        Gfx.line(c, l, headerH, l, H, 0xFF000000.toInt(), 1f)
        if (page.source.size == 0) return
        val item = page.source[page.selected.coerceIn(0, page.source.size - 1)]
        val pw = W - l
        val sz = minOf(pw - 22f, H - headerH - 68f)
        val ax = l + (pw - sz) / 2; val ay = headerH + 11f
        Gfx.rect(c, ax - 1f, ay - 1f, ax + sz + 1f, ay + sz + 1f, 0x44FFFFFF)
        Gfx.art(c, ctx.art.get(item.artKey), ax, ay, ax + sz, ay + sz, 0xFF3A3E47.toInt(), 0xFF7C8290.toInt())
        val cx = l + pw / 2; val mw = pw - 16f
        Gfx.text(c, item.title, cx, ay + sz + 19f, 13.5f, pal.paneInk, Gfx.SANS_BOLD, Paint.Align.CENTER, maxW = mw)
        item.subtitle?.takeIf { it.isNotEmpty() }?.let { Gfx.text(c, it, cx, ay + sz + 34f, 11.5f, pal.paneAccent, Gfx.SANS_MED, Paint.Align.CENTER, maxW = mw) }
        item.detail?.takeIf { it.isNotEmpty() }?.let { Gfx.text(c, it, cx, ay + sz + (if (item.subtitle.isNullOrEmpty()) 34f else 48f), 10.5f, pal.paneDim, Gfx.SANS, Paint.Align.CENTER, maxW = mw) }
    }

    /** Main-menu preview pane: the current track's artwork when something is playing (fullscreen), else the icon tile. */
    private fun drawPreview(c: Canvas, page: ListPage, ctx: DrawCtx) {
        val l = PREVIEW_X
        Gfx.vgrad(c, l, headerH, W, H, pal.paneTop, pal.paneBot)
        Gfx.line(c, l, headerH, l, H, 0xFF000000.toInt(), 1f)
        if (page.source.size == 0) return
        val pw = W - l
        val t = ctx.np.track
        if (tall && t != null) {
            val sz = minOf(pw - 20f, H - headerH - 96f)
            val ax = l + (pw - sz) / 2; val ay = headerH + 14f
            Gfx.rect(c, ax - 1f, ay - 1f, ax + sz + 1f, ay + sz + 1f, 0x44FFFFFF)
            Gfx.art(c, ctx.art.get(t.artKey), ax, ay, ax + sz, ay + sz, 0xFF3A3E47.toInt(), 0xFF7C8290.toInt())
            val cx = l + pw / 2; val mw = pw - 14f
            Gfx.text(c, "NOW PLAYING", cx, ay + sz + 20f, 10f, pal.paneAccent, Gfx.SANS_BOLD, Paint.Align.CENTER)
            Gfx.text(c, t.title, cx, ay + sz + 38f, 14f, pal.paneInk, Gfx.SANS_BOLD, Paint.Align.CENTER, maxW = mw)
            Gfx.text(c, t.artist.ifEmpty { t.album }, cx, ay + sz + 54f, 11.5f, pal.paneDim, Gfx.SANS, Paint.Align.CENTER, maxW = mw)
            return
        }
        val icon = page.source[page.selected].icon
        val s = if (tall) 118f else 96f
        val cx = l + pw / 2; val cy = headerH + (if (tall) 100f else 84f)
        Gfx.vgrad(c, cx - s / 2, cy - s / 2, cx + s / 2, cy + s / 2, 0xFF7FB6FF.toInt(), 0xFF1F63C6.toInt(), 12f)
        Gfx.vgrad(c, cx - s / 2, cy - s / 2, cx + s / 2, cy, 0x66FFFFFF, 0x00FFFFFF, 12f)
        Gfx.glyph(c, icon, cx, cy, s * 0.8f, 0xFFFFFFFF.toInt())
        c.save()
        c.clipRect(cx - s / 2, cy + s / 2 + 4f, cx + s / 2, cy + s / 2 + 4f + 30f)
        c.scale(1f, -1f, cx, cy + s / 2 + 2f)
        Gfx.vgrad(c, cx - s / 2, cy - s / 2, cx + s / 2, cy + s / 2, 0x447FB6FF, 0x221F63C6, 12f)
        c.restore()
        Gfx.vgrad(c, cx - s / 2, cy + s / 2 + 4f, cx + s / 2, cy + s / 2 + 34f, Gfx.withAlpha(pal.paneBot, 0f), pal.paneBot)
    }

    private fun drawNowPlaying(c: Canvas, ctx: DrawCtx) {
        val np = ctx.np
        Gfx.vgrad(c, 0f, 0f, W, H, pal.npTop, pal.npBot)
        header(c, "Now Playing", ctx, right = if (np.count > 1 && tall) "${np.index + 1} of ${np.count}" else null)
        val t = np.track

        val by = H - (if (tall) 34f else 40f)
        val ay = headerH + (if (tall) 10f else 12f)
        val asz: Float
        val ax: Float
        if (tall) {
            // Fullscreen: artwork as large as the height allows, text tucked between it and the progress bar.
            asz = minOf(W - 30f, by - 76f - ay)
            ax = (W - asz) / 2
        } else { asz = 126f; ax = 16f }
        Gfx.rect(c, ax - 1, ay - 1, ax + asz + 1, ay + asz + 1, pal.artFrame)
        Gfx.art(c, ctx.art.get(t?.artKey), ax, ay, ax + asz, ay + asz, pal.artBg, pal.artGlyph)
        if (!tall) {
            c.save(); c.clipRect(ax, ay + asz + 2f, ax + asz, ay + asz + 22f); c.scale(1f, -1f, ax, ay + asz + 1f)
            Gfx.art(c, ctx.art.get(t?.artKey), ax, ay, ax + asz, ay + asz, pal.artBg, pal.artGlyph)
            c.restore()
            Gfx.vgrad(c, ax, ay + asz + 2f, ax + asz, ay + asz + 22f, Gfx.withAlpha(pal.npBot, 0.72f), pal.npBot)
        }

        if (t != null) {
            if (tall) {
                val cx = W / 2
                Gfx.text(c, t.title, cx, by - 40f, 19f, pal.npTitle, Gfx.SANS_BOLD, Paint.Align.CENTER, maxW = W - 24f)
                val line = listOf(t.artist, t.album).filter { it.isNotEmpty() }.joinToString("  -  ")
                if (line.isNotEmpty()) Gfx.text(c, line, cx, by - 19f, 13f, pal.npArtist, Gfx.SANS, Paint.Align.CENTER, maxW = W - 24f)
            } else {
                val tx = ax + asz + 14f; val tw = W - tx - 10f
                Gfx.text(c, t.title, tx, ay + 22f, 16f, pal.npTitle, Gfx.SANS_BOLD, maxW = tw)
                if (t.artist.isNotEmpty()) Gfx.text(c, t.artist, tx, ay + 46f, 14f, pal.npArtist, Gfx.SANS, maxW = tw)
                if (t.album.isNotEmpty()) Gfx.text(c, t.album, tx, ay + 68f, 14f, pal.npAlbum, Gfx.SANS, maxW = tw)
                if (np.count > 1) Gfx.text(c, "${np.index + 1} of ${np.count}", tx, ay + 92f, 12f, pal.npDim, Gfx.SANS)
            }
        }

        if (np.hudActive && np.mode == NowPlayingPage.Mode.VOLUME) {
            Gfx.speaker(c, 16f, by, 14f, pal.npDim, false)
            bar(c, 38f, W - 38f, by, np.volume, false)
            Gfx.speaker(c, W - 30f, by, 14f, pal.npDim, true)
        } else {
            val frac = if (np.durMs > 0) np.posMs.toFloat() / np.durMs else 0f
            bar(c, 16f, W - 16f, by, frac, np.hudActive && np.mode == NowPlayingPage.Mode.SCRUB)
            Gfx.text(c, MenuBuilder.fmtTime(np.posMs), 16f, by + 20f, 11f, pal.npDim)
            Gfx.text(c, "-" + MenuBuilder.fmtTime((np.durMs - np.posMs).coerceAtLeast(0)), W - 16f, by + 20f, 11f, pal.npDim, Gfx.SANS, Paint.Align.RIGHT)
        }
    }

    private fun bar(c: Canvas, l: Float, r: Float, cy: Float, frac: Float, knob: Boolean) {
        Gfx.vgrad(c, l, cy - 4f, r, cy + 4f, pal.barTop, pal.barBot, 4f)
        val fx = l + (r - l) * frac.coerceIn(0f, 1f)
        if (fx > l + 1) Gfx.vgrad(c, l, cy - 4f, fx, cy + 4f, 0xFF63AAF7.toInt(), 0xFF1F6ADB.toInt(), 4f)
        Gfx.rrectStroke(c, l, cy - 4f, r, cy + 4f, 4f, pal.barEdge, 1f)
        if (knob) { Gfx.circle(c, fx, cy, 7f, 0xFF1F6ADB.toInt()); Gfx.circle(c, fx, cy, 4f, 0xFFFFFFFF.toInt()) }
    }
}
