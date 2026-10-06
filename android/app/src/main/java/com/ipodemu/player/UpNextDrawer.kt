package com.ipodemu.player

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
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
import androidx.compose.runtime.getValue
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.Color
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
class PaneNav(val pane: String, val toggle: (String) -> Unit, val go: (String) -> Unit, val wide: Boolean = false)
val LocalPane = androidx.compose.runtime.staticCompositionLocalOf<PaneNav?> { null }
val PANES = listOf("lyrics", "art", "info", "queue")
/** The tabs of the wide layout, in the web's order. */
val SIDE_PANES = listOf("queue", "lyrics", "info")

/** The panes as pages of a pager: they follow the finger and settle with a spring, and the pills glide to a page. */
@OptIn(androidx.compose.foundation.ExperimentalFoundationApi::class)
@Composable
fun PanePager(nav: PaneNav, modifier: Modifier = Modifier, order: List<String> = PANES, page: @Composable (String) -> Unit) {
    // the effects below live as long as the pager does, so they must read the CURRENT pane, not the one they started with (that stale copy left a pill lit after a swipe)
    val navNow by androidx.compose.runtime.rememberUpdatedState(nav)
    val pager = androidx.compose.foundation.pager.rememberPagerState(initialPage = order.indexOf(nav.pane).coerceAtLeast(0)) { order.size }
    // a pill moves the pager; a finger moves the pager and then tells the pill. Neither fights the other: the pager is only driven while it is still, and only reports once it has come to rest
    androidx.compose.runtime.LaunchedEffect(nav.pane) { val i = order.indexOf(navNow.pane); if (i >= 0 && !pager.isScrollInProgress && pager.currentPage != i) pager.animateScrollToPage(i) }
    androidx.compose.runtime.LaunchedEffect(pager) { androidx.compose.runtime.snapshotFlow { pager.targetPage }.collect { p -> if (order[p] != navNow.pane) navNow.go(order[p]) } }
    androidx.compose.foundation.pager.HorizontalPager(pager, modifier, pageSpacing = 12.dp, beyondBoundsPageCount = 0, flingBehavior = androidx.compose.foundation.pager.PagerDefaults.flingBehavior(pager, snapPositionalThreshold = 0.25f)) { p ->
        Box(Modifier.fillMaxSize().graphicsLayer {
            // the page slides away a little and fades as it leaves the centre
            val off = kotlin.math.abs(pager.currentPage - p + pager.currentPageOffsetFraction).coerceIn(0f, 1f)
            alpha = 1f - 0.45f * off; scaleX = 1f - 0.04f * off; scaleY = 1f - 0.04f * off
        }, contentAlignment = Alignment.Center) { page(order[p]) }
    }
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
    // the queue sits on a slightly raised card, so the player beside it stands out
    LazyColumn(modifier.clip(RoundedCornerShape(18.dp)).background(Color(0x14FFFFFF)), contentPadding = PaddingValues(start = 12.dp, end = 12.dp, top = 12.dp, bottom = 12.dp)) {
        item {
            Column(Modifier.padding(start = 8.dp, top = 2.dp, bottom = 8.dp)) {
                Txt("Playing from", size = 12f, color = sc.onBgDim)
                Txt(from, size = 18f, weight = FontWeight.Bold, maxLines = 1)
            }
        }
        item {
            var on by remember { androidx.compose.runtime.mutableStateOf(app.prefs.autoplay) }
            Row(Modifier.fillMaxWidth().clickable { on = !on; app.prefs.autoplay = on }.padding(horizontal = 4.dp, vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Txt("Autoplay", size = 15f, weight = FontWeight.SemiBold)
                    Txt("Add similar songs to the end of the queue", size = 12.5f, color = sc.onBgDim)
                }
                SwitchPill(on)
            }
        }
        if (cur != null) item { DrawerRow(cur, current = true, onClick = {}) }
        if (next.isEmpty()) item { Txt("Nothing queued after this song.", Modifier.padding(horizontal = 4.dp, vertical = 8.dp), size = 14f, color = sc.onBgDim) }
        itemsIndexed(next, key = { _, p -> p.first }) { _, p -> DrawerRow(p.second, current = false) { app.player.skipTo(p.first) } }
    }
}

@Composable
private fun DrawerRow(t: Track, current: Boolean, onClick: () -> Unit) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val note = downloadNote(app, t)
    Row(Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp)).background(if (current) sc.accent.copy(alpha = .14f) else Color.Transparent).clickable(onClick = onClick).then(if (current) Modifier.drawBehind { drawRect(sc.accent, size = androidx.compose.ui.geometry.Size(3.dp.toPx(), size.height)) } else Modifier).padding(horizontal = 10.dp, vertical = 6.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(14.dp)) {
        Box(Modifier.size(46.dp)) {
            ArtImage(t.artKey, Modifier.fillMaxSize(), thumb = true, corner = 7.dp)
            if (current) Box(Modifier.fillMaxSize().clip(RoundedCornerShape(7.dp)).background(Color(0x77000000)), contentAlignment = Alignment.Center) { EqualizerBars(Modifier.size(20.dp), LocalApp.current.player.wantsToPlay, Color.White) }
        }
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(3.dp)) {
            Txt(t.title, size = 15f, weight = FontWeight.SemiBold, color = if (current) sc.accent else sc.onBg, maxLines = 2)
            Txt(listOf(t.artist, t.album).filter { it.isNotEmpty() }.joinToString(" · "), size = 12.5f, color = sc.onBgDim, maxLines = 2)
            if (note != null) NoteChip(note)
        }
        if (t.durationMs > 0) Txt(fmtTime(t.durationMs), size = 12.5f, color = sc.onBgDim)
    }
}
