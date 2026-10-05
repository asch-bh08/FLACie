package com.ipodemu.player

import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
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
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.windowInsetsTopHeight
import androidx.compose.foundation.layout.ime
import androidx.compose.foundation.layout.statusBars
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
import com.ipodemu.library.hiRes
import com.ipodemu.library.AudioFacts
import com.ipodemu.library.matchKey
import com.ipodemu.library.sortKey
import com.ipodemu.playback.PlayerController
import com.ipodemu.theme.Themes
import kotlinx.coroutines.Dispatchers
import com.ipodemu.library.WebCatalog
import com.ipodemu.library.DownloadStage
import com.ipodemu.library.DownloadStatus
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.withContext

// ---- navigation state ---------------------------------------------------------------------------------------------

enum class LibKind(val title: String) {
    SONGS("Songs"), ALBUMS("Albums"), ARTISTS("Artists"), PLAYLISTS("Playlists"), GENRES("Genres"), MEMOS("Voice Memos"),
    JELLYFIN("Jellyfin"), PLEX("Plex"), NAS("NAS only"),
}

enum class DetailKind { ALBUM, ARTIST, FOLDER, USER, GENRE, FAVORITES, RECENT, MIX, DOWNLOADS }

sealed interface Screen {
    data object Home : Screen
    data class Lib(val kind: LibKind) : Screen
    data class Detail(val kind: DetailKind, val id: String = "") : Screen
    data object Search : Screen
    data object Queue : Screen
    data object Settings : Screen
    data object Dashboard : Screen
    data object Music : Screen
    /** Modern theme's Library tab. */
    data object Library : Screen
    /** Favorites, recents and every source (Jellyfin, Plex, NAS, voice memos): what the Library tab used to list. */
    data object Sources : Screen
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
    /** The queue was opened from the full-screen player: going back from it returns there, not to the screen under it. */
    var queueFromPlayer = false
    fun openQueueFromPlayer() { nowPlaying = false; queueFromPlayer = true; push(Screen.Queue) }
    fun pop(): Boolean {
        if (stack.size <= 1) return false
        val gone = stack.removeAt(stack.lastIndex)
        if (gone == Screen.Queue && queueFromPlayer) { queueFromPlayer = false; nowPlaying = true }
        return true
    }
    fun home() { queueFromPlayer = false; while (stack.size > 1) stack.removeAt(stack.lastIndex) }
}

/** What the player is doing, as one immutable snapshot for composition. */
@androidx.compose.runtime.Immutable
data class PlayerSnap(val track: Track?, val playing: Boolean, val shuffle: Boolean, val repeat: Int, val index: Int, val count: Int)

@Composable
fun rememberSnap(player: PlayerController, prefs: Prefs): PlayerSnap {
    var tick by remember { mutableIntStateOf(0) }
    DisposableEffect(player) { val d = player.observe { tick++ }; onDispose { d() } }
    @Suppress("UNUSED_EXPRESSION") tick
    return PlayerSnap(player.current, player.wantsToPlay, prefs.shuffle, prefs.repeat, player.queueIndex, player.queue.size)
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
    // the other devices' state, so the remote bar knows when something starts or stops elsewhere (quicker while one is showing)
    LaunchedEffect(app.connect.available) {
        while (app.connect.available) { if (app.connect.connected) app.connect.loadSessions(); delay(if (app.connect.remoteNow() != null) 3_000 else 8_000) }
    }
    LaunchedEffect(ui.nowPlayingRequest) { if (ui.nowPlayingRequest > 0 && app.player.hasQueue) nav.nowPlaying = true }
    val libRev = rememberLibRev(app.library)

    androidx.compose.runtime.CompositionLocalProvider(LocalLibRev provides libRev) {
    var backDrag by remember { mutableFloatStateOf(0f) }
    val sb = if (LocalHardware.current) 2 else app.prefs.swipeBack   // device view: MENU on the wheel is the only back
    // not on Now Playing: its seek bar starts inside the edge zone on phones, so dragging the knob closed the player
    val backEnabled = sb != 2 && nav.sheet == null && nav.nameDialog == null && !nav.nowPlaying && nav.stack.size > 1
    val edgePx = with(androidx.compose.ui.platform.LocalDensity.current) { 22.dp.toPx() }
    // Now Playing: edge only, or dragging the seek bar / swiping the cover counted as "back"
    val swipeZone = if (sb == 1 || nav.nowPlaying || app.prefs.swipeRowRight != 0) edgePx else 1e9f // iPhone-style: swipe right anywhere goes back (Now Playing keeps the edge, its cover swipes skip tracks)
    Box(
        Modifier.fillMaxSize().edgeSwipeBack(backEnabled, swipeZone, { backDrag = it }) { if (nav.nowPlaying) nav.nowPlaying = false else nav.pop() },
    ) {
        val modern = LocalStyle.current.modern
        // the iPod theme has no tabs: its stack must always start at Home (a Modern tab root like Settings would make Back exit)
        if (!modern && nav.stack.first() != Screen.Home) androidx.compose.runtime.SideEffect { if (nav.stack.first() != Screen.Home) nav.stack.add(0, Screen.Home) }
        // and the reverse on switching to Modern: a tab screen pushed on top of Home becomes its own tab root, so the nav highlights it
        if (modern && nav.stack.size > 1 && Tab.entries.any { it.root == nav.top }) androidx.compose.runtime.SideEffect { Tab.entries.firstOrNull { it.root == nav.top }?.let { nav.selectTab(it) } }
        BackHandler(enabled = modern && !ui.pickerOpen && nav.sheet == null && nav.nameDialog == null && !nav.nowPlaying && nav.stack.size == 1 && nav.top != Screen.Home) { nav.selectTab(Tab.HOME) }
        BoxWithConstraints(Modifier.fillMaxSize()) {
        // Modern: a navigation rail when there is width to spare (unfolded Fold, tablet, landscape, the square RG Rotate),
        // a bottom bar on portrait phones and the Fold cover screen
        val rail = modern && (maxWidth >= 600.dp || maxWidth >= maxHeight)
        Row(Modifier.fillMaxSize()) {
        if (rail) NavRail(nav)
        Column(Modifier.weight(1f).fillMaxHeight().graphicsLayer { translationX = if (nav.nowPlaying) 0f else backDrag }) {
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
            // music playing on another of this user's devices while this phone is quiet: a remote bar instead (Spotify Connect style)
            // only WHICH device (not its moving position) is read here, so the poll every few seconds does not recompose the whole screen
            val remoteId by remember { androidx.compose.runtime.derivedStateOf { app.connect.remoteNow()?.id } }
            if (remoteId != null && !snap.playing && !nav.nowPlaying && modern && !LocalHardware.current) {
                AnimatedVisibility(remember { androidx.compose.animation.core.MutableTransitionState(false).apply { targetState = true } }, enter = slideInVertically(androidx.compose.animation.core.spring(0.8f, 380f)) { it } + androidx.compose.animation.fadeIn(tween(160))) { RemoteMiniPlayer(remoteId!!) }
            } else if (snap.track != null && !nav.nowPlaying && (modern || nav.top != Screen.Home) && !LocalHardware.current) {
                // the bar slides up from the bottom the first time a song starts (and when coming back from the full player)
                AnimatedVisibility(remember { androidx.compose.animation.core.MutableTransitionState(false).apply { targetState = true } }, enter = slideInVertically(androidx.compose.animation.core.spring(0.8f, 380f)) { it } + androidx.compose.animation.fadeIn(tween(160))) { MiniPlayer(snap, nav) }
            }
            if (modern && !rail) BottomNav(nav)
        }
        }
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
        Screen.Dashboard -> SettingsScreen(nav, dashboard = true)
        Screen.Music -> MusicMenu(nav)
        Screen.Library -> ExploreScreen(nav, snap)
        Screen.Sources -> LibraryHome(nav)
    }
}

// ---- shared pieces ------------------------------------------------------------------------------------------------

/** iPod-OS style title bar: glossy Back pill on the left, centred bold title, actions on the right. */
@Composable
fun TopBar(title: String, nav: PlayerNav?, showBack: Boolean, actions: @Composable () -> Unit = {}) {
    if (LocalStyle.current.modern) { ModernTopBar(title, if (showBack && nav != null && nav.stack.size > 1) ({ nav.pop() }) else null, actions); return }
    val sc = LocalScheme.current
    Box(
        Modifier.fillMaxWidth().height(52.dp)
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
    showArt: Boolean = true, index: Int? = null, sheetExtra: List<SheetItem> = emptyList(), sheetExtraFor: ((Track) -> List<SheetItem>)? = null,
) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val fav by app.userData.favState(t.path)   // per-track state: a favourite toggle recomposes only this row
    val hiRes = remember(t, AudioFacts.version) { t.hiRes }   // not worked out again on every recomposition while scrolling
    val isCurrent = snap.track?.path == t.path
    val ctx = androidx.compose.ui.platform.LocalContext.current
    val rCode = app.prefs.swipeRowRight; val lCode = app.prefs.swipeRowLeft
    // built once per (track, favourite, settings): stable objects keep the row's pointer handlers from restarting
    val hw = LocalHardware.current   // the faithful device view has no swipe shortcuts
    val right = remember(t, fav, rCode, hw) { if (hw) null else swipeAction(rCode, app, t, fav, ctx) }
    val left = remember(t, fav, lCode, hw) { if (hw) null else swipeAction(lCode, app, t, fav, ctx) }
    SwipeRow(right = right, left = left, modifier = modifier) {
    val focus = androidx.compose.ui.platform.LocalFocusManager.current
    // tapping the song that is already playing opens the player (or resumes it) instead of starting it over; any tap puts the keyboard away
    val keyboard = androidx.compose.ui.platform.LocalSoftwareKeyboardController.current
    val tap = { focus.clearFocus(); keyboard?.hide(); if (isCurrent) { if (!snap.playing) app.player.toggle(); nav.nowPlaying = true } else onPlay() }
    IpodRow(onClick = tap, onLong = { openTrackSheet(app, nav, t, sheetExtra + (sheetExtraFor?.invoke(t) ?: emptyList())) }, height = 56.dp,
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
                if (hiRes) HiResBadge()
                SourceBadge(t.source)
                if (t.durationMs > 0) Txt(fmtTime(t.durationMs), size = 13f, color = rowDim())
                Box(Modifier.size(44.dp).clip(RoundedCornerShape(50)).semantics { contentDescription = "More options for ${t.title}" }.clickable { openTrackSheet(app, nav, t, sheetExtra + (sheetExtraFor?.invoke(t) ?: emptyList())) }, contentAlignment = Alignment.Center) {
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
    if (!Tweaks.badges) return
    val color = sourceColor(source)
    Box(Modifier.clip(RoundedCornerShape(50)).background(color.copy(alpha = 0.16f)).padding(horizontal = 7.dp, vertical = 3.dp)) {
        Txt(if (source == com.ipodemu.library.TrackSource.NAS) "NAS" else if (source == com.ipodemu.library.TrackSource.CLOUD) "Streaming" else source.name.lowercase().replaceFirstChar { it.uppercase() }, size = 11f, weight = FontWeight.SemiBold, color = color)
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
    // administrators only, kept quiet at the foot of the menu: a Hi-Res copy of a song already in the library (saved beside it; nothing is downloaded instead if none exists)
    if (app.ui.isAdmin && t.isMusic && app.prefs.slskdUrl.isNotBlank() && app.prefs.slskdApiKey.isNotBlank() && app.prefs.fileMoverUrl.isNotBlank() && !t.path.contains("[Hi-Res]", ignoreCase = true)) {
        items += SheetItem("✦ Hi-Res version…", Glyph.DOWN) {
            nav.sheet = SheetSpec("Get a Hi-Res copy?", "Hi-Res files are 2 to 5 times bigger (roughly 40 to 200 MB). It is saved next to this one. If no Hi-Res copy exists, nothing is changed.", listOf(
                SheetItem("Yes, download Hi-Res", Glyph.DOWN) { app.library.requestDownload(t.artist, t.title, t.album, t.durationMs, hiRes = true, fallbackToNormal = false) },
                SheetItem("Cancel", Glyph.CLOSE) { },
            ))
        }
    }
    nav.sheet = SheetSpec(t.title, t.artist.ifEmpty { null }, items)
}

internal fun playlistPicker(app: App, nav: PlayerNav, t: Track): SheetSpec {
    val ud = app.userData
    val items = ArrayList<SheetItem>()
    items += SheetItem("New playlist...", Glyph.PLUS) { nav.nameDialog = { name -> val p = ud.createPlaylist(name, t.path); app.ui.say("Added \"${t.title}\" to $name", "Undo") { ud.removeFromPlaylist(p.id, t.path) } } }
    shownPlaylists(app).forEach { p ->
        items += SheetItem(p.name, Glyph.LIST) {
            if (p.paths.any { it == t.path || app.library.resolve(it, ud.meta[it])?.let { r -> r.matchKey == t.matchKey } == true }) app.ui.say("\"${t.title}\" is already in ${p.name}", warn = true)
            else { ud.addToPlaylist(p.id, t.path); app.ui.say("Added \"${t.title}\" to ${p.name}", "Undo") { ud.removeFromPlaylist(p.id, t.path); app.ui.say("Removed \"${t.title}\" from ${p.name}") } }
        }
    }
    return SheetSpec("Add to playlist", t.title, items)
}

@Composable
private fun NameDialog(onDone: (String) -> Unit, onDismiss: () -> Unit) {
    val sc = LocalScheme.current
    var text by remember { mutableStateOf("") }
    val fr = remember { FocusRequester() }
    LaunchedEffect(Unit) { try { fr.requestFocus() } catch (_: Exception) {} }
    Box(Modifier.fillMaxSize().background(Color(0xAA000000)).imePadding().pointerInput(Unit) { detectTapGestures { onDismiss() } }, contentAlignment = Alignment.Center) {
        Column(
            Modifier.padding(24.dp).fillMaxWidth().clip(RoundedCornerShape(20.dp)).background(sc.top.mix(Color.Black, .4f))
                .pointerInput(Unit) { detectTapGestures { } }.padding(20.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            Txt("New playlist", size = 20f, weight = FontWeight.Bold)
            BasicTextField(
                text, { text = it }, singleLine = true, cursorBrush = SolidColor(sc.accent),
                textStyle = TextStyle(color = sc.onBg, fontSize = 18.sp),
                keyboardOptions = androidx.compose.foundation.text.KeyboardOptions(capitalization = androidx.compose.ui.text.input.KeyboardCapitalization.Words, imeAction = androidx.compose.ui.text.input.ImeAction.Done),
                keyboardActions = androidx.compose.foundation.text.KeyboardActions(onDone = { if (text.isNotBlank()) { onDone(text.trim()); onDismiss() } }),
                modifier = Modifier.fillMaxWidth().focusRequester(fr).clip(RoundedCornerShape(12.dp)).background(Color(0x22FFFFFF)).padding(14.dp),
            )
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.align(Alignment.End)) {
                GlossPill("Cancel", onDismiss)
                GlossPill("Create", { if (text.isNotBlank()) { onDone(text.trim()); onDismiss() } }, primary = true)
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
    val hour = androidx.compose.runtime.produceState(System.currentTimeMillis() / 3_600_000L) { while (true) { delay(60_000); value = System.currentTimeMillis() / 3_600_000L } }.value
    val recentsKey = app.userData.recents.firstOrNull()
    // the shelves are worked out in the background (they shuffle and sort the whole library several times: that on the main thread was the stutter
    // when scrolling Home and when coming back to it after playing something) and kept, so the last ones show at once
    var home by remember { androidx.compose.runtime.mutableStateOf(HomeCache.last) }
    LaunchedEffect(libRev, hour, recentsKey) {
        val d = kotlinx.coroutines.withContext(Dispatchers.Default) {
            try { HomeData(com.ipodemu.library.HomeShelves.build(lib, app.userData), com.ipodemu.library.HomeShelves.moodsAvailable(lib)) } catch (_: Exception) { null }
        }
        if (d != null) { HomeCache.last = d; home = d }
    }
    val shelves = home?.shelves ?: emptyList()
    val moodList = home?.moods ?: emptyList()
    var mood by remember { androidx.compose.runtime.mutableStateOf<String?>(null) }
    app.userData.rev
    app.ui.rev
    val clock by androidx.compose.runtime.produceState("") { while (true) { value = java.text.SimpleDateFormat("h:mm a", java.util.Locale.getDefault()).format(java.util.Date()); delay(15000) } }
    Column(Modifier.fillMaxSize()) {
        val modern = LocalStyle.current.modern
        if (modern) ModernTopBar(greeting(), null) { AccountAction() }
        else TopBar(if (app.prefs.timeInTitle) clock else "iPod", nav, showBack = false) {
            TopAction(Glyph.SEARCH, "Search") { nav.push(Screen.Search) }
            TopAction(Glyph.GEAR, "Settings") { nav.push(Screen.Settings) }
        }
        LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 24.dp)) {
            if (!modern) item { NowPlayingCard(snap, nav) { val s = lib.songs(); if (s.isNotEmpty()) { app.player.shuffleAll(s) } } }
            item {
                Row(Modifier.horizontalScroll(rememberScrollState()).padding(horizontal = 12.dp, vertical = 4.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    GlossPill("Shuffle All", { val s = lib.songs(); if (s.isNotEmpty()) { app.player.shuffleAll(s) } }, icon = Glyph.SHUFFLE, height = 32.dp)
                    GlossPill("Favorites", { nav.push(Screen.Detail(DetailKind.FAVORITES)) }, icon = Glyph.HEART, height = 32.dp)
                    GlossPill("Recent", { nav.push(Screen.Detail(DetailKind.RECENT)) }, icon = Glyph.CLOCK, height = 32.dp)
                    GlossPill("Downloads", { nav.push(Screen.Detail(DetailKind.DOWNLOADS)) }, icon = Glyph.DOWN, height = 32.dp)
                }
            }
            // YT Music style shelves (see library/Recommend.kt); the Daily Mix moves on every hour
            if (moodList.isNotEmpty()) item {
                Row(Modifier.horizontalScroll(rememberScrollState()).padding(horizontal = 12.dp, vertical = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    moodList.forEach { m -> GlossPill(m, { mood = if (mood == m) null else m }, height = 34.dp, primary = mood == m) }
                }
            }
            mood?.let { mname ->
                val ts = com.ipodemu.library.HomeShelves.moodTracks(lib, mname)
                item { SectionHeader(mname) }
                item {
                    LazyRow(contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(14.dp)) {
                        itemsIndexed(ts) { i, t -> TrackCard(t, Modifier.width(130.dp)) { app.player.play(ts, i, null) } }
                    }
                }
            }
            if (mood == null) items(shelves, key = { it.id }) { sh ->
                Column {
                    SectionHeader(sh.title)
                    if (sh.id == "quick-picks") { QuickPicks(sh.tracks) { i -> app.player.play(sh.tracks, i, null) }; return@items }
                    LazyRow(contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(14.dp)) {
                        if (sh.tracks.isNotEmpty()) itemsIndexed(sh.tracks) { i, t -> TrackCard(t, Modifier.width(130.dp)) { app.player.play(sh.tracks, i, null) } }
                        else items(sh.albums, key = { it.tracks.first().albumKey }) { g -> AlbumCard(g, Modifier.width(150.dp)) { nav.push(Screen.Detail(DetailKind.ALBUM, g.tracks.first().albumKey)) } }
                    }
                }
            }
            // playlists are one tap away here as well as in the Playlists tab
            val lists = shownPlaylists(app); val by0 = lib.byPath()
            if (lists.isNotEmpty() && mood == null) {
                item { SectionHeader("Your playlists") }
                item {
                    LazyRow(contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(14.dp)) {
                        items(lists.take(14), key = { it.id }) { pl ->
                            val ts = remember(pl.paths.size, libRev) { pl.paths.mapNotNull { by0[it] ?: lib.resolve(it, app.userData.meta[it]) } }
                            AlbumCard(com.ipodemu.library.Group("${pl.name} (${pl.paths.size})", ts, ts.firstNotNullOfOrNull { it.artKey }), Modifier.width(150.dp)) { nav.push(Screen.Detail(DetailKind.USER, pl.id)) }
                        }
                    }
                }
            }
            if (!modern) item { SectionHeader("Library") }
            if (!modern) item {
                Column(Modifier.padding(horizontal = 16.dp).clip(RoundedCornerShape(16.dp)).background(sc.card).border(1.dp, sc.cardBorder, RoundedCornerShape(16.dp))) {
                    MenuRow("Playlists", Glyph.LIST, "${shownPlaylists(app).size + shownFolderPlaylists(app).size}") { nav.push(Screen.Lib(LibKind.PLAYLISTS)) }
                    MenuRow("Artists", Glyph.ARTIST, "${lib.artists().size}") { nav.push(Screen.Lib(LibKind.ARTISTS)) }
                    MenuRow("Albums", Glyph.ALBUM, "${lib.albums().size}") { nav.push(Screen.Lib(LibKind.ALBUMS)) }
                    MenuRow("Songs", Glyph.NOTE, "${lib.songs().size}") { nav.push(Screen.Lib(LibKind.SONGS)) }
                    MenuRow("Genres", Glyph.STAR, "${lib.genres().size}") { nav.push(Screen.Lib(LibKind.GENRES)) }
                    MenuRow("Voice Memos", Glyph.MIC, "${lib.memos.size}") { nav.push(Screen.Lib(LibKind.MEMOS)) }
                    if (lib.jellyfinTracks.isNotEmpty()) MenuRow("Jellyfin", Glyph.JELLYFIN, "${lib.jellyfinTracks.size}") { nav.push(Screen.Lib(LibKind.JELLYFIN)) }
                    if (lib.plexTracks.isNotEmpty()) MenuRow("Plex", Glyph.PLEX, "${lib.plexTracks.size}") { nav.push(Screen.Lib(LibKind.PLEX)) }
                    lib.nasOnlyTracks().takeIf { it.isNotEmpty() }?.let { n -> MenuRow("NAS only", Glyph.NAS, "${n.size}") { nav.push(Screen.Lib(LibKind.NAS)) } }
                }
            }
        }
    }
}

@Composable
fun SectionHeader(text: String) {
    val sc = LocalScheme.current
    Txt(text.uppercase(), Modifier.padding(start = 20.dp, top = 22.dp, bottom = 8.dp), size = 12f, weight = FontWeight.Bold, color = Palette.faint)
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
        TopBar(kind.title, nav, showBack = true) { TopAction(Glyph.SEARCH, "Search") { nav.push(Screen.Search) } }
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
            LibKind.NAS -> SongList(lib.nasOnlyTracks().sortedBy { sortKey(it.title) }, nav, snap, showArt = true, sections = 1)
        }
    }
}

@Composable
private fun ShuffleHeader(tracks: List<Track>, nav: PlayerNav, sortLabel: String, onSort: () -> Unit) {
    val app = LocalApp.current
    Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 12.dp), horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
        GlossPill("Play", { app.player.play(tracks, 0, null) }, icon = Glyph.PLAY, primary = true)
        GlossPill("Shuffle", { app.player.play(tracks, tracks.indices.random(), true) }, icon = Glyph.SHUFFLE)
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
    sheetExtra: List<SheetItem> = emptyList(), sheetExtraFor: ((Track) -> List<SheetItem>)? = null, header: (@Composable () -> Unit)? = null, sections: Int = 0, missing: List<Pair<String, String>> = emptyList(),
) {
    val app = LocalApp.current
    if (tracks.isEmpty() && missing.isEmpty()) { EmptyState("Nothing here yet"); return }
    val sc = LocalScheme.current
    val row: @Composable (Int, Track) -> Unit = { i, t ->
        TrackRow(t, nav, snap, showArt = showArt, index = if (numbered) i + 1 else null, sheetExtra = sheetExtra, sheetExtraFor = sheetExtraFor, onPlay = {
            app.player.play(tracks, i, null)
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
        if (missing.isNotEmpty()) {
            item { SectionHeader("Not in your library") }
            item {
                Row(Modifier.padding(horizontal = 16.dp, vertical = 4.dp)) { GlossPill("Download all ${missing.size}", { missing.forEach { (t, a) -> app.library.requestDownload(a, t, "", 0) } }, icon = Glyph.DOWN, height = 34.dp) }
            }
            itemsIndexed(missing, key = { i, m -> "m$i${m.first}" }) { _, (t, a) ->
                val st = app.library.downloads[app.library.songKey(a, t)]
                IpodRow({ app.library.requestDownload(a, t, "", 0) }, height = 56.dp, trailing = { Txt(st?.message?.take(18) ?: "Download", size = 13f, color = sc.accent) }) { _ ->
                    Column { Txt(t, size = 16f, color = sc.onBgDim, maxLines = 1); Txt(a, size = 13f, color = sc.onBgDim, maxLines = 1) }
                }
            }
        }
    }
}

@Composable
internal fun AlbumGrid(albums: List<Group>, nav: PlayerNav) {
    if (albums.isEmpty()) { EmptyState("No albums yet"); return }
    LazyVerticalGrid(GridCells.Adaptive(150.dp), Modifier.fillMaxSize(), contentPadding = PaddingValues(12.dp, 12.dp, 12.dp, 24.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        items(albums, key = { it.tracks.first().albumKey }) { g -> AlbumCard(g) { nav.push(Screen.Detail(DetailKind.ALBUM, g.tracks.first().albumKey)) } }
    }
}

@OptIn(androidx.compose.foundation.ExperimentalFoundationApi::class)
@Composable
internal fun GroupList(groups: List<Group>, nav: PlayerNav, circle: Boolean, target: (Group) -> Screen) {
    val sc = LocalScheme.current
    if (groups.isEmpty()) { EmptyState("Nothing here yet"); return }
    val sections = remember(groups) { groups.groupBy { letterOf(sortKey(it.name)) } }
    LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 24.dp)) {
        sections.forEach { (l, list) ->
            stickyHeader(key = "h$l") { LetterHeader(l) }
            items(list, key = { it.name + "|" + (it.tracks.firstOrNull()?.path ?: "") }) { g ->
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
        items(shownPlaylists(app), key = { it.id }) { p ->
            val by = lib.byPath()
            val keys = p.paths.asSequence().take(20).mapNotNull { (by[it] ?: lib.resolve(it, ud.meta[it]))?.artKey }.toList()
            // a cover already on disk, else one a server can supply; a local key with no file behind it shows nothing
            val first = keys.firstOrNull { app.art.has(it) } ?: keys.firstOrNull { it.startsWith("jf") || it.startsWith("px") || it.startsWith("it") }
            IpodRow({ nav.push(Screen.Detail(DetailKind.USER, p.id)) },
                onLong = { nav.sheet = SheetSpec(p.name, songCount(p.paths.size), listOfNotNull(
                    // share the Jellyfin copy with someone else on the same server (it then shows in their Jellyfin and FLACie)
                    p.jfId?.takeIf { app.prefs.signedIn }?.let { jf -> SheetItem("Share with...", Glyph.ARTIST) {
                        val toast = { m: String -> android.widget.Toast.makeText(app, m, android.widget.Toast.LENGTH_LONG).show() }
                        app.account.otherUsers { users, err ->
                            if (err != null || users.isEmpty()) toast(err ?: "No other users on this server")
                            else nav.sheet = SheetSpec("Share \"${p.name}\"", "They can play it; only you can change it", users.map { (uid, name) ->
                                SheetItem(name, Glyph.ARTIST) { app.account.sharePlaylist(jf, uid, false) { m -> toast(if (m == "Shared") "Shared \"${p.name}\" with $name" else m) } }
                            })
                        }
                    } },
                    SheetItem("Delete playlist", Glyph.CLOSE) {
                    nav.sheet = SheetSpec("Delete \"${p.name}\"?", if (p.jfId != null) "It is also deleted from your Jellyfin account" else null, listOf(
                        SheetItem("Delete", Glyph.CLOSE) { ud.deletePlaylist(p.id) }, SheetItem("Cancel", Glyph.BACK) {}))
                })) },
                height = 56.dp, leading = { ArtImage(first, Modifier.size(44.dp), thumb = true, corner = 10.dp) },
                trailing = { CountChevron(p.paths.size) }) { hi ->
                Txt(p.name, size = 17f, weight = FontWeight.Medium, color = if (hi) Color.White else sc.onBg)
            }
        }
        items(shownFolderPlaylists(app), key = { "f" + it.name + "|" + (it.tracks.firstOrNull()?.path ?: "") }) { g ->
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
        DetailKind.USER -> ud.playlists.firstOrNull { it.id == d.id }?.let { title = it.name; tracks = it.paths.mapNotNull { p -> lib.resolve(p, ud.meta[p]) }; art = tracks.firstNotNullOfOrNull { t -> t.artKey }; userId = it.id }
        DetailKind.FAVORITES -> { title = "Favorites"; tracks = ud.favorites.mapNotNull { lib.resolve(it, ud.meta[it]) }.distinctBy { it.path }; art = tracks.firstNotNullOfOrNull { it.artKey } }
        DetailKind.RECENT -> { title = "Recently Played"; tracks = ud.recents.mapNotNull { by[it] ?: lib.resolve(it, ud.meta[it]) }; art = tracks.firstNotNullOfOrNull { it.artKey } }
        DetailKind.DOWNLOADS -> { title = "Recently Downloaded"; tracks = recentDownloads(app); art = tracks.firstNotNullOfOrNull { it.artKey } }
        DetailKind.MIX -> mix?.let { title = it.title; subtitle = it.subtitle; tracks = it.tracks; art = it.artKey }
    }
    BackdropArt(nav, art)
    val total = tracks.sumOf { it.durationMs }
    val info = songCount(tracks.size) + if (total > 0) "  -  " + (total / 60000).toString() + " min" else ""
    val extra = if (userId != null) listOf(SheetItem("Remove from playlist", Glyph.CLOSE) { }) else emptyList()
    val missing = if (userId != null && app.library.loaded) ud.playlists.firstOrNull { it.id == userId }?.paths.orEmpty().filter { lib.resolve(it, ud.meta[it]) == null }.mapNotNull { ud.meta[it] }.filter { it.first.isNotBlank() } else emptyList()
    Column(Modifier.fillMaxSize()) {
        TopBar(title.ifEmpty { "Playlist" }, nav, showBack = true)
        if (tracks.isEmpty() && missing.isEmpty()) { EmptyState(if (d.kind == DetailKind.FAVORITES) "Tap the heart on a song to add it here" else "Nothing here yet"); return@Column }
        BoxWithConstraints(Modifier.fillMaxSize()) {
            val wide = maxWidth > 620.dp && maxWidth > maxHeight * 1.25f
            if (wide) {
                Row(Modifier.fillMaxSize()) {
                    Column(Modifier.width(300.dp).padding(20.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
                        DetailHeader(art, title, subtitle.ifEmpty { info }, if (subtitle.isEmpty()) "" else info, tracks, nav, userId)
                    }
                    SongList(tracks, nav, snap, showArt = false, numbered = true, sheetExtraFor = userId?.let { id -> { t: Track -> playlistSongItems(app, id, t) } }, missing = missing)
                }
            } else {
                SongList(tracks, nav, snap, showArt = false, numbered = true, sheetExtraFor = userId?.let { id -> { t: Track -> playlistSongItems(app, id, t) } }, missing = missing, header = {
                    Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
                        DetailHeader(art, title, subtitle.ifEmpty { info }, if (subtitle.isEmpty()) "" else info, tracks, nav, userId)
                    }
                })
            }
        }
    }
}


@Composable
private fun DetailHeader(art: String?, title: String, line1: String, line2: String, tracks: List<Track>, nav: PlayerNav, playlistId: String? = null) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    Row(horizontalArrangement = Arrangement.spacedBy(14.dp), verticalAlignment = Alignment.CenterVertically) {
        ArtImage(art, Modifier.size(104.dp), corner = 12.dp)
        Column(verticalArrangement = Arrangement.spacedBy(3.dp)) {
            Txt(title, size = 20f, weight = FontWeight.Bold, maxLines = 2)
            if (line1.isNotEmpty()) Txt(line1, size = 13f, color = sc.onBgDim)
            if (line2.isNotEmpty()) Txt(line2, size = 12f, color = sc.onBgDim)
        }
    }
    // below the art row rather than beside the title, so both buttons fit in the narrow side column of wide layouts
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        GlossPill("Play", { app.player.play(tracks, 0, null) }, icon = Glyph.PLAY, primary = true, height = 40.dp)
        GlossPill("Shuffle", { app.player.play(tracks, tracks.indices.random(), true) }, icon = Glyph.SHUFFLE, height = 40.dp)
    }
    if (playlistId != null) {
        // editing: the playlist's own actions (songs have Move and Remove in their menu)
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            GlossPill("Rename", { nav.nameDialog = { n -> app.userData.rename(playlistId, n); app.ui.say("Renamed to \"$n\"") } }, height = 36.dp)
            GlossPill("Remove dupes", {
                val ud = app.userData
                val n = ud.removeDuplicates(playlistId) { p -> app.library.resolve(p, ud.meta[p])?.matchKey ?: p }
                app.ui.say(if (n == 0) "No duplicates in this playlist" else "Removed $n duplicate${if (n == 1) "" else "s"}", warn = n == 0)
            }, height = 36.dp)
            GlossPill("Delete", { nav.sheet = SheetSpec("Delete this playlist?", null, listOf(SheetItem("Delete", Glyph.CLOSE) { app.userData.deletePlaylist(playlistId); nav.pop() }, SheetItem("Cancel", Glyph.BACK) {})) }, height = 36.dp)
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
    BackHandler(enabled = !app.ui.pickerOpen && nav.sheet == null && nav.nameDialog == null && nav.stack.size > 1) { nav.pop() }
    val sc = LocalScheme.current
    var query by app.ui::searchQuery
    var results by remember { mutableStateOf(Results(emptyList(), emptyList())) }
    var web by remember { mutableStateOf(WebResults(emptyList(), emptyList())) }
    var webLoading by remember { mutableStateOf(false) }
    val fr = remember { FocusRequester() }
    val searchFocus = androidx.compose.ui.platform.LocalFocusManager.current
    // no auto-focus: it popped the keyboard, whose first Back press hid it instead of leaving the screen (and trapped controller focus in the field)
    // Keyed on libRev too, so a download landing updates the results without retyping.
    LaunchedEffect(query, libRev) {
        val q = query.trim()
        if (q.isEmpty()) { results = Results(emptyList(), emptyList()); return@LaunchedEffect }
        delay(140)
        results = withContext(Dispatchers.Default) {
            val lib = app.library
            // every word of the query, in any order, anywhere in title + artist + album ("intro xx" finds "The xx - Intro")
            // punctuation-blind: "scream and shout will i am" finds "Scream & Shout" by will.i.am
            // ranked by word: exact > start > inside, title > artist > album, and a typo or two only when little else matches
            val docs = com.ipodemu.library.searchDocs(lib)
            Results(
                com.ipodemu.library.SearchRank.rank(lib.songs(), { docs.song(it) }, q, 150),
                com.ipodemu.library.SearchRank.rank(lib.albums(), { docs.album(it) }, q, 30),
            )
        }
    }
    // Songs and albums that aren't owned yet, most popular first (iTunes ranks by popularity, so a hit song comes before
    // anyone else's track with the same name). Debounced longer than the local filter: these are network calls.
    LaunchedEffect(query) {
        val q = query.trim()
        // guests have no servers or downloads, so the online catalog has nothing to offer them
        if (q.isEmpty() || app.ui.guest) { web = WebResults(emptyList(), emptyList()); webLoading = false; return@LaunchedEffect }
        delay(450)
        webLoading = true
        val songs = withContext(Dispatchers.IO) { WebCatalog.songs(q) }
        web = WebResults(songs, emptyList())
        web = WebResults(songs, withContext(Dispatchers.IO) { WebCatalog.albumsWithTop(q, songs.firstOrNull()) })
        webLoading = false
    }
    Column(Modifier.fillMaxSize().imePadding()) {
        // the big title goes while typing, so more results fit above the keyboard
        val imeUp = androidx.compose.foundation.layout.WindowInsets.ime.getBottom(androidx.compose.ui.platform.LocalDensity.current) > 0
        if (!imeUp) TopBar("Search", nav, showBack = true)
        else Spacer(Modifier.windowInsetsTopHeight(androidx.compose.foundation.layout.WindowInsets.statusBars))
        Row(
            Modifier.padding(16.dp).fillMaxWidth().height(46.dp).clip(RoundedCornerShape(50)).background(Color(0x26FFFFFF)).border(1.dp, sc.cardBorder, RoundedCornerShape(50)).padding(horizontal = 16.dp),
            verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            GlyphIcon(Glyph.SEARCH, Modifier.size(20.dp), sc.onBgDim)
            Box(Modifier.weight(1f)) {
                if (query.isEmpty()) Txt("Songs, albums, artists", size = 16f, color = sc.onBgDim)
                BasicTextField(query, { query = it }, singleLine = true, cursorBrush = SolidColor(sc.accent), textStyle = TextStyle(color = sc.onBg, fontSize = 16.sp), modifier = Modifier.fillMaxWidth().focusRequester(fr),
                    keyboardOptions = androidx.compose.foundation.text.KeyboardOptions(autoCorrect = false, imeAction = androidx.compose.ui.text.input.ImeAction.Search),
                    keyboardActions = androidx.compose.foundation.text.KeyboardActions(onSearch = { searchFocus.clearFocus() }))
            }
            if (query.isNotEmpty()) IconAction(Glyph.CLOSE, "Clear search", { query = "" }, tint = sc.onBgDim, size = 40.dp, iconScale = 0.45f)
        }
        val r = results
        val w = web
        val q = query.trim()
        // owned copies of web hits: a song you have plays instead of offering a download
        // a copy per library change, so rows recompose as download progress arrives
        val dl = remember(libRev) { HashMap(app.library.downloads) }
        val ownedSongs = remember(libRev) { app.library.songs().associateBy { com.ipodemu.library.matchKey(it.title, it.artist) } }
        val ownedAlbums = remember(libRev) { app.library.albums().associateBy { ownedAlbumKey(it.tracks.firstOrNull()?.artist.orEmpty(), it.name) } }
        val nothingAtAll = r.songs.isEmpty() && r.albums.isEmpty() && w.songs.isEmpty() && w.albums.isEmpty() && !webLoading
        // Ranked simply: an exact title match beats a prefix match beats a loose contains/artist-only match.
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
        var webSongsExpanded by remember(q) { mutableStateOf(false) }
        // the row keeps its scroll offset by item key; when a new result list arrives (or a download reshuffles it) it could stay scrolled
        // a few pixels, clipping the first card, so it snaps back to the start whenever the first album changes
        val albumRowState = androidx.compose.foundation.lazy.rememberLazyListState()
        androidx.compose.runtime.LaunchedEffect(q, w.albums.firstOrNull()?.id) { albumRowState.scrollToItem(0) }
        if (query.isBlank()) EmptyState("Search your music")
        else if (nothingAtAll) {
            Column(Modifier.fillMaxSize()) {
                Box(Modifier.weight(1f)) { EmptyState("No results") }
                if (!app.ui.guest) DownloadRequestBar(app, q)
            }
        }
        else LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 24.dp)) {
            // albums as one swipeable row of covers near the top, so they're visible with the keyboard up; a query naming an
            // album ("short n sweet") puts that row first, like YT Music
            val albumNamed = w.albums.firstOrNull()?.takeIf { it.kind != "Single" }?.let { a -> a.cleanTitle.lowercase().filter { it.isLetterOrDigit() } == q.lowercase().filter { it.isLetterOrDigit() } } == true
            val webAlbums: androidx.compose.foundation.lazy.LazyListScope.() -> Unit = {
                if (w.albums.isNotEmpty()) {
                    item { SectionHeader("Albums and EPs") }
                    item(key = "walbums") {
                        androidx.compose.foundation.lazy.LazyRow(state = albumRowState, contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                            items(w.albums, key = { it.id }) { a ->
                                val owned = ownedAlbums[ownedAlbumKey(a.artist, a.cleanTitle)]
                                AlbumCard(a, owned != null, dl[app.library.albumKey(a)],
                                    onOpen = { owned?.let { nav.push(Screen.Detail(DetailKind.ALBUM, it.tracks.first().albumKey)) } },
                                    onDownload = { app.library.requestAlbum(a) })
                            }
                        }
                    }
                }
            }
            if (albumNamed) webAlbums()
            if (topSong != null) {
                item { SectionHeader("Top result") }
                item(key = "top${topSong.path}") {
                    TopResultCard(topSong) { searchFocus.clearFocus(); if (snap.track?.path == topSong.path) { if (!snap.playing) app.player.toggle(); nav.nowPlaying = true } else { app.player.play(listOf(topSong), 0, null) } }
                }
            }
            if (!albumNamed) webAlbums()
            if (w.songs.isNotEmpty()) {
                item { SectionHeader("Songs") }
                val shown = if (webSongsExpanded) w.songs else w.songs.take(8)
                itemsIndexed(shown, key = { i, s -> "ws$i${s.artist}${s.title}" }) { _, s ->
                    val owned = ownedSongs[com.ipodemu.library.matchKey(s.title, s.artist)]
                    val status = dl[app.library.songKey(s.artist, s.title)]
                    WebRow(s.title, status?.takeIf { it.stage != DownloadStage.DONE }?.message ?: "${s.artist} · ${s.album}", s.artUrl, owned, status,
                        onPlay = { searchFocus.clearFocus(); owned?.let { if (snap.track?.path == it.path) { if (!snap.playing) app.player.toggle(); nav.nowPlaying = true } else { app.player.play(listOf(it), 0, null) } } },
                        onDownload = { app.library.requestDownload(s.artist, s.title, s.album, s.durationMs) },
                        // the three-line menu: Download, Hi-Res (administrators, per song, after a confirmation: the files are several times the size), Find on YouTube
                        onHiRes = {
                            val items = ArrayList<SheetItem>()
                            items += SheetItem("Download", Glyph.DOWN) { app.library.requestDownload(s.artist, s.title, s.album, s.durationMs) }
                            if (app.ui.isAdmin && app.prefs.slskdUrl.isNotBlank() && app.prefs.slskdApiKey.isNotBlank() && app.prefs.fileMoverUrl.isNotBlank()) items += SheetItem("✦ Hi-Res download…", Glyph.DOWN) {
                                nav.sheet = SheetSpec("Download in Hi-Res?", "Hi-Res files are 2 to 5 times bigger (roughly 40 to 200 MB). Only \"${s.title}\" is downloaded, as a separate [Hi-Res] copy. If no Hi-Res copy exists you get the normal quality instead.", listOf(
                                    SheetItem("Yes, download \"${s.title}\" in Hi-Res", Glyph.DOWN) { app.library.requestDownload(s.artist, s.title, s.album, s.durationMs, hiRes = true) },
                                    SheetItem("Cancel", Glyph.CLOSE) { },
                                ))
                            }
                            items += SheetItem("Find on YouTube", Glyph.SEARCH) {
                                app.startActivity(android.content.Intent(android.content.Intent.ACTION_VIEW, android.net.Uri.parse("https://www.youtube.com/results?search_query=" + android.net.Uri.encode("${s.artist} ${s.title}"))).addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK))
                            }
                            nav.sheet = SheetSpec(s.title, s.artist.ifEmpty { null }, items)
                        })
                }
                if (!webSongsExpanded && w.songs.size > 8) item { ShowMoreRow(w.songs.size - 8) { webSongsExpanded = true } }
            }
            if (restSongs.isNotEmpty()) {
                item { SectionHeader("In your library") }
                val shown = if (songsExpanded) restSongs else restSongs.take(4)
                itemsIndexed(shown, key = { i, t -> "s$i${t.path}" }) { i, t -> TrackRow(t, nav, snap, onPlay = { app.player.play(listOf(t), 0, null) }) }
                if (!songsExpanded && restSongs.size > 4) item { ShowMoreRow(restSongs.size - 4) { songsExpanded = true } }
            }
            if (r.albums.isNotEmpty()) {
                item { SectionHeader("Your albums") }
                val shownAlbums = if (albumsExpanded) r.albums else r.albums.take(3)
                items(shownAlbums, key = { "b" + it.tracks.first().albumKey }) { g ->
                    IpodRow({ nav.push(Screen.Detail(DetailKind.ALBUM, g.tracks.first().albumKey)) }, height = 56.dp, leading = { ArtImage(g.artKey, Modifier.size(48.dp), thumb = true, corner = 8.dp) },
                        trailing = { OwnedTag() }) { hi ->
                        Column { Txt(g.name, size = 16f, color = if (hi) Color.White else sc.onBg); Txt(g.tracks.firstOrNull()?.artist ?: "", size = 13f, color = if (hi) Color(0xDDFFFFFF) else sc.onBgDim) }
                    }
                }
                if (!albumsExpanded && r.albums.size > 3) item { ShowMoreRow(r.albums.size - 3) { albumsExpanded = true } }
            }
            if (webLoading) item { Row(Modifier.fillMaxWidth().padding(16.dp), horizontalArrangement = Arrangement.Center) { Txt("Searching for more...", size = 13f, color = sc.onBgDim) } }
            if (!webLoading && w.songs.isEmpty() && !app.ui.guest) item { DownloadRequestBar(app, q) }
        }
    }
}


/** An album/EP/single in Search's swipeable row: cover with a Download (or progress, or Open) button on it, title, kind and artist. */
@Composable
private fun AlbumCard(a: WebCatalog.Album, owned: Boolean, status: DownloadStatus?, onOpen: () -> Unit, onDownload: () -> Unit) {
    val sc = LocalScheme.current
    val busy = status != null && status.stage != DownloadStage.DONE && status.stage != DownloadStage.FAILED
    val failed = status?.stage == DownloadStage.FAILED
    val act = { if (owned) onOpen() else if (!busy) onDownload() }
    Column(Modifier.width(132.dp).clip(RoundedCornerShape(10.dp)).clickable(onClick = act)
        .semantics { contentDescription = "${a.cleanTitle}, ${a.kind} by ${a.artist}. " + if (owned) "In your library" else if (busy) status!!.message else "Download" }) {
        Box {
            RemoteArtImage(a.artUrl, Modifier.size(132.dp), corner = 10.dp)
            Box(Modifier.align(Alignment.BottomEnd).padding(6.dp).height(32.dp).widthIn(min = 32.dp).clip(RoundedCornerShape(50))
                .background(if (owned || busy) Color(0xCC000000) else sc.accent).padding(horizontal = 8.dp), contentAlignment = Alignment.Center) {
                when {
                    owned -> Txt("Open", size = 12f, weight = FontWeight.Medium, color = Color.White)
                    busy -> Txt(Regex("""\d+%""").find(status!!.message)?.value ?: "...", size = 12f, weight = FontWeight.Medium, color = Color.White)
                    else -> GlyphIcon(Glyph.DOWN, Modifier.size(16.dp), sc.accent.readableInk())
                }
            }
        }
        Txt(a.cleanTitle, size = 14f, weight = FontWeight.Medium, color = sc.onBg, maxLines = 1, modifier = Modifier.padding(top = 6.dp))
        Txt(if (busy || failed) status!!.message else "${a.kind} · ${a.artist}", size = 12f, color = if (failed) Color(0xFFFF9C9C) else sc.onBgDim, maxLines = 1)
    }
}
private class WebResults(val songs: List<WebCatalog.Song>, val albums: List<WebCatalog.Album>)

/** "Espresso", "Espresso EP", "Espresso - Single" and "Espresso (Deluxe)" are one release when checking what's owned. */
private fun ownedAlbumKey(artist: String, album: String) = com.ipodemu.library.primaryArtist(artist) + "|" +
    album.lowercase().replace(Regex("""\s*[(\[](deluxe|expanded|anniversary|remaster)[^)\]]*[)\]]"""), "").replace(Regex("""\s*-?\s*\b(ep|single)\s*$"""), "").filter { it.isLetterOrDigit() }

/**
 * A song or album from the web catalog: cover, title, a second line that turns into live progress while downloading, and
 * one action: Play (or Open) when it's already in the library, otherwise Download, a percentage while it runs, then Play.
 */
@Composable
private fun WebRow(title: String, subtitle: String, imageUrl: String?, owned: Track?, status: DownloadStatus?, onPlay: () -> Unit, onDownload: () -> Unit, actionLabel: String = "Download $title", onHiRes: (() -> Unit)? = null) {
    val sc = LocalScheme.current
    val busy = status != null && status.stage != DownloadStage.DONE && status.stage != DownloadStage.FAILED
    val failed = status?.stage == DownloadStage.FAILED
    IpodRow({ if (owned != null) onPlay() else if (!busy) onDownload() }, height = 60.dp,
        leading = { RemoteArtImage(imageUrl, Modifier.size(48.dp), corner = 8.dp) },
        trailing = {
            when {
                owned != null -> GlossPill("Play", onPlay, height = 32.dp)
                busy -> Txt(Regex("""\d+%""").find(status!!.message)?.value ?: "...", size = 13f, weight = FontWeight.Medium, color = sc.accent)
                else -> Row(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalAlignment = Alignment.CenterVertically) {
                    if (onHiRes != null) IconAction(Glyph.LIST, "More download options for $title", onHiRes, size = 36.dp)
                    GlossPill(if (failed) "Retry" else "Download", onDownload, Modifier.semantics { contentDescription = actionLabel }, height = 32.dp)
                }
            }
        },
    ) { hi ->
        Column {
            Txt(title, size = 15f, color = if (hi) Color.White else sc.onBg, maxLines = 1)
            Txt(subtitle, size = 12f, color = if (failed && !hi) Color(0xFFFF9C9C) else if (hi) Color(0xDDFFFFFF) else sc.onBgDim, maxLines = 1)
        }
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
        Box(Modifier.size(44.dp).clip(CircleShape).background(sc.accent).semantics { contentDescription = "Play" }.clickable(onClick = onPlay), contentAlignment = Alignment.Center) {
            GlyphIcon(Glyph.PLAY, Modifier.size(20.dp), sc.accent.readableInk())
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

/** When the catalog has nothing: download the query as typed ("Artist - Title", or the whole query as the title). */
@Composable
private fun DownloadRequestBar(app: App, query: String) {
    rememberLibRev(app.library) // redraw as the request progresses
    if (query.isEmpty()) return
    val sc = LocalScheme.current
    val parts = query.split(" - ", limit = 2)
    val artist = if (parts.size > 1) parts[0].trim() else ""
    val title = if (parts.size > 1) parts[1].trim() else query
    val status = app.library.downloads[app.library.songKey(artist, title)]
    Column(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Txt(if (parts.size > 1) "Not in the catalog." else "Not in the catalog. Typing \"Artist - Title\" finds the right file faster.", size = 13f, color = sc.onBgDim, maxLines = 3)
        GlossPill(if (artist.isNotEmpty()) "Download \"$title\" by $artist" else "Download \"$query\"", { app.library.requestDownload(artist, title, "") }, height = 40.dp)
        status?.let { s ->
            val color = when (s.stage) {
                DownloadStage.DONE -> Color(0xFF7CE0A0)
                DownloadStage.FAILED -> Color(0xFFFF9C9C)
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
        // opens on the song playing now (what already played stays above it)
        val list = androidx.compose.foundation.lazy.rememberLazyListState(initialFirstVisibleItemIndex = snap.index.coerceIn(0, q.size - 1))
        LazyColumn(Modifier.fillMaxSize(), state = list, contentPadding = PaddingValues(bottom = 24.dp)) {
            itemsIndexed(q, key = { i, t -> "$i${t.path}" }) { i, t ->
                val cur = i == snap.index
                val remove = remember(i, t) { SwipeAction("Remove", Glyph.CLOSE, { Color(0xFFD9423F) }) { app.player.removeFromQueue(i) } }
                SwipeRow(right = remove, left = remove) {
                IpodRow({ app.player.skipTo(i) }, height = 56.dp,
                    leading = { Box(Modifier.width(30.dp), contentAlignment = Alignment.Center) { if (cur) EqualizerBars(Modifier.size(18.dp), snap.playing, sc.accent) else Txt("${i + 1}", size = 14f, color = sc.onBgDim) } },
                    trailing = { Box(Modifier.size(44.dp).clip(RoundedCornerShape(50)).semantics { contentDescription = "Remove ${t.title}" }.clickable { app.player.removeFromQueue(i) }, contentAlignment = Alignment.Center) { GlyphIcon(Glyph.CLOSE, Modifier.size(18.dp), sc.onBgDim) } }) { hi ->
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

/** A title-bar action: a 48dp flat icon in the Modern theme, the glossy round button under the iPod theme. */
@Composable
fun TopAction(g: Glyph, label: String, onClick: () -> Unit) {
    if (LocalStyle.current.modern) IconAction(g, label, onClick)
    else GlossButton(onClick, label = label, size = 40.dp) { GlyphIcon(g, Modifier.size(22.dp), Color.White) }
}

private fun greeting(): String = when (java.util.Calendar.getInstance().get(java.util.Calendar.HOUR_OF_DAY)) {
    in 5..11 -> "Good morning"; in 12..17 -> "Good afternoon"; else -> "Good evening"
}

/** Top-right of Home: the signed-in account's initial (or a person glyph), opening the Account screen. */
@Composable
private fun AccountAction() {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val name = app.prefs.accountUserName
    Box(Modifier.size(48.dp).clip(CircleShape).clickable { app.ui.accountOpen = true }, contentAlignment = Alignment.Center) {
        Box(Modifier.size(34.dp).clip(CircleShape).background(if (name.isNotEmpty()) sc.accent else Color(0x22FFFFFF)), contentAlignment = Alignment.Center) {
            if (name.isNotEmpty()) Txt(name.take(1).uppercase(), size = 16f, weight = FontWeight.Bold, color = Color.White)
            else GlyphIcon(Glyph.ARTIST, Modifier.size(20.dp), sc.onBg)
        }
    }
}

/** Playlists the Playlists screen lists: the account's own and any made here, not the old on-device ones hidden at upgrade;
 * folder/m3u-derived groups only while browsing an iPod in Sync mode. */
fun shownPlaylists(app: com.ipodemu.App): List<com.ipodemu.library.UserPlaylist> =
    app.prefs.hiddenPlaylists.let { hidden -> app.userData.playlists.toList().filter { it.id !in hidden } }
fun shownFolderPlaylists(app: com.ipodemu.App): List<com.ipodemu.library.Group> =
    if (app.library.source == com.ipodemu.library.Library.Source.SYNC) app.library.playlists() else emptyList()

/** Finished downloads, newest first, as playable tracks: the Soulseek file itself, or the library's copy once Jellyfin has
 * it (a Lidarr grab); a whole-album download brings in that album's songs. */
fun recentDownloads(app: com.ipodemu.App): List<Track> {
    val lib = app.library
    val out = LinkedHashMap<String, Track>()
    for (d in app.userData.downloads.toList()) {
        val t = lib.resolve(d.path, d.title to d.artist)
        if (t != null) { out.putIfAbsent(t.path, t); continue }
        lib.albums().firstOrNull { it.name.equals(d.album.ifBlank { d.title }, true) && it.tracks.any { x -> com.ipodemu.library.primaryArtist(x.artist) == com.ipodemu.library.primaryArtist(d.artist) } }
            ?.tracks?.forEach { out.putIfAbsent(it.path, it) }
    }
    return out.values.toList()
}

/** Per-song actions on a playlist page: move it up/down/to the top, or take it out (with Undo). */
private fun playlistSongItems(app: App, id: String, t: Track): List<SheetItem> {
    val ud = app.userData; val lib = app.library
    val p = ud.playlists.firstOrNull { it.id == id } ?: return emptyList()
    val i = p.paths.indexOfFirst { it == t.path || lib.resolve(it, ud.meta[it])?.path == t.path }
    if (i < 0) return emptyList()
    val items = ArrayList<SheetItem>()
    if (i > 0) { items += SheetItem("Move up", Glyph.CHEVRON) { ud.move(id, i, i - 1) }; items += SheetItem("Move to top", Glyph.CHEVRON) { ud.move(id, i, 0) } }
    if (i < p.paths.lastIndex) items += SheetItem("Move down", Glyph.CHEVRON) { ud.move(id, i, i + 1) }
    items += SheetItem("Remove from playlist", Glyph.CLOSE) {
        val path = ud.removeAt(id, i)
        if (path != null) app.ui.say("Removed \"${t.title}\" from ${p.name}", "Undo") { ud.insertAt(id, i, path) }
    }
    return items
}

private class HomeData(val shelves: List<com.ipodemu.library.HomeShelf>, val moods: List<String>)
private object HomeCache { @Volatile var last: HomeData? = null }
