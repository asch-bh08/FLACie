package com.ipodemu.player

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.animation.core.tween
import androidx.compose.ui.graphics.graphicsLayer
import kotlinx.coroutines.launch
import androidx.compose.foundation.gestures.detectVerticalDragGestures
import com.ipodemu.playback.PlayerController

/** Slim full-width bar at the bottom of every screen except Home (which leads with the Now Playing card). */
@Composable
fun MiniPlayer(snap: PlayerSnap, nav: PlayerNav) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val t = snap.track ?: return
    val pos by rememberPosition(app.player, snap.playing)
    val dur = app.player.durationMs
    val frac = if (dur > 0) (pos.toFloat() / dur).coerceIn(0f, 1f) else 0f
    Box(
        Modifier.fillMaxWidth().navigationBarsPadding().height(56.dp)
            .background(Brush.verticalGradient(listOf(Color(0x40FFFFFF), Color(0x1AFFFFFF))))
            .trackSwipe({ app.player.prev() }, { app.player.next() })
            .clickable { nav.nowPlaying = true }
            .pointerInput(Unit) {
                var up = 0f
                detectVerticalDragGestures(onDragStart = { up = 0f }, onDragEnd = { up = 0f }) { _, dy -> up += dy; if (up < -28f) { up = 0f; nav.nowPlaying = true } }
            },
    ) {
        Box(Modifier.align(Alignment.TopStart).fillMaxWidth().height(1.dp).background(sc.onBg.copy(alpha = .16f)))
        Row(Modifier.fillMaxSize().padding(start = 8.dp, end = 8.dp, top = 2.dp), verticalAlignment = Alignment.CenterVertically) {
            ArtImage(t.artKey, Modifier.size(40.dp), thumb = true, corner = 7.dp)
            Column(Modifier.weight(1f).padding(horizontal = 10.dp)) {
                Txt(t.title, size = 14f, weight = FontWeight.SemiBold)
                Txt(t.artist.ifEmpty { t.album }, size = 12f, color = sc.onBgDim)
            }
            GlossButton({ app.player.toggle() }, size = 38.dp, primary = true) { GlyphIcon(if (snap.playing) Glyph.PAUSE else Glyph.PLAY, Modifier.size(20.dp), Color.White) }
            Box(Modifier.size(6.dp))
            GlossButton({ app.player.next() }, size = 34.dp) { GlyphIcon(Glyph.NEXT, Modifier.size(17.dp), Color.White) }
        }
        Box(Modifier.align(Alignment.BottomStart).fillMaxWidth().height(2.dp).background(sc.onBg.copy(alpha = .14f))) {
            Box(Modifier.fillMaxWidth(frac).fillMaxHeight().background(sc.accent))
        }
    }
}

/** Home's lead card: what's playing right now, with play/pause and skip, or a prompt to start something. */
@Composable
fun NowPlayingCard(snap: PlayerSnap, nav: PlayerNav, onShuffleAll: () -> Unit) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val t = snap.track
    val shape = RoundedCornerShape(16.dp)
    val pos by rememberPosition(app.player, snap.playing && t != null)
    val dur = app.player.durationMs
    val frac = if (dur > 0) (pos.toFloat() / dur).coerceIn(0f, 1f) else 0f
    Box(
        Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 10.dp).clip(shape)
            .background(Brush.verticalGradient(listOf(Color(0x40FFFFFF), Color(0x1AFFFFFF))))
            .border(1.dp, sc.cardBorder, shape)
            .clickable { if (t != null) nav.nowPlaying = true else onShuffleAll() },
    ) {
        Row(Modifier.padding(10.dp), verticalAlignment = Alignment.CenterVertically) {
            ArtImage(t?.artKey, Modifier.size(64.dp), corner = 10.dp)
            Column(Modifier.weight(1f).padding(horizontal = 12.dp)) {
                if (t != null) {
                    Txt(if (snap.playing) "NOW PLAYING" else "PAUSED", size = 11f, weight = FontWeight.Bold, color = sc.accentLight)
                    Txt(t.title, size = 16f, weight = FontWeight.Bold)
                    Txt(t.artist.ifEmpty { t.album }, size = 13f, color = sc.onBgDim)
                } else {
                    Txt("Nothing playing", size = 16f, weight = FontWeight.Bold)
                    Txt("Tap to shuffle your library", size = 13f, color = sc.onBgDim)
                }
            }
            if (t != null) {
                GlossButton({ app.player.toggle() }, size = 44.dp, primary = true) { GlyphIcon(if (snap.playing) Glyph.PAUSE else Glyph.PLAY, Modifier.size(22.dp), Color.White) }
                Box(Modifier.size(6.dp))
                GlossButton({ app.player.next() }, size = 38.dp) { GlyphIcon(Glyph.NEXT, Modifier.size(18.dp), Color.White) }
            } else GlossButton({ onShuffleAll() }, size = 44.dp, primary = true) { GlyphIcon(Glyph.SHUFFLE, Modifier.size(22.dp), Color.White) }
        }
        if (t != null) Box(Modifier.align(Alignment.BottomStart).padding(horizontal = 12.dp).fillMaxWidth().height(3.dp).clip(RoundedCornerShape(2.dp)).background(sc.onBg.copy(alpha = .16f))) {
            Box(Modifier.fillMaxWidth(frac).fillMaxHeight().background(sc.accent))
        }
    }
}

@Composable
fun NowPlayingScreen(snap: PlayerSnap, nav: PlayerNav) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val t = snap.track
    var dragY by remember { mutableFloatStateOf(0f) }
    val scope = androidx.compose.runtime.rememberCoroutineScope()
    Box(
        Modifier.fillMaxSize()
            .graphicsLayer { translationY = dragY }
            .drawBehind { drawRect(Brush.verticalGradient(listOf(sc.top, sc.bottom))) }
            .pointerInput(Unit) {
                fun back() { scope.launch { androidx.compose.animation.core.animate(dragY, 0f) { v, _ -> dragY = v } } }
                detectVerticalDragGestures(
                    onDragEnd = {
                        if (dragY > size.height * 0.16f) scope.launch {
                            androidx.compose.animation.core.animate(dragY, size.height.toFloat(), animationSpec = tween(200)) { v, _ -> dragY = v }
                            nav.nowPlaying = false
                        } else back()
                    },
                    onDragCancel = { back() },
                ) { c, dy -> if (dy > 0f || dragY > 0f) { c.consume(); dragY = (dragY + dy).coerceAtLeast(0f) } }
            }
            .pointerInput(Unit) { detectTapGestures { } },
    ) {
        if (t == null) { EmptyState("Nothing playing"); return@Box }
        Box(Modifier.align(Alignment.TopCenter).statusBarsPadding().padding(top = 6.dp).size(width = 38.dp, height = 4.dp).clip(RoundedCornerShape(2.dp)).background(sc.onBg.copy(alpha = .3f)))
        BoxWithConstraints(Modifier.fillMaxSize().statusBarsPadding().navigationBarsPadding()) {
            val boxH = maxHeight
            val twoCol = maxWidth > maxHeight * 0.8f
            if (twoCol) {
                Row(Modifier.fillMaxSize().padding(horizontal = 14.dp, vertical = 8.dp), horizontalArrangement = Arrangement.spacedBy(16.dp), verticalAlignment = Alignment.CenterVertically) {
                    Box(Modifier.weight(0.52f).fillMaxHeight(), contentAlignment = Alignment.Center) {
                        NpArt(t.artKey, Modifier.widthIn(max = boxH - 24.dp))
                    }
                    Column(Modifier.weight(0.48f).fillMaxHeight().navigationBarsPadding(), verticalArrangement = Arrangement.SpaceEvenly) {
                        NpHeader(nav)
                        NpInfo(snap, center = false)
                        NpSeek(snap)
                        NpTransport(snap)
                        NpExtras(snap, nav)
                    }
                }
            } else {
                Column(Modifier.fillMaxSize().padding(horizontal = 22.dp, vertical = 10.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                    NpHeader(nav)
                    Box(Modifier.weight(1f).fillMaxWidth().padding(vertical = 10.dp), contentAlignment = Alignment.Center) {
                        NpArt(t.artKey, Modifier.widthIn(max = 420.dp))
                    }
                    NpInfo(snap, center = true)
                    Box(Modifier.height(6.dp))
                    NpSeek(snap)
                    NpTransport(snap)
                    Box(Modifier.height(10.dp))
                    NpExtras(snap, nav)
                    Box(Modifier.height(6.dp))
                }
            }
        }
    }
}

@Composable
private fun NpHeader(nav: PlayerNav) {
    val sc = LocalScheme.current
    Row(Modifier.fillMaxWidth().height(44.dp), verticalAlignment = Alignment.CenterVertically) {
        GlossButton({ nav.nowPlaying = false }, size = 36.dp) { GlyphIcon(Glyph.DOWN, Modifier.size(22.dp), Color.White) }
        Txt("Now Playing", Modifier.weight(1f), size = 16f, weight = FontWeight.Bold, align = TextAlign.Center)
        GlossButton({ nav.nowPlaying = false; nav.push(Screen.Queue) }, size = 36.dp) { GlyphIcon(Glyph.QUEUE, Modifier.size(22.dp), Color.White) }
    }
}

@Composable
private fun NpArt(key: String?, modifier: Modifier) {
    val app = LocalApp.current
    ArtImage(
        key, modifier.trackSwipe({ app.player.prev() }, { app.player.next() }).aspectRatio(1f).shadow(28.dp, RoundedCornerShape(20.dp), clip = false, ambientColor = Color.Black, spotColor = Color.Black),
        corner = 20.dp,
    )
}

@Composable
private fun NpInfo(snap: PlayerSnap, center: Boolean) {
    val sc = LocalScheme.current
    val t = snap.track ?: return
    val a = if (center) TextAlign.Center else TextAlign.Start
    val app = LocalApp.current
    Column(Modifier.fillMaxWidth().trackSwipe({ app.player.prev() }, { app.player.next() }), horizontalAlignment = if (center) Alignment.CenterHorizontally else Alignment.Start) {
        Txt(t.title, Modifier.fillMaxWidth(), size = 24f, weight = FontWeight.Bold, align = a)
        Txt(t.artist.ifEmpty { "Unknown Artist" }, Modifier.fillMaxWidth().padding(top = 2.dp), size = 17f, color = sc.accentLight, align = a)
        if (t.album.isNotEmpty()) Txt(t.album, Modifier.fillMaxWidth(), size = 14f, color = sc.onBgDim, align = a)
    }
}

@Composable
private fun NpSeek(snap: PlayerSnap) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val pos by rememberPosition(app.player, snap.playing)
    val dur = app.player.durationMs
    Column(Modifier.fillMaxWidth()) {
        SeekBar(if (dur > 0) pos.toFloat() / dur else 0f,
            onSeek = { f -> if (dur > 0) app.player.seekTo((f * dur).toLong()) },
            onNudge = { d -> app.player.seekBy((d * 5000).toLong()) }, Modifier.fillMaxWidth())
        Row(Modifier.fillMaxWidth()) {
            Txt(fmtTime(pos), Modifier.weight(1f), size = 13f, color = sc.onBgDim)
            Txt("-" + fmtTime((dur - pos).coerceAtLeast(0)), size = 13f, color = sc.onBgDim)
        }
    }
}

@Composable
private fun NpTransport(snap: PlayerSnap) {
    val app = LocalApp.current
    val playFocus = remember { FocusRequester() }
    LaunchedEffect(Unit) { try { playFocus.requestFocus() } catch (_: Exception) {} }
    var bump by remember { mutableFloatStateOf(0f) }
    androidx.compose.foundation.layout.BoxWithConstraints(Modifier.fillMaxWidth()) {
        // shrink the row on narrow columns so all five buttons always fit
        val k = (maxWidth / 320.dp).coerceIn(0.62f, 1f)
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceEvenly, verticalAlignment = Alignment.CenterVertically) {
            GlossButton({ app.prefs.shuffle = !app.prefs.shuffle; app.player.applyModes(); bump += 1f }, size = 44.dp * k, primary = snap.shuffle) {
                GlyphIcon(Glyph.SHUFFLE, Modifier.size(22.dp * k), Color.White)
            }
            GlossButton({ app.player.prev() }, size = 56.dp * k) { GlyphIcon(Glyph.PREV, Modifier.size(26.dp * k), Color.White) }
            GlossButton({ app.player.toggle() }, Modifier.focusRequester(playFocus), size = 74.dp * k, primary = true) {
                GlyphIcon(if (snap.playing) Glyph.PAUSE else Glyph.PLAY, Modifier.size(38.dp * k), Color.White)
            }
            GlossButton({ app.player.next() }, size = 56.dp * k) { GlyphIcon(Glyph.NEXT, Modifier.size(26.dp * k), Color.White) }
            GlossButton({ app.prefs.repeat = (app.prefs.repeat + 1) % 3; app.player.applyModes(); bump += 1f }, size = 44.dp * k, primary = snap.repeat != 0) {
                GlyphIcon(if (snap.repeat == 2) Glyph.REPEAT_ONE else Glyph.REPEAT, Modifier.size(22.dp * k), Color.White)
            }
        }
    }
}

@Composable
private fun NpExtras(snap: PlayerSnap, nav: PlayerNav) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val t = snap.track ?: return
    app.userData.rev
    val fav = app.userData.isFavorite(t.path)
    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceEvenly, verticalAlignment = Alignment.CenterVertically) {
            GlossButton({ app.userData.toggleFavorite(t.path) }, size = 40.dp, primary = fav) { GlyphIcon(if (fav) Glyph.HEART_FILLED else Glyph.HEART, Modifier.size(22.dp), Color.White) }
            GlossButton({ nav.sheet = eqSheet(app) }, size = 44.dp) { GlyphIcon(Glyph.LIST, Modifier.size(22.dp), Color.White) }
            GlossButton({ nav.sheet = sleepSheet(app) }, size = 44.dp, primary = app.player.sleepMinutes > 0) { GlyphIcon(Glyph.CLOCK, Modifier.size(22.dp), Color.White) }
            GlossButton({ openTrackSheet(app, nav, t) }, size = 44.dp) { GlyphIcon(Glyph.MORE, Modifier.size(22.dp), Color.White) }
        }
    }
}

private fun eqSheet(app: com.ipodemu.App) = SheetSpec("Equalizer", app.prefs.eq, PlayerController.EQ_NAMES.map { n ->
    SheetItem(if (app.prefs.eq == n) "$n  (on)" else n, if (app.prefs.eq == n) Glyph.CHECK else Glyph.LIST) { app.prefs.eq = n; app.player.applyEq() }
})

private fun sleepSheet(app: com.ipodemu.App) = SheetSpec("Sleep Timer", if (app.player.sleepMinutes > 0) "${app.player.sleepMinutes} min" else "Off",
    listOf(0, 15, 30, 60, 90, 120).map { m -> SheetItem(if (m == 0) "Off" else "$m minutes", Glyph.CLOCK) { app.player.setSleepTimer(m) } })
