package com.ipodemu.theme

import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import com.ipodemu.input.Zone
import com.ipodemu.nav.Item
import com.ipodemu.nav.Kind
import com.ipodemu.nav.ListPage
import com.ipodemu.nav.MenuBuilder
import com.ipodemu.nav.NowPlayingPage
import com.ipodemu.nav.Page
import kotlin.math.ceil
import kotlin.math.floor

/**
 * iPod (5th generation, "Video") era: white polycarbonate body, full-width Helvetica-style lists with no
 * preview pane, thin header, Extras submenu holding Voice Memos.
 */
class ClassicTheme(
    override val id: String,
    override val displayName: String,
    override val chrome: Chrome,
    override val device: DeviceSpec,
) : ListTheme() {
    override val cardTop = 0xFF3B3E44.toInt()
    override val cardBottom = 0xFF17181B.toInt()
    override val cardAccent = 0xFF5C9CF0.toInt()
    override val headerH = 22f
    override val rowH = 24f

    private val W: Float get() = logicalW
    private val H: Float get() = logicalH
    private val BLUE_TOP = 0xFF4C90EC.toInt()
    private val BLUE_BOT = 0xFF1B55CC.toInt()

    override fun buildRoot(b: MenuBuilder): ListPage = b.rootPage {
        b.statusItems() + listOf(
            b.musicItem { musicMenu(b) },
            com.ipodemu.nav.Item("Extras", icon = com.ipodemu.nav.Icon.EXTRAS) { nav ->
                nav.push(b.page("Extras") { listOf(b.voiceMemosItem()) })
            },
            b.settingsItem(),
            b.shuffleItem(),
        ) + b.nowPlayingItems()
    }

    private fun musicMenu(b: MenuBuilder) = b.page("Music") {
        listOf(b.playlistsItem(), b.artistsItem(), b.albumsItem(), b.songsItem(), b.genresItem())
    }

    override fun drawScreen(c: Canvas, page: Page, ctx: DrawCtx) {
        when (page) {
            is ListPage -> drawList(c, page, ctx)
            is NowPlayingPage -> drawNowPlaying(c, ctx)
        }
    }

    private fun header(c: Canvas, title: String, ctx: DrawCtx) {
        Gfx.vgrad(c, 0f, 0f, W, headerH, 0xFFFFFFFF.toInt(), 0xFFCFD2D8.toInt())
        Gfx.line(c, 0f, headerH - 0.5f, W, headerH - 0.5f, 0xFF8E9299.toInt(), 1f)
        Gfx.text(c, title, W / 2, 16f, 13f, 0xFF000000.toInt(), Gfx.SANS_BOLD, Paint.Align.CENTER, maxW = 210f)
        if (ctx.status.playing) Gfx.triangle(c, 8f, headerH / 2, 9f, 0xFF000000.toInt())
        Gfx.battery(c, W - 6f, headerH / 2, ctx.status.battery, ctx.status.charging, 0xFF000000.toInt(), 0xFF000000.toInt())
        ctx.status.clock?.let { Gfx.text(c, it, W - 32f, 16f, 12f, 0xFF000000.toInt(), Gfx.SANS, Paint.Align.RIGHT) }
    }

    private fun drawList(c: Canvas, page: ListPage, ctx: DrawCtx) {
        Gfx.rect(c, 0f, 0f, W, H, 0xFFFFFFFF.toInt())
        val listW = if (hasArtPane(page)) W * 0.55f else W
        val rows = rowsVisible()
        val n = page.source.size
        c.save()
        c.clipRect(0f, headerH, listW, H)
        val first = floor(page.scroll).toInt().coerceAtLeast(0)
        val last = minOf(n - 1, ceil(page.scroll + rows).toInt())
        for (i in first..last) drawRow(c, page, page.source[i], i == page.selected && !page.free, headerH + (i - page.scroll) * rowH, ctx, listW)
        c.restore()
        if (hasArtPane(page)) drawArtPane(c, page, ctx, listW)
        else if (page.kind == Kind.MENU && H > 300f) drawNowPlayingCard(c, ctx, headerH + n * rowH + 12f)
        header(c, page.title, ctx)
    }

    /** Tall screens (controller deployed): the space under the short main menu shows what's playing, artwork as big as fits. */
    private fun drawNowPlayingCard(c: Canvas, ctx: DrawCtx, y0: Float) {
        val t = ctx.np.track ?: return
        if (H - y0 < 110f) return
        Gfx.line(c, 10f, y0 - 6f, W - 10f, y0 - 6f, 0xFFD0D3D9.toInt(), 1f)
        val sz = minOf(W - 40f, H - y0 - 52f)
        val ax = (W - sz) / 2
        Gfx.art(c, ctx.art.get(t.artKey), ax, y0, ax + sz, y0 + sz, 0xFFE7E7EA.toInt(), 0xFFB5B5BB.toInt())
        Gfx.rrectStroke(c, ax, y0, ax + sz, y0 + sz, 0f, 0xFF9A9AA0.toInt(), 1f)
        Gfx.text(c, t.title, W / 2, y0 + sz + 18f, 14f, 0xFF000000.toInt(), Gfx.SANS_BOLD, Paint.Align.CENTER, maxW = W - 24f)
        val sub = listOf(t.artist, t.album).filter { it.isNotEmpty() }.joinToString("  -  ")
        if (sub.isNotEmpty()) Gfx.text(c, sub, W / 2, y0 + sz + 34f, 11.5f, 0xFF5A5A5F.toInt(), Gfx.SANS, Paint.Align.CENTER, maxW = W - 24f)
    }

    private fun drawRow(c: Canvas, page: ListPage, item: Item, sel: Boolean, y: Float, ctx: DrawCtx, w: Float) {
        val fg = if (sel) 0xFFFFFFFF.toInt() else 0xFF000000.toInt()
        if (sel) Gfx.vgrad(c, 0f, y, w, y + rowH, BLUE_TOP, BLUE_BOT)
        var x = 8f
        if (item.checked != null) { if (item.checked.invoke()) Gfx.check(c, 13f, y + rowH / 2, 8f, fg, 2f); x = 24f }
        var right = w - 8f
        if (item.chevron) { Gfx.chevron(c, right - 3f, y + rowH / 2, 10f, fg, 2f); right -= 14f }
        val v = item.value?.invoke() ?: if (page.kind == Kind.MEMOS) item.subtitle else null
        if (v != null) {
            val vw = Gfx.text(c, v, right, y + 17f, 12f, if (sel) 0xFFFFFFFF.toInt() else 0xFF5A5A5F.toInt(), Gfx.SANS, Paint.Align.RIGHT, maxW = w * 0.45f)
            right -= vw + 8f
        }
        Gfx.text(c, item.title, x, y + 17.5f, 15f, fg, Gfx.SANS, maxW = right - x)
    }

    override fun listRight(page: ListPage): Float = if (hasArtPane(page)) W * 0.55f else W

    private fun hasArtPane(p: ListPage) = p.kind == Kind.SONGS || p.kind == Kind.ALBUMS || p.kind == Kind.ARTISTS

    private fun drawArtPane(c: Canvas, page: ListPage, ctx: DrawCtx, l: Float) {
        Gfx.vgrad(c, l, headerH, W, H, 0xFFF4F5F8.toInt(), 0xFFD8DBE1.toInt())
        Gfx.line(c, l, headerH, l, H, 0xFF8E9299.toInt(), 1f)
        if (page.source.size == 0) return
        val item = page.source[page.selected.coerceIn(0, page.source.size - 1)]
        val pw = W - l
        val sz = minOf(pw - 22f, H - headerH - 66f)
        val ax = l + (pw - sz) / 2; val ay = headerH + 11f
        Gfx.art(c, ctx.art.get(item.artKey), ax, ay, ax + sz, ay + sz, 0xFFE3E3E6.toInt(), 0xFFB5B5BB.toInt())
        Gfx.rrectStroke(c, ax, ay, ax + sz, ay + sz, 0f, 0xFF6A6A70.toInt(), 1f)
        val cx = l + pw / 2; val mw = pw - 16f
        Gfx.text(c, item.title, cx, ay + sz + 19f, 13.5f, 0xFF000000.toInt(), Gfx.SANS_BOLD, Paint.Align.CENTER, maxW = mw)
        item.subtitle?.takeIf { it.isNotEmpty() }?.let { Gfx.text(c, it, cx, ay + sz + 34f, 11.5f, 0xFF1F5ED0.toInt(), Gfx.SANS, Paint.Align.CENTER, maxW = mw) }
        item.detail?.takeIf { it.isNotEmpty() }?.let { Gfx.text(c, it, cx, ay + sz + (if (item.subtitle.isNullOrEmpty()) 34f else 48f), 10.5f, 0xFF5A5A5F.toInt(), Gfx.SANS, Paint.Align.CENTER, maxW = mw) }
    }

    private fun drawNowPlaying(c: Canvas, ctx: DrawCtx) {
        val np = ctx.np
        Gfx.rect(c, 0f, 0f, W, H, 0xFFFFFFFF.toInt())
        header(c, "Now Playing", ctx)
        val t = np.track
        val by = H - 38f
        // square / portrait screens (controller deployed, phones): the artwork takes the freed height instead of leaving it blank
        val tall = H > 300f
        val ay = headerH + (if (tall) 10f else 14f)
        val asz = if (tall) minOf(W - 30f, by - 76f - ay) else maxOf(104f, minOf(H - headerH - 14f - 64f, W * 0.42f))
        val ax = if (tall) (W - asz) / 2 else 12f
        Gfx.art(c, ctx.art.get(t?.artKey), ax, ay, ax + asz, ay + asz, 0xFFE7E7EA.toInt(), 0xFFB5B5BB.toInt())
        Gfx.rrectStroke(c, ax, ay, ax + asz, ay + asz, 0f, 0xFF9A9AA0.toInt(), 1f)
        if (t != null) {
            if (tall) {
                val cx = W / 2
                Gfx.text(c, t.title, cx, by - 40f, 17f, 0xFF000000.toInt(), Gfx.SANS_BOLD, Paint.Align.CENTER, maxW = W - 24f)
                val line = listOf(t.artist, t.album).filter { it.isNotEmpty() }.joinToString("  -  ")
                if (line.isNotEmpty()) Gfx.text(c, line, cx, by - 19f, 13f, 0xFF000000.toInt(), Gfx.SANS, Paint.Align.CENTER, maxW = W - 24f)
                if (np.count > 1) Gfx.text(c, "${np.index + 1} of ${np.count}", W - 10f, headerH + 16f, 12f, 0xFF6A6A70.toInt(), Gfx.SANS, Paint.Align.RIGHT)
            } else {
                val tx = ax + asz + 12f; val tw = W - tx - 8f
                if (np.count > 1) Gfx.text(c, "${np.index + 1} of ${np.count}", tx, ay + 12f, 12f, 0xFF6A6A70.toInt())
                Gfx.text(c, t.title, tx, ay + 40f, 15f, 0xFF000000.toInt(), Gfx.SANS_BOLD, maxW = tw)
                if (t.artist.isNotEmpty()) Gfx.text(c, t.artist, tx, ay + 62f, 14f, 0xFF000000.toInt(), Gfx.SANS, maxW = tw)
                if (t.album.isNotEmpty()) Gfx.text(c, t.album, tx, ay + 82f, 14f, 0xFF000000.toInt(), Gfx.SANS, maxW = tw)
            }
        }
        if (np.hudActive && np.mode == NowPlayingPage.Mode.VOLUME) {
            bar(c, 14f, W - 14f, by, np.volume)
            Gfx.text(c, "Volume", W / 2, by + 20f, 11f, 0xFF000000.toInt(), Gfx.SANS, Paint.Align.CENTER)
        } else {
            val frac = if (np.durMs > 0) np.posMs.toFloat() / np.durMs else 0f
            bar(c, 14f, W - 14f, by, frac)
            if (np.hudActive) Gfx.triangle(c, 14f + (W - 28f) * frac.coerceIn(0f, 1f) - 4f, by + 12f, 7f, 0xFF000000.toInt(), false)
            Gfx.text(c, MenuBuilder.fmtTime(np.posMs), 14f, by + 22f, 11f, 0xFF000000.toInt())
            Gfx.text(c, "-" + MenuBuilder.fmtTime((np.durMs - np.posMs).coerceAtLeast(0)), W - 14f, by + 22f, 11f, 0xFF000000.toInt(), Gfx.SANS, Paint.Align.RIGHT)
        }
    }

    private fun bar(c: Canvas, l: Float, r: Float, cy: Float, frac: Float) {
        Gfx.rect(c, l, cy - 5f, r, cy + 5f, 0xFFFFFFFF.toInt())
        val fx = l + (r - l) * frac.coerceIn(0f, 1f)
        if (fx > l) Gfx.vgrad(c, l, cy - 5f, fx, cy + 5f, 0xFF5C9CF0.toInt(), 0xFF1F5ED0.toInt())
        Gfx.rrectStroke(c, l, cy - 5f, r, cy + 5f, 0f, 0xFF000000.toInt(), 1.2f)
    }
}
