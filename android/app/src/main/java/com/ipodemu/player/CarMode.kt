package com.ipodemu.player

import androidx.activity.compose.BackHandler
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.systemBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.media3.common.util.UnstableApi
import com.ipodemu.playback.LiveAnalysis

/** Every live graph, the Visualizer first (owner, 12 Nov: "car mode should have all the graphs"): a tap or a swipe on the graph moves through them. */
internal val CarVisuals: List<Pair<String, String>> = infoGraphs(0, 2).filter { it.first != "rate" }.sortedBy { if (it.first == "leds") 0 else 1 }

/** The next graph after [mode] (round to the first after the last; an unknown one starts at the first). */
internal fun nextCarVisual(mode: String): String {
    val i = CarVisuals.indexOfFirst { it.first == mode }
    return CarVisuals[(i + 1) % CarVisuals.size].first
}

/** The graph before [mode] (round to the last before the first). */
internal fun prevCarVisual(mode: String): String {
    val i = CarVisuals.indexOfFirst { it.first == mode }.coerceAtLeast(0)
    return CarVisuals[(i - 1 + CarVisuals.size) % CarVisuals.size].first
}

/**
 * Car mode (owner, 12 Nov: "easy on/off, keeps the screen on, less clutter, nice visualisers and buttons"): the song big, a live visualiser
 * (a tap changes it), and only the buttons a driver needs, each at least 72 dp: previous, play / pause, next, favourite, the DJ and the mic.
 * A swipe sideways anywhere skips. The screen stays on while it is up. Back or Exit closes it. The same as the web's /car page.
 */
@OptIn(UnstableApi::class)
@Composable
fun CarScreen(onExit: () -> Unit) {
    val app = LocalApp.current
    val snap = rememberSnap(app.player, app.prefs)
    BackHandler(onBack = onExit)
    val view = LocalView.current
    DisposableEffect(Unit) { val was = view.keepScreenOn; view.keepScreenOn = true; onDispose { view.keepScreenOn = was } }
    val t = snap.track
    val ctx = androidx.compose.ui.platform.LocalContext.current
    val analysis = remember {
        // the song itself at full level, as the web's graphs read it
        LiveAnalysis(app.player.live)
    }
    var viz by rememberSaveable { mutableStateOf(app.prefs.carVisual.ifEmpty { "leds" }) }
    val p = if (t != null) rememberSleevePalette(t) else null
    val accent = p?.main?.value ?: LocalScheme.current.accent
    val tone = p?.tone?.value ?: Color(0xFF24525A)
    val ground = p?.ground?.value ?: Color(0xFF101416)
    val ink = Color(0xFFF4EFE6)
    val dim = Color(0xA6F4EFE6)
    BoxWithConstraints(
        Modifier.fillMaxSize().background(Brush.verticalGradient(listOf(tone, ground, Color.Black)))
            .systemBarsPadding().padding(16.dp),
    ) {
        val wide = maxWidth > maxHeight
        val top: @Composable () -> Unit = {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Box(Modifier.clip(RoundedCornerShape(50)).background(accent.copy(alpha = .22f)).padding(horizontal = 12.dp, vertical = 6.dp)) {
                    MonoLabel("● Car mode", size = 12f, color = accent)
                }
                Spacer(Modifier.weight(1f))
                Box(
                    Modifier.height(56.dp).clip(RoundedCornerShape(28.dp)).background(ink.copy(alpha = .14f))
                        .semantics { contentDescription = "Exit car mode"; role = Role.Button }.clickable(onClick = onExit).padding(horizontal = 22.dp),
                    contentAlignment = Alignment.Center,
                ) { Txt("✕  Exit", size = 18f, weight = FontWeight.Bold, color = ink) }
            }
        }
        val song: @Composable (Modifier) -> Unit = { m ->
            Row(m, verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                ArtImage(t?.artKey, Modifier.size(if (wide) 96.dp else 84.dp), corner = 16.dp)
                Column(Modifier.weight(1f)) {
                    Txt(t?.title ?: "Nothing playing", size = 28f, weight = FontWeight.ExtraBold, color = ink, maxLines = 2)
                    Txt(t?.artist ?: "Pick something on Home", size = 18f, color = dim, maxLines = 1)
                }
            }
        }
        val pos = rememberPosition(app.player, snap.playing)
        val seek: @Composable () -> Unit = { CarSeek(pos.value, app.player.durationMs, accent, ink) { app.player.seekTo(it) } }
        val graph: @Composable (Modifier) -> Unit = { m ->
            BoxWithConstraints(
                m.clip(RoundedCornerShape(24.dp)).background(ground.copy(alpha = .5f))
                    .semantics { contentDescription = "Graph: " + (CarVisuals.firstOrNull { it.first == viz }?.second ?: "") + ". Tap or swipe for the next one"; role = Role.Button }
                    .clickable { viz = nextCarVisual(viz); app.prefs.carVisual = viz }
                    // a swipe on the graph changes the graph, never the song
                    .pointerInput(Unit) {
                        var dx = 0f
                        detectHorizontalDragGestures(onDragStart = { dx = 0f }, onDragEnd = {
                            if (dx < -80f) { viz = nextCarVisual(viz); app.prefs.carVisual = viz } else if (dx > 80f) { viz = prevCarVisual(viz); app.prefs.carVisual = viz }
                        }) { c, d -> c.consume(); dx += d }
                    }.padding(10.dp),
            ) {
                LiveGraph(viz, analysis, snap.playing, maxHeight, accent, tone, ground)
                MonoLabel(CarVisuals.firstOrNull { it.first == viz }?.second ?: "", Modifier.align(Alignment.TopEnd), size = 12f, color = Color(0xA6F4EFE6))
            }
        }
        val controls: @Composable (Modifier) -> Unit = { m ->
            Column(m, verticalArrangement = Arrangement.spacedBy(14.dp)) {
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
                    CarButton("Previous", Modifier.weight(1f), 88.dp, tone, ink, onClick = { app.player.prev() }) { GlyphIcon(Glyph.PREV, Modifier.size(36.dp), ink) }
                    CarButton(if (snap.playing) "Pause" else "Play", Modifier.weight(1.4f), 88.dp, accent, ink, onClick = { app.player.toggle() }) {
                        GlyphIcon(if (snap.playing) Glyph.PAUSE else Glyph.PLAY, Modifier.size(44.dp), Color(DesignColour.readableOn(accent.toArgb())))
                    }
                    CarButton("Next", Modifier.weight(1f), 88.dp, tone, ink, onClick = { app.player.next() }) { GlyphIcon(Glyph.NEXT, Modifier.size(36.dp), ink) }
                }
                // the player bar's other buttons too (owner, 12 Nov: "keep this, or what you can of it, in car mode"): shuffle and repeat at the ends
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
                    CarButton(if (snap.shuffle) "Shuffle on" else "Shuffle off", Modifier.weight(1f), 72.dp, if (snap.shuffle) accent.copy(alpha = .3f) else ink.copy(alpha = .12f), ink, on = snap.shuffle,
                        onClick = { app.prefs.shuffle = !app.prefs.shuffle; app.player.applyModes() }) { GlyphIcon(Glyph.SHUFFLE, Modifier.size(28.dp), if (snap.shuffle) accent else ink) }
                    app.userData.rev
                    val fav = t != null && app.userData.isFavorite(t)
                    CarButton(if (fav) "Remove from favourites" else "Favourite", Modifier.weight(1f), 72.dp, ink.copy(alpha = .12f), ink, onClick = { t?.let { app.userData.toggleFavorite(it) } }) {
                        GlyphIcon(if (fav) Glyph.HEART_FILLED else Glyph.HEART, Modifier.size(30.dp), if (fav) accent else ink)
                    }
                    snap.dj?.let { dj ->
                        CarButton(djLabel(dj), Modifier.weight(1f), 72.dp, if (dj) accent.copy(alpha = .3f) else ink.copy(alpha = .12f), ink, on = dj, onClick = { djTap(app) }) {
                            GlyphIcon(Glyph.DJ, Modifier.size(32.dp), if (dj) accent else ink)
                        }
                        Box(Modifier.weight(1f).height(72.dp).clip(RoundedCornerShape(24.dp)).background(ink.copy(alpha = .12f)), contentAlignment = Alignment.Center) {
                            DjMicButton(64.dp, tint = ink, glass = Color.Transparent, lit = accent)
                        }
                    }
                    val rep = snap.repeat != com.ipodemu.playback.RepeatCycle.OFF
                    CarButton(com.ipodemu.playback.RepeatCycle.label(snap.repeat), Modifier.weight(1f), 72.dp, if (rep) accent.copy(alpha = .3f) else ink.copy(alpha = .12f), ink, on = rep, onClick = { repeatTap(app) }) {
                        GlyphIcon(if (snap.repeat == com.ipodemu.playback.RepeatCycle.ONE) Glyph.REPEAT_ONE else Glyph.REPEAT, Modifier.size(28.dp), if (rep) accent else ink)
                    }
                }
            }
        }
        if (wide) Row(Modifier.fillMaxSize(), horizontalArrangement = Arrangement.spacedBy(18.dp)) {
            Column(Modifier.weight(1f).fillMaxHeight(), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                top(); song(Modifier.fillMaxWidth()); seek(); graph(Modifier.weight(1f).fillMaxWidth())
            }
            controls(Modifier.weight(1f).fillMaxHeight().padding(top = 72.dp))
        } else Column(Modifier.fillMaxSize(), verticalArrangement = Arrangement.spacedBy(16.dp)) {
            top(); song(Modifier.fillMaxWidth()); seek(); graph(Modifier.weight(1f).fillMaxWidth()); controls(Modifier.fillMaxWidth())
        }
    }
}

/** A big rounded car button that dips when pressed; [on] adds an On state for TalkBack. */
@Composable
private fun CarButton(label: String, modifier: Modifier, height: Dp, bg: Color, ink: Color, on: Boolean? = null, onClick: () -> Unit = {}, icon: @Composable () -> Unit) {
    val src = remember { MutableInteractionSource() }
    val pressed by src.collectIsPressedAsState()
    val s by animateFloatAsState(if (pressed) .93f else 1f, spring(.55f, 700f), label = "carBtn")
    Box(
        modifier.height(height).graphicsLayer { scaleX = s; scaleY = s }.clip(RoundedCornerShape(28.dp)).background(bg)
            .semantics { contentDescription = label; role = Role.Button; if (on != null) stateDescription = if (on) "On" else "Off" }
            .clickable(src, null, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) { icon() }
}


/** Car mode's seek bar: thick, a tap or a drag anywhere on it moves the song, the times beside it. */
@Composable
private fun CarSeek(posMs: Long, durMs: Long, accent: Color, ink: Color, onSeek: (Long) -> Unit) {
    var drag by remember { mutableStateOf(-1f) }
    val dur = durMs.coerceAtLeast(1)
    val frac = if (drag >= 0f) drag else (posMs.toFloat() / dur).coerceIn(0f, 1f)
    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
        MonoLabel(fmtTime((frac * dur).toLong()), size = 13f, color = ink.copy(alpha = .7f), upper = false)
        Box(Modifier.weight(1f).height(44.dp)
            .semantics { contentDescription = "Seek"; stateDescription = fmtTime((frac * dur).toLong()) + " of " + fmtTime(dur) }
            .pointerInput(durMs) { detectTapGestures { o -> onSeek(((o.x / size.width).coerceIn(0f, 1f) * durMs).toLong()) } }
            .pointerInput(durMs) {
                detectHorizontalDragGestures(onDragStart = { o -> drag = (o.x / size.width).coerceIn(0f, 1f) }, onDragEnd = { onSeek((drag * durMs).toLong()); drag = -1f }, onDragCancel = { drag = -1f }) { c, _ -> c.consume(); drag = (c.position.x / size.width).coerceIn(0f, 1f) }
            }
            .drawBehind {
                val y = size.height / 2; val hh = 10.dp.toPx()
                drawRoundRect(ink.copy(alpha = .2f), androidx.compose.ui.geometry.Offset(0f, y - hh / 2), androidx.compose.ui.geometry.Size(size.width, hh), androidx.compose.ui.geometry.CornerRadius(hh))
                drawRoundRect(accent, androidx.compose.ui.geometry.Offset(0f, y - hh / 2), androidx.compose.ui.geometry.Size(size.width * frac, hh), androidx.compose.ui.geometry.CornerRadius(hh))
                drawCircle(ink, (if (drag >= 0f) 14 else 11).dp.toPx(), androidx.compose.ui.geometry.Offset(size.width * frac, y))
            })
        MonoLabel(fmtTime(dur), size = 13f, color = ink.copy(alpha = .7f), upper = false)
    }
}
