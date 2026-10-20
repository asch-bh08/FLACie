package com.ipodemu.player

import androidx.compose.ui.graphics.luminance
import androidx.compose.foundation.clickable
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.heightIn
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.util.LruCache
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
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import com.ipodemu.App

val LocalApp = staticCompositionLocalOf<App> { error("App not provided") }

// ---- glyphs -------------------------------------------------------------------------------------------------------

enum class Glyph {
    PLAY, PAUSE, NEXT, PREV, SHUFFLE, REPEAT, REPEAT_ONE, HEART, HEART_FILLED, SEARCH, QUEUE, CHEVRON, BACK, MORE, CLOSE,
    NOTE, GEAR, VOLUME, CHECK, PLUS, ALBUM, ARTIST, MIC, CLOCK, IPOD, LIST, STAR, DOWN, JELLYFIN, PLEX, NAS, LYRICS, DEVICES, JAM, INFO, CASSETTE,
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
            if (g == Glyph.HEART_FILLED) drawPath(p, c) else drawPath(p, c, style = Stroke(w * 1.2f, cap = StrokeCap.Round, join = StrokeJoin.Round))
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
        Glyph.MORE -> for (i in 0..2) drawCircle(c, s * .08f, Offset(x(.24f + i * .26f), y(.5f)))
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
        Glyph.CASSETTE -> {
            // Retro's mark, as on the web: a cassette, its two reels and the window between them
            drawRoundRect(c, Offset(x(.1f), y(.24f)), Size(s * .8f, s * .54f), androidx.compose.ui.geometry.CornerRadius(s * .07f), style = line)
            drawCircle(c, s * .075f, Offset(x(.34f), y(.46f)), style = line)
            drawCircle(c, s * .075f, Offset(x(.66f), y(.46f)), style = line)
            drawPath(path { moveTo(x(.3f), y(.78f)); lineTo(x(.36f), y(.66f)); lineTo(x(.64f), y(.66f)); lineTo(x(.7f), y(.78f)) }, c, style = line)
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
        Glyph.PLEX -> {
            // Plex's own mark: an arc wrapped around a play triangle.
            drawArc(c, -50f, 280f, false, Offset(x(.14f), y(.14f)), Size(s * .72f, s * .72f), style = line)
            drawPath(path { moveTo(x(.42f), y(.36f)); lineTo(x(.68f), y(.5f)); lineTo(x(.42f), y(.64f)); close() }, c)
        }
        Glyph.LYRICS -> {
            // a speech bubble with text lines
            drawRoundRect(c, Offset(x(.12f), y(.16f)), Size(s * .76f, s * .56f), androidx.compose.ui.geometry.CornerRadius(s * .1f), style = line)
            drawPath(path { moveTo(x(.3f), y(.72f)); lineTo(x(.26f), y(.88f)); lineTo(x(.46f), y(.72f)) }, c, style = line)
            drawLine(c, Offset(x(.26f), y(.36f)), Offset(x(.74f), y(.36f)), w, StrokeCap.Round)
            drawLine(c, Offset(x(.26f), y(.52f)), Offset(x(.6f), y(.52f)), w, StrokeCap.Round)
        }
        Glyph.INFO -> {
            drawCircle(c, s * .38f, Offset(x(.5f), y(.5f)), style = line)
            drawLine(c, Offset(x(.5f), y(.46f)), Offset(x(.5f), y(.68f)), w, StrokeCap.Round)
            drawCircle(c, s * .035f, Offset(x(.5f), y(.32f)))
        }
        Glyph.NAS -> {
            // Two stacked drive bays, like a small network-attached-storage tower.
            drawRoundRect(c, Offset(x(.2f), y(.14f)), Size(s * .6f, s * .32f), androidx.compose.ui.geometry.CornerRadius(s * .06f), style = line)
            drawRoundRect(c, Offset(x(.2f), y(.54f)), Size(s * .6f, s * .32f), androidx.compose.ui.geometry.CornerRadius(s * .06f), style = line)
            drawCircle(c, s * .04f, Offset(x(.3f), y(.3f)))
            drawCircle(c, s * .04f, Offset(x(.3f), y(.7f)))
        }
        Glyph.DEVICES -> {
            // a speaker beside a phone: playback on another device
            drawRoundRect(c, Offset(x(.12f), y(.14f)), Size(s * .4f, s * .72f), androidx.compose.ui.geometry.CornerRadius(s * .07f), style = line)
            drawCircle(c, s * .1f, Offset(x(.32f), y(.6f)), style = line)
            drawCircle(c, s * .04f, Offset(x(.32f), y(.3f)))
            drawRoundRect(c, Offset(x(.62f), y(.3f)), Size(s * .26f, s * .48f), androidx.compose.ui.geometry.CornerRadius(s * .05f), style = line)
        }
        Glyph.JAM -> {
            // two people side by side: listening together
            drawCircle(c, s * .12f, Offset(x(.36f), y(.32f)), style = line)
            drawCircle(c, s * .12f, Offset(x(.66f), y(.36f)), style = line)
            drawPath(path { moveTo(x(.14f), y(.84f)); quadraticBezierTo(x(.36f), y(.5f), x(.58f), y(.84f)) }, c, style = line)
            drawPath(path { moveTo(x(.5f), y(.62f)); quadraticBezierTo(x(.7f), y(.52f), x(.88f), y(.84f)) }, c, style = line)
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
    onClick: () -> Unit, modifier: Modifier = Modifier, size: Dp = 56.dp, primary: Boolean = false, enabled: Boolean = true, label: String? = null,
    content: @Composable BoxScope.() -> Unit,
) {
    val sc = LocalScheme.current
    val style = LocalStyle.current
    val src = remember { MutableInteractionSource() }
    val pressed by src.collectIsPressedAsState()
    val focused by src.collectIsFocusedAsState()
    val modern = style.modern
    val brush = if (modern) androidx.compose.ui.graphics.SolidColor(if (primary) sc.accent else if (pressed) Palette.surface3 else Palette.surface2)
    else if (primary) Brush.verticalGradient(listOf(sc.accentLight, sc.accent, sc.accentDark))
    else Brush.verticalGradient(listOf(Color(0x59FFFFFF), Color(0x1FFFFFFF)))
    Box(
        modifier
            .size(size)
            .pressSpring(pressed, 0.88f).graphicsLayer { alpha = if (enabled) 1f else 0.4f }
            .clip(CircleShape)
            .background(brush)
            .then(if (modern && !focused) (if (primary) Modifier else Modifier.border(1.dp, Palette.line, CircleShape)) else Modifier.border(if (focused) 2.5.dp else 1.dp, if (focused) Color.White else Color(0x40FFFFFF), CircleShape))
            .drawBehind {
                if (style.glossy) drawOval(
                    Brush.verticalGradient(listOf(Color(0x66FFFFFF), Color.Transparent)),
                    Offset(this.size.width * .1f, this.size.height * .03f), Size(this.size.width * .8f, this.size.height * .5f),
                )
            }
            .then(if (label != null) Modifier.semantics { contentDescription = label; role = androidx.compose.ui.semantics.Role.Button } else Modifier)
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
    val modern = style.modern
    // design I: a plain pill is outlined on the ground (no grey fill), the chosen or main one is filled
    val brush = if (modern) androidx.compose.ui.graphics.SolidColor(if (primary) sc.accent else Color.Transparent)
    else if (primary) Brush.verticalGradient(listOf(sc.accentLight, sc.accent, sc.accentDark))
    else Brush.verticalGradient(listOf(Color(0x52FFFFFF), Color(0x1AFFFFFF)))
    Row(
        modifier
            .height(maxOf(height, 44.dp))   // a touch target of at least 44 dp
            .pressSpring(pressed, 0.92f)
            .clip(shape).background(brush)
            .then(if (modern && !focused) (if (primary) Modifier else Modifier.border(1.dp, sc.onBg.copy(alpha = .28f), shape)) else Modifier.border(if (focused) 2.5.dp else 1.dp, if (focused) Color.White else Color(0x40FFFFFF), shape))
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
        // a pale accent (a yellow or white cover) needs dark text to stay readable
        val ink = if (primary && modern) sc.accent.readableInk() else Color.White
        if (icon != null) { GlyphIcon(icon, Modifier.size(20.dp), ink); if (text.isNotEmpty()) Box(Modifier.width(8.dp)) }
        if (text.isNotEmpty()) Txt(text, size = 15f, weight = FontWeight.SemiBold, color = ink)
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
    // pressed or focused, from ONE collector (the two collectIs...AsState calls were two coroutines per row, and a list builds dozens of rows while it scrolls)
    val hiState = remember { androidx.compose.runtime.mutableStateOf(false) }
    val ringState = remember { androidx.compose.runtime.mutableStateOf(false) }
    LaunchedEffect(src) {
        val presses = ArrayList<androidx.compose.foundation.interaction.PressInteraction.Press>()
        var focused = false
        src.interactions.collect { i ->
            when (i) {
                is androidx.compose.foundation.interaction.PressInteraction.Press -> presses.add(i)
                is androidx.compose.foundation.interaction.PressInteraction.Release -> presses.remove(i.press)
                is androidx.compose.foundation.interaction.PressInteraction.Cancel -> presses.remove(i.press)
                is androidx.compose.foundation.interaction.FocusInteraction.Focus -> focused = true
                is androidx.compose.foundation.interaction.FocusInteraction.Unfocus -> focused = false
            }
            hiState.value = presses.isNotEmpty() || focused
            ringState.value = focused
        }
    }
    val hi = hiState.value
    val ring = ringState.value
    // what wheelTracked() does, without the composed{} modifier (its remember + node chain per row was measurable on a 10 000-song list)
    val wheelFr = remember { androidx.compose.ui.focus.FocusRequester() }
    Row(
        modifier.fillMaxWidth().height(if (Tweaks.compact) height * 0.84f else height)
            .then(if (focusRequester != null) Modifier.focusRequester(focusRequester) else Modifier)
            // nothing is painted behind an idle row (it used to draw a transparent gradient shader per row, every frame)
            .then(if (hi && style.modern) Modifier.background(Palette.surface2) else if (hi) Modifier.background(Brush.verticalGradient(listOf(sc.accentLight, sc.accentDark))) else Modifier)
            .then(if (!style.modern || (hi && style.glossy)) Modifier.drawBehind {
                if (!hi && !style.modern) drawLine(sc.onBg.copy(alpha = .1f), Offset(size.height, size.height - 1f), Offset(size.width, size.height - 1f), 1f)
                if (hi && style.glossy) drawRect(Brush.verticalGradient(listOf(Color(0x40FFFFFF), Color.Transparent)), size = Size(size.width, size.height * .5f))
            } else Modifier)
            .focusRequester(wheelFr).onFocusChanged { if (it.isFocused) WheelFocus.last = wheelFr }
            .then(if (ring) Modifier.border(FocusRingWidth, Color.White, RoundedCornerShape(10.dp)) else Modifier)
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

/**
 * The cover as a bitmap: from memory at once, else loaded by a coroutine that lives and dies with the composable (a row that scrolls away
 * cancels its request in the gate). [full] = the Now Playing size (Jellyfin covers: one 960 px picture per song, the row-size one shows while it loads).
 */
@Composable
fun rememberArt(key: String?, thumb: Boolean, full: Boolean = false): Bitmap? {
    val art = LocalApp.current.art
    // keyed state: produceState kept the previous song's cover on screen while the next one loaded (or for good when it had none)
    val exact = remember(key, thumb, full) { art.peek(key, thumb, full) }
    // the big cover starts from any smaller copy already in memory (the list's or the mini player's), so it never shows the placeholder while one exists
    val state = remember(key, thumb, full) { mutableStateOf(exact ?: if (full) art.peek(key, thumb = false) ?: art.peek(key, thumb = true) else null) }
    if (full && exact == null && key != null && state.value == null) LaunchedEffect(key) {
        // nothing in memory: the row-size copy usually sits on disk and decodes in a few milliseconds, long before the big one arrives
        val small = art.load(key, thumb = true, full = false, priority = com.ipodemu.library.Priority.PLAYING)
        if (small != null && state.value == null) state.value = small
    }
    val token = remember { Any() }
    if (key != null && !full) {
        androidx.compose.runtime.DisposableEffect(token, key, thumb) { com.ipodemu.CoverProbe.enter(token, key, state.value != null); onDispose { com.ipodemu.CoverProbe.exit(token) } }
    }
    // no coroutine at all for a cover that is already in memory (most rows of a list you scroll back through)
    if (full && key != null) {
        // how long the big cover takes to show anything, in the log (FLACieCover), so the time to the first cover pixel can be measured on a device
        val t0 = remember(key) { android.os.SystemClock.elapsedRealtime() }
        val logged = remember(key) { booleanArrayOf(false) }
        if (state.value != null && !logged[0]) { logged[0] = true; android.util.Log.i("FLACieCover", "first pixel ${key.take(12)} after ${android.os.SystemClock.elapsedRealtime() - t0} ms") }
    }
    if (exact == null && key != null) LaunchedEffect(key, thumb, full) {
        val b = art.load(key, thumb, full, if (full) com.ipodemu.library.Priority.PLAYING else com.ipodemu.library.Priority.VISIBLE)
        if (b == null) android.util.Log.i("FLACieCover", "no bitmap for ${key.take(10)} (thumb=$thumb)")
        b?.let { state.value = it; com.ipodemu.CoverProbe.loaded(token); com.ipodemu.Boot.once("first cover loaded") }
    }
    return state.value
}

@Composable
fun ArtImage(key: String?, modifier: Modifier = Modifier, thumb: Boolean = false, corner: Dp = 8.dp, circle: Boolean = false, full: Boolean = false) {
    val bmp = rememberArt(key, thumb, full)
    val sc = LocalScheme.current
    val shape = remember(circle, corner) { if (circle) CircleShape else RoundedCornerShape(corner) }
    val img = remember(bmp) { bmp?.asImageBitmap() }
    // the placeholder gradient is only drawn while there is no cover: a gradient shader behind every loaded cover was wasted work per row per frame
    Box(modifier.clip(shape), contentAlignment = Alignment.Center) {
        // the big cover crossfades from the small copy to the sharp one, and from one song to the next, instead of jumping
        if (img != null && full) androidx.compose.animation.Crossfade(img, label = "cover", animationSpec = androidx.compose.animation.core.tween(if (Tweaks.motion) 260 else 0)) {
            Image(it, null, Modifier.fillMaxSize(), contentScale = ContentScale.Crop)
        }
        else if (img != null) Image(img, null, Modifier.fillMaxSize(), contentScale = ContentScale.Crop)
        else {
            Box(Modifier.fillMaxSize().background(Brush.linearGradient(listOf(sc.onBg.copy(alpha = .16f), sc.onBg.copy(alpha = .05f)))))
            GlyphIcon(Glyph.NOTE, Modifier.fillMaxSize(0.42f), sc.onBg.copy(alpha = .35f))
        }
    }
}

private val remoteArtCache = object : LruCache<String, Bitmap>(8 * 1024 * 1024) {
    override fun sizeOf(k: String, v: Bitmap) = v.byteCount
}

/** Cover art for a catalog search result (Lidarr's own image cache) -- not yet in the library, so
 * there's no local artKey for it. Fetches and decodes the URL directly with a small in-memory cache,
 * separate from ArtCache (which is keyed to this app's own library items, not arbitrary URLs). */
@Composable
fun RemoteArtImage(url: String?, modifier: Modifier = Modifier, corner: Dp = 8.dp) {
    val sc = LocalScheme.current
    var bmp by remember(url) { mutableStateOf(url?.let { remoteArtCache.get(it) }) }
    LaunchedEffect(url) {
        if (url == null || bmp != null) return@LaunchedEffect
        val loaded = withContext(Dispatchers.IO) {
            try {
                val conn = java.net.URL(url).openConnection() as java.net.HttpURLConnection
                conn.connectTimeout = 5000; conn.readTimeout = 8000
                conn.inputStream.use { BitmapFactory.decodeStream(it) }
            } catch (_: Exception) { null }
        }
        if (loaded != null) { remoteArtCache.put(url, loaded); bmp = loaded }
    }
    Box(modifier.clip(RoundedCornerShape(corner)).background(Brush.linearGradient(listOf(sc.onBg.copy(alpha = .16f), sc.onBg.copy(alpha = .05f)))), contentAlignment = Alignment.Center) {
        val b = bmp
        if (b != null) Image(b.asImageBitmap(), null, Modifier.fillMaxSize(), contentScale = ContentScale.Crop)
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

/**
 * Progress track with a knob; drag or tap to seek, left/right keys nudge. [thick] is the Now Playing look (modern style): an 8 dp track that grows to 10 dp
 * under the finger, a bigger knob, a lighter [buffered] stretch behind the played part, and a haptic tick each tenth of the bar while dragging.
 */
@Composable
fun SeekBar(fraction: Float, onSeek: (Float) -> Unit, onNudge: (Float) -> Unit, modifier: Modifier = Modifier, onScrub: (Float?) -> Unit = {}, thick: Boolean = false, buffered: Float = 0f) {
    val sc = LocalScheme.current
    val style = LocalStyle.current
    val big = thick && style.modern
    var drag by remember { mutableStateOf<Float?>(null) }
    val src = remember { MutableInteractionSource() }
    val focused by src.collectIsFocusedAsState()
    val haptic = androidx.compose.ui.platform.LocalHapticFeedback.current
    // after the finger lifts the knob stays where it was let go until playback reports that position (it used to jump back to the old place for up to a quarter of a second, then forward)
    var pending by remember { mutableStateOf<Float?>(null) }
    val shown = SeekHold.shown(drag, pending, fraction)
    androidx.compose.runtime.LaunchedEffect(pending, fraction) { if (SeekHold.settled(pending, fraction)) pending = null }
    androidx.compose.runtime.LaunchedEffect(pending) { if (pending != null) { kotlinx.coroutines.delay(SeekHold.GIVE_UP_MS); pending = null } }
    val scrub by androidx.compose.runtime.rememberUpdatedState(onScrub)
    androidx.compose.runtime.LaunchedEffect(drag, pending) { scrub(drag ?: pending) }
    // pointerInput(Unit) outlives recompositions: call the latest onSeek, not the one captured before the track had a duration.
    val seek by androidx.compose.runtime.rememberUpdatedState(onSeek)
    // the track and the knob grow under the finger (settle at once when animations are off)
    val dragging = drag != null
    val trackTarget = if (big) (if (dragging) 10f else 8f) else if (style.modern) 4f else 8f
    val knobTarget = if (big) (if (dragging) 12f else 8f) else if (dragging || focused) 11f else if (style.modern) 7f else 8f
    val trackDp by androidx.compose.animation.core.animateFloatAsState(trackTarget, if (Tweaks.motion) androidx.compose.animation.core.tween(120) else androidx.compose.animation.core.snap(), label = "track")
    val knobDp by androidx.compose.animation.core.animateFloatAsState(knobTarget, if (Tweaks.motion) androidx.compose.animation.core.spring(stiffness = androidx.compose.animation.core.Spring.StiffnessMedium) else androidx.compose.animation.core.snap(), label = "knob")
    Box(
        modifier.height(if (big) 48.dp else 44.dp)
            .onKeyEvent {
                if (it.type == KeyEventType.KeyDown && it.key == Key.DirectionLeft) { onNudge(-1f); true }
                else if (it.type == KeyEventType.KeyDown && it.key == Key.DirectionRight) { onNudge(1f); true } else false
            }
            .combinedClickableNoRipple(src, true, {})
            // one gesture from touch-down: the bar owns the finger (consumed at once, so Now Playing's swipe-down, track
            // swipes and swipe-back never take it), follows it, and seeks where it lifts -- a tap is just a zero-length drag
            .pointerInput(Unit) {
                awaitEachGesture {
                    val down = awaitFirstDown(requireUnconsumed = false)
                    down.consume()
                    drag = ((down.position.x - 12.dp.toPx()) / (size.width - 24.dp.toPx()).coerceAtLeast(1f)).coerceIn(0f, 1f)
                    var lifted = false
                    while (true) {
                        val ev = awaitPointerEvent()
                        val c = ev.changes.firstOrNull { it.id == down.id } ?: break
                        val prev = drag ?: 0f
                        drag = ((c.position.x - 12.dp.toPx()) / (size.width - 24.dp.toPx()).coerceAtLeast(1f)).coerceIn(0f, 1f)
                        if (ScrubModel.tickCrossed(prev, drag ?: prev)) haptic.performHapticFeedback(androidx.compose.ui.hapticfeedback.HapticFeedbackType.TextHandleMove)
                        c.consume()
                        if (!c.pressed) { lifted = true; break }
                    }
                    if (lifted) drag?.let { pending = it; seek(it) }
                    drag = null
                }
            },
        contentAlignment = Alignment.CenterStart,
    ) {
        Canvas(Modifier.fillMaxWidth().height(if (big) 40.dp else 36.dp)) {
            val modern = style.modern
            val th = trackDp.dp.toPx(); val cy = size.height / 2
            val pad = 12.dp.toPx(); val tw = size.width - 2 * pad   // the knob's radius of room at each end: it is never cut off at the start or the end
            val round = androidx.compose.ui.geometry.CornerRadius(th / 2)
            drawRoundRect(sc.onBg.copy(alpha = .22f), Offset(pad, cy - th / 2), Size(tw, th), round)
            // what is already loaded, ahead of the playing position
            if (big && buffered > shown) drawRoundRect(sc.onBg.copy(alpha = .38f), Offset(pad, cy - th / 2), Size(tw * buffered.coerceIn(0f, 1f), th), round)
            val fx = pad + tw * shown
            if (fx > pad && modern) drawRoundRect(sc.onBg, Offset(pad, cy - th / 2), Size(fx - pad, th), round)
            else if (fx > pad) drawRoundRect(Brush.verticalGradient(listOf(sc.accentLight, sc.accentDark), cy - th / 2, cy + th / 2), Offset(pad, cy - th / 2), Size(fx - pad, th), round)
            drawCircle(Color.White, knobDp.dp.toPx(), Offset(fx, cy))
            if (!modern) drawCircle(sc.accent, 4.dp.toPx(), Offset(fx, cy))
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
    // the scrim fades in and the sheet slides up; tapping outside slides it down again before it is removed
    val shown = remember { androidx.compose.animation.core.MutableTransitionState(false).apply { targetState = true } }
    val scrim by androidx.compose.animation.core.animateFloatAsState(if (shown.targetState) 1f else 0f, androidx.compose.animation.core.tween(220), label = "scrim")
    val scope = androidx.compose.runtime.rememberCoroutineScope()
    fun closeAnimated() { if (!shown.targetState) return; shown.targetState = false; scope.launch { kotlinx.coroutines.delay(230L); onDismiss() } }
    Box(Modifier.fillMaxSize().background(Color(0x99000000).copy(alpha = 0.6f * scrim)).pointerInput(Unit) { detectTapGestures { closeAnimated() } }) {
      androidx.compose.foundation.layout.BoxWithConstraints(Modifier.fillMaxSize().safeArea()) {
        androidx.compose.animation.AnimatedVisibility(
            shown, Modifier.align(Alignment.BottomCenter),
            enter = androidx.compose.animation.slideInVertically(androidx.compose.animation.core.spring(0.82f, 420f)) { it } + androidx.compose.animation.fadeIn(androidx.compose.animation.core.tween(160)),
            exit = androidx.compose.animation.slideOutVertically(androidx.compose.animation.core.tween(220)) { it } + androidx.compose.animation.fadeOut(androidx.compose.animation.core.tween(200)),
        ) {
        // capped to the screen and scrollable, so long lists (EQ presets, landscape phones) never run off the bottom
        Column(
            Modifier.fillMaxWidth().widthIn(max = 640.dp)
                .heightIn(max = maxHeight * 0.9f)
                .clip(RoundedCornerShape(topStart = 22.dp, topEnd = 22.dp))
                .background(Brush.verticalGradient(listOf(sc.top.copy(alpha = 1f).mix(Color.Black, .35f), sc.bottom.mix(Color.Black, .25f))))
                .pointerInput(Unit) { detectTapGestures { } }
                .padding(bottom = 12.dp),
        ) {
            Column(Modifier.padding(horizontal = 20.dp, vertical = 14.dp)) {
                Txt(title, size = 18f, weight = FontWeight.Bold)
                if (subtitle != null) Txt(subtitle, size = 13f, color = sc.onBgDim)
            }
            Column(Modifier.weight(1f, fill = false).verticalScroll(androidx.compose.foundation.rememberScrollState())) {
                items.forEachIndexed { i, item ->
                    IpodRow(onClick = { onDismiss(); item.onClick() }, height = 54.dp, focusRequester = if (i == 0) first else null,
                        leading = { GlyphIcon(item.glyph, Modifier.size(24.dp), if (LocalRowHi.current) Color.White else sc.onBg) }) { hi -> Txt(item.label, size = 16f, color = if (hi) Color.White else sc.onBg) }
                }
            }
        }
        }
      }
    }
}

fun Color.mix(other: Color, t: Float) = Color(
    red + (other.red - red) * t, green + (other.green - green) * t, blue + (other.blue - blue) * t, alpha,
)

/** Text field for the service setup forms: no autocorrect (URLs and keys), Next moves to the following field, Done closes
 * the keyboard on the last one, and [secret] masks passwords. */
@Composable
fun SetupTextField(
    value: String, onValueChange: (String) -> Unit, modifier: Modifier = Modifier, singleLine: Boolean = true,
    cursorBrush: Brush = SolidColor(Color.White), textStyle: TextStyle = TextStyle(color = Color.White, fontSize = 16.sp),
    secret: Boolean = false, last: Boolean = false,
) {
    val focus = androidx.compose.ui.platform.LocalFocusManager.current
    BasicTextField(
        value, onValueChange, modifier, singleLine = singleLine, cursorBrush = cursorBrush, textStyle = textStyle,
        keyboardOptions = androidx.compose.foundation.text.KeyboardOptions(
            keyboardType = if (secret) androidx.compose.ui.text.input.KeyboardType.Password else androidx.compose.ui.text.input.KeyboardType.Uri,
            autoCorrect = false,
            imeAction = if (last) androidx.compose.ui.text.input.ImeAction.Done else androidx.compose.ui.text.input.ImeAction.Next,
        ),
        keyboardActions = androidx.compose.foundation.text.KeyboardActions(
            onNext = { focus.moveFocus(androidx.compose.ui.focus.FocusDirection.Down) },
            onDone = { focus.clearFocus() },
        ),
        visualTransformation = if (secret) androidx.compose.ui.text.input.PasswordVisualTransformation() else androidx.compose.ui.text.input.VisualTransformation.None,
    )
}

/** White or near-black, whichever reads better on this colour (WCAG relative luminance). */
fun Color.readableInk(): Color = if (luminance() > 0.4f) Color(0xFF111114) else Color.White

/** The Hi-Res download button (administrators): an amber outlined pill with a star, so it reads as a deliberate extra next to the normal Download. */
@Composable
fun HiResPill(onClick: () -> Unit, modifier: Modifier = Modifier) {
    val amber = Color(0xFFFFC857)
    Row(
        modifier.height(32.dp).clip(RoundedCornerShape(50)).background(amber.copy(alpha = 0.10f)).border(1.dp, amber.copy(alpha = 0.45f), RoundedCornerShape(50))
            .then(Modifier.clickable(onClick = onClick)).padding(horizontal = 12.dp),
        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(5.dp),
    ) {
        Txt("✦", size = 12f, weight = FontWeight.Bold, color = amber)
        Txt("Hi-Res", size = 13f, weight = FontWeight.Bold, color = amber)
    }
}

/** The badge for a Hi-Res file: filled amber, so it stands out from the quiet source badges. */
@Composable
fun HiResBadge(compact: Boolean = false) {
    Box(Modifier.clip(RoundedCornerShape(50)).background(Color(0xFFFFC857)).padding(horizontal = if (compact) 6.dp else 8.dp, vertical = if (compact) 1.dp else 3.dp)) {
        Txt("✦ Hi-Res", size = if (compact) 10f else 11f, weight = FontWeight.ExtraBold, color = Color(0xFF2A1D00))
    }
}


/** Opens Retro (the iPods, the desk and the listening scenes, as on the web): ui/RetroActivity.kt. Back comes back here. */
fun openRetro(ctx: android.content.Context) {
    ctx.startActivity(android.content.Intent(ctx, com.ipodemu.ui.RetroActivity::class.java))
}
