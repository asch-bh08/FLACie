package com.ipodemu.player

import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.StartOffset
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.keyframes
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawWithContent
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.rotate
import androidx.compose.ui.graphics.graphicsLayer

/**
 * Home that moves on its own (owner, 12 Nov: "new animations need to be visible and alive"), as the web's design-j-motion.css "Alive": the cover behind
 * the greeting drifts and breathes, light sweeps across the mood tiles one after another, the artist playing now has a ring that turns. Only drawing
 * and graphics-layer changes (nothing is laid out again); all off with the app's Animations switch (Tweaks.motion).
 */

/** A slow drift and zoom, for a big picture behind text (28 s each way, as the web). */
@Composable
fun Modifier.drift(): Modifier {
    if (!Tweaks.motion) return this
    val t = rememberInfiniteTransition(label = "drift")
    val k by t.animateFloat(0f, 1f, infiniteRepeatable(tween(28000, easing = FastOutSlowInEasing), RepeatMode.Reverse), label = "driftK")
    return this.graphicsLayer {
        val s = 1.08f + .08f * k; scaleX = s; scaleY = s
        translationX = -size.width * .025f * k; translationY = size.height * .015f * k
    }
}

/** A band of light that sweeps across now and then (every 7 s; [phase] staggers tiles side by side), over what is drawn. */
@Composable
fun Modifier.sheen(phase: Int): Modifier {
    if (!Tweaks.motion) return this
    val t = rememberInfiniteTransition(label = "sheen")
    val x by t.animateFloat(
        -0.6f, 1.6f,
        infiniteRepeatable(keyframes { durationMillis = 7000; -0.6f at 0; -0.6f at 4900; 1.6f at 7000 using FastOutSlowInEasing }, initialStartOffset = StartOffset(1200 + phase * 700)),
        label = "sheenX",
    )
    return this.drawWithContent {
        drawContent()
        if (x > -0.5f && x < 1.5f) {
            val cx = size.width * x; val band = size.width * .35f
            rotate(-18f, Offset(cx, size.height / 2)) {
                drawRect(Brush.horizontalGradient(listOf(Color.Transparent, Color.White.copy(alpha = .22f), Color.Transparent), startX = cx - band / 2, endX = cx + band / 2),
                    Offset(cx - band / 2, -size.height), androidx.compose.ui.geometry.Size(band, size.height * 3))
            }
        }
    }
}

/** A soft round glow that wanders inside the thing (9 s each way; [phase] makes neighbours differ). */
@Composable
fun Modifier.blob(color: Color, phase: Int): Modifier {
    if (!Tweaks.motion) return this
    val t = rememberInfiniteTransition(label = "blob")
    val k by t.animateFloat(0f, 1f, infiniteRepeatable(tween(9000, easing = FastOutSlowInEasing), RepeatMode.Reverse, StartOffset(phase * 1300)), label = "blobK")
    return this.drawWithContent {
        val r = size.width * .35f * (1f + .35f * k)
        val c = Offset(size.width * (.2f + .5f * k), size.height * (.7f - .6f * k))
        drawCircle(Brush.radialGradient(listOf(color.copy(alpha = .45f), Color.Transparent), c, r), r, c)
        drawContent()
    }
}

/** A ring in [color] that turns (one turn in 3.2 s): the artist playing now. Off, nothing is drawn. */
@Composable
fun Modifier.turningRing(color: Color, on: Boolean, width: Float = 3f): Modifier {
    if (!on) return this
    val angle = if (Tweaks.motion) {
        val t = rememberInfiniteTransition(label = "ring")
        t.animateFloat(0f, 360f, infiniteRepeatable(tween(3200, easing = LinearEasing)), label = "ringA").value
    } else 0f
    return this.drawWithContent {
        drawContent()
        rotate(angle) {
            drawCircle(Brush.sweepGradient(listOf(color, Color.Transparent, color, Color.Transparent, color)), size.minDimension / 2 - width * density / 2, style = Stroke(width * density))
        }
    }
}

/** A slow pan from side to side, breathing a little (a minute each way): the song playing now behind Home's greeting (owner, 12 Nov). */
@Composable
fun Modifier.pan(): Modifier {
    if (!Tweaks.motion) return this.graphicsLayer { scaleX = 1.1f; scaleY = 1.1f }
    val t = rememberInfiniteTransition(label = "pan")
    val k by t.animateFloat(-1f, 1f, infiniteRepeatable(tween(60000, easing = FastOutSlowInEasing), RepeatMode.Reverse), label = "panK")
    return this.graphicsLayer {
        val s = 1.14f + .04f * (1f - k * k); scaleX = s; scaleY = s
        translationX = size.width * .035f * k
    }
}
