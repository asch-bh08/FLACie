package com.ipodemu.player

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.clickable
import androidx.compose.foundation.verticalScroll
import kotlinx.coroutines.launch
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.wrapContentSize
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.TransformOrigin
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.drawIntoCanvas
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.nativeCanvas
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import com.ipodemu.input.Zone
import com.ipodemu.theme.Colorway
import com.ipodemu.theme.Family
import com.ipodemu.theme.Model
import com.ipodemu.theme.WheelChrome
import kotlin.math.atan2
import kotlin.math.hypot
import kotlin.math.roundToInt

private fun Int.c() = Color(this)

/** Draws a physical iPod body (and wheel for click models) into a size-of-the-composable box. */
fun DrawScope.drawIpodBody(m: Model, cw: Colorway, withWheel: Boolean = true) {
    val w = size.width; val h = size.height
    val rad = w * m.radius
    val dark = cw.darkUi || cw.top < 0xFF606060.toInt()
    // ground shadow
    drawRoundRect(Color(0x66000000), Offset(-2f, 2f), Size(w + 4f, h + 4f), CornerRadius(rad + 2f))
    drawRoundRect(Brush.verticalGradient(listOf(cw.top.c(), cw.bottom.c())), Offset.Zero, Size(w, h), CornerRadius(rad))
    drawRoundRect(Brush.verticalGradient(listOf(Color(if (dark) 0x33FFFFFF else 0x66FFFFFF), Color.Transparent), 0f, h * .45f), Offset.Zero, Size(w, h * .45f), CornerRadius(rad))
    drawRoundRect(Color(if (dark) 0x44FFFFFF else 0xAAFFFFFF.toInt()), Offset(1.5f, 1.5f), Size(w - 3f, h - 3f), CornerRadius(rad), style = Stroke(2f))
    drawRoundRect(cw.edge.c(), Offset.Zero, Size(w, h), CornerRadius(rad), style = Stroke(1.5f))
    val sx = m.screen.left * w; val sy = m.screen.top * h
    val sw = m.screen.width() * w; val sh = m.screen.height() * h
    if (m.touch) {
        // dark (or white) front face inset from the metal rim, then the glass
        val inset = w * 0.02f
        drawRoundRect(cw.bezel.c(), Offset(inset, inset), Size(w - 2 * inset, h - 2 * inset), CornerRadius(rad * .9f))
        drawRoundRect(Color(0xFF000000), Offset(sx, sy), Size(sw, sh), CornerRadius(w * 0.02f))
        if (m.home) {
            val r = w * 0.085f
            val cy = (m.screen.bottom + (1f - m.screen.bottom) * 0.5f) * h
            drawCircle(Color(0x66000000), r + 1f, Offset(w / 2, cy + 1f))
            drawCircle(if (cw.bezel.c().luminance() > .6f) Color(0xFFD9DADD) else Color(0xFF17181B), r, Offset(w / 2, cy), style = Stroke(w * 0.012f))
            drawRoundRect(if (cw.bezel.c().luminance() > .6f) Color(0xFFC9CACE) else Color(0xFF3A3B40), Offset(w / 2 - w * .028f, cy - w * .028f), Size(w * .056f, w * .056f), CornerRadius(w * .008f), style = Stroke(w * 0.008f))
        }
    } else {
        val m2 = w * m.bezelInset
        drawRoundRect(cw.bezel.c(), Offset(sx - m2, sy - m2), Size(sw + 2 * m2, sh + 2 * m2), CornerRadius(m2 * 1.6f))
        if (withWheel) drawIntoCanvas { c -> WheelChrome.draw(c.nativeCanvas, m.wheelCx * w, m.wheelCy * h, m.wheelR * w, cw.wheel, null) }
    }
}

private fun Color.luminance() = 0.2126f * red + 0.7152f * green + 0.0722f * blue

/**
 * Shows [content] (the Player UI) inside a physical iPod: the content renders at a comfortable virtual size and is scaled
 * to fit the model's screen. Click models get a working wheel: tap the ring for Menu / Prev / Next / Play.
 */
@Composable
fun DeviceFrame(m: Model, cw: Colorway, onWheel: (Zone) -> Unit, onStep: (Int) -> Unit, content: @Composable () -> Unit) {
    BoxWithConstraints(Modifier.fillMaxSize().background(Color(cw.backdrop))) {
        val aw = maxWidth; val ah = maxHeight
        // Fit the whole body when that leaves a readable screen; otherwise grow the body until the iPod screen is at least
        // MIN_SCREEN wide (never wider than the window) and let it scroll vertically: screen at the top, wheel below.
        val fitW = minOf(ah * 0.97f * m.aspect, aw * 0.98f)
        val bw = maxOf(fitW, minOf(aw * 0.98f, MIN_SCREEN / m.screen.width()))
        val bh = bw / m.aspect
        val d = LocalDensity.current
        val scroll = androidx.compose.foundation.rememberScrollState()
        val scope = androidx.compose.runtime.rememberCoroutineScope()
        Box(Modifier.fillMaxSize().verticalScroll(scroll)) {
        Box(Modifier.fillMaxWidth().defaultMinSize(minHeight = ah), contentAlignment = Alignment.Center) {
        Box(Modifier.size(bw, bh)) {
            Canvas(Modifier.fillMaxSize()) { drawIpodBody(m, cw) }
            val sW = bw * m.screen.width(); val sH = bh * m.screen.height()
            val virtW: Dp = (sW / 0.85f).coerceIn(300.dp, 560.dp)   // lay the UI out near real size: text stays ~85% of normal
            val virtH = virtW * (sH.value / sW.value)
            val scale = sW.value / virtW.value
            Box(
                Modifier.offset(bw * m.screen.left, bh * m.screen.top).size(sW, sH)
                    .clip(RoundedCornerShape(bw * (if (m.touch) 0.02f else 0.008f))),
            ) {
                Box(Modifier.fillMaxSize().wrapContentSize(Alignment.TopStart, unbounded = true)) {
                    Box(Modifier.size(virtW, virtH).graphicsLayer { transformOrigin = TransformOrigin(0f, 0f); scaleX = with(d) { sW.toPx() / virtW.toPx() }; scaleY = scaleX }) { content() }
                }
            }
            if (!m.touch && m.family != Family.TOUCH) {
                val r = bw * m.wheelR
                Box(
                    Modifier.offset(bw * m.wheelCx - r, bh * m.wheelCy - r).size(r * 2)
                        .pointerInput(m) {
                            val slop = viewConfiguration.touchSlop
                            val step = 22f // degrees of rotation per detent
                            awaitEachGesture {
                                val down = awaitFirstDown()
                                val cx = size.width / 2f; val cy = size.height / 2f
                                fun ang(p: Offset) = Math.toDegrees(atan2((p.y - cy).toDouble(), (p.x - cx).toDouble())).toFloat()
                                val onRing = hypot(down.position.x - cx, down.position.y - cy) > cx * 0.38f
                                var last = ang(down.position); var acc = 0f; var rotating = false
                                while (true) {
                                    val c = awaitPointerEvent().changes.firstOrNull { it.id == down.id } ?: break
                                    if (!c.pressed) {
                                        if (!rotating && (c.position - down.position).getDistance() <= slop * 2) {
                                            val o = down.position
                                            if (!onRing) onWheel(Zone.CENTER) else {
                                                val deg = Math.toDegrees(atan2((o.y - cy).toDouble(), (o.x - cx).toDouble())).roundToInt()
                                                onWheel(when { deg in -135..-46 -> Zone.MENU; deg in -45..45 -> Zone.NEXT; deg in 46..135 -> Zone.PLAY_PAUSE; else -> Zone.PREV })
                                            }
                                        }
                                        break
                                    }
                                    if (!rotating && onRing && (c.position - down.position).getDistance() > slop) { rotating = true; last = ang(c.position) }
                                    if (rotating) {
                                        val a = ang(c.position)
                                        var dd = a - last
                                        if (dd > 180f) dd -= 360f else if (dd < -180f) dd += 360f
                                        last = a; acc += dd
                                        while (acc >= step) { acc -= step; onStep(1) }
                                        while (acc <= -step) { acc += step; onStep(-1) }
                                        c.consume()
                                    }
                                }
                            }
                        },
                )
            }
        }
        }
        }
        if (scroll.maxValue > 0) {
            val atBottom = scroll.value > scroll.maxValue / 2
            Box(
                Modifier.align(Alignment.BottomEnd).padding(12.dp).size(40.dp).clip(androidx.compose.foundation.shape.CircleShape).background(Color(0xAA000000))
                    .clickable { scope.launch { scroll.animateScrollTo(if (atBottom) 0 else scroll.maxValue) } },
                contentAlignment = Alignment.Center,
            ) { GlyphIcon(Glyph.DOWN, Modifier.size(22.dp).graphicsLayer { rotationZ = if (atBottom) 180f else 0f }, Color.White) }
        }
    }
}

/** Smallest width the iPod's own screen may have inside the body view; below this the body grows and scrolls instead of shrinking. */
private val MIN_SCREEN = 260.dp
