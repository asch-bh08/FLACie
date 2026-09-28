package com.ipodemu.player

import androidx.activity.compose.BackHandler
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideOutHorizontally
import androidx.compose.animation.togetherWith
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutVertically
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.State
import androidx.compose.runtime.derivedStateOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.ipodemu.App
import com.ipodemu.Prefs
import com.ipodemu.library.Group
import com.ipodemu.library.Track
import com.ipodemu.library.sortKey
import com.ipodemu.playback.PlayerController
import com.ipodemu.theme.Themes
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.withContext

// ---- navigation state ---------------------------------------------------------------------------------------------

enum class LibKind(val title: String) {
    SONGS("Songs"), ALBUMS("Albums"), ARTISTS("Artists"), PLAYLISTS("Playlists"), GENRES("Genres"), MEMOS("Voice Memos"),
    JELLYFIN("Jellyfin"), PLEX("Plex"), NAS("NAS"),
}

enum class DetailKind { ALBUM, ARTIST, FOLDER, USER, GENRE, FAVORITES, RECENT, MIX }

sealed interface Screen {
    data object Home : Screen
    data class Lib(val kind: LibKind) : Screen
    data class Detail(val kind: DetailKind, val id: String = "") : Screen
    data object Search : Screen
    data object Queue : Screen
    data object Settings : Screen
    data object Music : Screen
}

class SheetSpec(val title: String, val subtitle: String?, val items: List<SheetItem>)

@androidx.compose.runtime.Stable
class PlayerNav {
    val stack = mutableStateListOf<Screen>(Screen.Home)
    val top: Screen get() = stack.last()
    var nowPlaying by mutableStateOf(false)
    var sheet by mutableStateOf<SheetSpec?>(null)
    var nameDialog by mutableStateOf<((String) -> Unit)?>(null)
    /** Cover whose colours should tint the background (set by detail screens); null = the playing track's cover. */
    var overrideArt by mutableStateOf<String?>(null)

    fun push(s: Screen) { if (stack.lastOrNull() != s) stack.add(s) }
    fun pop(): Boolean { if (stack.size <= 1) return false; stack.removeAt(stack.lastIndex); return true }
    fun home() { while (stack.size > 1) stack.removeAt(stack.lastIndex) }
}

/** What the player is doing, as one immutable snapshot for composition. */
@androidx.compose.runtime.Immutable
data class PlayerSnap(val track: Track?, val playing: Boolean, val shuffle: Boolean, val repeat: Int, val index: Int, val count: Int)

@Composable
fun rememberSnap(player: PlayerController, prefs: Prefs): PlayerSnap {
    var tick by remember { mutableIntStateOf(0) }
    DisposableEffect(player) { val d = player.observe { tick++ }; onDispose { d() } }
    @Suppress("UNUSED_EXPRESSION") tick
    return PlayerSnap(player.current, player.isPlaying, prefs.shuffle, prefs.repeat, player.queueIndex, player.queue.size)
}

@Composable
fun rememberPosition(player: PlayerController, active: Boolean): State<Long> {
    val pos = remember { mutableLongStateOf(player.positionMs) }
    LaunchedEffect(active) {
        pos.longValue = player.positionMs
        while (active && isActive) { pos.longValue = player.positionMs; delay(250) }
    }
    return pos
}

fun fmtTime(ms: Long): String {
    val s = (ms / 1000).coerceAtLeast(0)
    val h = s / 3600
    return if (h > 0) "%d:%02d:%02d".format(h, s % 3600 / 60, s % 60) else "%d:%02d".format(s / 60, s % 60)
}

fun songCount(n: Int) = if (n == 1) "1 song" else "$n songs"

// ---- host ---------------------------------------------------------------------------------------------------------

/** Bumps whenever the library changes (scan progress, rescans) so lists and counts refresh. */
val LocalLibRev = androidx.compose.runtime.compositionLocalOf { 0 }

@Composable
fun rememberLibRev(lib: com.ipodemu.library.Library): Int {
    var rev by remember { mutableIntStateOf(0) }
    DisposableEffect(lib) { val d = lib.observe { rev++ }; onDispose { d() } }
    return rev
}


@Composable
fun PlayerHost(nav: PlayerNav) {
    val app = LocalApp.current
    val snap = rememberSnap(app.player, app.prefs)
    val sc = LocalScheme.current
    val ui = app.ui
    ui.rev

    BackHandler(enabled = !ui.pickerOpen && (nav.sheet != null || nav.nameDialog != null)) { nav.sheet = null; nav.nameDialog = null }
    BackHandler(enabled = !ui.pickerOpen && nav.sheet == null && nav.nameDialog == null && nav.nowPlaying) { nav.nowPlaying = false }
    BackHandler(enabled = !ui.pickerOpen && nav.sheet == null && nav.nameDialog == null && !nav.nowPlaying && nav.stack.size > 1) { nav.pop() }

    LaunchedEffect(nav.top) { WheelFocus.last = null }
    LaunchedEffect(ui.nowPlayingRequest) { if (ui.nowPlayingRequest > 0 && app.player.hasQueue) nav.nowPlaying = true }
    val libRev = rememberLibRev(app.library)

    androidx.compose.runtime.CompositionLocalProvider(LocalLibRev provides libRev) {
    var backDrag by remember { mutableFloatStateOf(0f) }
    val sb = if (LocalHardware.current) 2 else app.prefs.swipeBack   // device view: MENU on the wheel is the only back
    val backEnabled = sb != 2 && nav.sheet == null && nav.nameDialog == null && (nav.nowPlaying || nav.stack.size > 1)
    val edgePx = with(androidx.compose.ui.platform.LocalDensity.current) { 22.dp.toPx() }
    val swipeZone = if (sb == 1 || (!nav.nowPlaying && app.prefs.swipeRowRight != 0)) edgePx else 1e9f // iPhone-style: swipe right anywhere goes back (Now Playing keeps the edge, its cover swipes skip tracks)
    Box(
        Modifier.fillMaxSize().edgeSwipeBack(backEnabled, swipeZone, { backDrag = it }) { if (nav.nowPlaying) nav.nowPlaying = false else nav.pop() },
    ) {
        Column(Modifier.fillMaxSize().graphicsLayer { translationX = if (nav.nowPlaying) 0f else backDrag }) {
            Box(Modifier.weight(1f)) {
                // one cheap slide for the incoming screen only (no cross-fade, old screen is not kept composed);
                // going back needs no animation because the swipe-back drag already moved the page
                val depth = nav.stack.size
                val lastDepth = remember { androidx.compose.runtime.mutableIntStateOf(depth) }
                val forward = depth > lastDepth.intValue
                androidx.compose.runtime.SideEffect { lastDepth.intValue = depth }
                androidx.compose.runtime.key(nav.top) {
                    val slide = remember { androidx.compose.animation.core.Animatable(if (forward) 1f else 0f) }
                    LaunchedEffect(Unit) { if (slide.value > 0f) slide.animateTo(0f, tween(200)) }
                    Box(Modifier.fillMaxSize().graphicsLayer { translationX = slide.value * size.width * 0.28f }) { ScreenContent(nav.top, nav, snap) }
                }
            }
            if (snap.track != null && !nav.nowPlaying && nav.top != Screen.Home && !LocalHardware.current) MiniPlayer(snap, nav)
        }
        AnimatedVisibility(
            nav.nowPlaying,
            enter = slideInVertically(tween(320)) { it } + fadeIn(tween(200)),
            exit = slideOutVertically(tween(260)) { it } + fadeOut(tween(200)),
        ) { Box(Modifier.graphicsLayer { translationX = backDrag }) { NowPlayingScreen(snap, nav) } }
        nav.sheet?.let { s -> ActionSheet(s.title, s.subtitle, s.items) { nav.sheet = null } }
        nav.nameDialog?.let { done -> NameDialog(done) { nav.nameDialog = null } }
    }
    }
}

@Composable
private fun ScreenContent(screen: Screen, nav: PlayerNav, snap: PlayerSnap) {
    when (screen) {
        Screen.Home -> HomeScreen(nav, snap)
        is Screen.Lib -> LibraryScreen(screen.kind, nav, snap)
        is Screen.Detail -> DetailScreen(screen, nav, snap)
        Screen.Search -> SearchScreen(nav, snap)
        Screen.Queue -> QueueScreen(nav, snap)
        Screen.Settings -> SettingsScreen(nav)
        Screen.Music -> MusicMenu(nav)
    }
}

// ---- shared pieces ------------------------------------------------------------------------------------------------

/** iPod-OS style title bar: glossy Back pill on the left, centred bold title, actions on the right. */
@Composable
fun TopBar(title: String, nav: PlayerNav?, showBack: Boolean, actions: @Composable () -> Unit = {}) {
    val sc = LocalScheme.current
    Box(
        Modifier.fillMaxWidth().statusBarsPadding().height(44.dp)
            .background(Brush.verticalGradient(listOf(Color(0x33FFFFFF), Color(0x0FFFFFFF)))),
    ) {
        if (showBack && nav != null) GlossPill("Back", { nav.pop() }, Modifier.align(Alignment.CenterStart).padding(start = 10.dp), icon = Glyph.BACK, height = 32.dp)
        Txt(title, Modifier.align(Alignment.Center).padding(horizontal = 96.dp), size = 17f, weight = FontWeight.Bold, align = TextAlign.Center)
        Row(Modifier.align(Alignment.CenterEnd).padding(end = 10.dp), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) { actions() }
        Box(Modifier.align(Alignment.BottomCenter).fillMaxWidth().height(1.dp).background(sc.onBg.copy(alpha = .14f)))
    }
}

@Composable
fun TrackRow(
    t: Track, nav: PlayerNav, snap: PlayerSnap, onPlay: () -> Unit, modifier: Modifier = Modifier,
    showArt: Boolean = true, index: Int? = null, sheetExtra: List<SheetItem> = emptyList(),
) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val fav by app.userData.favState(t.path)   // per-track state: a favourite toggle recomposes only this row
    val isCurrent = snap.track?.path == t.path
    val ctx = androidx.compose.ui.platform.LocalContext.current
    val rCode = app.prefs.swipeRowRight; val lCode = app.prefs.swipeRowLeft
    // built once per (track, favourite, settings): stable objects keep the row's pointer handlers from restarting
    val hw = LocalHardware.current   // the faithful device view has no swipe shortcuts
    val right = remember(t, fav, rCode, hw) { if (hw) null else swipeAction(rCode, app, t, fav, ctx) }
    val left = remember(t, fav, lCode, hw) { if (hw) null else swipeAction(lCode, app, t, fav, ctx) }
    SwipeRow(right = right, left = left, modifier = modifier) {
    IpodRow(onClick = onPlay, onLong = { openTrackSheet(app, nav, t, sheetExtra) }, height = 56.dp,
        leading = {
            if (showArt) Box(Modifier.size(42.dp)) {
                ArtImage(t.artKey, Modifier.fillMaxSize(), thumb = true, corner = 8.dp)
                if (isCurrent) Box(Modifier.fillMaxSize().background(Color(0x66000000)), contentAlignment = Alignment.Center) {
                    EqualizerBars(Modifier.size(22.dp), snap.playing, Color.White)
                }
            } else if (index != null) {
                Box(Modifier.width(30.dp), contentAlignment = Alignment.Center) {
                    if (isCurrent) EqualizerBars(Modifier.size(18.dp), snap.playing, sc.accent) else Txt("$index", size = 14f, color = sc.onBgDim)
                }
            }
        },
        trailing = {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                if (fav) GlyphIcon(Glyph.HEART_FILLED, Modifier.size(18.dp), sc.accent)
                SourceBadge(t.source)
                if (t.durationMs > 0) Txt(fmtTime(t.durationMs), size = 13f, color = rowDim())
                Box(Modifier.size(38.dp).clip(RoundedCornerShape(50)).clickable { openTrackSheet(app, nav, t, sheetExtra) }, contentAlignment = Alignment.Center) {
                    GlyphIcon(Glyph.MORE, Modifier.size(22.dp), rowDim())
                }
            }
        },
    ) { hi ->
        Column {
            Txt(t.title, size = 16f, weight = if (isCurrent) FontWeight.Bold else FontWeight.Medium, color = if (hi) Color.White else if (isCurrent) sc.accent else sc.onBg)
            val sub = listOf(t.artist, t.album).filter { it.isNotEmpty() }.joinToString(" - ")
            if (sub.isNotEmpty()) Txt(sub, size = 13f, color = if (hi) Color(0xDDFFFFFF) else sc.onBgDim)
        }
    }
    }
}

/** Colours match ipodsync's Listen page (.pill.src-*) so the two apps read as one system. */
private fun sourceColor(s: com.ipodemu.library.TrackSource): Color = when (s) {
    com.ipodemu.library.TrackSource.LOCAL -> Color(0xFF4EE0A1)
    com.ipodemu.library.TrackSource.IPOD -> Color(0xFF8B6BFF)
    com.ipodemu.library.TrackSource.JELLYFIN -> Color(0xFFFF6FAE)
    com.ipodemu.library.TrackSource.PLEX -> Color(0xFFE5A00D)
    com.ipodemu.library.TrackSource.NAS -> Color(0xFF5FB8E0)
    com.ipodemu.library.TrackSource.CLOUD -> Color(0xFFB27CE0)
}

@Composable
private fun SourceBadge(source: com.ipodemu.library.TrackSource) {
    val color = sourceColor(source)
    Box(Modifier.clip(RoundedCornerShape(50)).background(color.copy(alpha = 0.16f)).padding(horizontal = 7.dp, vertical = 3.dp)) {
        Txt(source.name.lowercase().replaceFirstChar { it.uppercase() }, size = 10f, weight = FontWeight.SemiBold, color = color)
    }
}

private fun swipeAction(code: Int, app: App, t: Track, fav: Boolean, ctx: android.content.Context): SwipeAction? = when (code) {
    1 -> SwipeAction("Play next", Glyph.QUEUE, { Color(0xFF2A5FB0) }) { app.player.addNext(t); android.widget.Toast.makeText(ctx, "Playing next", android.widget.Toast.LENGTH_SHORT).show() }
    2 -> SwipeAction(if (fav) "Unfavorite" else "Favorite", if (fav) Glyph.CLOSE else Glyph.HEART_FILLED, { Color(0xFFD9427A) }) { app.userData.toggleFavorite(t.path) }
    3 -> SwipeAction("Add to queue", Glyph.PLUS, { Color(0xFF2A5FB0) }) { app.player.addToQueue(t); android.widget.Toast.makeText(ctx, "Added to queue", android.widget.Toast.LENGTH_SHORT).show() }
    else -> null
}

fun openTrackSheet(app: App, nav: PlayerNav, t: Track, extra: List<SheetItem> = emptyList()) {
    val ud = app.userData
    val items = ArrayList<SheetItem>()
    items += SheetItem("Play next", Glyph.PLAY) { app.player.addNext(t) }
    items += SheetItem("Add to queue", Glyph.QUEUE) { app.player.addToQueue(t) }
    items += SheetItem(if (ud.isFavorite(t.path)) "Remove from favorites" else "Add to favorites", if (ud.isFavorite(t.path)) Glyph.HEART_FILLED else Glyph.HEART) { ud.toggleFavorite(t.path) }
    items += SheetItem("Add to playlist...", Glyph.PLUS) { nav.sheet = playlistPicker(app, nav, t) }
    if (t.isMusic && t.album.isNotEmpty()) items += SheetItem("Go to album", Glyph.ALBUM) { nav.nowPlaying = false; nav.push(Screen.Detail(DetailKind.ALBUM, t.albumKey)) }
    if (t.isMusic && t.artist.isNotEmpty()) items += SheetItem("Go to artist", Glyph.ARTIST) { nav.nowPlaying = false; nav.push(Screen.Detail(DetailKind.ARTIST, t.albumArtist.ifEmpty { t.artist })) }
    items += extra
    nav.sheet = SheetSpec(t.title, t.artist.ifEmpty { null }, items)
}

private fun playlistPicker(app: App, nav: PlayerNav, t: Track): SheetSpec {
    val ud = app.userData
    val items = ArrayList<SheetItem>()
    items += SheetItem("New playlist...", Glyph.PLUS) { nav.nameDialog = { name -> ud.createPlaylist(name, t.path) } }
    ud.playlists.forEach { p -> items += SheetItem(p.name, Glyph.LIST) { ud.addToPlaylist(p.id, t.path) } }
    return SheetSpec("Add to playlist", t.title, items)
}

@Composable
private fun NameDialog(onDone: (String) -> Unit, onDismiss: () -> Unit) {
    val sc = LocalScheme.current
    var text by remember { mutableStateOf("") }
    val fr = remember { FocusRequester() }
    LaunchedEffect(Unit) { try { fr.requestFocus() } catch (_: Exception) {} }
    Box(Modifier.fillMaxSize().background(Color(0xAA000000)).pointerInput(Unit) { detectTapGestures { onDismiss() } }, contentAlignment = Alignment.Center) {
        Column(
            Modifier.padding(24.dp).fillMaxWidth().clip(RoundedCornerShape(20.dp)).background(sc.top.mix(Color.Black, .4f))
                .pointerInput(Unit) { detectTapGestures { } }.padding(20.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            Txt("New playlist", size = 20f, weight = FontWeight.Bold)
            BasicTextField(
                text, { text = it }, singleLine = true, cursorBrush = SolidColor(sc.accent),
                textStyle = TextStyle(color = sc.onBg, fontSize = 18.sp),
                modifier = Modifier.fillMaxWidth().focusRequester(fr).clip(RoundedCornerShape(12.dp)).background(Color(0x22FFFFFF)).padding(14.dp),
            )
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.align(Alignment.End)) {
                GlossPill("Cancel", onDismiss)
                GlossPill("Create", { onDone(text); onDismiss() }, primary = true)
            }
        }
    }
}

/** Sets the background tint to a cover while this composable is on screen. */
@Composable
fun BackdropArt(nav: PlayerNav, artKey: String?) {
    DisposableEffect(artKey) { nav.overrideArt = artKey; onDispose { if (nav.overrideArt == artKey) nav.overrideArt = null } }
}

// ---- home ---------------------------------------------------------------------------------------------------------

@Composable
private fun HomeScreen(nav: PlayerNav, snap: PlayerSnap) {
    if (LocalHardware.current) { HardwareHome(nav, snap); return }
    val app = LocalApp.current
    val sc = LocalScheme.current
    val lib = app.library
    val libRev = LocalLibRev.current
    val recentAlbums = remember(libRev) { lib.recentAlbums(20) }
    val mixes = remember(libRev) { com.ipodemu.library.Recommender.mixes(lib, app.userData) }
    app.userData.rev
    app.ui.rev
    val clock by androidx.compose.runtime.produceState("") { while (true) { value = java.text.SimpleDateFormat("h:mm a", java.util.Locale.getDefault()).format(java.util.Date()); delay(15000) } }
    Column(Modifier.fillMaxSize()) {
        TopBar(if (app.prefs.timeInTitle) clock else "iPod", nav, showBack = false) {
            GlossButton({ nav.push(Screen.Search) }, size = 34.dp) { GlyphIcon(Glyph.SEARCH, Modifier.size(20.dp), Color.White) }
            GlossButton({ nav.push(Screen.Settings) }, size = 34.dp) { GlyphIcon(Glyph.GEAR, Modifier.size(20.dp), Color.White) }
        }
        LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 24.dp)) {
            item { NowPlayingCard(snap, nav) { val s = lib.songs(); if (s.isNotEmpty()) { app.player.shuffleAll(s); nav.nowPlaying = true } } }
            item {
                Row(Modifier.horizontalScroll(rememberScrollState()).padding(horizontal = 12.dp, vertical = 4.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    GlossPill("Shuffle All", { val s = lib.songs(); if (s.isNotEmpty()) { app.player.shuffleAll(s); nav.nowPlaying = true } }, icon = Glyph.SHUFFLE, height = 32.dp)
                    GlossPill("Favorites", { nav.push(Screen.Detail(DetailKind.FAVORITES)) }, icon = Glyph.HEART, height = 32.dp)
                    GlossPill("Recent", { nav.push(Screen.Detail(DetailKind.RECENT)) }, icon = Glyph.CLOCK, height = 32.dp)
                }
            }
            forYouShelves(mixes, nav)
            item { SectionHeader("Library") }
            item {
                Column(Modifier.padding(horizontal = 16.dp).clip(RoundedCornerShape(16.dp)).background(sc.card).border(1.dp, sc.cardBorder, RoundedCornerShape(16.dp))) {
                    MenuRow("Playlists", Glyph.LIST, "${lib.playlists().size + app.userData.playlists.size}") { nav.push(Screen.Lib(LibKind.PLAYLISTS)) }
                    MenuRow("Artists", Glyph.ARTIST, "${lib.artists().size}") { nav.push(Screen.Lib(LibKind.ARTISTS)) }
                    MenuRow("Albums", Glyph.ALBUM, "${lib.albums().size}") { nav.push(Screen.Lib(LibKind.ALBUMS)) }
                    MenuRow("Songs", Glyph.NOTE, "${lib.songs().size}") { nav.push(Screen.Lib(LibKind.SONGS)) }
                    MenuRow("Genres", Glyph.STAR, "${lib.genres().size}") { nav.push(Screen.Lib(LibKind.GENRES)) }
                    MenuRow("Voice Memos", Glyph.MIC, "${lib.memos.size}") { nav.push(Screen.Lib(LibKind.MEMOS)) }
                    if (lib.jellyfinTracks.isNotEmpty()) MenuRow("Jellyfin", Glyph.JELLYFIN, "${lib.jellyfinTracks.size}") { nav.push(Screen.Lib(LibKind.JELLYFIN)) }
                    if (lib.plexTracks.isNotEmpty()) MenuRow("Plex", Glyph.PLEX, "${lib.plexTracks.size}") { nav.push(Screen.Lib(LibKind.PLEX)) }
                    if (lib.nasTracks.isNotEmpty()) MenuRow("NAS", Glyph.NAS, "${lib.nasTracks.size}") { nav.push(Screen.Lib(LibKind.NAS)) }
                }
            }
            if (recentAlbums.isNotEmpty()) {
                item { SectionHeader("Recently Added") }
                item {
                    LazyRow(contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(14.dp)) {
                        items(recentAlbums, key = { it.tracks.first().albumKey }) { g ->
                            AlbumCard(g, Modifier.width(150.dp)) { nav.push(Screen.Detail(DetailKind.ALBUM, g.tracks.first().albumKey)) }
                        }
                    }
                }
            }
            val by = lib.byPath()
            val recents = app.userData.recents.mapNotNull { by[it] }.take(15)
            if (recents.isNotEmpty()) {
                item { SectionHeader("Recently Played") }
                item {
                    LazyRow(contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(14.dp)) {
                        itemsIndexed(recents) { i, t ->
                            TrackCard(t, Modifier.width(130.dp)) { app.player.play(recents, i, null); nav.nowPlaying = true }
                        }
                    }
                }
            }
        }
    }
}

@Composable
fun SectionHeader(text: String) {
    val sc = LocalScheme.current
    Txt(text.uppercase(), Modifier.padding(start = 20.dp, top = 22.dp, bottom = 8.dp), size = 13f, weight = FontWeight.Bold, color = sc.onBgDim)
}

@Composable
private fun MenuRow(title: String, g: Glyph, count: String, onClick: () -> Unit) {
    val sc = LocalScheme.current
    IpodRow(onClick, height = 52.dp, leading = { IconTile(g, size = 38.dp) },
        trailing = { Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) { Txt(count, size = 14f, color = sc.onBgDim); GlyphIcon(Glyph.CHEVRON, Modifier.size(18.dp), sc.onBgDim) } }) { hi ->
        Txt(title, size = 17f, weight = FontWeight.Medium, color = if (hi) Color.White else sc.onBg)
    }
}

@Composable
fun AlbumCard(g: Group, modifier: Modifier = Modifier, onClick: () -> Unit) {
    val sc = LocalScheme.current
    val src = remember { MutableInteractionSource() }
    val focused by src.collectIsFocusedAsState()
    Column(modifier.clip(RoundedCornerShape(14.dp)).clickable(src, null, onClick = onClick).padding(4.dp)) {
        ArtImage(g.artKey, Modifier.fillMaxWidth().aspectRatio(1f).border(if (focused) 3.dp else 0.dp, if (focused) sc.accent else Color.Transparent, RoundedCornerShape(12.dp)), corner = 12.dp)
        Txt(g.name, Modifier.padding(top = 8.dp), size = 14f, weight = FontWeight.SemiBold)
        Txt(g.tracks.firstOrNull()?.artist ?: "", size = 12f, color = sc.onBgDim)
        g.tracks.firstOrNull()?.let { Box(Modifier.padding(top = 4.dp)) { SourceBadge(it.source) } }
    }
}

@Composable
private fun TrackCard(t: Track, modifier: Modifier = Modifier, onClick: () -> Unit) {
    val sc = LocalScheme.current
    val src = remember { MutableInteractionSource() }
    val focused by src.collectIsFocusedAsState()
    Column(modifier.clip(RoundedCornerShape(14.dp)).clickable(src, null, onClick = onClick).padding(4.dp)) {
        ArtImage(t.artKey, Modifier.fillMaxWidth().aspectRatio(1f).border(if (focused) 3.dp else 0.dp, if (focused) sc.accent else Color.Transparent, RoundedCornerShape(12.dp)), thumb = true, corner = 12.dp)
        Txt(t.title, Modifier.padding(top = 8.dp), size = 13f, weight = FontWeight.SemiBold)
        Txt(t.artist, size = 12f, color = sc.onBgDim)
        Box(Modifier.padding(top = 4.dp)) { SourceBadge(t.source) }
    }
}

// ---- library lists ------------------------------------------------------------------------------------------------

@Composable
private fun LibraryScreen(kind: LibKind, nav: PlayerNav, snap: PlayerSnap) {
    LocalLibRev.current
    val app = LocalApp.current
    val lib = app.library
    app.userData.rev
    Column(Modifier.fillMaxSize()) {
        TopBar(kind.title, nav, showBack = true) {
            GlossButton({ nav.push(Screen.Search) }, size = 34.dp) { GlyphIcon(Glyph.SEARCH, Modifier.size(20.dp), Color.White) }
        }
        when (kind) {
            LibKind.SONGS -> {
                var sort by androidx.compose.runtime.saveable.rememberSaveable { androidx.compose.runtime.mutableIntStateOf(0) }
                var sourceFilter by androidx.compose.runtime.saveable.rememberSaveable { androidx.compose.runtime.mutableStateOf<com.ipodemu.library.TrackSource?>(null) }
                val libRev = LocalLibRev.current
                val allSongs = remember(sort, libRev) { sortedSongs(lib, app.userData, sort) }
                val songs = remember(allSongs, sourceFilter) { sourceFilter?.let { f -> allSongs.filter { it.source == f } } ?: allSongs }
                SongList(songs, nav, snap, showArt = true, sections = if (sort == 0) 1 else if (sort == 1) 2 else 0,
                    header = {
                        ShuffleHeader(songs, nav, SORTS[sort]) { sort = (sort + 1) % SORTS.size }
                        SourceFilterChips(allSongs, sourceFilter) { sourceFilter = it }
                    })
            }
            LibKind.MEMOS -> { val memos = lib.memos; SongList(memos, nav, snap, showArt = false) }
            LibKind.ALBUMS -> AlbumGrid(lib.albums(), nav)
            LibKind.ARTISTS -> GroupList(lib.artists(), nav, circle = true) { Screen.Detail(DetailKind.ARTIST, it.name) }
            LibKind.GENRES -> GroupList(lib.genres(), nav, circle = false) { Screen.Detail(DetailKind.GENRE, it.name) }
            LibKind.PLAYLISTS -> PlaylistsList(nav)
            LibKind.JELLYFIN -> SongList(lib.jellyfinTracks.sortedBy { sortKey(it.title) }, nav, snap, showArt = true, sections = 1)
            LibKind.PLEX -> SongList(lib.plexTracks.sortedBy { sortKey(it.title) }, nav, snap, showArt = true, sections = 1)
            LibKind.NAS -> SongList(lib.nasTracks.sortedBy { sortKey(it.title) }, nav, snap, showArt = true, sections = 1)
        }
    }
}

@Composable
private fun ShuffleHeader(tracks: List<Track>, nav: PlayerNav, sortLabel: String, onSort: () -> Unit) {
    val app = LocalApp.current
    Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 12.dp), horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
        GlossPill("Play", { app.player.play(tracks, 0, null); nav.nowPlaying = true }, icon = Glyph.PLAY, primary = true)
        GlossPill("Shuffle", { app.player.play(tracks, tracks.indices.random(), true); nav.nowPlaying = true }, icon = Glyph.SHUFFLE)
        GlossPill(sortLabel, onSort, height = 32.dp)
        Txt(songCount(tracks.size), Modifier.weight(1f), size = 13f, color = LocalScheme.current.onBgDim, align = TextAlign.End)
    }
}

/** "All / Local / iPod / Jellyfin" chips (with live counts) narrowing the Songs screen to one
 * source -- a pure display filter, same list ipodsync's Listen page shows, chosen (over grouped
 * sections) to match this list's existing pattern of a filter row above a flat, sortable list. */
@Composable
private fun SourceFilterChips(all: List<Track>, current: com.ipodemu.library.TrackSource?, onChange: (com.ipodemu.library.TrackSource?) -> Unit) {
    val counts = remember(all) { all.groupingBy { it.source }.eachCount() }
    if (counts.size <= 1) return   // nothing to filter when everything's from one source
    Row(
        Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()).padding(horizontal = 16.dp, vertical = 4.dp),
        horizontalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        @Composable
        fun chip(label: String, value: com.ipodemu.library.TrackSource?, count: Int) {
            GlossPill("$label ($count)", { onChange(value) }, height = 30.dp, primary = current == value)
        }
        chip("All", null, all.size)
        for (s in com.ipodemu.library.TrackSource.entries) {
            val n = counts[s] ?: continue
            chip(s.name.lowercase().replaceFirstChar { it.uppercase() }, s, n)
        }
    }
}

@Composable
@OptIn(androidx.compose.foundation.ExperimentalFoundationApi::class)
fun SongList(
    tracks: List<Track>, nav: PlayerNav, snap: PlayerSnap, showArt: Boolean, numbered: Boolean = false,
    sheetExtra: List<SheetItem> = emptyList(), header: (@Composable () -> Unit)? = null, sections: Int = 0,
) {
    val app = LocalApp.current
    if (tracks.isEmpty()) { EmptyState("Nothing here yet"); return }
    val sc = LocalScheme.current
    val row: @Composable (Int, Track) -> Unit = { i, t ->
        TrackRow(t, nav, snap, showArt = showArt, index = if (numbered) i + 1 else null, sheetExtra = sheetExtra, onPlay = {
            app.player.play(tracks, i, null); nav.nowPlaying = true
        })
    }
    val groups = remember(tracks, sections) { if (sections > 0) tracks.withIndex().groupBy { letterOf(sortKey(if (sections == 2) it.value.artist.ifEmpty { "#" } else it.value.title)) } else emptyMap() }
    LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 24.dp)) {
        if (header != null) item { header() }
        if (sections > 0) {
            groups.forEach { (l, list) ->
                stickyHeader(key = "h$l") { LetterHeader(l) }
                items(list, key = { "${it.index}${it.value.path}" }) { row(it.index, it.value) }
            }
        } else itemsIndexed(tracks, key = { i, t -> "$i${t.path}" }) { i, t -> row(i, t) }
        item { CountFooter(songCount(tracks.size) + totalTime(tracks)) }
    }
}

@Composable
private fun AlbumGrid(albums: List<Group>, nav: PlayerNav) {
    if (albums.isEmpty()) { EmptyState("No albums yet"); return }
    LazyVerticalGrid(GridCells.Adaptive(150.dp), Modifier.fillMaxSize(), contentPadding = PaddingValues(12.dp, 12.dp, 12.dp, 24.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        items(albums, key = { it.tracks.first().albumKey }) { g -> AlbumCard(g) { nav.push(Screen.Detail(DetailKind.ALBUM, g.tracks.first().albumKey)) } }
    }
}

@OptIn(androidx.compose.foundation.ExperimentalFoundationApi::class)
@Composable
private fun GroupList(groups: List<Group>, nav: PlayerNav, circle: Boolean, target: (Group) -> Screen) {
    val sc = LocalScheme.current
    if (groups.isEmpty()) { EmptyState("Nothing here yet"); return }
    val sections = remember(groups) { groups.groupBy { letterOf(sortKey(it.name)) } }
    LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 24.dp)) {
        sections.forEach { (l, list) ->
            stickyHeader(key = "h$l") { LetterHeader(l) }
            items(list, key = { it.name }) { g ->
            IpodRow({ nav.push(target(g)) }, height = 56.dp,
                leading = { ArtImage(g.artKey, Modifier.size(44.dp), thumb = true, corner = 10.dp, circle = circle) },
                trailing = { CountChevron(g.tracks.size) }) { hi ->
                Txt(g.name, size = 17f, weight = FontWeight.Medium, color = if (hi) Color.White else sc.onBg)
            }
            }
        }
        item { CountFooter("${groups.size} ${if (circle) "artists" else "items"}") }
    }
}

@Composable
private fun PlaylistsList(nav: PlayerNav) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val ud = app.userData
    ud.rev
    val lib = app.library
    LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 24.dp)) {
        item {
            IpodRow({ nav.nameDialog = { ud.createPlaylist(it) } }, height = 52.dp, leading = { IconTile(Glyph.PLUS, size = 44.dp) }) { hi ->
                Txt("New Playlist...", size = 16f, weight = FontWeight.Medium, color = if (hi) Color.White else sc.accent)
            }
        }
        item {
            val favs = ud.favorites.size
            IpodRow({ nav.push(Screen.Detail(DetailKind.FAVORITES)) }, height = 56.dp, leading = { IconTile(Glyph.HEART_FILLED, size = 44.dp, tint = Color(0xFFE5486B)) },
                trailing = { CountChevron(favs) }) { hi ->
                Txt("Favorites", size = 17f, weight = FontWeight.Medium, color = if (hi) Color.White else sc.onBg)
            }
        }
        items(ud.playlists.toList(), key = { it.id }) { p ->
            val by = lib.byPath()
            val first = p.paths.firstNotNullOfOrNull { by[it]?.artKey }
            IpodRow({ nav.push(Screen.Detail(DetailKind.USER, p.id)) },
                onLong = { nav.sheet = SheetSpec(p.name, songCount(p.paths.size), listOf(SheetItem("Delete playlist", Glyph.CLOSE) { ud.deletePlaylist(p.id) })) },
                height = 56.dp, leading = { ArtImage(first, Modifier.size(44.dp), thumb = true, corner = 10.dp) },
                trailing = { CountChevron(p.paths.size) }) { hi ->
                Txt(p.name, size = 17f, weight = FontWeight.Medium, color = if (hi) Color.White else sc.onBg)
            }
        }
        items(lib.playlists(), key = { "f" + it.name }) { g ->
            IpodRow({ nav.push(Screen.Detail(DetailKind.FOLDER, g.name)) }, height = 56.dp,
                leading = { ArtImage(g.artKey, Modifier.size(44.dp), thumb = true, corner = 10.dp) },
                trailing = { CountChevron(g.tracks.size) }) { hi ->
                Txt(g.name, size = 17f, weight = FontWeight.Medium, color = if (hi) Color.White else sc.onBg)
            }
        }
    }
}

@Composable
fun EmptyState(text: String) {
    val sc = LocalScheme.current
    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(12.dp)) {
            GlyphIcon(Glyph.NOTE, Modifier.size(56.dp), sc.onBg.copy(alpha = .3f))
            Txt(text, size = 16f, color = sc.onBgDim)
        }
    }
}

// ---- detail pages -------------------------------------------------------------------------------------------------

@Composable
private fun DetailScreen(d: Screen.Detail, nav: PlayerNav, snap: PlayerSnap) {
    LocalLibRev.current
    val app = LocalApp.current
    val lib = app.library
    val ud = app.userData
    ud.rev
    val libRev = LocalLibRev.current
    val by = lib.byPath()
    var title = ""; var subtitle = ""; var art: String? = null
    val mix = if (d.kind == DetailKind.MIX) remember(d.id, libRev) { com.ipodemu.library.Recommender.find(d.id, lib, ud) } else null
    var tracks: List<Track> = emptyList()
    var userId: String? = null
    when (d.kind) {
        DetailKind.ALBUM -> lib.albums().firstOrNull { it.tracks.firstOrNull()?.albumKey == d.id }?.let { title = it.name; tracks = it.tracks; art = it.artKey; subtitle = it.tracks.firstOrNull()?.artist ?: "" }
        DetailKind.ARTIST -> lib.artists().firstOrNull { it.name == d.id }?.let { title = it.name; tracks = it.tracks; art = it.artKey }
        DetailKind.GENRE -> lib.genres().firstOrNull { it.name == d.id }?.let { title = it.name; tracks = it.tracks; art = it.artKey }
        DetailKind.FOLDER -> lib.playlists().firstOrNull { it.name == d.id }?.let { title = it.name; tracks = it.tracks; art = it.artKey }
        DetailKind.USER -> ud.playlists.firstOrNull { it.id == d.id }?.let { title = it.name; tracks = it.paths.mapNotNull { p -> by[p] }; art = tracks.firstNotNullOfOrNull { t -> t.artKey }; userId = it.id }
        DetailKind.FAVORITES -> { title = "Favorites"; tracks = ud.favorites.mapNotNull { by[it] }; art = tracks.firstNotNullOfOrNull { it.artKey } }
        DetailKind.RECENT -> { title = "Recently Played"; tracks = ud.recents.mapNotNull { by[it] }; art = tracks.firstNotNullOfOrNull { it.artKey } }
        DetailKind.MIX -> mix?.let { title = it.title; subtitle = it.subtitle; tracks = it.tracks; art = it.artKey }
    }
    BackdropArt(nav, art)
    val total = tracks.sumOf { it.durationMs }
    val info = songCount(tracks.size) + if (total > 0) "  -  " + (total / 60000).toString() + " min" else ""
    val extra = if (userId != null) listOf(SheetItem("Remove from playlist", Glyph.CLOSE) { }) else emptyList()
    Column(Modifier.fillMaxSize()) {
        TopBar(title.ifEmpty { "Playlist" }, nav, showBack = true)
        if (tracks.isEmpty()) { EmptyState(if (d.kind == DetailKind.FAVORITES) "Tap the heart on a song to add it here" else "Nothing here yet"); return@Column }
        BoxWithConstraints(Modifier.fillMaxSize()) {
            val wide = maxWidth > 620.dp && maxWidth > maxHeight * 1.25f
            if (wide) {
                Row(Modifier.fillMaxSize()) {
                    Column(Modifier.width(300.dp).padding(20.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
                        DetailHeader(art, title, subtitle.ifEmpty { info }, if (subtitle.isEmpty()) "" else info, tracks, nav)
                    }
                    SongList(tracks, nav, snap, showArt = false, numbered = true, sheetExtra = removeExtra(userId, ud))
                }
            } else {
                SongList(tracks, nav, snap, showArt = false, numbered = true, sheetExtra = removeExtra(userId, ud), header = {
                    Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
                        DetailHeader(art, title, subtitle.ifEmpty { info }, if (subtitle.isEmpty()) "" else info, tracks, nav)
                    }
                })
            }
        }
    }
}

private fun removeExtra(userId: String?, ud: com.ipodemu.library.UserData): List<SheetItem> = emptyList()

@Composable
private fun DetailHeader(art: String?, title: String, line1: String, line2: String, tracks: List<Track>, nav: PlayerNav) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    Row(horizontalArrangement = Arrangement.spacedBy(14.dp), verticalAlignment = Alignment.CenterVertically) {
        ArtImage(art, Modifier.size(104.dp), corner = 12.dp)
        Column(verticalArrangement = Arrangement.spacedBy(3.dp)) {
            Txt(title, size = 20f, weight = FontWeight.Bold, maxLines = 2)
            if (line1.isNotEmpty()) Txt(line1, size = 13f, color = sc.onBgDim)
            if (line2.isNotEmpty()) Txt(line2, size = 12f, color = sc.onBgDim)
            Row(Modifier.padding(top = 6.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        GlossPill("Play", { app.player.play(tracks, 0, null); nav.nowPlaying = true }, icon = Glyph.PLAY, primary = true, height = 36.dp)
                GlossPill("Shuffle", { app.player.play(tracks, tracks.indices.random(), true); nav.nowPlaying = true }, icon = Glyph.SHUFFLE, height = 36.dp)
            }
        }
    }
}

// ---- search -------------------------------------------------------------------------------------------------------

private class Results(val songs: List<Track>, val albums: List<Group>)

/** A small "you already have this" tag, so an owned result reads as obviously different from a
 * catalog hit at a glance without needing to compare sections. */
@Composable
private fun OwnedTag() {
    val sc = LocalScheme.current
    Box(Modifier.clip(RoundedCornerShape(6.dp)).background(Color(0xFF2E7D4F)).padding(horizontal = 7.dp, vertical = 3.dp)) {
        Txt("OWNED", size = 10f, weight = FontWeight.Bold, color = Color.White)
    }
}

@Composable
private fun SearchScreen(nav: PlayerNav, snap: PlayerSnap) {
    val app = LocalApp.current
    val libRev = rememberLibRev(app.library)
    // the focused text field used to swallow the first Back press; leave the screen straight away
    BackHandler(enabled = !app.ui.pickerOpen && nav.sheet == null && nav.nameDialog == null) { nav.pop() }
    val sc = LocalScheme.current
    var query by remember { mutableStateOf("") }
    var results by remember { mutableStateOf(Results(emptyList(), emptyList())) }
    var catalog by remember { mutableStateOf(com.ipodemu.library.CatalogResults.EMPTY) }
    var catalogLoading by remember { mutableStateOf(false) }
    val fr = remember { FocusRequester() }
    // no auto-focus: it popped the keyboard, whose first Back press hid it instead of leaving the screen (and trapped controller focus in the field)
    // Keyed on libRev too, not just query -- a download landing (Library's own background merge)
    // used to leave stale results on screen until the user retyped the search themselves.
    LaunchedEffect(query, libRev) {
        val q = query.trim()
        if (q.isEmpty()) { results = Results(emptyList(), emptyList()); return@LaunchedEffect }
        delay(140)
        results = withContext(Dispatchers.Default) {
            val lib = app.library
            Results(
                lib.songs().filter { it.title.contains(q, true) || it.artist.contains(q, true) || it.album.contains(q, true) }.take(150),
                lib.albums().filter { it.name.contains(q, true) || (it.tracks.firstOrNull()?.artist ?: "").contains(q, true) }.take(30),
            )
        }
    }
    // Separately debounced (longer -- these are live network calls to Lidarr/Soulseek, not a local
    // filter) so typing doesn't fire a search per keystroke.
    LaunchedEffect(query) {
        val q = query.trim()
        if (q.isEmpty()) { catalog = com.ipodemu.library.CatalogResults.EMPTY; catalogLoading = false; return@LaunchedEffect }
        delay(600)
        catalogLoading = true
        catalog = try { app.library.catalogSearch(q) } catch (_: Exception) { com.ipodemu.library.CatalogResults.EMPTY }
        catalogLoading = false
    }
    Column(Modifier.fillMaxSize().imePadding()) {
        TopBar("Search", nav, showBack = true)
        Row(
            Modifier.padding(16.dp).fillMaxWidth().height(46.dp).clip(RoundedCornerShape(50)).background(Color(0x26FFFFFF)).border(1.dp, sc.cardBorder, RoundedCornerShape(50)).padding(horizontal = 16.dp),
            verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            GlyphIcon(Glyph.SEARCH, Modifier.size(20.dp), sc.onBgDim)
            Box(Modifier.weight(1f)) {
                if (query.isEmpty()) Txt("Songs, albums, artists", size = 16f, color = sc.onBgDim)
                BasicTextField(query, { query = it }, singleLine = true, cursorBrush = SolidColor(sc.accent), textStyle = TextStyle(color = sc.onBg, fontSize = 16.sp), modifier = Modifier.fillMaxWidth().focusRequester(fr))
            }
            if (query.isNotEmpty()) Box(Modifier.size(28.dp).clickable { query = "" }, contentAlignment = Alignment.Center) { GlyphIcon(Glyph.CLOSE, Modifier.size(18.dp), sc.onBgDim) }
        }
        val r = results
        val c = catalog
        val nothingAtAll = r.songs.isEmpty() && r.albums.isEmpty() && c.albums.isEmpty() && c.tracks.isEmpty() && !catalogLoading
        // A confident single best match first (like a "top result" card), everything else -- other
        // songs, albums, not-yet-owned catalog hits -- collapsed behind a "Show more" by default so
        // a broad query doesn't read as a wall of equally-weighted rows. Ranked simply: an exact
        // title match beats a prefix match beats a loose contains/artist-only match.
        val q = query.trim()
        fun songScore(t: Track) = when {
            t.title.equals(q, true) -> 0
            t.title.startsWith(q, true) -> 1
            t.title.contains(q, true) -> 2
            t.artist.equals(q, true) -> 3
            else -> 4
        }
        val rankedSongs = remember(r.songs, q) { r.songs.sortedBy { songScore(it) } }
        val topSong = rankedSongs.firstOrNull()
        val restSongs = if (topSong != null) rankedSongs.drop(1) else rankedSongs
        var songsExpanded by remember(q) { mutableStateOf(false) }
        var albumsExpanded by remember(q) { mutableStateOf(false) }
        val songCap = 4; val albumCap = 3
        if (query.isBlank()) EmptyState("Search your music")
        else if (nothingAtAll) {
            Column(Modifier.fillMaxSize()) {
                Box(Modifier.weight(1f)) { EmptyState("No results") }
                DownloadRequestBar(app, query.trim())
            }
        }
        else LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 24.dp)) {
            if (topSong != null) {
                item { SectionHeader("Top result") }
                item(key = "top${topSong.path}") {
                    TopResultCard(topSong) { app.player.play(listOf(topSong), 0, null); nav.nowPlaying = true }
                }
            }
            if (restSongs.isNotEmpty()) {
                item { SectionHeader("Songs") }
                val shown = if (songsExpanded) restSongs else restSongs.take(songCap)
                itemsIndexed(shown, key = { i, t -> "s$i${t.path}" }) { i, t -> TrackRow(t, nav, snap, onPlay = { app.player.play(shown, i, null); nav.nowPlaying = true }) }
                if (!songsExpanded && restSongs.size > songCap) {
                    item { ShowMoreRow(restSongs.size - songCap) { songsExpanded = true } }
                }
            }
            if (c.tracks.isNotEmpty()) {
                item { SectionHeader("Found on Soulseek") }
                itemsIndexed(c.tracks, key = { i, t -> "ct$i${t.artist}${t.title}" }) { _, t ->
                    CatalogRow(t.title, t.artist, t.imageUrl) { app.library.requestDownload(t.artist, t.title, "") }
                }
            }
            if (r.albums.isNotEmpty()) {
                item { SectionHeader("Albums") }
                val shownAlbums = if (albumsExpanded) r.albums else r.albums.take(albumCap)
                items(shownAlbums, key = { "b" + it.tracks.first().albumKey }) { g ->
                    IpodRow({ nav.push(Screen.Detail(DetailKind.ALBUM, g.tracks.first().albumKey)) }, height = 56.dp, leading = { ArtImage(g.artKey, Modifier.size(48.dp), thumb = true, corner = 8.dp) },
                        trailing = { OwnedTag() }) { hi ->
                        Column { Txt(g.name, size = 16f, color = if (hi) Color.White else sc.onBg); Txt(g.tracks.firstOrNull()?.artist ?: "", size = 13f, color = if (hi) Color(0xDDFFFFFF) else sc.onBgDim) }
                    }
                }
                if (!albumsExpanded && r.albums.size > albumCap) {
                    item { ShowMoreRow(r.albums.size - albumCap) { albumsExpanded = true } }
                }
            }
            if (c.albums.isNotEmpty()) {
                item { SectionHeader("Albums you don't have") }
                itemsIndexed(c.albums, key = { i, al -> "cb$i${al.artist}${al.title}" }) { _, al ->
                    CatalogRow(al.title, al.artist, al.imageUrl) { app.library.requestDownload(al.artist, "", al.title) }
                }
            }
            if (catalogLoading) item { Row(Modifier.fillMaxWidth().padding(16.dp), horizontalArrangement = Arrangement.Center) { Txt("Searching Lidarr/Soulseek...", size = 13f, color = sc.onBgDim) } }
            item { DownloadRequestBar(app, query.trim(), compact = true) }
        }
    }
}

/** One row in a "not in your library" catalog section -- cover art (when the source has any),
 * title/subtitle, and an explicit Download pill so the action is obvious without relying on the
 * section header alone. */
@Composable
private fun CatalogRow(title: String, subtitle: String, imageUrl: String?, circle: Boolean = false, onDownload: () -> Unit) {
    val sc = LocalScheme.current
    IpodRow({ onDownload() }, height = 56.dp,
        leading = { RemoteArtImage(imageUrl, Modifier.size(48.dp).let { if (circle) it.clip(CircleShape) else it }, corner = if (circle) 24.dp else 8.dp) },
        trailing = { GlossPill("Download", onDownload, height = 32.dp) },
    ) { hi ->
        Column { Txt(title, size = 15f, color = if (hi) Color.White else sc.onBg, maxLines = 1); Txt(subtitle, size = 12f, color = if (hi) Color(0xDDFFFFFF) else sc.onBgDim, maxLines = 1) }
    }
}

/** The single best-matching owned song for the current query, shown large above everything else --
 * YT-Music-style "one clear answer first" instead of a flat wall of equally-weighted rows. */
@Composable
private fun TopResultCard(t: Track, onPlay: () -> Unit) {
    val sc = LocalScheme.current
    val app = LocalApp.current
    // NAS scans and Cloud/Soulseek injections never populate artKey (no embedded-tag reading, no
    // local art cache entry) -- fall back to the same Lidarr cover lookup catalog rows use, so the
    // one card the user actually sees first isn't stuck with the generic note-glyph placeholder.
    var fallbackArtUrl by remember(t.path) { mutableStateOf<String?>(null) }
    LaunchedEffect(t.path) {
        if (t.artKey == null) fallbackArtUrl = app.library.albumArtUrl(t.artist, t.album)
    }
    Row(
        Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 4.dp)
            .clip(RoundedCornerShape(16.dp)).background(sc.accent.copy(alpha = 0.12f))
            .clickable(onClick = onPlay).padding(12.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        if (t.artKey == null && fallbackArtUrl != null) RemoteArtImage(fallbackArtUrl, Modifier.size(64.dp), corner = 10.dp)
        else ArtImage(t.artKey, Modifier.size(64.dp), thumb = true, corner = 10.dp)
        Column(Modifier.weight(1f).padding(start = 14.dp)) {
            Txt(t.title, size = 18f, weight = FontWeight.Bold, color = sc.onBg, maxLines = 1)
            Txt(t.artist, size = 14f, color = sc.onBgDim, maxLines = 1)
            Box(Modifier.padding(top = 4.dp)) { SourceBadge(t.source) }
        }
        Box(Modifier.size(44.dp).clip(CircleShape).background(sc.accent).clickable(onClick = onPlay), contentAlignment = Alignment.Center) {
            GlyphIcon(Glyph.PLAY, Modifier.size(20.dp), Color.White)
        }
    }
}

/** Collapsed-section expander -- keeps a long song/album list from dumping everything at once. */
@Composable
private fun ShowMoreRow(count: Int, onClick: () -> Unit) {
    val sc = LocalScheme.current
    Row(
        Modifier.fillMaxWidth().clickable(onClick = onClick).padding(horizontal = 20.dp, vertical = 12.dp),
        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        Txt("Show $count more", size = 14f, weight = FontWeight.Medium, color = sc.accent)
    }
}

/** Search's "Download this instead" affordance: parses a naive "Artist - Title" split from the
 * query (or falls back to using the whole query as both), and shows live status once requested --
 * see Library.requestDownload/DownloadCoordinator for what actually happens on tap. */
@Composable
private fun DownloadRequestBar(app: App, query: String, compact: Boolean = false) {
    if (query.isEmpty()) return
    val sc = LocalScheme.current
    val status = app.library.downloadStatus
    val parts = query.split(" - ", limit = 2)
    val artist = parts[0].trim()
    val title = if (parts.size > 1) parts[1].trim() else query
    Column(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = if (compact) 10.dp else 20.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        if (!compact) Txt("Can't find \"$query\"?", size = 14f, color = sc.onBgDim)
        GlossPill("Download \"$query\"", { app.library.requestDownload(artist, title, "") }, height = 40.dp, primary = !compact)
        status?.let { s ->
            val color = when (s.stage) {
                com.ipodemu.library.DownloadStage.DONE -> Color(0xFF7CE0A0)
                com.ipodemu.library.DownloadStage.FAILED -> Color(0xFFFF8080)
                else -> sc.onBgDim
            }
            Txt(s.message, size = 13f, color = color)
        }
    }
}

// ---- queue --------------------------------------------------------------------------------------------------------

@Composable
private fun QueueScreen(nav: PlayerNav, snap: PlayerSnap) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val q = app.player.queueTracks()
    Column(Modifier.fillMaxSize()) {
        TopBar("Up Next", nav, showBack = true) { if (q.isNotEmpty()) GlossPill("Clear", { app.player.clearQueue(); nav.pop() }, height = 30.dp) }
        if (q.isEmpty()) { EmptyState("The queue is empty"); return@Column }
        LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 24.dp)) {
            itemsIndexed(q, key = { i, t -> "$i${t.path}" }) { i, t ->
                val cur = i == snap.index
                val remove = remember(i, t) { SwipeAction("Remove", Glyph.CLOSE, { Color(0xFFD9423F) }) { app.player.removeFromQueue(i) } }
                SwipeRow(right = remove, left = remove) {
                IpodRow({ app.player.skipTo(i) }, height = 56.dp,
                    leading = { Box(Modifier.width(30.dp), contentAlignment = Alignment.Center) { if (cur) EqualizerBars(Modifier.size(18.dp), snap.playing, sc.accent) else Txt("${i + 1}", size = 14f, color = sc.onBgDim) } },
                    trailing = { Box(Modifier.size(40.dp).clip(RoundedCornerShape(50)).clickable { app.player.removeFromQueue(i) }, contentAlignment = Alignment.Center) { GlyphIcon(Glyph.CLOSE, Modifier.size(18.dp), sc.onBgDim) } }) { hi ->
                    Column {
                        Txt(t.title, size = 16f, weight = if (cur) FontWeight.Bold else FontWeight.Medium, color = if (hi) Color.White else if (cur) sc.accent else sc.onBg)
                        Txt(t.artist, size = 13f, color = if (hi) Color(0xDDFFFFFF) else sc.onBgDim)
                    }
                }
                }
            }
        }
    }
}

// ---- iPod list conventions ----------------------------------------------------------------------------------------

fun letterOf(s: String): String = s.trimStart().firstOrNull()?.let { if (it.isLetter()) it.uppercaseChar().toString() else "#" } ?: "#"

fun totalTime(tracks: List<Track>): String {
    val min = tracks.sumOf { it.durationMs } / 60000
    return if (min <= 0) "" else if (min >= 60) ", ${min / 60} hr ${min % 60} min" else ", $min min"
}

/** Grey letter bar that sticks to the top while its section scrolls, like the iPod's alphabetical lists. */
@Composable
fun LetterHeader(letter: String) {
    val sc = LocalScheme.current
    Box(Modifier.fillMaxWidth().height(24.dp).background(Brush.verticalGradient(listOf(sc.top.copy(alpha = .96f), sc.bottom.copy(alpha = .96f)))).padding(horizontal = 16.dp), contentAlignment = Alignment.CenterStart) {
        Txt(letter, size = 13f, weight = FontWeight.Bold, color = sc.accentLight)
    }
}

/** "583 Songs, 2 hr 10 min" line at the end of a list. */
@Composable
fun CountFooter(text: String) {
    Box(Modifier.fillMaxWidth().padding(vertical = 18.dp), contentAlignment = Alignment.Center) {
        Txt(text, size = 13f, color = LocalScheme.current.onBgDim)
    }
}

/** Right-aligned count + disclosure chevron, as in the iPod's Playlists / Artists lists. */
@Composable
fun CountChevron(count: Int) {
    val sc = LocalScheme.current
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
        Txt("$count", size = 14f, color = rowDim())
        GlyphIcon(Glyph.CHEVRON, Modifier.size(18.dp), rowDim())
    }
}

// ---- richer browsing ----------------------------------------------------------------------------------------------

val SORTS = listOf("A-Z", "Artist", "Recent", "Most played")

fun sortedSongs(lib: com.ipodemu.library.Library, ud: com.ipodemu.library.UserData, sort: Int): List<Track> = when (sort) {
    1 -> lib.songs().sortedWith(compareBy({ sortKey(it.artist.ifEmpty { "~" }) }, { sortKey(it.album) }, { it.discNo }, { it.trackNo }))
    2 -> lib.songs().sortedByDescending { it.mtime }
    3 -> lib.songs().sortedByDescending { ud.plays[it.path] ?: 0 }.filter { (ud.plays[it.path] ?: 0) > 0 }.ifEmpty { lib.songs() }
    else -> lib.songs()
}
