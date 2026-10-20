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
import androidx.compose.foundation.lazy.items
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
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.ipodemu.App
import com.ipodemu.library.Track
import com.ipodemu.library.UpNextMerge
import com.ipodemu.library.matchKey
import com.ipodemu.playback.QueueEdit

/**
 * The panes of the full-screen player, left to right: Lyrics, the cover, Info, Up next. Swipe sideways to move between them (a swipe
 * no longer skips the song: Previous and Next do that); the pills jump straight to a pane and a second press goes back to the cover.
 */
class PaneNav(val pane: String, val toggle: (String) -> Unit, val go: (String) -> Unit, val wide: Boolean = false)
val LocalPane = androidx.compose.runtime.staticCompositionLocalOf<PaneNav?> { null }
/** The player's navigation state, for panes that open an action sheet (the queue's per-song actions). */
val LocalPlayerNav = androidx.compose.runtime.staticCompositionLocalOf<PlayerNav?> { null }

/** Takes the song at queue [index] out of the queue, with an Undo message (the same pattern as removing a song from a playlist). */
fun removeFromQueueWithUndo(app: App, index: Int) {
    val r = app.player.removeAt(index)
    if (r == null) { if (app.player.hasQueue && !app.player.canEditQueue) app.ui.say("A Jam's queue is shared; it cannot be changed from here", warn = true); return }
    app.ui.say("Removed \"${r.track.title}\" from Up next", "Undo") { app.player.undoRemoval(r) }
}

/**
 * The actions of one song in Up next, as a sheet (reachable with touch and with the d-pad / gamepad, which have no drag): play next, move up,
 * move down, remove. [pos] is its place among the [count] upcoming songs shown ([more] when further ones follow); the current song can only be removed.
 */
fun upNextSheet(app: App, t: Track, index: Int, pos: Int, count: Int, more: Boolean, current: Boolean): SheetSpec {
    val items = ArrayList<SheetItem>()
    if (!current && app.player.canEditQueue) {
        val a = QueueEdit.rowActions(pos, count, more)
        if (a.top) items += SheetItem("Play next", Glyph.CHEVRON) { app.player.moveUpNext(index, QueueEdit.Step.TOP) }
        if (a.up) items += SheetItem("Move up", Glyph.CHEVRON) { app.player.moveUpNext(index, QueueEdit.Step.UP) }
        if (a.down) items += SheetItem("Move down", Glyph.CHEVRON) { app.player.moveUpNext(index, QueueEdit.Step.DOWN) }
    }
    items += SheetItem("Remove from queue", Glyph.CLOSE) { removeFromQueueWithUndo(app, index) }
    return SheetSpec(t.title, t.artist.ifEmpty { null }, items)
}
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
    androidx.compose.foundation.pager.HorizontalPager(pager, modifier, pageSpacing = 12.dp, beyondViewportPageCount = 0, flingBehavior = androidx.compose.foundation.pager.PagerDefaults.flingBehavior(pager, snapPositionalThreshold = 0.25f)) { p ->
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
    val nav = LocalPlayerNav.current
    val next = remember(snap.track?.path, snap.index, snap.shuffle, snap.repeat, snap.count, snap.rev) { app.player.upNext(30) }
    val more = next.size >= 30
    val from = app.player.contextName ?: "Your queue"
    val cur = snap.track
    val plan = rememberUpNextPlan(next, snap.count, snap.rev, snap.track?.path)
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
        if (cur != null) item { DrawerRow(cur, current = true, onClick = {}, onMore = nav?.let { n -> { n.sheet = upNextSheet(app, cur, snap.index, -1, next.size, more, true) } }) }
        if (next.isEmpty()) item { Txt("Nothing queued after this song.", Modifier.padding(horizontal = 4.dp, vertical = 8.dp), size = 14f, color = sc.onBgDim) }
        itemsIndexed(next, key = { _, p -> p.first }) { pos, p ->
            Column(Modifier.fillMaxWidth()) {
                if (p.first == plan.dividerBefore) AutoplayDivider()
                DrawerRow(p.second, current = false, onMore = nav?.let { n -> { n.sheet = upNextSheet(app, p.second, p.first, pos, next.size, more, false) } }) { app.player.skipTo(p.first) }
            }
        }
        // Autoplay suggestions still being fetched: display only (not queue items, so the indices and actions above are untouched)
        if (plan.dividerAtEnd) item(key = "ap-divider") { AutoplayDivider() }
        items(plan.pending, key = { "ap-" + it.pending.key }) { PendingDrawerRow(it) }
    }
}

/** The merge of the queue with the suggestions Autoplay is fetching, for display (see [UpNextMerge]); redraws as they start, land or fail. */
@Composable
fun rememberUpNextPlan(next: List<Pair<Int, Track>>, queueCount: Int, queueRev: Int, current: String?): UpNextMerge.Plan {
    val app = LocalApp.current
    val ap = app.autoplay
    ap.rev                              // a suggestion was asked for, landed or was given up
    app.library.tags.structRev          // a download started, finished or failed
    val queueKeys = remember(queueCount, queueRev, current) { app.player.queueTracks().mapTo(HashSet()) { it.matchKey } }
    val now = System.currentTimeMillis()
    return UpNextMerge.plan(next, queueKeys, { ap.isAutoplay(it) }, ap.pending()) { k -> app.library.tags.statusOf(k, now) }
}

/** "Autoplay · up next after this": the divider above the songs Autoplay added (and above the ones it is still fetching). */
@Composable
fun AutoplayDivider() {
    val sc = LocalScheme.current
    Txt(UpNextMerge.DIVIDER, Modifier.padding(start = 4.dp, end = 4.dp, top = 14.dp, bottom = 6.dp), size = 12f, weight = FontWeight.Bold, color = sc.onBgDim)
}

/** A suggestion that is being fetched: cover, title, artist and its tag. Not playable, not removable, not focusable. */
@Composable
fun PendingDrawerRow(r: UpNextMerge.PendingRow) {
    val sc = LocalScheme.current
    val p = r.pending
    val narrow = androidx.compose.ui.platform.LocalConfiguration.current.screenWidthDp < 520
    Row(Modifier.fillMaxWidth().padding(horizontal = 10.dp, vertical = 6.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(14.dp)) {
        RemoteArtImage(p.artUrl, Modifier.size(46.dp), corner = 7.dp)
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(3.dp)) {
            Txt(p.title, size = 15f, weight = FontWeight.SemiBold, color = sc.onBg, maxLines = 2)
            Txt(listOf(p.artist, p.album).filter { it.isNotEmpty() }.joinToString(" · "), size = 12.5f, color = sc.onBgDim, maxLines = 2)
            DownloadTagLine(p.key, null, compact = narrow)
        }
        if (p.durationMs > 0) Txt(fmtTime(p.durationMs), size = 12.5f, color = sc.onBgDim)
    }
}

@Composable
private fun DrawerRow(t: Track, current: Boolean, onMore: (() -> Unit)? = null, onClick: () -> Unit) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    Row(Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp)).background(if (current) sc.accent.copy(alpha = .14f) else Color.Transparent).clickable(onClick = onClick).then(if (current) Modifier.drawBehind { drawRect(sc.accent, size = androidx.compose.ui.geometry.Size(3.dp.toPx(), size.height)) } else Modifier).padding(horizontal = 10.dp, vertical = 6.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(14.dp)) {
        Box(Modifier.size(46.dp)) {
            ArtImage(t.artKey, Modifier.fillMaxSize(), thumb = true, corner = 7.dp)
            if (current) Box(Modifier.fillMaxSize().clip(RoundedCornerShape(7.dp)).background(Color(0x77000000)), contentAlignment = Alignment.Center) { EqualizerBars(Modifier.size(20.dp), LocalApp.current.player.wantsToPlay, Color.White) }
        }
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(3.dp)) {
            Txt(t.title, size = 15f, weight = FontWeight.SemiBold, color = if (current) sc.accent else sc.onBg, maxLines = 2)
            Txt(queueRowSubtitle(t.artist, t.album), size = 12.5f, color = sc.onBgDim, maxLines = 2)
            DownloadTagLine(t, compact = androidx.compose.ui.platform.LocalConfiguration.current.screenWidthDp < 520)
        }
        if (t.durationMs > 0) Txt(fmtTime(t.durationMs), size = 12.5f, color = sc.onBgDim)
        if (onMore != null) Box(
            Modifier.size(48.dp).clip(RoundedCornerShape(50)).semantics { contentDescription = "More for ${t.title}" }.clickable(onClick = onMore),
            contentAlignment = Alignment.Center,
        ) { GlyphIcon(Glyph.MORE, Modifier.size(20.dp), sc.onBgDim) }
    }
}

/** The line under a queue row's title: artist and album joined by a middle dot (written as · so a file saved in the wrong encoding can never garble it). */
fun queueRowSubtitle(artist: String, album: String): String = listOf(artist, album).filter { it.isNotEmpty() }.joinToString(" · ")
