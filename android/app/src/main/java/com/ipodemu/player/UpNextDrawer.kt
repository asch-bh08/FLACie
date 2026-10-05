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

/**
 * The YouTube Music bottom sheet: a bar at the foot of the full-screen player showing what plays next; drag it up (slowly, it follows the
 * finger) to see the whole queue with cover, artist, album and whether the song was just downloaded. Tap a song to jump to it.
 */
@Composable
fun UpNextDrawer(snap: PlayerSnap, modifier: Modifier = Modifier) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val scope = rememberCoroutineScope()
    val frac = remember { Animatable(0f) }
    app.userData.rev
    val next = remember(snap.track?.path, snap.index, snap.shuffle, snap.repeat, snap.count) { app.player.upNext(30) }
    BackHandler(enabled = frac.value > 0.02f) { scope.launch { frac.animateTo(0f, tween(220)) } }
    BoxWithConstraints(modifier.fillMaxSize()) {
        val peek = 68.dp
        val full = maxHeight - 64.dp
        val range = with(LocalDensity.current) { (full - peek).toPx() }
        val shape = RoundedCornerShape(topStart = 22.dp, topEnd = 22.dp)
        var lastDy = 0f
        Column(
            Modifier.align(Alignment.BottomCenter).fillMaxWidth().height(peek + (full - peek) * frac.value).clip(shape).background(Color(0xFF17171B)),
        ) {
            Column(
                Modifier.fillMaxWidth().height(peek)
                    .pointerInput(range) {
                        detectVerticalDragGestures(
                            onDragStart = { lastDy = 0f },
                            onDragEnd = { scope.launch { frac.animateTo(if ((lastDy < 0 && frac.value > 0.12f) || (lastDy >= 0 && frac.value > 0.85f)) 1f else 0f, tween(260)) } },
                            onDragCancel = { scope.launch { frac.animateTo(if (frac.value > 0.5f) 1f else 0f, tween(200)) } },
                        ) { _, dy -> lastDy = dy; scope.launch { frac.snapTo((frac.value - dy / range).coerceIn(0f, 1f)) } }
                    }
                    .clickable { scope.launch { frac.animateTo(if (frac.value > 0.5f) 0f else 1f, tween(260)) } },
            ) {
                Box(Modifier.align(Alignment.CenterHorizontally).padding(top = 8.dp).size(width = 36.dp, height = 4.dp).clip(RoundedCornerShape(2.dp)).background(Color(0x55FFFFFF)))
                Row(Modifier.fillMaxWidth().padding(horizontal = 20.dp).weight(1f), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    Txt("UP NEXT", size = 12f, weight = FontWeight.Bold, color = Palette.faint)
                    val n = next.firstOrNull()?.second
                    if (n != null) {
                        ArtImage(n.artKey, Modifier.size(34.dp).graphicsLayer { alpha = 1f - frac.value }, thumb = true, corner = 6.dp)
                        Column(Modifier.weight(1f).graphicsLayer { alpha = 1f - frac.value }) {
                            Txt(n.title, size = 14f, weight = FontWeight.SemiBold, maxLines = 1)
                            Txt(n.artist, size = 12f, color = sc.onBgDim, maxLines = 1)
                        }
                    } else Txt("Nothing queued after this", Modifier.weight(1f), size = 13f, color = sc.onBgDim)
                    GlyphIcon(Glyph.CHEVRON, Modifier.size(18.dp).graphicsLayer { rotationZ = if (frac.value > 0.5f) 90f else -90f }, sc.onBgDim)
                }
            }
            if (frac.value > 0.02f) {
                val cur = snap.track
                LazyColumn(Modifier.fillMaxSize().graphicsLayer { alpha = frac.value }, contentPadding = PaddingValues(bottom = 24.dp)) {
                    if (cur != null) item {
                        Column {
                            Txt("NOW PLAYING", Modifier.padding(start = 20.dp, top = 4.dp, bottom = 6.dp), size = 11f, weight = FontWeight.Bold, color = Palette.faint)
                            DrawerRow(cur, current = true, onClick = {})
                        }
                    }
                    item { Txt("NEXT", Modifier.padding(start = 20.dp, top = 14.dp, bottom = 6.dp), size = 11f, weight = FontWeight.Bold, color = Palette.faint) }
                    if (next.isEmpty()) item { Txt("Nothing queued after this song.", Modifier.padding(horizontal = 20.dp, vertical = 8.dp), size = 14f, color = sc.onBgDim) }
                    itemsIndexed(next, key = { _, p -> p.first }) { _, p -> DrawerRow(p.second, current = false) { app.player.skipTo(p.first); scope.launch { frac.animateTo(0f, tween(220)) } } }
                }
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
