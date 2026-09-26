package com.ipodemu.player

import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.animate
import androidx.compose.animation.core.spring
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.composed
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.PointerEventPass
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.launch
import kotlin.math.abs

/**
 * Swipe from the left edge to go back / close. Watches at the Initial pass so it works over lists and buttons; it only
 * claims the gesture once the finger is clearly moving right, so taps and vertical scrolls near the edge are untouched.
 */
fun Modifier.edgeSwipeBack(enabled: Boolean, edge: Float, onProgress: (Float) -> Unit, onBack: () -> Unit): Modifier =
    if (!enabled) this else pointerInput(Unit) {
        val slop = viewConfiguration.touchSlop
        val commit = size.width * 0.28f
        awaitEachGesture {
            val down = awaitFirstDown(requireUnconsumed = false, pass = PointerEventPass.Initial)
            if (down.position.x > edge) return@awaitEachGesture
            var claimed = false
            var dx = 0f
            while (true) {
                val ev = awaitPointerEvent(PointerEventPass.Initial)
                val c = ev.changes.firstOrNull { it.id == down.id } ?: break
                dx = c.position.x - down.position.x
                val dy = c.position.y - down.position.y
                if (!claimed) {
                    if (abs(dy) > slop && abs(dy) > abs(dx)) break
                    if (dx > slop && dx > abs(dy)) claimed = true
                }
                if (claimed) { c.consume(); onProgress(dx.coerceAtLeast(0f)) }
                if (!c.pressed) break
            }
            if (claimed && dx > commit) onBack()
            onProgress(0f)
        }
    }

/** Horizontal flick that skips tracks: the element follows the finger, then springs back once it fires. */
fun Modifier.trackSwipe(onPrev: () -> Unit, onNext: () -> Unit): Modifier = composed {
    var x by remember { mutableFloatStateOf(0f) }
    val scope = rememberCoroutineScope()
    val commit = with(LocalDensity.current) { 84.dp.toPx() }
    fun settle() { scope.launch { animate(x, 0f, animationSpec = spring(0.75f, Spring.StiffnessMedium)) { v, _ -> x = v } } }
    this
        .graphicsLayer { translationX = x * 0.55f; alpha = 1f - (abs(x) / (commit * 6f)).coerceAtMost(0.4f) }
        .pointerInput(Unit) {
            detectHorizontalDragGestures(
                onDragEnd = {
                    if (x <= -commit) { onNext() }
                    else if (x >= commit) { onPrev() }
                    settle()
                },
                onDragCancel = { settle() },
            ) { c, dx -> c.consume(); x = (x + dx).coerceIn(-commit * 2f, commit * 2f) }
        }
}

class SwipeAction(val label: String, val glyph: Glyph, val color: () -> Color, val onTrigger: () -> Unit)

/** A row that reveals an action under it as it is dragged sideways (right = [right], left = [left]) and fires past a threshold. */
@Composable
fun SwipeRow(right: SwipeAction?, left: SwipeAction?, modifier: Modifier = Modifier, content: @Composable () -> Unit) {
    if (right == null && left == null) { Box(modifier) { content() }; return }
    val d = LocalDensity.current
    val scope = rememberCoroutineScope()
    val trigger = with(d) { 88.dp.toPx() }
    val limit = trigger * 1.5f
    var x by remember { mutableFloatStateOf(0f) }
    var armed by remember { mutableStateOf(false) }
    fun settle() { scope.launch { animate(x, 0f, animationSpec = spring(0.8f, Spring.StiffnessMediumLow)) { v, _ -> x = v } } }
    Box(
        modifier.pointerInput(right, left) {
            detectHorizontalDragGestures(
                onDragEnd = {
                    val act = if (x >= trigger) right else if (x <= -trigger) left else null
                    if (act != null) { act.onTrigger() }
                    armed = false; settle()
                },
                onDragCancel = { armed = false; settle() },
            ) { c, dx ->
                c.consume()
                val lo = if (left != null) -limit else 0f
                val hi = if (right != null) limit else 0f
                x = (x + dx * 0.9f).coerceIn(lo, hi)
                val a = abs(x) >= trigger
                if (a != armed) { armed = a; }
            }
        },
    ) {
        val act = if (x > 0f) right else if (x < 0f) left else null
        if (act != null) {
            Box(
                Modifier.align(if (x > 0f) Alignment.CenterStart else Alignment.CenterEnd).fillMaxHeight().width(with(d) { abs(x).toDp() })
                    .background(act.color().copy(alpha = if (armed) 1f else 0.72f)),
            ) {
                Box(Modifier.align(if (x > 0f) Alignment.CenterStart else Alignment.CenterEnd).width(88.dp).fillMaxHeight(), contentAlignment = Alignment.Center) {
                    GlyphIcon(act.glyph, Modifier.graphicsLayer { val s = if (armed) 1.25f else 0.9f; scaleX = s; scaleY = s }.size(26.dp), Color.White)
                }
            }
        }
        Box(Modifier.graphicsLayer { translationX = x }) { content() }
    }
}

/** Remembers the last row/button that held focus, so the wheel can put the highlight back after a touch clears it. */
object WheelFocus {
    var last: androidx.compose.ui.focus.FocusRequester? = null
    fun restore() { try { last?.requestFocus() } catch (_: Exception) { last = null } }
}

fun Modifier.wheelTracked(): Modifier = composed {
    val fr = remember { androidx.compose.ui.focus.FocusRequester() }
    this.focusRequester(fr).onFocusChanged { if (it.isFocused) WheelFocus.last = fr }
}
