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
}

enum class DetailKind { ALBUM, ARTIST, FOLDER, USER, GENRE, FAVORITES, RECENT }

sealed interface Screen {
    data object Home : Screen
    data class Lib(val kind: LibKind) : Screen
    data class Detail(val kind: DetailKind, val id: String = "") : Screen
    data object Search : Screen
    data object Queue : Screen
    data object Settings : Screen
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

    BackHandler(enabled = nav.sheet != null || nav.nameDialog != null) { nav.sheet = null; nav.nameDialog = null }
    BackHandler(enabled = nav.sheet == null && nav.nameDialog == null && nav.nowPlaying) { nav.nowPlaying = false }
    BackHandler(enabled = nav.sheet == null && nav.nameDialog == null && !nav.nowPlaying && nav.stack.size > 1) { nav.pop() }

    LaunchedEffect(nav.top) { WheelFocus.last = null }
    LaunchedEffect(ui.nowPlayingRequest) { if (ui.nowPlayingRequest > 0 && app.player.hasQueue) nav.nowPlaying = true }
    val libRev = rememberLibRev(app.library)

    androidx.compose.runtime.CompositionLocalProvider(LocalLibRev provides libRev) {
    var backDrag by remember { mutableFloatStateOf(0f) }
    val sb = app.prefs.swipeBack
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
            if (snap.track != null && !nav.nowPlaying) MiniPlayer(snap, nav)
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
    }
}

// ---- shared pieces ------------------------------------------------------------------------------------------------

/** iPod-OS style title bar: glossy Back pill on the left, centred bold title, actions on the right. */
@Composable
fun TopBar(title: String, nav: PlayerNav?, showBack: Boolean, actions: @Composable () -> Unit = {}) {
    val sc = LocalScheme.current
    Box(
        Modifier.fillMaxWidth().statusBarsPadding().height(60.dp)
            .background(Brush.verticalGradient(listOf(Color(0x33FFFFFF), Color(0x0FFFFFFF)))),
    ) {
        if (showBack && nav != null) GlossPill("Back", { nav.pop() }, Modifier.align(Alignment.CenterStart).padding(start = 10.dp), icon = Glyph.BACK, height = 38.dp)
        Txt(title, Modifier.align(Alignment.Center).padding(horizontal = 110.dp), size = 19f, weight = FontWeight.Bold, align = TextAlign.Center)
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
    val right = remember(t, fav, rCode) { swipeAction(rCode, app, t, fav, ctx) }
    val left = remember(t, fav, lCode) { swipeAction(lCode, app, t, fav, ctx) }
    SwipeRow(right = right, left = left, modifier = modifier) {
    IpodRow(onClick = onPlay, onLong = { openTrackSheet(app, nav, t, sheetExtra) }, height = 66.dp,
        leading = {
            if (showArt) Box(Modifier.size(50.dp)) {
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
                if (t.durationMs > 0) Txt(fmtTime(t.durationMs), size = 13f, color = sc.onBgDim)
                Box(Modifier.size(38.dp).clip(RoundedCornerShape(50)).clickable { openTrackSheet(app, nav, t, sheetExtra) }, contentAlignment = Alignment.Center) {
                    GlyphIcon(Glyph.MORE, Modifier.size(22.dp), sc.onBgDim)
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
    LocalLibRev.current
    val app = LocalApp.current
    val sc = LocalScheme.current
    val lib = app.library
    app.userData.rev
    Column(Modifier.fillMaxSize()) {
        TopBar("iPod", nav, showBack = false) {
            GlossButton({ nav.push(Screen.Search) }, size = 38.dp) { GlyphIcon(Glyph.SEARCH, Modifier.size(20.dp), Color.White) }
            GlossButton({ nav.push(Screen.Settings) }, size = 38.dp) { GlyphIcon(Glyph.GEAR, Modifier.size(20.dp), Color.White) }
        }
        LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 24.dp)) {
            item {
                Row(Modifier.horizontalScroll(rememberScrollState()).padding(horizontal = 16.dp, vertical = 14.dp), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    GlossPill("Shuffle All", { val s = lib.songs(); if (s.isNotEmpty()) { app.player.shuffleAll(s); nav.nowPlaying = true } }, icon = Glyph.SHUFFLE, primary = true)
                    GlossPill("Favorites", { nav.push(Screen.Detail(DetailKind.FAVORITES)) }, icon = Glyph.HEART)
                    GlossPill("Recent", { nav.push(Screen.Detail(DetailKind.RECENT)) }, icon = Glyph.CLOCK)
                }
            }
            item { SectionHeader("Library") }
            item {
                Column(Modifier.padding(horizontal = 16.dp).clip(RoundedCornerShape(16.dp)).background(sc.card).border(1.dp, sc.cardBorder, RoundedCornerShape(16.dp))) {
                    MenuRow("Playlists", Glyph.LIST, "${lib.playlists().size + app.userData.playlists.size}") { nav.push(Screen.Lib(LibKind.PLAYLISTS)) }
                    MenuRow("Artists", Glyph.ARTIST, "${lib.artists().size}") { nav.push(Screen.Lib(LibKind.ARTISTS)) }
                    MenuRow("Albums", Glyph.ALBUM, "${lib.albums().size}") { nav.push(Screen.Lib(LibKind.ALBUMS)) }
                    MenuRow("Songs", Glyph.NOTE, "${lib.songs().size}") { nav.push(Screen.Lib(LibKind.SONGS)) }
                    MenuRow("Genres", Glyph.STAR, "${lib.genres().size}") { nav.push(Screen.Lib(LibKind.GENRES)) }
                    MenuRow("Voice Memos", Glyph.MIC, "${lib.memos.size}") { nav.push(Screen.Lib(LibKind.MEMOS)) }
                }
            }
            val recentAlbums = lib.recentAlbums(20)
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
    IpodRow(onClick, height = 62.dp, leading = { IconTile(g, size = 38.dp) },
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
            GlossButton({ nav.push(Screen.Search) }, size = 38.dp) { GlyphIcon(Glyph.SEARCH, Modifier.size(20.dp), Color.White) }
        }
        when (kind) {
            LibKind.SONGS -> { val songs = lib.songs(); SongList(songs, nav, snap, showArt = true, sections = true, header = { ShuffleHeader(songs, nav) }) }
            LibKind.MEMOS -> { val memos = lib.memos; SongList(memos, nav, snap, showArt = false) }
            LibKind.ALBUMS -> AlbumGrid(lib.albums(), nav)
            LibKind.ARTISTS -> GroupList(lib.artists(), nav, circle = true) { Screen.Detail(DetailKind.ARTIST, it.name) }
            LibKind.GENRES -> GroupList(lib.genres(), nav, circle = false) { Screen.Detail(DetailKind.GENRE, it.name) }
            LibKind.PLAYLISTS -> PlaylistsList(nav)
        }
    }
}

@Composable
private fun ShuffleHeader(tracks: List<Track>, nav: PlayerNav) {
    val app = LocalApp.current
    Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 12.dp), horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
        GlossPill("Play", { app.player.play(tracks, 0, null); nav.nowPlaying = true }, icon = Glyph.PLAY, primary = true)
        GlossPill("Shuffle", { app.player.play(tracks, tracks.indices.random(), true); nav.nowPlaying = true }, icon = Glyph.SHUFFLE)
        Txt(songCount(tracks.size), Modifier.weight(1f), size = 13f, color = LocalScheme.current.onBgDim, align = TextAlign.End)
    }
}

@Composable
@OptIn(androidx.compose.foundation.ExperimentalFoundationApi::class)
fun SongList(
    tracks: List<Track>, nav: PlayerNav, snap: PlayerSnap, showArt: Boolean, numbered: Boolean = false,
    sheetExtra: List<SheetItem> = emptyList(), header: (@Composable () -> Unit)? = null, sections: Boolean = false,
) {
    val app = LocalApp.current
    if (tracks.isEmpty()) { EmptyState("Nothing here yet"); return }
    val sc = LocalScheme.current
    val row: @Composable (Int, Track) -> Unit = { i, t ->
        TrackRow(t, nav, snap, showArt = showArt, index = if (numbered) i + 1 else null, sheetExtra = sheetExtra, onPlay = {
            app.player.play(tracks, i, null); nav.nowPlaying = true
        })
    }
    val groups = remember(tracks, sections) { if (sections) tracks.withIndex().groupBy { letterOf(sortKey(it.value.title)) } else emptyMap() }
    LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 24.dp)) {
        if (header != null) item { header() }
        if (sections) {
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

@Composable
private fun GroupList(groups: List<Group>, nav: PlayerNav, circle: Boolean, target: (Group) -> Screen) {
    val sc = LocalScheme.current
    if (groups.isEmpty()) { EmptyState("Nothing here yet"); return }
    LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 24.dp)) {
        items(groups, key = { it.name }) { g ->
            IpodRow({ nav.push(target(g)) }, height = 68.dp,
                leading = { ArtImage(g.artKey, Modifier.size(52.dp), thumb = true, corner = 10.dp, circle = circle) },
                trailing = { CountChevron(g.tracks.size) }) { hi ->
                Txt(g.name, size = 17f, weight = FontWeight.Medium, color = if (hi) Color.White else sc.onBg)
            }
        }
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
            IpodRow({ nav.nameDialog = { ud.createPlaylist(it) } }, height = 60.dp, leading = { IconTile(Glyph.PLUS, size = 44.dp) }) { hi ->
                Txt("New Playlist...", size = 16f, weight = FontWeight.Medium, color = if (hi) Color.White else sc.accent)
            }
        }
        item {
            val favs = ud.favorites.size
            IpodRow({ nav.push(Screen.Detail(DetailKind.FAVORITES)) }, height = 68.dp, leading = { IconTile(Glyph.HEART_FILLED, size = 52.dp, tint = Color(0xFFE5486B)) },
                trailing = { CountChevron(favs) }) { hi ->
                Txt("Favorites", size = 17f, weight = FontWeight.Medium, color = if (hi) Color.White else sc.onBg)
            }
        }
        items(ud.playlists.toList(), key = { it.id }) { p ->
            val by = lib.byPath()
            val first = p.paths.firstNotNullOfOrNull { by[it]?.artKey }
            IpodRow({ nav.push(Screen.Detail(DetailKind.USER, p.id)) },
                onLong = { nav.sheet = SheetSpec(p.name, songCount(p.paths.size), listOf(SheetItem("Delete playlist", Glyph.CLOSE) { ud.deletePlaylist(p.id) })) },
                height = 68.dp, leading = { ArtImage(first, Modifier.size(52.dp), thumb = true, corner = 10.dp) },
                trailing = { CountChevron(p.paths.size) }) { hi ->
                Txt(p.name, size = 17f, weight = FontWeight.Medium, color = if (hi) Color.White else sc.onBg)
            }
        }
        items(lib.playlists(), key = { "f" + it.name }) { g ->
            IpodRow({ nav.push(Screen.Detail(DetailKind.FOLDER, g.name)) }, height = 68.dp,
                leading = { ArtImage(g.artKey, Modifier.size(52.dp), thumb = true, corner = 10.dp) },
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
    val by = lib.byPath()
    var title = ""; var subtitle = ""; var art: String? = null
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

private class Results(val songs: List<Track>, val albums: List<Group>, val artists: List<Group>)

@Composable
private fun SearchScreen(nav: PlayerNav, snap: PlayerSnap) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    var query by remember { mutableStateOf("") }
    var results by remember { mutableStateOf(Results(emptyList(), emptyList(), emptyList())) }
    val fr = remember { FocusRequester() }
    LaunchedEffect(Unit) { try { fr.requestFocus() } catch (_: Exception) {} }
    LaunchedEffect(query) {
        val q = query.trim()
        if (q.isEmpty()) { results = Results(emptyList(), emptyList(), emptyList()); return@LaunchedEffect }
        delay(140)
        results = withContext(Dispatchers.Default) {
            val lib = app.library
            Results(
                lib.songs().filter { it.title.contains(q, true) || it.artist.contains(q, true) || it.album.contains(q, true) }.take(150),
                lib.albums().filter { it.name.contains(q, true) || (it.tracks.firstOrNull()?.artist ?: "").contains(q, true) }.take(30),
                lib.artists().filter { it.name.contains(q, true) }.take(30),
            )
        }
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
        if (query.isBlank()) EmptyState("Search your music")
        else if (r.songs.isEmpty() && r.albums.isEmpty() && r.artists.isEmpty()) EmptyState("No results")
        else LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 24.dp)) {
            if (r.artists.isNotEmpty()) {
                item { SectionHeader("Artists") }
                items(r.artists.take(5), key = { "a" + it.name }) { g ->
                    IpodRow({ nav.push(Screen.Detail(DetailKind.ARTIST, g.name)) }, height = 60.dp, leading = { ArtImage(g.artKey, Modifier.size(44.dp), thumb = true, circle = true) },
                        trailing = { GlyphIcon(Glyph.CHEVRON, Modifier.size(16.dp), sc.onBgDim) }) { hi -> Txt(g.name, size = 16f, color = if (hi) Color.White else sc.onBg) }
                }
            }
            if (r.albums.isNotEmpty()) {
                item { SectionHeader("Albums") }
                items(r.albums.take(6), key = { "b" + it.tracks.first().albumKey }) { g ->
                    IpodRow({ nav.push(Screen.Detail(DetailKind.ALBUM, g.tracks.first().albumKey)) }, height = 64.dp, leading = { ArtImage(g.artKey, Modifier.size(48.dp), thumb = true, corner = 8.dp) },
                        trailing = { GlyphIcon(Glyph.CHEVRON, Modifier.size(16.dp), sc.onBgDim) }) { hi ->
                        Column { Txt(g.name, size = 16f, color = if (hi) Color.White else sc.onBg); Txt(g.tracks.firstOrNull()?.artist ?: "", size = 13f, color = if (hi) Color(0xDDFFFFFF) else sc.onBgDim) }
                    }
                }
            }
            if (r.songs.isNotEmpty()) {
                item { SectionHeader("Songs") }
                itemsIndexed(r.songs, key = { i, t -> "s$i${t.path}" }) { i, t -> TrackRow(t, nav, snap, onPlay = { app.player.play(r.songs, i, null); nav.nowPlaying = true }) }
            }
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
        TopBar("Up Next", nav, showBack = true) { if (q.isNotEmpty()) GlossPill("Clear", { app.player.clearQueue(); nav.pop() }, height = 38.dp) }
        if (q.isEmpty()) { EmptyState("The queue is empty"); return@Column }
        LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 24.dp)) {
            itemsIndexed(q, key = { i, t -> "$i${t.path}" }) { i, t ->
                val cur = i == snap.index
                val remove = remember(i, t) { SwipeAction("Remove", Glyph.CLOSE, { Color(0xFFD9423F) }) { app.player.removeFromQueue(i) } }
                SwipeRow(right = remove, left = remove) {
                IpodRow({ app.player.skipTo(i) }, height = 64.dp,
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
        Txt("$count", size = 14f, color = sc.onBgDim)
        GlyphIcon(Glyph.CHEVRON, Modifier.size(18.dp), sc.onBgDim)
    }
}
