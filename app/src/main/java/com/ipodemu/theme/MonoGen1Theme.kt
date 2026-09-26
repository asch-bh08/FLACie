package com.ipodemu.theme

import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import com.ipodemu.input.Zone
import com.ipodemu.nav.Icon
import com.ipodemu.nav.Item
import com.ipodemu.nav.Kind
import com.ipodemu.nav.ListPage
import com.ipodemu.nav.MenuBuilder
import com.ipodemu.nav.NowPlayingPage
import com.ipodemu.nav.Page
import kotlin.math.ceil
import kotlin.math.floor

/**
 * Original 2001 iPod: 160x128 monochrome LCD, no artwork, Playlists/Browse/Extras/Settings menu,
 * separate push-buttons around a mechanical scroll wheel.
 */
class MonoGen1Theme(
    override val id: String,
    override val displayName: String,
    override val chrome: Chrome,
    override val device: DeviceSpec,
    private val buttons: Boolean,
) : ListTheme() {
    override val keyTop = 0xFFFBFAF5.toInt()
    override val keyBottom = 0xFFE0DED3.toInt()
    override val keyEdge = 0xFFA9A79C.toInt()
    override val keyInk = 0xFF5B5A52.toInt()
    override val cardTop = 0xFFCFD6BE.toInt()
    override val cardBottom = 0xFFC3CAB1.toInt()
    override val cardInk = 0xFF15190F.toInt()
    override val cardDim = 0xFF4C5340.toInt()
    override val cardAccent = 0xFF15190F.toInt()
    override val cardOnInk = 0xFFCFD6BE.toInt()
    override val headerH = 16f
    override val rowH = 14f

    private val W: Float get() = logicalW
    private val H: Float get() = logicalH
    private val LCD = 0xFFCFD6BE.toInt()
    private val INK = 0xFF15190F.toInt()
    private val FONT = 11f

    override fun buildRoot(b: MenuBuilder): ListPage = b.rootPage {
        b.statusItems() + listOf(
            b.playlistsItem(),
            Item("Browse", icon = Icon.BROWSE) { nav ->
                nav.push(b.page("Browse") { listOf(b.artistsItem(), b.albumsItem(), b.songsItem(), b.genresItem()) })
            },
            Item("Extras", icon = Icon.EXTRAS) { nav -> nav.push(b.page("Extras") { listOf(b.voiceMemosItem()) }) },
            b.settingsItem(),
            b.shuffleItem(),
        ) + b.nowPlayingItems()
    }

    override fun drawWheel(c: Canvas, wheelCx: Float, wheelCy: Float, wheelR: Float, pressed: Zone?) {
        if (!buttons) { super.drawWheel(c, wheelCx, wheelCy, wheelR, pressed); return }
        Gfx.circle(c, wheelCx, wheelCy, wheelR * 1.03f, 0xFFB8B6AB.toInt())
        Gfx.radial(c, wheelCx, wheelCy, wheelR, 0xFFF4F2EA.toInt(), 0xFFDAD8CD.toInt())
        Gfx.circleStroke(c, wheelCx, wheelCy, wheelR, 0xFF9E9C90.toInt(), 1.5f)
        val label = 0xFF6B6A62.toInt()
        val br = wheelR * 0.17f; val d = wheelR * 0.78f
        fun button(z: Zone, x: Float, y: Float) {
            Gfx.circle(c, x, y + 1.5f, br, 0x33000000)
            Gfx.radial(c, x, y, br * 1.3f, 0xFFFFFFFF.toInt(), if (pressed == z) 0xFFB5B3A8.toInt() else 0xFFDEDCD1.toInt(), br, y - br * 0.3f)
            Gfx.circleStroke(c, x, y, br, 0xFF9E9C90.toInt(), 1f)
        }
        button(Zone.MENU, wheelCx, wheelCy - d); button(Zone.NEXT, wheelCx + d, wheelCy)
        button(Zone.PREV, wheelCx - d, wheelCy); button(Zone.PLAY_PAUSE, wheelCx, wheelCy + d)
        val h = br * 0.8f
        Gfx.text(c, "MENU", wheelCx, wheelCy - d + br * 0.28f, br * 0.62f, label, Gfx.SANS_BOLD, Paint.Align.CENTER)
        Gfx.triangle(c, wheelCx + d - h * 0.6f, wheelCy, h, label); Gfx.triangle(c, wheelCx + d + h * 0.05f, wheelCy, h, label)
        Gfx.triangle(c, wheelCx - d + h * 0.6f, wheelCy, h, label, false); Gfx.triangle(c, wheelCx - d - h * 0.05f, wheelCy, h, label, false)
        Gfx.triangle(c, wheelCx - h * 1.15f, wheelCy + d, h, label); Gfx.pause(c, wheelCx + h * 0.1f, wheelCy + d, h * 0.9f, label)
        val cr = wheelR * 0.36f
        Gfx.circle(c, wheelCx, wheelCy + 1.5f, cr, 0x33000000)
        Gfx.radial(c, wheelCx, wheelCy, cr * 1.3f, 0xFFFFFFFF.toInt(), if (pressed == Zone.CENTER) 0xFFB5B3A8.toInt() else 0xFFDEDCD1.toInt(), cr, wheelCy - cr * 0.3f)
        Gfx.circleStroke(c, wheelCx, wheelCy, cr, 0xFF9E9C90.toInt(), 1.2f)
    }

    override fun drawScreen(c: Canvas, page: Page, ctx: DrawCtx) {
        when (page) {
            is ListPage -> drawList(c, page, ctx)
            is NowPlayingPage -> drawNowPlaying(c, ctx)
        }
    }

    private fun t(c: Canvas, s: String, x: Float, y: Float, color: Int = INK, align: Paint.Align = Paint.Align.LEFT, maxW: Float = Float.MAX_VALUE) =
        Gfx.text(c, s, x, y, FONT, color, Gfx.MONO_BOLD, align, maxW, aa = false)

    private fun header(c: Canvas, title: String, ctx: DrawCtx) {
        Gfx.rect(c, 0f, 0f, W, headerH, LCD)
        t(c, title, W / 2, 12f, INK, Paint.Align.CENTER, 100f)
        Gfx.line(c, 0f, headerH - 1f, W, headerH - 1f, INK, 1f)
        if (ctx.status.playing) Gfx.triangle(c, 4f, headerH / 2 - 0.5f, 7f, INK)
        Gfx.battery(c, W - 3f, headerH / 2 - 0.5f, ctx.status.battery, ctx.status.charging, INK, INK)
        ctx.status.clock?.let { t(c, it, W - 26f, 12f, INK, Paint.Align.RIGHT) }
    }

    private fun drawList(c: Canvas, page: ListPage, ctx: DrawCtx) {
        Gfx.rect(c, 0f, 0f, W, H, LCD)
        val rows = rowsVisible()
        val n = page.source.size
        c.save(); c.clipRect(0f, headerH, W, H)
        val first = floor(page.scroll).toInt().coerceAtLeast(0)
        val last = minOf(n - 1, ceil(page.scroll + rows).toInt())
        for (i in first..last) {
            val item = page.source[i]
            val y = headerH + (i - page.scroll) * rowH
            val sel = i == page.selected && !page.free
            val fg = if (sel) LCD else INK
            if (sel) Gfx.rect(c, 0f, y, W - 6f, y + rowH, INK)
            var x = 3f
            if (item.checked != null) { if (item.checked.invoke()) t(c, "✓", x, y + 11f, fg); x = 12f }
            var right = W - 10f
            if (item.chevron) { t(c, ">", right, y + 11f, fg, Paint.Align.RIGHT); right -= 9f }
            val v = item.value?.invoke() ?: if (page.kind == Kind.MEMOS) item.subtitle else null
            if (v != null) { val vw = t(c, v, right, y + 11f, fg, Paint.Align.RIGHT, 70f); right -= vw + 4f }
            t(c, item.title, x, y + 11f, fg, maxW = right - x)
        }
        c.restore()
        // scrollbar
        if (n > rows) {
            Gfx.rect(c, W - 5f, headerH + 1f, W - 1f, H - 1f, 0xFF9EA68F.toInt())
            val trackH = H - headerH - 2f
            val th = (trackH * rows / n).coerceAtLeast(6f)
            val ty = headerH + 1f + (trackH - th) * (page.scroll / (n - rows).toFloat().coerceAtLeast(1f))
            Gfx.rect(c, W - 5f, ty, W - 1f, ty + th, INK)
        }
        header(c, page.title, ctx)
    }

    private fun drawNowPlaying(c: Canvas, ctx: DrawCtx) {
        val np = ctx.np
        Gfx.rect(c, 0f, 0f, W, H, LCD)
        header(c, "Now Playing", ctx)
        val tr = np.track
        val dy = ((H - 128f) / 2f).coerceAtLeast(0f)   // tall screens: centre the block instead of leaving the bottom empty
        if (tr != null) {
            t(c, tr.title, W / 2, 44f + dy, INK, Paint.Align.CENTER, W - 8f)
            if (tr.artist.isNotEmpty()) t(c, tr.artist, W / 2, 60f + dy, INK, Paint.Align.CENTER, W - 8f)
            if (tr.album.isNotEmpty()) t(c, tr.album, W / 2, 76f + dy, INK, Paint.Align.CENTER, W - 8f)
        }
        if (np.count > 1) t(c, "${np.index + 1} of ${np.count}", W / 2, 30f + dy, INK, Paint.Align.CENTER)
        val by = 100f + dy
        if (np.hudActive && np.mode == NowPlayingPage.Mode.VOLUME) {
            Gfx.speaker(c, 6f, by + 2f, 10f, INK, false)
            blocks(c, 22f, W - 8f, by, np.volume)
        } else {
            val frac = if (np.durMs > 0) np.posMs.toFloat() / np.durMs else 0f
            Gfx.rect(c, 8f, by - 3f, W - 8f, by + 3f, INK)
            Gfx.rect(c, 9f, by - 2f, W - 9f, by + 2f, LCD)
            Gfx.rect(c, 9f, by - 2f, 9f + (W - 18f) * frac.coerceIn(0f, 1f), by + 2f, INK)
            t(c, MenuBuilder.fmtTime(np.posMs), 8f, by + 16f)
            t(c, "-" + MenuBuilder.fmtTime((np.durMs - np.posMs).coerceAtLeast(0)), W - 8f, by + 16f, INK, Paint.Align.RIGHT)
        }
    }

    private fun blocks(c: Canvas, l: Float, r: Float, cy: Float, frac: Float) {
        val n = 16; val w = (r - l) / n
        val on = (frac.coerceIn(0f, 1f) * n).toInt()
        for (i in 0 until n) {
            val x = l + i * w
            if (i < on) Gfx.rect(c, x + 1f, cy - 4f, x + w - 1f, cy + 4f, INK)
            else Gfx.rect(c, x + 1f, cy + 2f, x + w - 1f, cy + 4f, INK)
        }
    }
}
