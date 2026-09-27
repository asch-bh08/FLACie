package com.ipodemu.player

import android.graphics.Bitmap
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxScope
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicText
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.key.Key
import androidx.compose.ui.input.key.KeyEventType
import androidx.compose.ui.input.key.key
import androidx.compose.ui.input.key.onKeyEvent
import androidx.compose.ui.input.key.type
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.ipodemu.App

val LocalApp = staticCompositionLocalOf<App> { error("App not provided") }

// ---- glyphs -------------------------------------------------------------------------------------------------------

enum class Glyph {
    PLAY, PAUSE, NEXT, PREV, SHUFFLE, REPEAT, REPEAT_ONE, HEART, HEART_FILLED, SEARCH, QUEUE, CHEVRON, BACK, MORE, CLOSE,
    NOTE, GEAR, VOLUME, CHECK, PLUS, ALBUM, ARTIST, MIC, CLOCK, IPOD, LIST, STAR, DOWN, JELLYFIN,
}

@Composable
fun GlyphIcon(g: Glyph, modifier: Modifier = Modifier, tint: Color = LocalScheme.current.onBg) =
    Canvas(modifier) { drawGlyph(g, tint) }

fun DrawScope.drawGlyph(g: Glyph, c: Color) {
    val s = minOf(size.width, size.height)
    val ox = (size.width - s) / 2; val oy = (size.height - s) / 2
    fun x(v: Float) = ox + v * s
    fun y(v: Float) = oy + v * s
    fun path(build: Path.() -> Unit) = Path().apply(build)
    val w = s * 0.09f
    val line = Stroke(w, cap = StrokeCap.Round, join = StrokeJoin.Round)
    when (g) {
        Glyph.PLAY -> drawPath(path { moveTo(x(.3f), y(.16f)); lineTo(x(.84f), y(.5f)); lineTo(x(.3f), y(.84f)); close() }, c)
        Glyph.PAUSE -> {
            drawRoundRect(c, Offset(x(.24f), y(.17f)), Size(s * .19f, s * .66f), androidx.compose.ui.geometry.CornerRadius(s * .04f))
            drawRoundRect(c, Offset(x(.57f), y(.17f)), Size(s * .19f, s * .66f), androidx.compose.ui.geometry.CornerRadius(s * .04f))
        }
        Glyph.NEXT -> {
            drawPath(path { moveTo(x(.16f), y(.2f)); lineTo(x(.68f), y(.5f)); lineTo(x(.16f), y(.8f)); close() }, c)
            drawRoundRect(c, Offset(x(.72f), y(.2f)), Size(s * .12f, s * .6f), androidx.compose.ui.geometry.CornerRadius(s * .03f))
        }
        Glyph.PREV -> {
            drawPath(path { moveTo(x(.84f), y(.2f)); lineTo(x(.32f), y(.5f)); lineTo(x(.84f), y(.8f)); close() }, c)
            drawRoundRect(c, Offset(x(.16f), y(.2f)), Size(s * .12f, s * .6f), androidx.compose.ui.geometry.CornerRadius(s * .03f))
        }
        Glyph.SHUFFLE -> {
            drawPath(path { moveTo(x(.1f), y(.3f)); cubicTo(x(.5f), y(.3f), x(.5f), y(.7f), x(.76f), y(.7f)) }, c, style = line)
            drawPath(path { moveTo(x(.1f), y(.7f)); cubicTo(x(.5f), y(.7f), x(.5f), y(.3f), x(.76f), y(.3f)) }, c, style = line)
            drawPath(path { moveTo(x(.74f), y(.18f)); lineTo(x(.92f), y(.3f)); lineTo(x(.74f), y(.42f)); close() }, c)
            drawPath(path { moveTo(x(.74f), y(.58f)); lineTo(x(.92f), y(.7f)); lineTo(x(.74f), y(.82f)); close() }, c)
        }
        Glyph.REPEAT, Glyph.REPEAT_ONE -> {
            drawPath(path { moveTo(x(.16f), y(.56f)); lineTo(x(.16f), y(.44f)); quadraticBezierTo(x(.16f), y(.3f), x(.3f), y(.3f)); lineTo(x(.68f), y(.3f)) }, c, style = line)
            drawPath(path { moveTo(x(.84f), y(.44f)); lineTo(x(.84f), y(.56f)); quadraticBezierTo(x(.84f), y(.7f), x(.7f), y(.7f)); lineTo(x(.32f), y(.7f)) }, c, style = line)
            drawPath(path { moveTo(x(.66f), y(.18f)); lineTo(x(.86f), y(.3f)); lineTo(x(.66f), y(.42f)); close() }, c)
            drawPath(path { moveTo(x(.34f), y(.58f)); lineTo(x(.14f), y(.7f)); lineTo(x(.34f), y(.82f)); close() }, c)
            if (g == Glyph.REPEAT_ONE) drawPath(path { moveTo(x(.46f), y(.42f)); lineTo(x(.52f), y(.38f)); lineTo(x(.52f), y(.62f)) }, c, style = Stroke(w * .8f, cap = StrokeCap.Round, join = StrokeJoin.Round))
        }
        Glyph.HEART, Glyph.HEART_FILLED -> {
            val p = path {
                moveTo(x(.5f), y(.86f))
                cubicTo(x(.06f), y(.56f), x(.1f), y(.16f), x(.31f), y(.16f))
                cubicTo(x(.42f), y(.16f), x(.5f), y(.24f), x(.5f), y(.32f))
                cubicTo(x(.5f), y(.24f), x(.58f), y(.16f), x(.69f), y(.16f))
                cubicTo(x(.9f), y(.16f), x(.94f), y(.56f), x(.5f), y(.86f)); close()
            }
            if (g == Glyph.HEART_FILLED) drawPath(p, c) else drawPath(p, c, style = line)
        }
        Glyph.SEARCH -> {
            drawCircle(c, s * .24f, Offset(x(.43f), y(.43f)), style = line)
            drawLine(c, Offset(x(.62f), y(.62f)), Offset(x(.86f), y(.86f)), w, StrokeCap.Round)
        }
        Glyph.QUEUE -> {
            for (i in 0..2) drawLine(c, Offset(x(.12f), y(.28f + i * .2f)), Offset(x(.56f), y(.28f + i * .2f)), w, StrokeCap.Round)
            drawPath(path { moveTo(x(.66f), y(.44f)); lineTo(x(.9f), y(.62f)); lineTo(x(.66f), y(.8f)); close() }, c)
        }
        Glyph.LIST -> for (i in 0..2) drawLine(c, Offset(x(.14f), y(.28f + i * .22f)), Offset(x(.86f), y(.28f + i * .22f)), w, StrokeCap.Round)
        Glyph.CHEVRON -> drawPath(path { moveTo(x(.36f), y(.2f)); lineTo(x(.66f), y(.5f)); lineTo(x(.36f), y(.8f)) }, c, style = line)
        Glyph.BACK -> drawPath(path { moveTo(x(.64f), y(.2f)); lineTo(x(.34f), y(.5f)); lineTo(x(.64f), y(.8f)) }, c, style = line)
        Glyph.DOWN -> drawPath(path { moveTo(x(.2f), y(.36f)); lineTo(x(.5f), y(.66f)); lineTo(x(.8f), y(.36f)) }, c, style = line)
        Glyph.MORE -> for (i in 0..2) drawCircle(c, s * .07f, Offset(x(.24f + i * .26f), y(.5f)))
        Glyph.CLOSE -> {
            drawLine(c, Offset(x(.24f), y(.24f)), Offset(x(.76f), y(.76f)), w, StrokeCap.Round)
            drawLine(c, Offset(x(.76f), y(.24f)), Offset(x(.24f), y(.76f)), w, StrokeCap.Round)
        }
        Glyph.NOTE -> {
            drawCircle(c, s * .13f, Offset(x(.33f), y(.74f)))
            drawCircle(c, s * .13f, Offset(x(.7f), y(.66f)))
            drawRect(c, Offset(x(.4f), y(.22f)), Size(s * .07f, s * .52f))
            drawRect(c, Offset(x(.77f), y(.14f)), Size(s * .07f, s * .52f))
            drawPath(path { moveTo(x(.4f), y(.22f)); lineTo(x(.84f), y(.12f)); lineTo(x(.84f), y(.26f)); lineTo(x(.4f), y(.36f)); close() }, c)
        }
        Glyph.GEAR -> {
            for (i in 0 until 8) {
                val a = Math.toRadians(i * 45.0)
                drawLine(c, Offset(x(.5f) + Math.cos(a).toFloat() * s * .3f, y(.5f) + Math.sin(a).toFloat() * s * .3f),
                    Offset(x(.5f) + Math.cos(a).toFloat() * s * .44f, y(.5f) + Math.sin(a).toFloat() * s * .44f), s * .13f, StrokeCap.Round)
            }
            drawCircle(c, s * .3f, Offset(x(.5f), y(.5f)))
            drawCircle(Color(0x66000000), s * .12f, Offset(x(.5f), y(.5f)))
        }
        Glyph.VOLUME -> {
            drawPath(path { moveTo(x(.14f), y(.4f)); lineTo(x(.3f), y(.4f)); lineTo(x(.5f), y(.22f)); lineTo(x(.5f), y(.78f)); lineTo(x(.3f), y(.6f)); lineTo(x(.14f), y(.6f)); close() }, c)
            drawArc(c, -45f, 90f, false, Offset(x(.4f), y(.32f)), Size(s * .3f, s * .36f), style = line)
            drawArc(c, -45f, 90f, false, Offset(x(.4f), y(.2f)), Size(s * .48f, s * .6f), style = line)
        }
        Glyph.CHECK -> drawPath(path { moveTo(x(.2f), y(.52f)); lineTo(x(.42f), y(.74f)); lineTo(x(.82f), y(.28f)) }, c, style = line)
        Glyph.PLUS -> {
            drawLine(c, Offset(x(.2f), y(.5f)), Offset(x(.8f), y(.5f)), w, StrokeCap.Round)
            drawLine(c, Offset(x(.5f), y(.2f)), Offset(x(.5f), y(.8f)), w, StrokeCap.Round)
        }
        Glyph.ALBUM -> {
            drawCircle(c, s * .38f, Offset(x(.5f), y(.5f)), style = line)
            drawCircle(c, s * .1f, Offset(x(.5f), y(.5f)))
        }
        Glyph.ARTIST -> {
            drawCircle(c, s * .18f, Offset(x(.5f), y(.32f)))
            drawArc(c, 180f, 180f, true, Offset(x(.18f), y(.56f)), Size(s * .64f, s * .5f))
        }
        Glyph.MIC -> {
            drawRoundRect(c, Offset(x(.38f), y(.14f)), Size(s * .24f, s * .42f), androidx.compose.ui.geometry.CornerRadius(s * .12f))
            drawArc(c, 0f, 180f, false, Offset(x(.26f), y(.3f)), Size(s * .48f, s * .4f), style = line)
            drawLine(c, Offset(x(.5f), y(.7f)), Offset(x(.5f), y(.86f)), w, StrokeCap.Round)
        }
        Glyph.CLOCK -> {
            drawCircle(c, s * .38f, Offset(x(.5f), y(.5f)), style = line)
            drawLine(c, Offset(x(.5f), y(.5f)), Offset(x(.5f), y(.28f)), w, StrokeCap.Round)
            drawLine(c, Offset(x(.5f), y(.5f)), Offset(x(.66f), y(.6f)), w, StrokeCap.Round)
        }
        Glyph.IPOD -> {
            drawRoundRect(c, Offset(x(.26f), y(.1f)), Size(s * .48f, s * .8f), androidx.compose.ui.geometry.CornerRadius(s * .08f), style = line)
            drawRect(c, Offset(x(.34f), y(.18f)), Size(s * .32f, s * .24f))
            drawCircle(c, s * .13f, Offset(x(.5f), y(.64f)), style = line)
        }
        Glyph.STAR -> {
            val p = path {
                for (i in 0 until 10) {
                    val r = if (i % 2 == 0) .42f else .18f
                    val a = Math.toRadians(-90.0 + i * 36.0)
                    val px = x(.5f) + Math.cos(a).toFloat() * r * s; val py = y(.52f) + Math.sin(a).toFloat() * r * s
                    if (i == 0) moveTo(px, py) else lineTo(px, py)
                }
                close()
            }
            drawPath(p, c)
        }
        Glyph.JELLYFIN -> {
            drawCircle(c, s * .16f, Offset(x(.28f), y(.56f)))
            drawCircle(c, s * .22f, Offset(x(.5f), y(.42f)))
            drawCircle(c, s * .17f, Offset(x(.72f), y(.56f)))
            drawRoundRect(c, Offset(x(.16f), y(.56f)), Size(s * .68f, s * .22f), androidx.compose.ui.geometry.CornerRadius(s * .11f))
        }
    }
}

// ---- text ---------------------------------------------------------------------------------------------------------

@Composable
fun Txt(
    text: String, modifier: Modifier = Modifier, size: Float = 16f, weight: FontWeight = FontWeight.Normal,
    color: Color = LocalScheme.current.onBg, maxLines: Int = 1, align: TextAlign = TextAlign.Start,
) {
    BasicText(
        text, modifier,
        style = TextStyle(color = color, fontSize = size.sp, fontWeight = weight, fontFamily = LocalStyle.current.font, textAlign = align),
        maxLines = maxLines, overflow = TextOverflow.Ellipsis,
    )
}

// ---- iPod-styled buttons ------------------------------------------------------------------------------------------

/** Round Aqua-style button: gradient body, specular highlight, focus ring for the D-pad. */
@Composable
fun GlossButton(
    onClick: () -> Unit, modifier: Modifier = Modifier, size: Dp = 56.dp, primary: Boolean = false, enabled: Boolean = true,
    content: @Composable BoxScope.() -> Unit,
) {
    val sc = LocalScheme.current
    val style = LocalStyle.current
    val src = remember { MutableInteractionSource() }
    val pressed by src.collectIsPressedAsState()
    val focused by src.collectIsFocusedAsState()
    val brush = if (primary) Brush.verticalGradient(listOf(sc.accentLight, sc.accent, sc.accentDark))
    else Brush.verticalGradient(listOf(Color(0x59FFFFFF), Color(0x1FFFFFFF)))
    Box(
        modifier
            .size(size)
            .graphicsLayer { val s = if (pressed) 0.92f else 1f; scaleX = s; scaleY = s; alpha = if (enabled) 1f else 0.4f }
            .clip(CircleShape)
            .background(brush)
            .border(if (focused) 2.5.dp else 1.dp, if (focused) Color.White else Color(0x40FFFFFF), CircleShape)
            .drawBehind {
                if (style.glossy) drawOval(
                    Brush.verticalGradient(listOf(Color(0x66FFFFFF), Color.Transparent)),
                    Offset(this.size.width * .1f, this.size.height * .03f), Size(this.size.width * .8f, this.size.height * .5f),
                )
            }
            .combinedClickableNoRipple(src, enabled, onClick),
        contentAlignment = Alignment.Center, content = content,
    )
}

@OptIn(ExperimentalFoundationApi::class)
private fun Modifier.combinedClickableNoRipple(src: MutableInteractionSource, enabled: Boolean, onClick: () -> Unit, onLong: (() -> Unit)? = null) =
    this.wheelTracked().combinedClickable(interactionSource = src, indication = null, enabled = enabled, onClick = onClick, onLongClick = onLong)

/** Pill button with optional icon: primary (filled with the accent) or glass. */
@Composable
fun GlossPill(
    text: String, onClick: () -> Unit, modifier: Modifier = Modifier, icon: Glyph? = null, primary: Boolean = false,
    height: Dp = 46.dp,
) {
    val sc = LocalScheme.current
    val style = LocalStyle.current
    val src = remember { MutableInteractionSource() }
    val pressed by src.collectIsPressedAsState()
    val focused by src.collectIsFocusedAsState()
    val shape = RoundedCornerShape(50)
    val brush = if (primary) Brush.verticalGradient(listOf(sc.accentLight, sc.accent, sc.accentDark))
    else Brush.verticalGradient(listOf(Color(0x52FFFFFF), Color(0x1AFFFFFF)))
    Row(
        modifier
            .height(height)
            .graphicsLayer { val s = if (pressed) 0.96f else 1f; scaleX = s; scaleY = s }
            .clip(shape).background(brush)
            .border(if (focused) 2.5.dp else 1.dp, if (focused) Color.White else Color(0x40FFFFFF), shape)
            .drawBehind {
                if (style.glossy) drawRoundRect(
                    Brush.verticalGradient(listOf(Color(0x55FFFFFF), Color.Transparent)),
                    Offset(size.width * .04f, size.height * .04f), Size(size.width * .92f, size.height * .5f),
                    androidx.compose.ui.geometry.CornerRadius(size.height / 2),
                )
            }
            .combinedClickableNoRipple(src, true, onClick)
            .padding(horizontal = 20.dp),
        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.Center,
    ) {
        if (icon != null) { GlyphIcon(icon, Modifier.size(20.dp), Color.White); if (text.isNotEmpty()) Box(Modifier.width(8.dp)) }
        if (text.isNotEmpty()) Txt(text, size = 15f, weight = FontWeight.SemiBold, color = Color.White)
    }
}

/** Rounded-square icon tile, like the glossy blue icons in the iPod's main menu. */
@Composable
fun IconTile(g: Glyph, modifier: Modifier = Modifier, size: Dp = 44.dp, tint: Color? = null) {
    val sc = LocalScheme.current
    val style = LocalStyle.current
    val base = tint ?: sc.accent
    val shape = RoundedCornerShape((size.value * .24f).dp)
    Box(
        modifier.size(size).clip(shape)
            .background(Brush.verticalGradient(listOf(base.copy(alpha = 1f), base.copy(alpha = .78f))))
            .drawBehind {
                if (style.glossy) drawRect(Brush.verticalGradient(listOf(Color(0x66FFFFFF), Color.Transparent)), size = Size(this.size.width, this.size.height * .5f))
            },
        contentAlignment = Alignment.Center,
    ) { GlyphIcon(g, Modifier.size(size * .58f), Color.White) }
}

// ---- rows ---------------------------------------------------------------------------------------------------------
val LocalRowHi = androidx.compose.runtime.compositionLocalOf { false }

/** Secondary text colour that stays readable on the highlighted (focused/pressed) row. */
@Composable
fun rowDim(): Color = if (LocalRowHi.current) Color(0xDDFFFFFF) else LocalScheme.current.onBgDim

/** iPod-style list row: hairline divider, and the classic glossy accent selection when focused or pressed. */
@OptIn(ExperimentalFoundationApi::class)
@Composable
fun IpodRow(
    onClick: () -> Unit, modifier: Modifier = Modifier, onLong: (() -> Unit)? = null, height: Dp = 64.dp,
    focusRequester: FocusRequester? = null,
    leading: (@Composable () -> Unit)? = null, trailing: (@Composable () -> Unit)? = null,
    content: @Composable (highlighted: Boolean) -> Unit,
) {
    val sc = LocalScheme.current
    val style = LocalStyle.current
    val src = remember { MutableInteractionSource() }
    val pressed by src.collectIsPressedAsState()
    val focused by src.collectIsFocusedAsState()
    val hi = pressed || focused
    Row(
        modifier.fillMaxWidth().height(height)
            .then(if (focusRequester != null) Modifier.focusRequester(focusRequester) else Modifier)
            .background(if (hi) Brush.verticalGradient(listOf(sc.accentLight, sc.accentDark)) else Brush.verticalGradient(listOf(Color.Transparent, Color.Transparent)))
            .drawBehind {
                if (!hi) drawLine(sc.onBg.copy(alpha = .1f), Offset(size.height, size.height - 1f), Offset(size.width, size.height - 1f), 1f)
                if (hi && style.glossy) drawRect(Brush.verticalGradient(listOf(Color(0x40FFFFFF), Color.Transparent)), size = Size(size.width, size.height * .5f))
            }
            .wheelTracked()
            .combinedClickable(interactionSource = src, indication = null, onClick = onClick, onLongClick = onLong)
            .padding(horizontal = 16.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        androidx.compose.runtime.CompositionLocalProvider(LocalRowHi provides hi) {
        if (leading != null) { leading(); Box(Modifier.width(14.dp)) }
        Box(Modifier.weight(1f)) { content(hi) }
        if (trailing != null) { Box(Modifier.width(10.dp)); trailing() }
        }
    }
}

// ---- artwork ------------------------------------------------------------------------------------------------------

@Composable
fun rememberArt(key: String?, thumb: Boolean): Bitmap? {
    val art = LocalApp.current.art
    val bmp by produceState(initialValue = art.peek(key, thumb), key, thumb) { value = art.load(key, thumb) ?: art.peek(key, thumb) }
    return bmp
}

@Composable
fun ArtImage(key: String?, modifier: Modifier = Modifier, thumb: Boolean = false, corner: Dp = 8.dp, circle: Boolean = false) {
    val bmp = rememberArt(key, thumb)
    val sc = LocalScheme.current
    val shape = if (circle) CircleShape else RoundedCornerShape(corner)
    Box(modifier.clip(shape).background(Brush.linearGradient(listOf(sc.onBg.copy(alpha = .16f), sc.onBg.copy(alpha = .05f)))), contentAlignment = Alignment.Center) {
        if (bmp != null) Image(bmp.asImageBitmap(), null, Modifier.fillMaxSize(), contentScale = ContentScale.Crop)
        else GlyphIcon(Glyph.NOTE, Modifier.fillMaxSize(0.42f), sc.onBg.copy(alpha = .35f))
    }
}

// ---- misc ---------------------------------------------------------------------------------------------------------

@Composable
fun EqualizerBars(modifier: Modifier = Modifier, playing: Boolean, color: Color) {
    val t = rememberInfiniteTransition(label = "eq")
    val a by t.animateFloat(0.25f, 1f, infiniteRepeatable(tween(520, easing = LinearEasing), RepeatMode.Reverse), label = "a")
    val b by t.animateFloat(1f, 0.3f, infiniteRepeatable(tween(430, easing = LinearEasing), RepeatMode.Reverse), label = "b")
    val c by t.animateFloat(0.4f, 0.95f, infiniteRepeatable(tween(610, easing = LinearEasing), RepeatMode.Reverse), label = "c")
    Canvas(modifier) {
        val bw = size.width / 5f
        val hs = if (playing) listOf(a, b, c) else listOf(.3f, .3f, .3f)
        hs.forEachIndexed { i, h ->
            val bh = size.height * h
            drawRoundRect(color, Offset(i * bw * 2f, size.height - bh), Size(bw, bh), androidx.compose.ui.geometry.CornerRadius(bw / 2))
        }
    }
}

/** iPod-style progress track with a knob; drag or tap to seek, left/right keys nudge. */
@Composable
fun SeekBar(fraction: Float, onSeek: (Float) -> Unit, onNudge: (Float) -> Unit, modifier: Modifier = Modifier) {
    val sc = LocalScheme.current
    var drag by remember { mutableStateOf<Float?>(null) }
    val src = remember { MutableInteractionSource() }
    val focused by src.collectIsFocusedAsState()
    val shown = (drag ?: fraction).coerceIn(0f, 1f)
    Box(
        modifier.height(36.dp)
            .onKeyEvent {
                if (it.type == KeyEventType.KeyDown && it.key == Key.DirectionLeft) { onNudge(-1f); true }
                else if (it.type == KeyEventType.KeyDown && it.key == Key.DirectionRight) { onNudge(1f); true } else false
            }
            .combinedClickableNoRipple(src, true, {})
            .pointerInput(Unit) { detectTapGestures { o -> onSeek((o.x / size.width).coerceIn(0f, 1f)) } }
            .pointerInput(Unit) {
                detectHorizontalDragGestures(
                    onDragStart = { drag = (it.x / size.width).coerceIn(0f, 1f) },
                    onDragEnd = { drag?.let(onSeek); drag = null },
                    onDragCancel = { drag = null },
                    onHorizontalDrag = { c, _ -> drag = (c.position.x / size.width).coerceIn(0f, 1f) },
                )
            },
        contentAlignment = Alignment.CenterStart,
    ) {
        Canvas(Modifier.fillMaxWidth().height(36.dp)) {
            val th = 8.dp.toPx(); val cy = size.height / 2
            drawRoundRect(sc.onBg.copy(alpha = .22f), Offset(0f, cy - th / 2), Size(size.width, th), androidx.compose.ui.geometry.CornerRadius(th / 2))
            val fx = size.width * shown
            if (fx > 0f) drawRoundRect(Brush.verticalGradient(listOf(sc.accentLight, sc.accentDark), cy - th / 2, cy + th / 2), Offset(0f, cy - th / 2), Size(fx, th), androidx.compose.ui.geometry.CornerRadius(th / 2))
            drawCircle(Color.White, if (drag != null || focused) 11.dp.toPx() else 8.dp.toPx(), Offset(fx, cy))
            drawCircle(sc.accent, 4.dp.toPx(), Offset(fx, cy))
        }
    }
}

/** Dark scrim + bottom sheet of actions. The first row takes focus so gamepads work. */
class SheetItem(val label: String, val glyph: Glyph, val onClick: () -> Unit)

@Composable
fun ActionSheet(title: String, subtitle: String?, items: List<SheetItem>, onDismiss: () -> Unit) {
    val sc = LocalScheme.current
    val first = remember { FocusRequester() }
    LaunchedEffect(Unit) { try { first.requestFocus() } catch (_: Exception) {} }
    Box(Modifier.fillMaxSize().background(Color(0x99000000)).pointerInput(Unit) { detectTapGestures { onDismiss() } }) {
        Column(
            Modifier.align(Alignment.BottomCenter).fillMaxWidth()
                .clip(RoundedCornerShape(topStart = 22.dp, topEnd = 22.dp))
                .background(Brush.verticalGradient(listOf(sc.top.copy(alpha = 1f).mix(Color.Black, .35f), sc.bottom.mix(Color.Black, .25f))))
                .pointerInput(Unit) { detectTapGestures { } }
                .padding(bottom = 12.dp),
        ) {
            Column(Modifier.padding(horizontal = 20.dp, vertical = 14.dp)) {
                Txt(title, size = 18f, weight = FontWeight.Bold)
                if (subtitle != null) Txt(subtitle, size = 13f, color = sc.onBgDim)
            }
            items.forEachIndexed { i, item ->
                IpodRow(onClick = { item.onClick(); onDismiss() }, height = 54.dp, focusRequester = if (i == 0) first else null,
                    leading = { GlyphIcon(item.glyph, Modifier.size(24.dp), if (LocalRowHi.current) Color.White else sc.onBg) }) { hi -> Txt(item.label, size = 16f, color = if (hi) Color.White else sc.onBg) }
            }
        }
    }
}

fun Color.mix(other: Color, t: Float) = Color(
    red + (other.red - red) * t, green + (other.green - green) * t, blue + (other.blue - blue) * t, alpha,
)
