package com.ipodemu.theme

import android.graphics.RectF

/**
 * A physical iPod body drawn around the screen and wheel (the "device" look). Fractions are relative to the body
 * rect: x by its width, y by its height; [wheelR] is a fraction of the body width.
 */
class BodySpec(
    val aspect: Float,
    val screen: RectF,
    val wheelCx: Float,
    val wheelCy: Float,
    val wheelR: Float,
    val radius: Float,
    val top: Int,
    val bottom: Int,
    val edge: Int,
    val highlight: Int,
    val sheen: Int,
    val bezel: Int,
    val bezelInset: Float,
)

/** Everything a skin changes apart from the on-screen UI: faceplate colours, wheel, optional body. */
class Chrome(
    val flatTop: Int,
    val flatBottom: Int,
    val flatLine: Int,
    val wheel: WheelStyle,
    val body: BodySpec? = null,
    val backdrop: Int = 0xFF0B0C0E.toInt(),
)

private fun c(v: Long) = v.toInt()

object Chromes {
    val whiteWheel = WheelStyle(
        ringLight = c(0xFFFFFFFF), ringDark = c(0xFFD9DBE0), edge = c(0xFFB4B7BE),
        centerLight = c(0xFFF7F8FA), centerDark = c(0xFFCDD0D6), centerEdge = c(0xFFA9ACB4), label = c(0xFF7C8087),
    )
    val classicWheel = WheelStyle(
        ringLight = c(0xFFFDFDFD), ringDark = c(0xFFE2E2E4), edge = c(0xFFB9B9BD),
        centerLight = c(0xFFFAFAFA), centerDark = c(0xFFDADADD), centerEdge = c(0xFFB0B0B5), label = c(0xFF9A9A9F),
    )
    val darkWheel = WheelStyle(
        ringLight = c(0xFF3D3F46), ringDark = c(0xFF1B1C20), edge = c(0xFF08080A),
        centerLight = c(0xFF34363C), centerDark = c(0xFF131417), centerEdge = c(0xFF050506), label = c(0xFFB7BAC2),
        pressed = c(0x33FFFFFF),
    )

}
