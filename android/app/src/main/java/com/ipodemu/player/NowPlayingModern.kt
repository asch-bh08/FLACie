package com.ipodemu.player

import androidx.compose.foundation.background
import androidx.compose.animation.togetherWith
import androidx.compose.animation.animateContentSize
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import kotlinx.coroutines.launch
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.min
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import com.ipodemu.library.Lyrics
import com.ipodemu.library.Track

/**
 * The Modern theme's Now Playing, laid out by shape rather than one layout squeezed to fit:
 *  - portrait (phones, Fold cover): art on top, then title, seek, transport and actions -- lyrics replace the art;
 *  - square-ish / landscape (RG Rotate, Fold inner, phone landscape): art (or lyrics) left, controls right;
 *  - wide (tablet, Fold inner landscape, >= 840dp): art, controls and lyrics side by side, lyrics always shown.
 */
@Composable
fun ModernNowPlayingBody(snap: PlayerSnap, nav: PlayerNav) {
    val t = snap.track ?: return
    var pane by rememberSaveable { mutableStateOf("art") }
    BoxWithConstraints(Modifier.fillMaxSize()) {
        val w = maxWidth; val h = maxHeight
        val ratio = w / h
        // taller than wide (phones, Fold cover): the cover, Lyrics, Info and Up next are pages you swipe between;
        // as wide as tall or wider (Fold unfolded, tablets, landscape): like the web, cover and controls on the left, Up next / Lyrics / Info always on the right
        val sideBySide = ratio >= 0.95f
        val shown = if (sideBySide && pane == "art") "queue" else pane
        val paneNav = PaneNav(shown, { p -> pane = if (!sideBySide && pane == p) "art" else p }, { p -> pane = p }, wide = sideBySide)
        val lyricsToggle: Pair<Boolean, () -> Unit> = (shown == "lyrics") to { paneNav.toggle("lyrics") }
        val infoToggle: Pair<Boolean, () -> Unit> = (shown == "info") to { paneNav.toggle("info") }
        androidx.compose.runtime.CompositionLocalProvider(LocalPane provides paneNav) {
        if (sideBySide) {
            // like the web: the cover and controls on the left, and on the right three tabs (Up next, Lyrics, Info) you can also swipe through
            Row(Modifier.fillMaxSize().padding(start = 28.dp, end = 28.dp, bottom = 16.dp), horizontalArrangement = Arrangement.spacedBy(44.dp)) {
                Column(Modifier.weight(0.5f).fillMaxHeight(), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    Header(nav)
                    BoxWithConstraints(Modifier.weight(1f).fillMaxWidth(), contentAlignment = Alignment.Center) {
                        NpArtModern(t, Modifier.size(min(maxWidth, maxHeight)))
                    }
                    InfoRow(snap, nav)
                    Seek(snap)
                    Transport(snap)
                }
                Column(Modifier.weight(0.5f).fillMaxHeight().padding(top = 10.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
                    SideTabs(shown) { paneNav.go(it) }
                    PanePager(paneNav, Modifier.weight(1f).fillMaxWidth(), order = SIDE_PANES) { p ->
                        when (p) {
                            "info" -> TrackInfoPanel(t, snap, Modifier.fillMaxSize())
                            "lyrics" -> LyricsPanel(t, snap.playing, Modifier.fillMaxSize(), big = true)
                            else -> QueuePanel(snap, Modifier.fillMaxSize())
                        }
                    }
                }
            }
        
} else Column(Modifier.fillMaxSize().padding(horizontal = 22.dp, vertical = 8.dp)) {
            Header(nav)
            Box(Modifier.weight(1f).fillMaxWidth().padding(vertical = 12.dp), contentAlignment = Alignment.Center) {
                ArtOrPanel(t, snap, shown == "lyrics", shown == "info", big = true, art = Modifier.fillMaxWidth().aspectRatio(1f, matchHeightConstraintsFirst = true))
            }
            Controls(snap, nav, lyricsToggle = lyricsToggle, header = false, info = infoToggle)
            Box(Modifier.height(20.dp))
        }
        }
    }
}

/** Small tags under the title, like the web's: where it plays from, the format, its rate and bit rate. */
@OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
@Composable
private fun FormatChips(t: Track, snap: PlayerSnap) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val ai = remember(t.path, snap.playing) { audioInfo(app, t) }
    val tags = listOfNotNull(
        t.source.name.lowercase().replaceFirstChar { it.uppercase() },
        if (ai.hiRes) "Hi-Res" else ai.codec.takeIf { it.isNotEmpty() },
        ai.sampleRate.takeIf { it > 0 }?.let { "%.1f kHz".format(it / 1000.0) },
        ai.kbps.takeIf { it > 0 }?.let { "$it kbps" },
    )
    if (tags.isEmpty()) return
    // one inline row: where it plays from, then the format, rate and bit rate together in a single badge (it scrolls sideways rather than wrapping)
    val src = tags.first(); val rest = tags.drop(1)
    Row(Modifier.padding(top = 8.dp).horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(6.dp), verticalAlignment = Alignment.CenterVertically) {
        Txt(src, Modifier.clip(RoundedCornerShape(50)).background(sc.accent.copy(alpha = .22f)).padding(horizontal = 10.dp, vertical = 3.dp), size = 11.5f, weight = FontWeight.Bold, color = sc.accent, maxLines = 1)
        if (rest.isNotEmpty()) Txt(rest.joinToString(" · "), Modifier.clip(RoundedCornerShape(50)).background(Color(0x22FFFFFF)).padding(horizontal = 10.dp, vertical = 3.dp), size = 11.5f, weight = FontWeight.Bold, color = if (rest.first() == "Hi-Res") Color(0xFFFFC857) else sc.onBgDim, maxLines = 1)
    }
}

/** The three tabs beside the cover, like the web's: Up next, Lyrics, Info (swiping the panel moves between them too). */
@Composable
private fun SideTabs(selected: String, onSelect: (String) -> Unit) {
    val sc = LocalScheme.current
    Row(Modifier.clip(RoundedCornerShape(50)).background(Color(0x1AFFFFFF)).padding(4.dp), horizontalArrangement = Arrangement.spacedBy(4.dp)) {
        for ((id, label, g) in listOf(Triple("queue", "Up next", Glyph.QUEUE), Triple("lyrics", "Lyrics", Glyph.LYRICS), Triple("info", "Info", Glyph.INFO))) {
            val on = selected == id
            Row(
                Modifier.height(40.dp).clip(RoundedCornerShape(50)).background(if (on) Color(0x33FFFFFF) else Color.Transparent)
                    .semantics { contentDescription = label }.clickable { onSelect(id) }.padding(horizontal = 18.dp),
                verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                GlyphIcon(g, Modifier.size(18.dp), if (on) Color.White else sc.onBgDim)
                Txt(label, size = 14f, weight = FontWeight.SemiBold, color = if (on) Color.White else sc.onBgDim, maxLines = 1)
            }
        }
    }
}

@Composable
private fun Header(nav: PlayerNav) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    Row(Modifier.fillMaxWidth().height(52.dp), verticalAlignment = Alignment.CenterVertically) {
        IconAction(Glyph.DOWN, "Close", { nav.nowPlaying = false })
        Txt("Now Playing", Modifier.weight(1f), size = 14f, weight = FontWeight.SemiBold, color = sc.onBgDim, align = TextAlign.Center)
        if (!app.ui.guest) IconAction(Glyph.JAM, if (app.jam.inJam) "In a Jam" else "Jam", { app.ui.jamOpen = true }, tint = if (app.jam.inJam) sc.accent else sc.onBg)
        if (!app.ui.guest) IconAction(Glyph.DEVICES, "Devices", { app.ui.devicesOpen = true })
    }
}

/** Title/artist + heart, seek, transport and the action row, stacked; shared by every layout. */
@Composable
private fun Controls(snap: PlayerSnap, nav: PlayerNav, lyricsToggle: Pair<Boolean, () -> Unit>?, header: Boolean = true, info: Pair<Boolean, () -> Unit>? = null) {
    Column(Modifier.fillMaxWidth().riseIn(120).animateContentSize(), verticalArrangement = Arrangement.spacedBy(16.dp)) {
    if (header) Header(nav)
    InfoRow(snap, nav)
    ActionRow(snap, nav, lyricsToggle, spread = true, info = info)
    Seek(snap)
    Transport(snap)
    }
}

@Composable
private fun InfoRow(snap: PlayerSnap, nav: PlayerNav) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val t = snap.track ?: return
    val fav by app.userData.favState(t.path)
    val pn = LocalPane.current
    val compact = pn != null && pn.pane != "art" && !pn.wide
    val tags = formatTags(app, t, snap)
    // the title block eases between its two sizes with the page you swipe to (a pane open gets the room; the format tags stay in both)
    androidx.compose.animation.AnimatedContent(
        compact, Modifier.fillMaxWidth(),
        transitionSpec = {
            androidx.compose.animation.fadeIn(androidx.compose.animation.core.tween(220)) togetherWith androidx.compose.animation.fadeOut(androidx.compose.animation.core.tween(140)) using
                androidx.compose.animation.SizeTransform(clip = false) { _, _ -> androidx.compose.animation.core.tween(240) }
        },
        label = "titleRow",
    ) { small ->
        if (small) {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                ArtImage(t.artKey, Modifier.size(40.dp), thumb = true, corner = 8.dp)
                Column(Modifier.weight(1f)) {
                    Txt(t.title, size = 16f, weight = FontWeight.Bold, maxLines = 1)
                    Txt((listOf(t.artist.ifEmpty { "Unknown Artist" }) + tags).joinToString(" · "), size = 12.5f, color = sc.onBgDim, maxLines = 1)
                }
                IconAction(if (fav) Glyph.HEART_FILLED else Glyph.HEART, if (fav) "Unfavorite" else "Favorite", { app.userData.toggleFavorite(t.path) }, tint = if (fav) sc.accent else sc.onBg)
            }
        } else {
            Column(Modifier.fillMaxWidth()) {
                Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                    Column(Modifier.weight(1f)) {
                        Txt(t.title, size = 22f, weight = FontWeight.Bold, maxLines = 2)
                        Txt(t.artist.ifEmpty { "Unknown Artist" }, Modifier.padding(top = 2.dp).clickable(enabled = t.artist.isNotEmpty()) {
                            nav.nowPlaying = false; nav.push(Screen.Detail(DetailKind.ARTIST, t.albumArtist.ifEmpty { t.artist }))
                        }, size = 16f, color = sc.onBgDim)
                    }
                    IconAction(if (fav) Glyph.HEART_FILLED else Glyph.HEART, if (fav) "Unfavorite" else "Favorite", { app.userData.toggleFavorite(t.path) }, tint = if (fav) sc.accent else sc.onBg)
                }
                // the format tags get their own full-width line (with the extra buttons on the right when there is room), so the title is never squeezed
                Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                    Box(Modifier.weight(1f)) { FormatChips(t, snap) }
                    if (pn?.wide == true) {
                        IconAction(Glyph.LIST, "Equalizer", { nav.sheet = eqSheet(app) })
                        IconAction(Glyph.PLUS, "Add to playlist", { nav.sheet = playlistPicker(app, nav, t) })
                        IconAction(Glyph.MORE, "More", { openTrackSheet(app, nav, t) })
                    }
                }
            }
        }
    }
}

/** Where it plays from, the format, its rate and bit rate (the same tags in the full title block and in the one-line one). */
private fun formatTags(app: com.ipodemu.App, t: Track, snap: PlayerSnap): List<String> {
    val ai = audioInfo(app, t)
    return listOfNotNull(
        t.source.name.lowercase().replaceFirstChar { it.uppercase() },
        if (ai.hiRes) "Hi-Res" else ai.codec.takeIf { it.isNotEmpty() },
        ai.sampleRate.takeIf { it > 0 }?.let { "%.1f kHz".format(it / 1000.0) },
        ai.kbps.takeIf { it > 0 }?.let { "$it kbps" },
    )
}

@Composable
private fun ActionRow(snap: PlayerSnap, nav: PlayerNav, lyricsToggle: Pair<Boolean, () -> Unit>?, spread: Boolean, info: Pair<Boolean, () -> Unit>? = null) {
    val app = LocalApp.current
    val t = snap.track ?: return
    // YouTube Music style: one scrollable row of pills; the active ones (Lyrics, Info, Equalizer on) fill with the accent
    Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
        if (lyricsToggle != null) Pill(Glyph.LYRICS, "Lyrics", lyricsToggle.first, lyricsToggle.second)
        if (info != null) Pill(Glyph.INFO, "Info", info.first, info.second)
        val pn = LocalPane.current
        Pill(Glyph.QUEUE, "Up next", pn?.pane == "queue") { if (pn != null) pn.toggle("queue") else nav.openQueueFromPlayer() }
        Pill(Glyph.LIST, "Equalizer", app.prefs.eq != "Off") { nav.sheet = eqSheet(app) }
        Pill(Glyph.PLUS, "Add to playlist", false) { nav.sheet = playlistPicker(app, nav, t) }
        Pill(Glyph.MORE, "More", false) { openTrackSheet(app, nav, t, if (info != null && !info.first) listOf(SheetItem("Song info", Glyph.INFO) { info.second() }) else emptyList()) }
    }
}

@Composable
private fun Pill(g: Glyph, label: String, active: Boolean, onClick: () -> Unit) {
    val sc = LocalScheme.current
    val src = remember { MutableInteractionSource() }
    val focused by src.collectIsFocusedAsState()
    val ink = if (active) sc.accent.readableInk() else Color.White
    Row(
        Modifier.height(38.dp).clip(RoundedCornerShape(19.dp)).background(if (active) sc.accent else Color(0x1FFFFFFF))
            .then(if (focused) Modifier.border(2.dp, Color.White, RoundedCornerShape(19.dp)) else Modifier)
            .semantics { contentDescription = label }
            .clickable(src, null, onClick = onClick).padding(horizontal = 14.dp),
        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(7.dp),
    ) {
        GlyphIcon(g, Modifier.size(18.dp), ink)
        Txt(label, size = 14f, weight = FontWeight.SemiBold, color = ink, maxLines = 1)
    }
}

@Composable
private fun Seek(snap: PlayerSnap) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val pos by rememberPosition(app.player, snap.playing)
    val dur = app.player.durationMs
    Column(Modifier.fillMaxWidth()) {
        SeekBar(if (dur > 0) pos.toFloat() / dur else 0f, onSeek = { f -> if (dur > 0) app.player.seekTo((f * dur).toLong()) },
            onNudge = { d -> app.player.seekBy((d * 5000).toLong()) }, Modifier.fillMaxWidth())
        Row(Modifier.fillMaxWidth()) {
            Txt(fmtTime(pos), Modifier.weight(1f), size = 12f, color = sc.onBgDim)
            Txt(fmtTime(dur.coerceAtLeast(0)), size = 12f, color = sc.onBgDim)
        }
    }
}

/** Volume for the player itself (the phone's keys still work). Honors the volume limit in Settings: the far end of the bar is that limit. */
@Composable
private fun VolumeRow() {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val limit = (app.prefs.volumeLimit / 100f).coerceAtLeast(0.05f)
    var frac by remember { mutableStateOf((app.player.volume / limit).coerceIn(0f, 1f)) }
    fun set(f: Float) { frac = f.coerceIn(0f, 1f); app.player.volume = frac * limit }
    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        GlyphIcon(Glyph.VOLUME, Modifier.size(18.dp), sc.onBgDim)
        SeekBar(frac, onSeek = { set(it) }, onNudge = { d -> set(frac + d * 0.05f) }, Modifier.weight(1f).padding(start = 8.dp)
            .semantics { contentDescription = "Volume ${(frac * 100).toInt()} percent" })
    }
}

/** Sized from the width it gets, so all five controls always fit (the old row pushed Repeat off a narrow column). */
@Composable
private fun Transport(snap: PlayerSnap) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val playFocus = remember { FocusRequester() }
    LaunchedEffect(Unit) { try { playFocus.requestFocus() } catch (_: Exception) {} }
    BoxWithConstraints(Modifier.fillMaxWidth()) {
        val play = min(78.dp, maxWidth * 0.26f)
        val big = min(58.dp, maxWidth * 0.19f)
        val small = min(46.dp, maxWidth * 0.15f)
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
            IconAction(Glyph.SHUFFLE, if (snap.shuffle) "Shuffle on" else "Shuffle off", { app.prefs.shuffle = !app.prefs.shuffle; app.player.applyModes() }, tint = if (snap.shuffle) sc.accent else sc.onBgDim, size = small, iconScale = 0.52f)
            IconAction(Glyph.PREV, "Previous", { app.player.prev() }, size = big, iconScale = 0.56f)
            PlayDisc(snap.playing, play, playFocus) { app.player.toggle() }
            IconAction(Glyph.NEXT, "Next", { app.player.next() }, size = big, iconScale = 0.56f)
            IconAction(if (snap.repeat == 2) Glyph.REPEAT_ONE else Glyph.REPEAT, when (snap.repeat) { 1 -> "Repeat all"; 2 -> "Repeat one"; else -> "Repeat off" }, { app.prefs.repeat = (app.prefs.repeat + 1) % 3; app.player.applyModes() },
                tint = if (snap.repeat != 0) sc.accent else sc.onBgDim, size = small, iconScale = 0.52f)
        }
    }
}

@Composable
private fun PlayDisc(playing: Boolean, size: Dp, focus: FocusRequester, onClick: () -> Unit) {
    val sc = LocalScheme.current
    val src = remember { MutableInteractionSource() }
    val focused by src.collectIsFocusedAsState()
    Box(
        Modifier.size(size).focusRequester(focus).clip(CircleShape).background(Color.White)
            .then(if (focused) Modifier.border(3.dp, sc.accent, CircleShape) else Modifier)
            .clickable(src, null, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) { GlyphIcon(if (playing) Glyph.PAUSE else Glyph.PLAY, Modifier.size(size * 0.45f), Color.Black) }
}

@Composable
private fun NpArtModern(t: Track, modifier: Modifier) {
    val app = LocalApp.current
    ArtImage(
        t.artKey, modifier.popIn(t.path, 0.88f)
            .shadow(24.dp, RoundedCornerShape(12.dp), clip = false, ambientColor = Color.Black, spotColor = Color.Black),
        corner = 12.dp,
    )
}

// ---- lyrics --------------------------------------------------------------------------------------------------------

private sealed interface LyricsState {
    data object Loading : LyricsState
    data object None : LyricsState
    class Ready(val lyrics: Lyrics) : LyricsState
}

/** Synced lyrics follow the song (current line bright, auto-scrolled to a third of the way down; tap a line to jump
 * there); plain lyrics just scroll. */
@Composable
fun LyricsPanel(t: Track, playing: Boolean, modifier: Modifier, big: Boolean) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    var state by remember(t.path) { mutableStateOf<LyricsState>(LyricsState.Loading) }
    LaunchedEffect(t.path) { state = app.lyrics.get(t)?.let { LyricsState.Ready(it) } ?: LyricsState.None }
    Box(modifier.clip(RoundedCornerShape(16.dp)).background(Color(0x1AFFFFFF))) {
        when (val s = state) {
            LyricsState.Loading -> Txt("Looking for lyrics...", Modifier.align(Alignment.Center), size = 15f, color = sc.onBgDim)
            LyricsState.None -> Column(Modifier.align(Alignment.Center).padding(20.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                Txt("No lyrics found", size = 17f, weight = FontWeight.SemiBold)
                Txt("Checked Jellyfin, local .lrc files and LRCLIB", Modifier.padding(top = 4.dp), size = 13f, color = sc.onBgDim, maxLines = 2, align = TextAlign.Center)
            }
            is LyricsState.Ready -> {
                val l = s.lyrics
                val pos by rememberPosition(app.player, playing && l.synced)
                val current = if (!l.synced) -1 else l.lines.indexOfLast { it.timeMs <= pos + 250 }
                val list = rememberLazyListState()
                LaunchedEffect(current) {
                    if (current >= 0) {
                        val vh = list.layoutInfo.viewportSize.height
                        list.animateScrollToItem(current, -(vh / 3))
                    }
                }
                val size = if (big) 22f else 18f
                LazyColumn(Modifier.fillMaxSize(), state = list, contentPadding = PaddingValues(horizontal = 20.dp, vertical = 24.dp), verticalArrangement = Arrangement.spacedBy(if (big) 14.dp else 10.dp)) {
                    itemsIndexed(l.lines) { i, line ->
                        val color = when {
                            !l.synced -> sc.onBg
                            i == current -> sc.onBg
                            i < current -> sc.onBg.copy(alpha = .35f)
                            else -> sc.onBg.copy(alpha = .55f)
                        }
                        Txt(line.text.ifEmpty { "♪" }, Modifier.fillMaxWidth().then(if (l.synced) Modifier.clickable { app.player.seekTo(line.timeMs) } else Modifier),
                            size = if (l.synced) size else size - 3f, weight = if (l.synced) FontWeight.Bold else FontWeight.Medium, color = color, maxLines = 4)
                    }
                    item { Txt("Lyrics via ${l.source}", Modifier.padding(top = 12.dp), size = 12f, color = sc.onBgDim) }
                }
            }
        }
    }
}

/** One of the matching round buttons under the transport: soft disc, accent-filled when that feature is on. */
@Composable
private fun RoundAction(g: Glyph, label: String, active: Boolean, onClick: () -> Unit) {
    val sc = LocalScheme.current
    val src = remember { MutableInteractionSource() }
    val focused by src.collectIsFocusedAsState()
    Box(
        Modifier.size(46.dp).clip(CircleShape).background(if (active) sc.accent else Color(0x1FFFFFFF))
            .then(if (focused) Modifier.border(2.dp, Color.White, CircleShape) else Modifier)
            .semantics { contentDescription = label }
            .clickable(src, null, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) { GlyphIcon(g, Modifier.size(22.dp), if (active) sc.accent.readableInk() else Color.White) }
}

/** The cover, or whichever panel (lyrics, or the Info graphs) has taken its place. */
@Composable
private fun ArtOrPanel(t: Track, snap: PlayerSnap, lyrics: Boolean, info: Boolean, big: Boolean, art: Modifier = Modifier.fillMaxSize()) {
    val pn = LocalPane.current
    val content: @Composable (String) -> Unit = { p ->
        when (p) {
            "queue" -> QueuePanel(snap, Modifier.fillMaxSize())
            "info" -> TrackInfoPanel(t, snap, Modifier.fillMaxSize())
            "lyrics" -> LyricsPanel(t, snap.playing, Modifier.fillMaxSize(), big)
            else -> NpArtModern(t, art)
        }
    }
    if (pn == null) Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) { content(if (info) "info" else if (lyrics) "lyrics" else "art") }
    else PanePager(pn, Modifier.fillMaxSize(), page = content)
}
