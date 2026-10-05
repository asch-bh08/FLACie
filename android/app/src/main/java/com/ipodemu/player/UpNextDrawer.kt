package com.ipodemu.player

import androidx.activity.compose.BackHandler
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectVerticalDragGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.ipodemu.App
import com.ipodemu.library.DownloadStage
import com.ipodemu.library.Track
import com.ipodemu.library.matchKey
import kotlinx.coroutines.launch

/** "Downloading", "Downloaded just now / today / this week", or null for a song that was not fetched by the app. */
fun downloadNote(app: App, t: Track): String? {
    val lib = app.library
    lib.downloads[lib.songKey(t.artist, t.title)]?.let { if (it.stage != DownloadStage.DONE && it.stage != DownloadStage.FAILED) return "Downloading" }
    val key = matchKey(t.title, t.artist)
    val e = app.userData.downloads.firstOrNull { matchKey(it.title, it.artist) == key } ?: return null
    val age = if (e.time > 0) System.currentTimeMillis() - e.time else Long.MAX_VALUE
    return when {
        age < 3_600_000L -> "Downloaded just now"
        age < 86_400_000L -> "Downloaded today"
        age < 7 * 86_400_000L -> "Downloaded this week"
        else -> "Downloaded"
    }
}

@Composable
private fun NoteChip(text: String) {
    val sc = LocalScheme.current
    val live = text == "Downloading"
    Txt(text, Modifier.clip(RoundedCornerShape(50)).background(if (live) sc.accent.copy(alpha = .22f) else Color(0x22FFFFFF)).padding(horizontal = 8.dp, vertical = 2.dp), size = 11f, weight = FontWeight.SemiBold, color = if (live) sc.accent else sc.onBgDim)
}

/** How far the Up next sheet is open (0 closed, 1 full), shared by the swipe zone and the "Up next" button in the player's actions. */
class UpNextState { val frac = Animatable(0f) }
val LocalUpNext = androidx.compose.runtime.staticCompositionLocalOf<UpNextState?> { null }

/**
 * The YouTube Music queue sheet, with no bar to look at: swipe up from the foot of the full-screen player (it follows the finger, slowly if you
 * like) or press "Up next", and the cover fades back while the sheet rises until the playing song is a small header and the queue fills the
 * screen: where it comes from, then cover, artist, album and whether the song was just downloaded. Tap a song to jump to it.
 */
@Composable
fun UpNextDrawer(snap: PlayerSnap, state: UpNextState, modifier: Modifier = Modifier) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val scope = rememberCoroutineScope()
    val frac = state.frac
    app.userData.rev
    val next = remember(snap.track?.path, snap.index, snap.shuffle, snap.repeat, snap.count) { app.player.upNext(30) }
    val from = app.player.contextName ?: "Your queue"
    BackHandler(enabled = frac.value > 0.02f) { scope.launch { frac.animateTo(0f, tween(240)) } }
    BoxWithConstraints(modifier.fillMaxSize()) {
        val full = maxHeight
        val range by androidx.compose.runtime.rememberUpdatedState(with(LocalDensity.current) { full.toPx() })
        val shape = RoundedCornerShape(topStart = 22.dp, topEnd = 22.dp)
        var lastDy = 0f
        val drag: suspend androidx.compose.ui.input.pointer.PointerInputScope.() -> Unit = {
            detectVerticalDragGestures(
                onDragStart = { lastDy = 0f },
                onDragEnd = { scope.launch { frac.animateTo(if ((lastDy < 0 && frac.value > 0.1f) || (lastDy >= 0 && frac.value > 0.85f)) 1f else 0f, tween(280)) } },
                onDragCancel = { scope.launch { frac.animateTo(if (frac.value > 0.5f) 1f else 0f, tween(200)) } },
            ) { _, dy -> lastDy = dy; scope.launch { frac.snapTo((frac.value - dy / range).coerceIn(0f, 1f)) } }
        }
        // the cover and controls fade back behind the rising sheet
        if (frac.value > 0.005f) Box(Modifier.fillMaxSize().background(Color.Black.copy(alpha = 0.78f * frac.value)).clickable(indication = null, interactionSource = remember { androidx.compose.foundation.interaction.MutableInteractionSource() }) { scope.launch { frac.animateTo(0f, tween(240)) } })
        // the invisible swipe zone at the foot of the player
        Box(Modifier.align(Alignment.BottomCenter).fillMaxWidth().height(56.dp).pointerInput(Unit) { drag() })
        if (frac.value > 0.005f) Column(
            Modifier.fillMaxSize().graphicsLayer { translationY = (1f - frac.value) * size.height }.clip(shape).background(Color(0xFF17171B)),
        ) {
            val cur = snap.track
            Box(Modifier.fillMaxWidth().height(76.dp).pointerInput(Unit) { drag() }) {
                Box(Modifier.align(Alignment.TopCenter).padding(top = 8.dp).size(width = 36.dp, height = 4.dp).clip(RoundedCornerShape(2.dp)).background(Color(0x55FFFFFF)))
                if (cur != null) Row(Modifier.fillMaxSize().padding(start = 20.dp, end = 8.dp, top = 12.dp).graphicsLayer { alpha = ((frac.value - 0.4f) * 2f).coerceIn(0f, 1f) }, verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(14.dp)) {
                    ArtImage(cur.artKey, Modifier.size(52.dp), thumb = true, corner = 8.dp)
                    Column(Modifier.weight(1f)) {
                        Txt(cur.title, size = 16f, weight = FontWeight.Bold, maxLines = 1)
                        Txt(cur.artist, size = 13f, color = sc.onBgDim, maxLines = 1)
                    }
                    IconAction(if (snap.playing) Glyph.PAUSE else Glyph.PLAY, if (snap.playing) "Pause" else "Play", { app.player.toggle() })
                }
            }
            LazyColumn(Modifier.fillMaxSize().graphicsLayer { alpha = ((frac.value - 0.25f) * 1.6f).coerceIn(0f, 1f) }, contentPadding = PaddingValues(bottom = 24.dp)) {
                item {
                    Column(Modifier.padding(start = 20.dp, top = 6.dp, bottom = 8.dp)) {
                        Txt("Playing from", size = 12f, color = sc.onBgDim)
                        Txt(from, size = 18f, weight = FontWeight.Bold, maxLines = 1)
                    }
                }
                if (cur != null) item { DrawerRow(cur, current = true, onClick = {}) }
                if (next.isEmpty()) item { Txt("Nothing queued after this song.", Modifier.padding(horizontal = 20.dp, vertical = 8.dp), size = 14f, color = sc.onBgDim) }
                itemsIndexed(next, key = { _, p -> p.first }) { _, p -> DrawerRow(p.second, current = false) { app.player.skipTo(p.first); scope.launch { frac.animateTo(0f, tween(240)) } } }
            }
        }
    }
}

@Composable
private fun DrawerRow(t: Track, current: Boolean, onClick: () -> Unit) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val note = downloadNote(app, t)
    Row(Modifier.fillMaxWidth().clickable(onClick = onClick).padding(horizontal = 20.dp, vertical = 7.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(14.dp)) {
        ArtImage(t.artKey, Modifier.size(52.dp), thumb = true, corner = 8.dp)
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Txt(t.title, size = 15f, weight = FontWeight.SemiBold, color = if (current) sc.accent else sc.onBg, maxLines = 1)
            Txt(listOf(t.artist, t.album).filter { it.isNotEmpty() }.joinToString(" · "), size = 12.5f, color = sc.onBgDim, maxLines = 1)
            if (note != null) NoteChip(note)
        }
        if (t.durationMs > 0) Txt(fmtTime(t.durationMs), size = 12.5f, color = sc.onBgDim)
    }
}
