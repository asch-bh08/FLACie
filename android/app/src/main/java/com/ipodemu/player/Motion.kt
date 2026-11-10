package com.ipodemu.player

import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.VisibilityThreshold
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.foundation.interaction.collectIsPressedAsState

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

/** Where an incoming page starts, as fractions: [slide0] of its width to the right, [rise0] dp below, [alpha0] see-through. It springs to rest. */
data class PageStart(val slide0: Float, val rise0: Float, val alpha0: Float)

/**
 * How a page change moves, matching the web's pages (owner, 9 Nov: "peak 2026" motion everywhere): deeper slides in from the right and fades up,
 * a tab change cross-fades with a small rise, Back only fades in (the swipe-back drag already moved the page). All off with Animations off.
 */
enum class PageMove(val start: PageStart) {
    PUSH(PageStart(0.28f, 0f, 0.4f)), BACK(PageStart(-0.06f, 0f, 0.5f)), TAB(PageStart(0f, 10f, 0f)), NONE(PageStart(0f, 0f, 1f));
    companion object {
        fun of(motion: Boolean, fromDepth: Int, toDepth: Int, sameTop: Boolean): PageMove = when {
            !motion || sameTop -> NONE
            toDepth > fromDepth -> PUSH
            toDepth < fromDepth -> BACK
            else -> TAB
        }
    }
}

/** The spring every page change uses: settles quickly with no wobble, so a page never looks like it bounces. */
fun <T> pageSpring() = spring<T>(dampingRatio = 0.92f, stiffness = 520f)

/** A row in a keyed list glides to its new place when the list changes (Up next reordered, a song removed) and fades in/out; off with Animations off. */
@Composable
fun Modifier.glide(scope: androidx.compose.foundation.lazy.LazyItemScope): Modifier =
    if (!Tweaks.motion) this else with(scope) { this@glide.animateItem(fadeInSpec = tween(180), placementSpec = spring(dampingRatio = 0.86f, stiffness = 420f, visibilityThreshold = androidx.compose.ui.unit.IntOffset.VisibilityThreshold), fadeOutSpec = tween(140)) }

/** The same for a card in a keyed grid (Playlists, albums). */
@Composable
fun Modifier.glide(scope: androidx.compose.foundation.lazy.grid.LazyGridItemScope): Modifier =
    if (!Tweaks.motion) this else with(scope) { this@glide.animateItem(fadeInSpec = tween(180), placementSpec = spring(dampingRatio = 0.86f, stiffness = 420f, visibilityThreshold = androidx.compose.ui.unit.IntOffset.VisibilityThreshold), fadeOutSpec = tween(140)) }

/** Keys for the songs in Up next that follow the song, not its place: the path plus which copy of it this is (a song queued twice gets #0 and #1). */
object QueueKeys {
    fun of(paths: List<String>): List<String> {
        val seen = HashMap<String, Int>()
        return paths.map { p -> val n = seen.getOrDefault(p, 0); seen[p] = n + 1; "$p#$n" }
    }
}

/** A cover card dips a little while pressed and springs back (as the web's covers lift on press). */
@Composable
fun Modifier.pressLift(src: androidx.compose.foundation.interaction.MutableInteractionSource, to: Float = 0.96f): Modifier {
    val pressed by src.collectIsPressedAsState()
    return pressSpring(pressed, to)
}

/** 0 -> 1 as something becomes the chosen one (a tab, a chip), springing; jumps straight there with Animations off. */
@Composable
fun chosenAmount(selected: Boolean, label: String = "chosen"): Float {
    val target = if (selected) 1f else 0f
    if (!Tweaks.motion) return target
    val v by animateFloatAsState(target, spring(dampingRatio = 0.7f, stiffness = 500f), label = label)
    return v
}

/** Rows already shown once (by their list key): a row rises in the first time it appears and simply sits there when scrolled back to. */
private val seenRows: MutableSet<String> = java.util.Collections.newSetFromMap(object : java.util.LinkedHashMap<String, Boolean>(256, .75f, true) {
    override fun removeEldestEntry(eldest: MutableMap.MutableEntry<String, Boolean>?) = size > 600
})

/**
 * For rows that arrive after their screen opened (search results as they come in): the first few rise and fade in one after another, the first
 * time each is shown. Off with Animations off.
 */
@Composable
fun Modifier.riseFirstTime(key: String, index: Int): Modifier {
    if (!Tweaks.motion) return this
    val first = remember(key) { synchronized(seenRows) { seenRows.add(key) } }
    if (!first || index > 12) return this
    return riseIn(delayMs = index * 30, key = key, riseDp = 12f)
}
