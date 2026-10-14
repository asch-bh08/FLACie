package com.ipodemu.theme

import android.graphics.Canvas
import android.graphics.Paint
import com.ipodemu.nav.ListPage

/** Cover Flow for every wheel theme that has it (Classic, nano): a row of covers in perspective with reflections, the selected one in the middle. */
object CoverFlow {
    private val flowPaint = Paint(Paint.ANTI_ALIAS_FLAG or Paint.FILTER_BITMAP_FLAG)
    private val flowM = android.graphics.Matrix()
    private val flowPath = android.graphics.Path()

    /** A cover as a quad: the edge next to the middle is taller than the outer one, which gives the turned look; [flip] draws its dimmed reflection under the floor line. */
    private fun flowQuad(c: Canvas, bmp: android.graphics.Bitmap?, x0: Float, x1: Float, h0: Float, h1: Float, floorY: Float, flip: Boolean, alpha: Int) {
        // x0/h0 is the left edge, x1/h1 the right one; the cover stands on the floor line
        val y = { h: Float, top: Boolean -> if (!flip) (if (top) floorY - h else floorY) else (if (top) floorY else floorY + h) }
        val dst = floatArrayOf(x0, y(h0, true), x1, y(h1, true), x1, y(h1, false), x0, y(h0, false))
        if (bmp == null) {
            flowPath.reset(); flowPath.moveTo(dst[0], dst[1]); flowPath.lineTo(dst[2], dst[3]); flowPath.lineTo(dst[4], dst[5]); flowPath.lineTo(dst[6], dst[7]); flowPath.close()
            flowPaint.color = 0xFF3A3D44.toInt(); flowPaint.alpha = alpha; c.drawPath(flowPath, flowPaint); return
        }
        val bw = bmp.width.toFloat(); val bh = bmp.height.toFloat()
        val src = if (flip) floatArrayOf(0f, bh, bw, bh, bw, 0f, 0f, 0f) else floatArrayOf(0f, 0f, bw, 0f, bw, bh, 0f, bh)
        flowM.setPolyToPoly(src, 0, dst, 0, 4)
        flowPaint.alpha = alpha; c.drawBitmap(bmp, flowM, flowPaint)
    }

    fun draw(c: Canvas, page: ListPage, ctx: DrawCtx, W: Float, H: Float) {
        Gfx.vgrad(c, 0f, 0f, W, H, 0xFF000000.toInt(), 0xFF22252B.toInt())
        val n = page.source.size
        if (n == 0) return
        val s = minOf(W * 0.46f, H * 0.5f)                       // the middle cover's side
        val floorY = H * 0.62f
        val mid = Math.round(page.scroll)
        val order = ((mid - 4)..(mid + 4)).filter { it in 0 until n }.sortedByDescending { kotlin.math.abs(it - page.scroll) }   // far ones first, the middle one on top
        for (i in order) {
            val off = i - page.scroll
            val k = off.coerceIn(-1f, 1f); val a = kotlin.math.abs(k)
            val side = if (off < 0) -1f else 1f
            val beyond = (kotlin.math.abs(off) - 1f).coerceAtLeast(0f)
            val cx = W / 2 + k * s * 0.62f + side * beyond * s * 0.2f
            val w = s * (1f - 0.58f * a)                          // narrower as it turns away
            val nearH = s * (1f - 0.1f * a); val farH = s * (1f - 0.34f * a)
            val left = cx - w / 2; val right = cx + w / 2
            val (hl, hr) = if (off < 0) farH to nearH else nearH to farH
            val bmp = ctx.art.get(page.source[i].artKey)
            val fade = (1f - (kotlin.math.abs(off) - 2.5f).coerceAtLeast(0f) * 0.6f).coerceIn(0f, 1f)
            flowQuad(c, bmp, left, right, hl, hr, floorY, false, (255 * fade).toInt())
            flowQuad(c, bmp, left, right, hl, hr, floorY, true, (70 * fade).toInt())
        }
        Gfx.vgrad(c, 0f, floorY, W, H, 0x00000000, 0xFF22252B.toInt())             // the reflection fades into the floor
        val item = page.source[page.selected.coerceIn(0, n - 1)]
        Gfx.text(c, item.title, W / 2, H * 0.86f, 14f, 0xFFFFFFFF.toInt(), Gfx.SANS_BOLD, Paint.Align.CENTER, maxW = W - 24f)
        item.subtitle?.takeIf { it.isNotEmpty() }?.let { Gfx.text(c, it, W / 2, H * 0.86f + 16f, 12f, 0xFFB5BAC2.toInt(), Gfx.SANS, Paint.Align.CENTER, maxW = W - 24f) }
    }

}
