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
    val a = remember(key) { Animatable(0f) }
    LaunchedEffect(key) { a.animateTo(1f, spring(dampingRatio = 0.55f, stiffness = 380f)) }
    return this.graphicsLayer { val s = from + (1f - from) * a.value; scaleX = s; scaleY = s; alpha = (a.value * 2f).coerceIn(0f, 1f) }
}

/** Slides up a little and fades in when it first appears; [delayMs] staggers a few of these one after another. */
@Composable
fun Modifier.riseIn(delayMs: Int = 0, key: Any? = Unit): Modifier {
    val a = remember(key) { Animatable(0f) }
    LaunchedEffect(key) { a.animateTo(1f, tween(420, delayMs, FastOutSlowInEasing)) }
    return this.graphicsLayer { translationY = (1f - a.value) * 28f * density; alpha = a.value }
}


/** A press makes the thing dip and spring back. */
@Composable
fun Modifier.pressSpring(pressed: Boolean, to: Float = 0.94f): Modifier {
    val s by animateFloatAsState(if (pressed) to else 1f, spring(dampingRatio = Spring.DampingRatioMediumBouncy, stiffness = Spring.StiffnessMedium), label = "press")
    return this.graphicsLayer { scaleX = s; scaleY = s }
}

/** A bounce each time [key] changes (a heart turning solid), not on first show. */
@Composable
fun Modifier.bumpOnChange(key: Any?): Modifier {
    val a = remember { Animatable(1f) }
    val first = remember { booleanArrayOf(true) }
    LaunchedEffect(key) {
        if (first[0]) { first[0] = false; return@LaunchedEffect }
        a.snapTo(0.6f); a.animateTo(1f, spring(dampingRatio = 0.35f, stiffness = 420f))
    }
    return this.graphicsLayer { scaleX = a.value; scaleY = a.value }
}
