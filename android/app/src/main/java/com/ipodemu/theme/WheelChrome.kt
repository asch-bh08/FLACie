package com.ipodemu.theme

import android.graphics.Canvas
import android.graphics.Paint
import com.ipodemu.input.Zone
import kotlin.math.cos
import kotlin.math.sin

/** Click-wheel artwork shared by the solid-wheel generations (Nano, Classic). */
class WheelStyle(
    val ringLight: Int, val ringDark: Int, val edge: Int,
    val centerLight: Int, val centerDark: Int, val centerEdge: Int,
    val label: Int,
    val pressed: Int = 0x33000000,
    val centerFrac: Float = 0.38f,
)

object WheelChrome {
    fun draw(c: Canvas, cx: Float, cy: Float, r: Float, s: WheelStyle, pressed: Zone?) {
        // ground shadow + ring
        Gfx.circle(c, cx, cy + r * 0.02f, r * 1.02f, 0x33000000)
        Gfx.radial(c, cx, cy, r * 1.1f, s.ringLight, s.ringDark, r, cy - r * 0.15f)
        Gfx.circleStroke(c, cx, cy, r - 0.5f, s.edge, 1.5f)

        // pressed wedge
        if (pressed != null && pressed != Zone.CENTER) {
            val mid = when (pressed) { Zone.MENU -> -90f; Zone.NEXT -> 0f; Zone.PLAY_PAUSE -> 90f; else -> 180f }
            Gfx.p.style = Paint.Style.FILL; Gfx.p.shader = null; Gfx.p.color = s.pressed
            val rect = Gfx.rectF(cx - r, cy - r, cx + r, cy + r)
            val inner = Gfx.rectF(cx - r * s.centerFrac, cy - r * s.centerFrac, cx + r * s.centerFrac, cy + r * s.centerFrac)
            val path = android.graphics.Path()
            path.arcTo(rect, mid - 45f, 90f); path.arcTo(inner, mid + 45f, -90f); path.close()
            c.drawPath(path, Gfx.p)
        }

        // labels
        val lr = r * 0.72f
        Gfx.text(c, "MENU", cx, cy - lr + r * 0.06f, r * 0.13f, s.label, Gfx.SANS_BOLD, Paint.Align.CENTER)
        val h = r * 0.13f
        // next |>>|
        run {
            val x = cx + lr - h; val y = cy
            Gfx.triangle(c, x - h * 0.6f, y, h, s.label); Gfx.triangle(c, x + h * 0.2f, y, h, s.label)
            Gfx.rect(c, x + h * 1.05f, y - h / 2, x + h * 1.05f + h * 0.18f, y + h / 2, s.label)
        }
        // prev |<<|
        run {
            val x = cx - lr + h; val y = cy
            Gfx.triangle(c, x + h * 0.6f, y, h, s.label, false); Gfx.triangle(c, x - h * 0.2f, y, h, s.label, false)
            Gfx.rect(c, x - h * 1.05f - h * 0.18f, y - h / 2, x - h * 1.05f, y + h / 2, s.label)
        }
        // play/pause
        run {
            val x = cx - h * 0.9f; val y = cy + lr - h * 0.2f
            Gfx.triangle(c, x, y, h, s.label); Gfx.pause(c, x + h * 1.2f, y, h * 0.9f, s.label)
        }

        // center button
        val cr = r * s.centerFrac
        Gfx.circle(c, cx, cy + cr * 0.03f, cr * 1.02f, 0x22000000)
        Gfx.radial(c, cx, cy, cr * 1.3f, s.centerLight, s.centerDark, cr, cy - cr * 0.3f)
        Gfx.circleStroke(c, cx, cy, cr, s.centerEdge, 1.2f)
        if (pressed == Zone.CENTER) Gfx.circle(c, cx, cy, cr, s.pressed)
    }

    fun pt(cx: Float, cy: Float, r: Float, deg: Float) = floatArrayOf(cx + r * cos(Math.toRadians(deg.toDouble())).toFloat(), cy + r * sin(Math.toRadians(deg.toDouble())).toFloat())
}
