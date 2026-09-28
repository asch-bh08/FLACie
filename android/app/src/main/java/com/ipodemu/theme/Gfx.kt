package com.ipodemu.theme

import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.LinearGradient
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RadialGradient
import android.graphics.RectF
import android.graphics.Shader
import android.graphics.Typeface
import com.ipodemu.nav.Icon
import kotlin.math.cos
import kotlin.math.sin

/** Small immediate-mode drawing kit shared by all themes. UI-thread only (shared Paint). */
object Gfx {
    val p = Paint(Paint.ANTI_ALIAS_FLAG)
    private val r = RectF()
    private val path = Path()
    private val src = android.graphics.Rect()

    val SANS: Typeface = Typeface.create("sans-serif", Typeface.NORMAL)
    val SANS_MED: Typeface = Typeface.create("sans-serif-medium", Typeface.NORMAL)
    val SANS_BOLD: Typeface = Typeface.create("sans-serif", Typeface.BOLD)
    val MONO_BOLD: Typeface = Typeface.create("monospace", Typeface.BOLD)

    private fun reset(color: Int, style: Paint.Style = Paint.Style.FILL) {
        p.shader = null; p.color = color; p.style = style; p.strokeWidth = 1f
        p.isAntiAlias = true; p.strokeCap = Paint.Cap.BUTT; p.strokeJoin = Paint.Join.MITER
    }

    fun rect(c: Canvas, l: Float, t: Float, rr: Float, b: Float, color: Int) {
        reset(color); c.drawRect(l, t, rr, b, p)
    }

    fun rrect(c: Canvas, l: Float, t: Float, rr: Float, b: Float, rad: Float, color: Int) {
        reset(color); r.set(l, t, rr, b); c.drawRoundRect(r, rad, rad, p)
    }

    fun rrectStroke(c: Canvas, l: Float, t: Float, rr: Float, b: Float, rad: Float, color: Int, w: Float) {
        reset(color, Paint.Style.STROKE); p.strokeWidth = w; r.set(l, t, rr, b); c.drawRoundRect(r, rad, rad, p)
    }

    // Gradients are cached at the origin and positioned with a canvas translate, so scrolling lists allocate nothing per frame.
    private val lgCache = HashMap<Long, LinearGradient>()
    private val rgCache = HashMap<Long, RadialGradient>()

    private fun key(a: Float, b: Int, c: Int): Long = (a.toRawBits().toLong() * 31 + b) * 31 + c

    fun vgrad(c: Canvas, l: Float, t: Float, rr: Float, b: Float, top: Int, bottom: Int, rad: Float = 0f) {
        val h = b - t
        val k = key(h, top, bottom)
        val sh = lgCache.getOrPut(k) { if (lgCache.size > 300) lgCache.clear(); LinearGradient(0f, 0f, 0f, h, top, bottom, Shader.TileMode.CLAMP) }
        reset(top); p.shader = sh
        r.set(l, 0f, rr, h)
        c.save(); c.translate(0f, t)
        if (rad > 0f) c.drawRoundRect(r, rad, rad, p) else c.drawRect(r, p)
        c.restore()
        p.shader = null
    }

    fun circle(c: Canvas, cx: Float, cy: Float, rad: Float, color: Int) { reset(color); c.drawCircle(cx, cy, rad, p) }

    fun circleStroke(c: Canvas, cx: Float, cy: Float, rad: Float, color: Int, w: Float) {
        reset(color, Paint.Style.STROKE); p.strokeWidth = w; c.drawCircle(cx, cy, rad, p)
    }

    fun radial(c: Canvas, cx: Float, cy: Float, rad: Float, inner: Int, outer: Int, drawR: Float = rad, gy: Float = cy) {
        val sh = rgCache.getOrPut(key(rad, inner, outer)) { if (rgCache.size > 300) rgCache.clear(); RadialGradient(0f, 0f, rad, inner, outer, Shader.TileMode.CLAMP) }
        reset(inner); p.shader = sh
        c.save(); c.translate(cx, gy)
        c.drawCircle(0f, cy - gy, drawR, p)
        c.restore()
        p.shader = null
    }

    fun line(c: Canvas, x0: Float, y0: Float, x1: Float, y1: Float, color: Int, w: Float = 1f) {
        reset(color, Paint.Style.STROKE); p.strokeWidth = w; c.drawLine(x0, y0, x1, y1, p)
    }

    /** Draws text with its baseline at [y]. Returns drawn width. */
    fun text(c: Canvas, s: String, x: Float, y: Float, size: Float, color: Int,
             face: Typeface = SANS, align: Paint.Align = Paint.Align.LEFT, maxW: Float = Float.MAX_VALUE,
             aa: Boolean = true): Float {
        reset(color); p.typeface = face; p.textSize = size; p.textAlign = align; p.isAntiAlias = aa
        val t = if (maxW < Float.MAX_VALUE) ellipsize(s, maxW) else s
        c.drawText(t, x, y, p)
        return p.measureText(t)
    }

    /** Wraps [s] to at most [maxLines] lines (ellipsized), baseline of the first line at [y]. Returns height used. */
    fun textLines(c: Canvas, s: String, x: Float, y: Float, size: Float, color: Int, face: Typeface,
                  maxW: Float, maxLines: Int, lineH: Float): Float {
        reset(color); p.typeface = face; p.textSize = size; p.textAlign = Paint.Align.LEFT
        var rest = s.trim(); var n = 0
        while (rest.isNotEmpty() && n < maxLines) {
            var cnt = p.breakText(rest, true, maxW, null)
            val line: String
            if (cnt >= rest.length) { line = rest; rest = "" }
            else if (n == maxLines - 1) { line = ellipsize(rest, maxW); rest = "" }
            else {
                val sp = rest.lastIndexOf(' ', cnt)
                if (sp > 0) cnt = sp
                line = rest.substring(0, cnt).trimEnd(); rest = rest.substring(cnt).trimStart()
            }
            c.drawText(line, x, y + n * lineH, p); n++
        }
        return n * lineH
    }

    fun measure(s: String, size: Float, face: Typeface = SANS): Float {
        p.typeface = face; p.textSize = size; return p.measureText(s)
    }

    /** Assumes p already holds the desired typeface/size. */
    private fun ellipsize(s: String, maxW: Float): String {
        if (p.measureText(s) <= maxW) return s
        val ell = "…"
        val n = p.breakText(s, true, (maxW - p.measureText(ell)).coerceAtLeast(0f), null)
        return s.substring(0, n).trimEnd() + ell
    }

    fun chevron(c: Canvas, cx: Float, cy: Float, h: Float, color: Int, w: Float = 2f) {
        reset(color, Paint.Style.STROKE); p.strokeWidth = w; p.strokeCap = Paint.Cap.ROUND; p.strokeJoin = Paint.Join.ROUND
        path.reset(); path.moveTo(cx - h * 0.3f, cy - h / 2); path.lineTo(cx + h * 0.3f, cy); path.lineTo(cx - h * 0.3f, cy + h / 2)
        c.drawPath(path, p)
    }

    fun check(c: Canvas, cx: Float, cy: Float, s: Float, color: Int, w: Float = 2f) {
        reset(color, Paint.Style.STROKE); p.strokeWidth = w; p.strokeCap = Paint.Cap.ROUND; p.strokeJoin = Paint.Join.ROUND
        path.reset(); path.moveTo(cx - s * 0.5f, cy); path.lineTo(cx - s * 0.1f, cy + s * 0.4f); path.lineTo(cx + s * 0.5f, cy - s * 0.4f)
        c.drawPath(path, p)
    }

    fun triangle(c: Canvas, x: Float, cy: Float, h: Float, color: Int, right: Boolean = true) {
        reset(color); path.reset()
        if (right) { path.moveTo(x, cy - h / 2); path.lineTo(x + h * 0.85f, cy); path.lineTo(x, cy + h / 2) }
        else { path.moveTo(x, cy - h / 2); path.lineTo(x - h * 0.85f, cy); path.lineTo(x, cy + h / 2) }
        path.close(); c.drawPath(path, p)
    }

    fun pause(c: Canvas, x: Float, cy: Float, h: Float, color: Int) {
        val w = h * 0.3f
        rect(c, x, cy - h / 2, x + w, cy + h / 2, color); rect(c, x + w * 1.7f, cy - h / 2, x + w * 2.7f, cy + h / 2, color)
    }

    fun speaker(c: Canvas, x: Float, cy: Float, h: Float, color: Int, waves: Boolean = true) {
        reset(color); path.reset()
        path.moveTo(x, cy - h * 0.18f); path.lineTo(x + h * 0.25f, cy - h * 0.18f); path.lineTo(x + h * 0.6f, cy - h * 0.5f)
        path.lineTo(x + h * 0.6f, cy + h * 0.5f); path.lineTo(x + h * 0.25f, cy + h * 0.18f); path.lineTo(x, cy + h * 0.18f)
        path.close(); c.drawPath(path, p)
        if (waves) {
            reset(color, Paint.Style.STROKE); p.strokeWidth = h * 0.09f; p.strokeCap = Paint.Cap.ROUND
            for (i in 1..2) { r.set(x + h * 0.5f - i * h * 0.05f, cy - i * h * 0.22f, x + h * 0.5f + i * h * 0.3f, cy + i * h * 0.22f); c.drawArc(r, -50f, 100f, false, p) }
        }
    }

    fun battery(c: Canvas, right: Float, cy: Float, level: Int, charging: Boolean, outline: Int, fill: Int) {
        val w = 20f; val h = 9f; val l = right - w - 2f
        rrectStroke(c, l, cy - h / 2, l + w, cy + h / 2, 1.5f, outline, 1f)
        rect(c, l + w, cy - 2f, l + w + 2f, cy + 2f, outline)
        val fw = (w - 3f) * level.coerceIn(0, 100) / 100f
        if (fw > 0) rect(c, l + 1.5f, cy - h / 2 + 1.5f, l + 1.5f + fw, cy + h / 2 - 1.5f, fill)
        if (charging) triangle(c, l + w / 2 - 2f, cy, 5f, outline, true)
    }

    /** Cover-art tile; draws a neutral placeholder when [bmp] is null. */
    fun art(c: Canvas, bmp: Bitmap?, l: Float, t: Float, rr: Float, b: Float, placeholder: Int, glyph: Int) {
        if (bmp == null) {
            rect(c, l, t, rr, b, placeholder)
            glyph(c, Icon.MUSIC, (l + rr) / 2, (t + b) / 2, (rr - l) * 0.5f, glyph)
            return
        }
        // center-crop to square-ish destination
        val dw = rr - l; val dh = b - t
        val sa = bmp.width.toFloat() / bmp.height; val da = dw / dh
        if (sa > da) { val w = (bmp.height * da).toInt(); src.set((bmp.width - w) / 2, 0, (bmp.width + w) / 2, bmp.height) }
        else { val h = (bmp.width / da).toInt(); src.set(0, (bmp.height - h) / 2, bmp.width, (bmp.height + h) / 2) }
        reset(0xFFFFFFFF.toInt()); p.isFilterBitmap = true
        r.set(l, t, rr, b); c.drawBitmap(bmp, src, r, p)
    }

    /** Menu-preview glyphs (flat vector approximations). */
    fun glyph(c: Canvas, icon: Icon, cx: Float, cy: Float, s: Float, color: Int) {
        reset(color)
        when (icon) {
            Icon.VOICE -> {
                rrect(c, cx - s * 0.16f, cy - s * 0.42f, cx + s * 0.16f, cy + s * 0.08f, s * 0.16f, color)
                reset(color, Paint.Style.STROKE); p.strokeWidth = s * 0.07f; p.strokeCap = Paint.Cap.ROUND
                r.set(cx - s * 0.28f, cy - s * 0.22f, cx + s * 0.28f, cy + s * 0.28f); c.drawArc(r, 0f, 180f, false, p)
                c.drawLine(cx, cy + s * 0.28f, cx, cy + s * 0.42f, p); c.drawLine(cx - s * 0.14f, cy + s * 0.42f, cx + s * 0.14f, cy + s * 0.42f, p)
            }
            Icon.SETTINGS -> {
                for (i in 0 until 8) {
                    val a = Math.toRadians(i * 45.0)
                    reset(color, Paint.Style.STROKE); p.strokeWidth = s * 0.12f
                    c.drawLine(cx + cos(a).toFloat() * s * 0.26f, cy + sin(a).toFloat() * s * 0.26f,
                        cx + cos(a).toFloat() * s * 0.4f, cy + sin(a).toFloat() * s * 0.4f, p)
                }
                circle(c, cx, cy, s * 0.3f, color); circle(c, cx, cy, s * 0.12f, 0x66000000)
            }
            Icon.SHUFFLE -> {
                reset(color, Paint.Style.STROKE); p.strokeWidth = s * 0.08f; p.strokeCap = Paint.Cap.ROUND; p.strokeJoin = Paint.Join.ROUND
                path.reset(); path.moveTo(cx - s * 0.4f, cy - s * 0.22f); path.cubicTo(cx, cy - s * 0.22f, cx, cy + s * 0.22f, cx + s * 0.3f, cy + s * 0.22f)
                c.drawPath(path, p)
                path.reset(); path.moveTo(cx - s * 0.4f, cy + s * 0.22f); path.cubicTo(cx, cy + s * 0.22f, cx, cy - s * 0.22f, cx + s * 0.3f, cy - s * 0.22f)
                c.drawPath(path, p)
                triangle(c, cx + s * 0.3f, cy - s * 0.22f, s * 0.2f, color); triangle(c, cx + s * 0.3f, cy + s * 0.22f, s * 0.2f, color)
            }
            Icon.NOW_PLAYING, Icon.PLAYLISTS -> triangle(c, cx - s * 0.18f, cy, s * 0.5f, color)
            Icon.EXTRAS, Icon.BROWSE -> for (i in -1..1) for (j in -1..1) circle(c, cx + i * s * 0.24f, cy + j * s * 0.24f, s * 0.07f, color)
            else -> { // music note
                circle(c, cx - s * 0.2f, cy + s * 0.25f, s * 0.13f, color); circle(c, cx + s * 0.2f, cy + s * 0.15f, s * 0.13f, color)
                rect(c, cx - s * 0.09f, cy - s * 0.3f, cx - s * 0.05f, cy + s * 0.25f, color)
                rect(c, cx + s * 0.31f, cy - s * 0.4f, cx + s * 0.35f, cy + s * 0.15f, color)
                reset(color); path.reset()
                path.moveTo(cx - s * 0.09f, cy - s * 0.3f); path.lineTo(cx + s * 0.35f, cy - s * 0.42f)
                path.lineTo(cx + s * 0.35f, cy - s * 0.3f); path.lineTo(cx - s * 0.09f, cy - s * 0.18f); path.close(); c.drawPath(path, p)
            }
        }
    }

    fun rectF(l: Float, t: Float, rr: Float, b: Float): RectF = RectF(l, t, rr, b)

    fun withAlpha(color: Int, a: Float): Int = (color and 0x00FFFFFF) or ((a.coerceIn(0f, 1f) * 255).toInt() shl 24)
}
