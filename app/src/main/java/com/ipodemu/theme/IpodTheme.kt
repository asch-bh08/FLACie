package com.ipodemu.theme

import android.graphics.Canvas
import android.graphics.RectF
import com.ipodemu.input.Key
import com.ipodemu.input.Zone
import com.ipodemu.nav.Icon
import com.ipodemu.library.ArtCache
import com.ipodemu.library.Track
import com.ipodemu.nav.ListPage
import com.ipodemu.nav.MenuBuilder
import com.ipodemu.nav.NowPlayingPage
import com.ipodemu.nav.Page
import com.ipodemu.playback.FileSpecs
import kotlin.math.floor

/** Logical screen resolution of one iPod generation; the view scales it to fill the display. */
class DeviceSpec(
    val screenW: Int,
    val screenH: Int,
)

class StatusInfo(val playing: Boolean, val battery: Int, val charging: Boolean, val clock: String? = null)

class NowPlayingModel(
    val track: Track?,
    val playing: Boolean,
    val posMs: Long,
    val durMs: Long,
    val mode: NowPlayingPage.Mode,
    val volume: Float,
    val index: Int,
    val count: Int,
    val shuffle: Boolean,
    val repeat: Int,
    /** True when the volume/scrub HUD should be shown instead of the idle progress bar. */
    val hudActive: Boolean,
)

class DrawCtx(
    val art: ArtCache, val status: StatusInfo, val np: NowPlayingModel, val specs: FileSpecs? = null,
    val upNext: List<Pair<Int, Track>> = emptyList(),
)

/** Geometry shared by the queue card renderer and the view's touch hit-testing (display px). */
object CardLayout {
    const val HEADER = 44f
    const val ROW = 55f
    /** Height reserved at the bottom of the file-info card for the Shuffle pill. */
    const val PILL_AREA = 64f
}

/** Everything era-specific lives behind this interface: chrome, screen UI, and menu tree. */
interface IpodTheme {
    val id: String
    val displayName: String
    val device: DeviceSpec

    /** Logical screen width in theme px; set by the view so the screen can fill the display at any aspect ratio. */
    var logicalW: Float

    /** Logical screen height; equals device.screenH in the wheel layout, taller in fullscreen. */
    var logicalH: Float

    /** The menu structure for this generation, composed from shared MenuBuilder pages. */
    fun buildRoot(b: MenuBuilder): ListPage

    /** Faceplate behind screen and wheel: [panel] is the whole inset body, [bezel] the screen surround. */
    fun drawFrame(c: Canvas, panel: RectF, bezel: RectF)

    /** Non-null for the "device" skins: a physical iPod body centred on the display instead of the flat faceplate. */
    val body: BodySpec?
    fun drawBody(c: Canvas, bodyRect: RectF, screen: RectF)
    /** Glass reflection over the screen (device skins). */
    fun drawGlass(c: Canvas, screen: RectF)

    /** Click-wheel artwork (the only chrome; no device body). */
    fun drawWheel(c: Canvas, cx: Float, cy: Float, r: Float, pressed: Zone?)

    /** Draw a page at logical resolution: canvas is already scaled/clipped to (0,0,screenW,screenH). */
    fun drawScreen(c: Canvas, page: Page, ctx: DrawCtx)

    /** Desired fractional first-visible row so the selection stays on screen. */
    fun targetScroll(page: ListPage): Float

    // Geometry for touch hit-testing, in logical screen px.
    val listTop: Float
    val rowPitch: Float
    fun listRight(page: ListPage): Float = logicalW
    fun maxScroll(page: ListPage): Float

    /** Touch key beside the wheel (canvas is in display px). */
    fun drawKey(c: Canvas, r: RectF, key: Key, pressed: Boolean, on: Boolean, enabled: Boolean)

    /** Info cards in the space beside the wheel (canvas is in display px). */
    fun drawQueueCard(c: Canvas, r: RectF, ctx: DrawCtx)
    fun drawSpecsCard(c: Canvas, r: RectF, ctx: DrawCtx)

    val backdrop: Int
}

/** Shared scroll maths and touch-key styling for row-list themes. */
abstract class ListTheme : IpodTheme {
    protected abstract val chrome: Chrome

    override val backdrop: Int get() = chrome.backdrop
    override val body: BodySpec? get() = chrome.body

    override fun drawFrame(c: Canvas, panel: RectF, bezel: RectF) {
        Gfx.vgrad(c, panel.left, panel.top, panel.right, panel.bottom, chrome.flatTop, chrome.flatBottom)
        Gfx.rect(c, bezel.left, bezel.bottom, bezel.right, bezel.bottom + 3f, chrome.flatLine)
        Gfx.rect(c, bezel.left, bezel.bottom + 3f, bezel.right, bezel.bottom + 5f, 0x33FFFFFF)
    }

    override fun drawBody(c: Canvas, bodyRect: RectF, screen: RectF) {
        val b = chrome.body ?: return
        val rad = bodyRect.width() * b.radius
        Gfx.rrect(c, bodyRect.left - 2f, bodyRect.top - 2f, bodyRect.right + 2f, bodyRect.bottom + 4f, rad + 2f, 0x66000000)
        Gfx.vgrad(c, bodyRect.left, bodyRect.top, bodyRect.right, bodyRect.bottom, b.top, b.bottom, rad)
        Gfx.vgrad(c, bodyRect.left, bodyRect.top, bodyRect.right, bodyRect.top + bodyRect.height() * 0.45f, b.sheen, 0x00FFFFFF, rad)
        Gfx.rrectStroke(c, bodyRect.left + 1.5f, bodyRect.top + 1.5f, bodyRect.right - 1.5f, bodyRect.bottom - 1.5f, rad, b.highlight, 2f)
        Gfx.rrectStroke(c, bodyRect.left, bodyRect.top, bodyRect.right, bodyRect.bottom, rad, b.edge, 1.5f)
        val m = bodyRect.width() * b.bezelInset
        Gfx.rrect(c, screen.left - m, screen.top - m, screen.right + m, screen.bottom + m, m * 1.6f, b.bezel)
    }

    override fun drawGlass(c: Canvas, screen: RectF) {
        Gfx.vgrad(c, screen.left, screen.top, screen.right, screen.top + screen.height() * 0.42f, 0x22FFFFFF, 0x00FFFFFF)
    }

    override fun drawWheel(c: Canvas, cx: Float, cy: Float, r: Float, pressed: Zone?) =
        WheelChrome.draw(c, cx, cy, r, chrome.wheel, pressed)

    override var logicalW: Float = 320f
    override var logicalH: Float = 240f
    protected abstract val headerH: Float
    protected abstract val rowH: Float
    protected open val footerH = 0f

    override val listTop: Float get() = headerH
    override val rowPitch: Float get() = rowH

    protected open val keyTop = 0xFFFFFFFF.toInt()
    protected open val keyBottom = 0xFFD3D6DC.toInt()
    protected open val keyEdge = 0xFF9EA1A9.toInt()
    protected open val keyInk = 0xFF5F636D.toInt()
    protected open val keyOn = 0xFF1F6ADB.toInt()

    protected fun rowsVisible(): Int = floor((logicalH - headerH - footerH) / rowH).toInt()

    override fun maxScroll(page: ListPage): Float =
        (page.source.size - (logicalH - headerH - footerH) / rowH).coerceAtLeast(0f)

    override fun targetScroll(page: ListPage): Float {
        if (page.free) return page.scroll
        val rows = rowsVisible()
        val max = (page.source.size - rows).coerceAtLeast(0).toFloat()
        var t = page.scroll
        if (page.selected < t) t = page.selected.toFloat()
        else if (page.selected > t + rows - 1) t = (page.selected - rows + 1).toFloat()
        return t.coerceIn(0f, max)
    }

    // Card palette (dark glass by default; themes override).
    protected open val cardTop = 0xFF2E3138.toInt()
    protected open val cardBottom = 0xFF0C0D10.toInt()
    protected open val cardInk = 0xFFFFFFFF.toInt()
    protected open val cardDim = 0xFF9BA1AE.toInt()
    protected open val cardAccent = 0xFF63A9FF.toInt()
    protected open val cardOnInk = 0xFFFFFFFF.toInt()

    /** Small pill button (Shuffle). */
    /** Shuffle pill, drawn inside the file-info card: outlined when off, filled with the accent when on. */
    override fun drawKey(c: Canvas, r: RectF, key: Key, pressed: Boolean, on: Boolean, enabled: Boolean) {
        val rad = r.height() * 0.5f
        val fill = when { on -> cardAccent; pressed -> 0x40FFFFFF; else -> 0x1FFFFFFF }
        Gfx.rrect(c, r.left, r.top, r.right, r.bottom, rad, fill)
        Gfx.rrectStroke(c, r.left, r.top, r.right, r.bottom, rad, if (on) cardAccent else cardDim, 1.5f)
        val ink = if (on) cardOnInk else cardInk
        val size = r.height() * 0.36f
        val label = if (on) "Shuffle On" else "Shuffle Off"
        val iconW = r.height() * 0.55f
        val tw = Gfx.measure(label, size, Gfx.SANS_MED)
        val x0 = r.centerX() - (iconW + 8f + tw) / 2
        Gfx.glyph(c, Icon.SHUFFLE, x0 + iconW / 2, r.centerY(), iconW * 1.5f, ink)
        Gfx.text(c, label, x0 + iconW + 8f, r.centerY() + size * 0.36f, size, ink, Gfx.SANS_MED)
    }

    private fun card(c: Canvas, r: RectF) {
        val rad = 22f
        Gfx.rrect(c, r.left, r.top + 3f, r.right, r.bottom + 3f, rad, 0x33000000)
        Gfx.vgrad(c, r.left, r.top, r.right, r.bottom, cardTop, cardBottom, rad)
        Gfx.rrectStroke(c, r.left, r.top, r.right, r.bottom, rad, 0x55000000, 2f)
        Gfx.vgrad(c, r.left + 3f, r.top + 3f, r.right - 3f, r.top + r.height() * 0.28f, 0x22FFFFFF, 0x00FFFFFF, rad - 3f)
    }

    override fun drawQueueCard(c: Canvas, r: RectF, ctx: DrawCtx) {
        card(c, r)
        val pad = 14f
        Gfx.text(c, "UP NEXT", r.left + pad, r.top + 30f, 13.5f, cardAccent, Gfx.SANS_BOLD)
        val rows = ((r.height() - CardLayout.HEADER) / CardLayout.ROW).toInt()
        val q = ctx.upNext
        if (q.isEmpty()) {
            Gfx.text(c, if (ctx.np.track == null) "Queue is empty" else "End of queue", r.left + pad, r.top + CardLayout.HEADER + 26f, 15f, cardDim, Gfx.SANS, maxW = r.width() - pad * 2)
            return
        }
        q.take(rows).forEachIndexed { i, (_, t) ->
            val y0 = r.top + CardLayout.HEADER + i * CardLayout.ROW
            Gfx.line(c, r.left + pad, y0, r.right - pad, y0, 0x33FFFFFF, 1f)
            Gfx.text(c, t.title, r.left + pad, y0 + 24f, 16f, cardInk, Gfx.SANS_MED, maxW = r.width() - pad * 2)
            Gfx.text(c, t.artist.ifEmpty { t.album }, r.left + pad, y0 + 43f, 13f, cardDim, Gfx.SANS, maxW = r.width() - pad * 2)
        }
    }

    override fun drawSpecsCard(c: Canvas, r: RectF, ctx: DrawCtx) {
        card(c, r)
        val pad = 14f
        Gfx.text(c, "FILE INFO", r.left + pad, r.top + 30f, 13.5f, cardAccent, Gfx.SANS_BOLD)
        val rows = ctx.specs?.rows
        if (rows == null) {
            Gfx.text(c, "No file playing", r.left + pad, r.top + 64f, 15f, cardDim, Gfx.SANS, maxW = r.width() - pad * 2)
            return
        }
        val top = r.top + 48f
        val pitch = ((r.bottom - CardLayout.PILL_AREA - top) / rows.size).coerceAtMost(32f)
        rows.forEachIndexed { i, (k, v) ->
            val y = top + pitch * i + pitch * 0.68f
            Gfx.text(c, k, r.left + pad, y, 12.5f, cardDim, Gfx.SANS)
            Gfx.text(c, v, r.right - pad, y, 14.5f, cardInk, Gfx.SANS_MED, android.graphics.Paint.Align.RIGHT, maxW = r.width() * 0.55f)
        }
    }
}
