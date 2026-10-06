package com.ipodemu.player

import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideOutHorizontally
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.ipodemu.App
import com.ipodemu.library.DownloadStage
import com.ipodemu.library.Track
import com.ipodemu.library.matchKey

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

/** A small, clearly visible tag under a song's title: pink while it is downloading, green once it was fetched (brighter the fresher it is). */
@Composable
private fun NoteChip(text: String) {
    val live = text == "Downloading"
    val fresh = text.endsWith("just now") || text.endsWith("today")
    val ink = if (live) LocalScheme.current.accent else if (fresh) Color(0xFF4EE0A1) else Color(0xFF8FC9B0)
    Row(
        Modifier.clip(RoundedCornerShape(50)).background(ink.copy(alpha = .18f)).padding(horizontal = 9.dp, vertical = 3.dp),
        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(5.dp),
    ) {
        GlyphIcon(Glyph.DOWN, Modifier.size(12.dp), ink)
        Txt(text, size = 12f, weight = FontWeight.Bold, color = ink)
    }
}

/**
 * The panes of the full-screen player, left to right: Lyrics, the cover, Info, Up next. Swipe sideways to move between them (a swipe
 * no longer skips the song: Previous and Next do that); the pills jump straight to a pane and a second press goes back to the cover.
 */
class PaneNav(val pane: String, val toggle: (String) -> Unit, val go: (String) -> Unit)
val LocalPane = androidx.compose.runtime.staticCompositionLocalOf<PaneNav?> { null }
private val PANES = listOf("lyrics", "art", "info", "queue")

fun Modifier.paneSwipe(nav: PaneNav?): Modifier = if (nav == null) this else pointerInput(nav.pane) {
    var dx = 0f
    detectHorizontalDragGestures(
        onDragStart = { dx = 0f },
        onDragEnd = {
            val i = PANES.indexOf(nav.pane)
            if (dx < -70f && i < PANES.lastIndex) nav.go(PANES[i + 1])
            else if (dx > 70f && i > 0) nav.go(PANES[i - 1])
        },
    ) { c, d -> c.consume(); dx += d }
}

/** The queue as a pane of the player: where it comes from, the song now playing, then what comes next, each with its download note. */
@Composable
fun QueuePanel(snap: PlayerSnap, modifier: Modifier = Modifier) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    app.userData.rev
    val next = remember(snap.track?.path, snap.index, snap.shuffle, snap.repeat, snap.count) { app.player.upNext(30) }
    val from = app.player.contextName ?: "Your queue"
    val cur = snap.track
    LazyColumn(modifier, contentPadding = PaddingValues(bottom = 16.dp)) {
        item {
            Column(Modifier.padding(start = 4.dp, top = 2.dp, bottom = 8.dp)) {
                Txt("Playing from", size = 12f, color = sc.onBgDim)
                Txt(from, size = 18f, weight = FontWeight.Bold, maxLines = 1)
            }
        }
        if (cur != null) item { DrawerRow(cur, current = true, onClick = {}) }
        if (next.isEmpty()) item { Txt("Nothing queued after this song.", Modifier.padding(horizontal = 4.dp, vertical = 8.dp), size = 14f, color = sc.onBgDim) }
        itemsIndexed(next, key = { _, p -> p.first }) { _, p -> DrawerRow(p.second, current = false) { app.player.skipTo(p.first) } }
    }
}

/** Slides the panes in and out sideways, in the order they sit. */
@Composable
fun PaneSwitcher(pane: String, modifier: Modifier = Modifier, content: @Composable (String) -> Unit) {
    AnimatedContent(
        pane, modifier,
        transitionSpec = {
            val dir = if (PANES.indexOf(targetState) > PANES.indexOf(initialState)) 1 else -1
            (slideInHorizontally(tween240()) { it * dir / 3 } + fadeIn(tween240())) togetherWith (slideOutHorizontally(tween240()) { -it * dir / 3 } + fadeOut(tween240()))
        },
        label = "pane",
    ) { p -> content(p) }
}
private fun <T> tween240() = androidx.compose.animation.core.tween<T>(240)

@Composable
private fun DrawerRow(t: Track, current: Boolean, onClick: () -> Unit) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val note = downloadNote(app, t)
    Row(Modifier.fillMaxWidth().clickable(onClick = onClick).padding(horizontal = 4.dp, vertical = 7.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(14.dp)) {
        ArtImage(t.artKey, Modifier.size(52.dp), thumb = true, corner = 8.dp)
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(3.dp)) {
            Txt(t.title, size = 15f, weight = FontWeight.SemiBold, color = if (current) sc.accent else sc.onBg, maxLines = 1)
            Txt(listOf(t.artist, t.album).filter { it.isNotEmpty() }.joinToString(" · "), size = 12.5f, color = sc.onBgDim, maxLines = 1)
            if (note != null) NoteChip(note)
        }
        if (t.durationMs > 0) Txt(fmtTime(t.durationMs), size = 12.5f, color = sc.onBgDim)
    }
}
