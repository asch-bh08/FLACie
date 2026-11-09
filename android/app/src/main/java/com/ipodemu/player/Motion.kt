package com.ipodemu.player

import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.graphicsLayer

/** Small shared motion: things pop, rise and spring instead of just appearing. */

/** Pops in (grows from a little smaller, fades up, with a springy overshoot) on first show and again whenever [key] changes: a new song's cover. */
@Composable
fun Modifier.popIn(key: Any?, from: Float = 0.86f): Modifier {
    if (!Tweaks.motion) return this
    val a = remember(key) { Animatable(0f) }
    LaunchedEffect(key) { a.animateTo(1f, spring(dampingRatio = 0.55f, stiffness = 380f)) }
    return this.graphicsLayer { val s = from + (1f - from) * a.value; scaleX = s; scaleY = s; alpha = (a.value * 2f).coerceIn(0f, 1f) }
}

/** Slides up a little and fades in when it first appears; [delayMs] staggers a few of these one after another; [riseDp] is how far it comes up from. */
@Composable
fun Modifier.riseIn(delayMs: Int = 0, key: Any? = Unit, riseDp: Float = 28f): Modifier {
    if (!Tweaks.motion) return this
    val a = remember(key) { Animatable(0f) }
    LaunchedEffect(key) { a.animateTo(1f, tween(420, delayMs, FastOutSlowInEasing)) }
    return this.graphicsLayer { translationY = (1f - a.value) * riseDp * density; alpha = a.value }
}

/** When a list or grid screen first showed: its rows rise in only then, not every time one scrolls back into view (that would flicker). */
@Composable
fun rememberEntrance(): Long = remember { android.os.SystemClock.uptimeMillis() }

/**
 * A row or a card coming in with the screen: a short rise and fade, one after another down the first few (as the web's lists come in). Rows laid out
 * later, by scrolling, just appear. Only alpha and translation move, so nothing is laid out again and nothing can overlap its neighbour; off with
 * the app's Animations switch (Tweaks.motion), like all motion here.
 */
@Composable
fun Modifier.entranceRise(since: Long, index: Int): Modifier {
    if (!Tweaks.motion) return this
    val fresh = remember { android.os.SystemClock.uptimeMillis() - since < 500L }
    if (!fresh) return this
    return this.riseIn(delayMs = index.coerceIn(0, 10) * 30, riseDp = 12f)
}


/** A press makes the thing dip and spring back. */
@Composable
fun Modifier.pressSpring(pressed: Boolean, to: Float = 0.94f): Modifier {
    if (!Tweaks.motion) return this
    val s by animateFloatAsState(if (pressed) to else 1f, spring(dampingRatio = Spring.DampingRatioMediumBouncy, stiffness = Spring.StiffnessMedium), label = "press")
    return this.graphicsLayer { scaleX = s; scaleY = s }
}

/** A bounce each time [key] changes (a heart turning solid), not on first show. */
@Composable
fun Modifier.bumpOnChange(key: Any?): Modifier {
    if (!Tweaks.motion) return this
    val a = remember { Animatable(1f) }
    val first = remember { booleanArrayOf(true) }
    LaunchedEffect(key) {
        if (first[0]) { first[0] = false; return@LaunchedEffect }
        a.snapTo(0.6f); a.animateTo(1f, spring(dampingRatio = 0.35f, stiffness = 420f))
    }
    return this.graphicsLayer { scaleX = a.value; scaleY = a.value }
}
