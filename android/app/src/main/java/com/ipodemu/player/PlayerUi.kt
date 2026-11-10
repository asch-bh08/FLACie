package com.ipodemu.player

import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.graphics.toArgb
import kotlinx.coroutines.launch
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
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.heightIn
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
import androidx.compose.foundation.lazy.grid.itemsIndexed as gridItemsIndexed
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
import androidx.compose.ui.focus.focusProperties
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
import com.ipodemu.library.RatingStore
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
import kotlinx.coroutines.launch

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
    /** A ready-made playlist from FLACie Web (owner, 2 Nov): its id, and its name to show while it loads. */
    data class Made(val id: String, val name: String) : Screen
    /** A song from the online catalog, before it is downloaded (owner, 6 Nov): its big cover, full title, artist, album, year and length, and its other versions. */
    data class CatalogSong(val song: com.ipodemu.library.WebCatalog.Song, val versions: List<com.ipodemu.library.WebCatalog.Song>) : Screen
    /** An album the library lacks, from the catalogue: its page, downloaded only from its button (owner, 6 Nov: a tap downloaded it). */
    data class CatalogAlbum(val album: com.ipodemu.library.WebCatalog.Album) : Screen
    data object Search : Screen
    data object Queue : Screen
    data object Settings : Screen
    data object Dashboard : Screen
    /** Settings > About > Licences (privacy = false) or Privacy (privacy = true): LegalUi.kt. */
    data class Legal(val privacy: Boolean) : Screen
    data object Offline : Screen
    /** The songs kept on the phone for playing without signal ("Downloads"), as against Settings > Offline and storage. */
    data object Downloads : Screen
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
    /** The list the screen on top is showing (a playlist, an album...): what a play from it reports as "Playing from". */
    var playContext: String? = null
    /** For a playlist page: (its [playContext], the playlist's DJ key), so a play from its songs carries the playlist's DJ switch. Only used while the name still matches. */
    var playContextId: Pair<String, String>? = null
    /** Settings opens at this section once (the Now Playing DJ button's long press opens "dj"). */
    var settingsSection: String? = null
    var sheet by mutableStateOf<SheetSpec?>(null)
    /** True while the design I player is on screen: it shows [sheet] in its own style. */
    var designSheets by mutableStateOf(false)
    var nameDialog by mutableStateOf<((String) -> Unit)?>(null)
    /** Set (to the current name) when the name dialog is renaming something, so it says "Rename playlist" / "Save" with the name filled in. */
    var nameDialogRename by mutableStateOf<String?>(null)
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

/** What the player is doing, as one immutable snapshot for composition. [dj]: the AI DJ for this queue, on or off; null when it can't work here (no FLACie Web or no Jellyfin sign-in). */
@androidx.compose.runtime.Immutable
data class PlayerSnap(val track: Track?, val playing: Boolean, val shuffle: Boolean, val repeat: Int, val index: Int, val count: Int, val rev: Int = 0, val speed: Float = 1f, val sleep: com.ipodemu.playback.SleepMode = com.ipodemu.playback.SleepMode.OFF, val dj: Boolean? = null,
                      /** The DJ's words while it talks (Captions), or null. */
                      val djCaption: String? = null)

@Composable
fun rememberSnap(player: PlayerController, prefs: Prefs): PlayerSnap {
    var tick by remember { mutableIntStateOf(0) }
    DisposableEffect(player) { val d = player.observe { tick++ }; onDispose { d() } }
    @Suppress("UNUSED_EXPRESSION") tick
    return PlayerSnap(player.current, player.wantsToPlay, prefs.shuffle, prefs.repeat, player.queueIndex, player.queue.size, player.queueRev, player.speed, player.sleepMode,
        if (player.djAvailable) player.djOn else null, player.djCaption)
}

@Composable
fun rememberPosition(player: PlayerController, active: Boolean): State<Long> {
    val pos = remember { mutableLongStateOf(player.positionMs) }
    LaunchedEffect(active) {
        pos.longValue = player.positionMs
        // paused: a slower re-read, so a seek made while paused still moves the bar and the time
        while (isActive) { pos.longValue = player.positionMs; delay(if (active) 250 else 500) }
    }
    return pos
}

fun fmtTime(ms: Long): String {
    val s = (ms / 1000).coerceAtLeast(0)
    val h = s / 3600
    // String.format is slow on Android (a Formatter and a Locale lookup per call) and a song list formats one time per row
    fun two(n: Long) = if (n < 10) "0$n" else "$n"
    return if (h > 0) "$h:" + two(s % 3600 / 60) + ":" + two(s % 60) else "" + (s / 60) + ":" + two(s % 60)
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


@OptIn(androidx.compose.animation.ExperimentalSharedTransitionApi::class)
@Composable
fun PlayerHost(nav: PlayerNav) {
    val app = LocalApp.current
    val snap = rememberSnap(app.player, app.prefs)
    val sc = LocalScheme.current
    val ui = app.ui
    ui.rev

    BackHandler(enabled = !ui.pickerOpen && (nav.sheet != null || nav.nameDialog != null)) { nav.sheet = null; nav.nameDialog = null; nav.nameDialogRename = null }
    BackHandler(enabled = !ui.pickerOpen && nav.sheet == null && nav.nameDialog == null && nav.nowPlaying) { nav.nowPlaying = false }
    BackHandler(enabled = !ui.pickerOpen && nav.sheet == null && nav.nameDialog == null && !nav.nowPlaying && nav.stack.size > 1) { nav.pop() }

    LaunchedEffect(nav.top) { WheelFocus.last = null }
    // the other devices' state, so the remote bar knows when something starts or stops elsewhere (quicker while one is showing)
    LaunchedEffect(app.connect.available) {
        while (app.connect.available) { if (app.connect.connected) app.connect.loadSessions(); delay(if (app.connect.remoteNow() != null) 3_000 else 8_000) }
    }
    // a playlist link shared to FLACie: show Playlists, whose link box takes it (NewPlaylistRow)
    LaunchedEffect(ui.sharedLinkRequest) { if (ui.sharedLinkRequest > 0) { nav.nowPlaying = false; nav.push(Screen.Lib(LibKind.PLAYLISTS)) } }
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
        androidx.compose.animation.SharedTransitionLayout(Modifier.fillMaxSize()) {
        val sharedLayout = this
        val modern = LocalStyle.current.modern
        // the iPod theme has no tabs: its stack must always start at Home (a Modern tab root like Settings would make Back exit)
        if (!modern && nav.stack.first() != Screen.Home) androidx.compose.runtime.SideEffect { if (nav.stack.first() != Screen.Home) nav.stack.add(0, Screen.Home) }
        // and the reverse on switching to Modern: a tab screen pushed on top of Home becomes its own tab root, so the nav highlights it
        if (modern && nav.stack.size > 1 && Tab.entries.any { it.root == nav.top }) androidx.compose.runtime.SideEffect { Tab.entries.firstOrNull { it.root == nav.top }?.let { nav.selectTab(it) } }
        BackHandler(enabled = modern && !ui.pickerOpen && nav.sheet == null && nav.nameDialog == null && !nav.nowPlaying && nav.stack.size == 1 && nav.top != Screen.Home) { nav.selectTab(Tab.HOME) }
        // what is covered (Now Playing, a menu, a dialog) must not take D-pad focus: Down used to walk into the page underneath
        BoxWithConstraints(Modifier.fillMaxSize().focusProperties { canFocus = !(nav.nowPlaying || nav.sheet != null || nav.nameDialog != null) }) {
        // Modern: a navigation rail when there is width to spare (unfolded Fold, tablet, landscape, the square RG Rotate),
        // a bottom bar on portrait phones and the Fold cover screen
        val rail = modern && (maxWidth >= 600.dp || maxWidth >= maxHeight)
        Row(Modifier.fillMaxSize()) {
        if (rail) NavRail(nav)
        Column(Modifier.weight(1f).fillMaxHeight().graphicsLayer { translationX = if (nav.nowPlaying) 0f else backDrag }) {
            Box(Modifier.weight(1f)) {
                // the incoming screen only moves (the old one is not kept composed, so it stays cheap): deeper slides in, a tab cross-fades
                // with a small rise, Back fades in because the swipe-back drag already moved the page (PageMove, as the web's pages)
                val depth = nav.stack.size
                val stateHolder = androidx.compose.runtime.saveable.rememberSaveableStateHolder()
                val lastDepth = remember { androidx.compose.runtime.mutableIntStateOf(depth) }
                val move = PageMove.of(Tweaks.motion, lastDepth.intValue, depth, sameTop = false)
                androidx.compose.runtime.SideEffect { lastDepth.intValue = depth }
                androidx.compose.runtime.key(nav.top) {
                    val start = remember { move.start }
                    val p = remember { androidx.compose.animation.core.Animatable(if (start == PageMove.NONE.start) 1f else 0f) }
                    LaunchedEffect(Unit) { if (p.value < 1f) p.animateTo(1f, pageSpring()) }
                    Box(Modifier.fillMaxSize().graphicsLayer {
                        val left = 1f - p.value
                        translationX = left * start.slide0 * size.width
                        translationY = left * start.rise0 * density
                        alpha = start.alpha0 + (1f - start.alpha0) * p.value
                    }) {
                        // each screen on the back stack keeps its saved state (a list's scroll position), so Back returns to the same place in it
                        val key = "${nav.stack.size}:${nav.top}"
                        stateHolder.SaveableStateProvider(key) { ScreenContent(nav.top, nav, snap) }
                    }
                }
            }
            // music playing on another of this user's devices while this phone is quiet: a remote bar instead (Spotify Connect style)
            // only WHICH device (not its moving position) is read here, so the poll every few seconds does not recompose the whole screen
            val remoteId by remember { androidx.compose.runtime.derivedStateOf { app.connect.remoteNow()?.id } }
            // while typing, the keyboard covers the bars below but their room was still kept: on a short phone (an S22 with display zoom up) the
            // results were cut off half way down an empty band. They step aside until the keyboard goes.
            val typing = androidx.compose.foundation.layout.WindowInsets.ime.getBottom(androidx.compose.ui.platform.LocalDensity.current) > 0
            // what is downloading, above the mini player, wherever you are (owner, 1 Nov: it took ten menus to find out)
            if (!typing && !nav.nowPlaying && modern && !LocalHardware.current && !app.ui.guest) DownloadActivityBar { nav.push(Screen.Downloads) }
            // what the DJ is doing after a press of the mic (as the web's DJ bar): listening, what it heard, thinking
            if (!typing && !nav.nowPlaying && !LocalHardware.current) DjStatusBar()
            if (typing) Unit
            else if (remoteId != null && !snap.playing && !nav.nowPlaying && modern && !LocalHardware.current) {
                AnimatedVisibility(remember { androidx.compose.animation.core.MutableTransitionState(false).apply { targetState = true } }, enter = slideInVertically(androidx.compose.animation.core.spring(0.8f, 380f)) { it } + androidx.compose.animation.fadeIn(tween(160))) { RemoteMiniPlayer(remoteId!!) }
            } else {
                val showMini = snap.track != null && !nav.nowPlaying && (modern || nav.top != Screen.Home) && !LocalHardware.current
                val miniState = remember { androidx.compose.animation.core.MutableTransitionState(false) }
                miniState.targetState = showMini
                // the bar slides up from the bottom the first time a song starts (and when coming back from the full player); it fades out as the full player opens, the cover flies on
                AnimatedVisibility(miniState, enter = slideInVertically(androidx.compose.animation.core.spring(0.8f, 380f)) { it } + androidx.compose.animation.fadeIn(tween(160)), exit = fadeOut(tween(160))) {
                    androidx.compose.runtime.CompositionLocalProvider(LocalSharedCover provides SharedCoverScope(sharedLayout, this)) { snap.track?.let { MiniPlayer(snap, nav) } }
                }
            }
            if (modern && !rail && !typing) BottomNav(nav)
        }
        }
        }
        AnimatedVisibility(
            nav.nowPlaying,
            // the full player springs up (and the cover flies into it); instant with Animations off
            enter = if (Tweaks.motion) slideInVertically(androidx.compose.animation.core.spring(0.9f, 380f)) { it } + fadeIn(tween(200)) else androidx.compose.animation.EnterTransition.None,
            exit = if (Tweaks.motion) slideOutVertically(androidx.compose.animation.core.spring(1f, 520f)) { it } + fadeOut(tween(200)) else androidx.compose.animation.ExitTransition.None,
        ) { Box(Modifier.graphicsLayer { translationX = backDrag }.focusProperties { canFocus = nav.sheet == null && nav.nameDialog == null }) {
            androidx.compose.runtime.CompositionLocalProvider(LocalPlayerNav provides nav, LocalSharedCover provides SharedCoverScope(sharedLayout, this@AnimatedVisibility)) { NowPlayingScreen(snap, nav) }
        } }
        // the full-screen player draws its menus in design I itself (LivingSleeve.DesignSheet)
        if (!(nav.nowPlaying && nav.designSheets)) nav.sheet?.let { s -> ActionSheet(s.title, s.subtitle, s.items) { nav.sheet = null } }
        nav.nameDialog?.let { done -> NameDialog(done, nav.nameDialogRename) { nav.nameDialog = null; nav.nameDialogRename = null } }
        }
    }
    }
}

@Composable
private fun ScreenContent(screen: Screen, nav: PlayerNav, snap: PlayerSnap) {
    when (screen) {
        Screen.Home -> HomeScreen(nav, snap)
        is Screen.Lib -> LibraryScreen(screen.kind, nav, snap)
        is Screen.Detail -> DetailScreen(screen, nav, snap)
        is Screen.Made -> MadeScreen(screen, nav, snap)
        is Screen.CatalogSong -> CatalogSongScreen(screen, nav, snap)
        is Screen.CatalogAlbum -> CatalogAlbumScreen(screen, nav, snap)
        Screen.Search -> SearchScreen(nav, snap)
        Screen.Queue -> QueueScreen(nav, snap)
        Screen.Settings -> SettingsScreen(nav)
        Screen.Dashboard -> SettingsScreen(nav, dashboard = true)
        is Screen.Legal -> if (screen.privacy) LegalScreen(nav, "Privacy", LegalDocs.PRIVACY) else LegalScreen(nav, "Licences", LegalDocs.LICENCES)
        Screen.Offline -> OfflineScreen(nav)
        Screen.Downloads -> DownloadsScreen(nav)
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
    picked: Boolean? = null, onPick: (() -> Unit)? = null, kind: String? = null,
) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val fav by app.userData.favState(t)   // per-track state: a favourite toggle recomposes only this row
    val hiRes = remember(t, AudioFacts.version) { t.hiRes }   // not worked out again on every recomposition while scrolling
    val isCurrent = snap.track?.path == t.path
    val ctx = androidx.compose.ui.platform.LocalContext.current
    val rCode = app.prefs.swipeRowRight; val lCode = app.prefs.swipeRowLeft
    // built once per (track, favourite, settings): stable objects keep the row's pointer handlers from restarting
    val hw = LocalHardware.current   // the faithful device view has no swipe shortcuts
    // a narrow screen (a phone, or the 480 dp wide RG Rotate with its 80 dp rail): the badges would leave the title room for 7 letters, so the source badge goes and Hi-Res moves under the title
    val cfg = androidx.compose.ui.platform.LocalConfiguration.current
    val narrow = cfg.screenWidthDp < 520
    // big text on a narrow phone: the time steps aside so the title keeps its room (ListFit; it is in the song's menu and on Now Playing)
    val showTime = ListFit.showTime(cfg.screenWidthDp.toFloat(), cfg.fontScale)
    // a download tag (running, recently finished, failed, or "New download" for a Cloud song past the hour) takes a third line under the artist;
    // the "New download" marker is left to the row's own badge where that is shown
    val badgeShown = !narrow && Tweaks.badges && app.library.mixedSources
    val tagged = isTagged(t, showNew = !badgeShown)
    val right = remember(t, fav, rCode, hw) { if (hw) null else swipeAction(rCode, app, t, fav, ctx) }
    val left = remember(t, fav, lCode, hw) { if (hw) null else swipeAction(lCode, app, t, fav, ctx) }
    SwipeRow(right = right, left = left, modifier = modifier) {
    val focus = androidx.compose.ui.platform.LocalFocusManager.current
    // tapping the song that is already playing opens the player (or resumes it) instead of starting it over; any tap puts the keyboard away
    val keyboard = androidx.compose.ui.platform.LocalSoftwareKeyboardController.current
    val tap = { focus.clearFocus(); keyboard?.hide(); if (picked != null && onPick != null) onPick() else if (isCurrent) { if (!snap.playing) app.player.toggle(); nav.nowPlaying = true } else onPlay() }
    IpodRow(onClick = tap, onLong = { openTrackSheet(app, nav, t, sheetExtra + (sheetExtraFor?.invoke(t) ?: emptyList())) }, height = if (tagged) 74.dp else 56.dp,
        leading = {
            // choosing several songs: the cover stays, with a tick on it (an album's numbered list gets the tick alone)
            if (picked != null && showArt) Box(Modifier.size(42.dp)) {
                ArtImage(t.artKey, Modifier.fillMaxSize(), thumb = true, corner = 8.dp)
                PickTick(picked, Modifier.align(Alignment.BottomEnd))
            } else if (picked != null) Box(Modifier.size(42.dp).clip(CircleShape).background(if (picked) sc.accent else Color(0x22FFFFFF)).semantics { stateDescription = if (picked) "Selected" else "Not selected" }, contentAlignment = Alignment.Center) {
                GlyphIcon(if (picked) Glyph.CHECK else Glyph.PLUS, Modifier.size(20.dp), if (picked) Color.Black else sc.onBg)
            } else if (showArt) Box(Modifier.size(42.dp)) {
                ArtImage(t.artKey, Modifier.fillMaxSize(), thumb = true, corner = 8.dp)
                if (isCurrent) Box(Modifier.fillMaxSize().background(Color(0x66000000)), contentAlignment = Alignment.Center) {
                    EqualizerBars(Modifier.size(22.dp), snap.playing, Color.White)
                }
            } else if (index != null) {
                Box(Modifier.width(30.dp), contentAlignment = Alignment.Center) {
                    if (isCurrent) EqualizerBars(Modifier.size(18.dp), snap.playing, sc.accent) else if (LocalStyle.current.modern) MonoLabel("$index", size = 13f, upper = false) else Txt("$index", size = 14f, color = sc.onBgDim)
                }
            }
        },
        trailing = {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                // on the phone for listening without signal (a dim mark while it is still on its way)
                DownloadedMark(t.path, 15.dp)
                if (fav) GlyphIcon(Glyph.HEART_FILLED, Modifier.size(18.dp), sc.accent)
                if (hiRes && !narrow) HiResBadge()
                if (!narrow) SourceBadge(t.source)
                // times are DM Mono in design I, so the columns of digits line up
                if (t.durationMs > 0 && showTime) { val d = remember(t.durationMs) { fmtTime(t.durationMs) }; if (LocalStyle.current.modern) MonoLabel(d, size = 12.5f, color = rowDim(), upper = false) else Txt(d, size = 13f, color = rowDim()) }
                val moreLabel = remember(t.title) { "More options for ${t.title}" }
                Box(Modifier.size(44.dp).clip(RoundedCornerShape(50)).semantics { contentDescription = moreLabel }.clickable { openTrackSheet(app, nav, t, sheetExtra + (sheetExtraFor?.invoke(t) ?: emptyList())) }, contentAlignment = Alignment.Center) {
                    GlyphIcon(Glyph.MORE, Modifier.size(22.dp), rowDim())
                }
            }
        },
    ) { hi ->
        Column {
            Txt(t.title, size = 16f, weight = if (isCurrent) FontWeight.Bold else FontWeight.Medium, color = if (hi) Color.White else if (isCurrent) sc.accent else sc.onBg)
            // search rows say what they are first ("Song · …"), as on the web (owner, 7 Nov: "a label like Spotify")
            val sub = remember(t.artist, t.album, kind) { (kind?.let { "$it · " } ?: "") + (if (t.artist.isEmpty()) t.album else if (t.album.isEmpty()) t.artist else t.artist + " · " + t.album) }
            if (narrow && hiRes) Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                HiResBadge(compact = true)
                if (sub.isNotEmpty()) Txt(sub, Modifier.weight(1f), size = 13f, color = if (hi) Color(0xDDFFFFFF) else sc.onBgDim)
            } else if (sub.isNotEmpty()) Txt(sub, size = 13f, color = if (hi) Color(0xDDFFFFFF) else sc.onBgDim)
            if (tagged) DownloadTagLine(t, hi = hi, showNew = !badgeShown)
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
    com.ipodemu.library.TrackSource.CLOUD -> Color(0xFF6DB3FF)   // the web's .badge.cloud text (blue)
}
/** The badge background: a tint of its own colour, except the new-download badge, which is the web's #14261c. */
private fun sourceBack(s: com.ipodemu.library.TrackSource, color: Color): Color =
    if (s == com.ipodemu.library.TrackSource.CLOUD) Color(0xFF12233B) else color.copy(alpha = 0.16f)

@Composable
private fun SourceBadge(source: com.ipodemu.library.TrackSource) {
    if (!Tweaks.badges || !LocalApp.current.library.mixedSources) return
    val color = sourceColor(source)
    Box(Modifier.clip(RoundedCornerShape(50)).background(sourceBack(source, color)).padding(horizontal = 7.dp, vertical = 3.dp)) {
        Txt(com.ipodemu.library.SourceLabel.badge(source), size = 11f, weight = FontWeight.SemiBold, color = color)
    }
}

private fun swipeAction(code: Int, app: App, t: Track, fav: Boolean, ctx: android.content.Context): SwipeAction? = when (code) {
    1 -> SwipeAction("Play next", Glyph.QUEUE, { Color(0xFF2A5FB0) }) { app.player.addNext(t); android.widget.Toast.makeText(ctx, "Playing next", android.widget.Toast.LENGTH_SHORT).show() }
    2 -> SwipeAction(if (fav) "Unfavorite" else "Favorite", if (fav) Glyph.CLOSE else Glyph.HEART_FILLED, { Color(0xFFD9427A) }) { app.userData.toggleFavorite(t) }
    3 -> SwipeAction("Add to queue", Glyph.PLUS, { Color(0xFF2A5FB0) }) { app.player.addToQueue(t); android.widget.Toast.makeText(ctx, "Added to queue", android.widget.Toast.LENGTH_SHORT).show() }
    else -> null
}

fun openTrackSheet(app: App, nav: PlayerNav, t: Track, extra: List<SheetItem> = emptyList()) {
    // the everyday choices first, in words, and the rarer ones one tap further in (owner, 7 Nov: "menus are too complicated"); as on the web
    val ud = app.userData
    val items = ArrayList<SheetItem>()
    items += SheetItem("Play next", Glyph.PLAY) { app.player.addNext(t) }
    items += SheetItem("Add to queue", Glyph.QUEUE) { app.player.addToQueue(t) }
    items += SheetItem("Add to playlist…", Glyph.PLUS) { nav.sheet = playlistPicker(app, nav, t) }
    items += SheetItem(if (ud.isFavorite(t)) "Remove from favourites" else "Add to favourites", if (ud.isFavorite(t)) Glyph.HEART_FILLED else Glyph.HEART) { ud.toggleFavorite(t) }
    if (t.isMusic && t.artist.isNotEmpty()) items += SheetItem("Go to artist", Glyph.ARTIST) { nav.nowPlaying = false; nav.push(Screen.Detail(DetailKind.ARTIST, t.albumArtist.ifEmpty { t.artist })) }
    if (t.isMusic && t.album.isNotEmpty()) items += SheetItem("Go to album", Glyph.ALBUM) { nav.nowPlaying = false; nav.push(Screen.Detail(DetailKind.ALBUM, t.albumKey)) }
    if (t.isMusic) items += SheetItem("Share", Glyph.JAM) { shareTrack(app, t) }
    if (t.source == com.ipodemu.library.TrackSource.JELLYFIN) {
        val off = app.offline.entry(t.path)
        if (off == null) items += SheetItem("Download for offline", Glyph.DOWN) { val n = app.offline.enqueue(listOf(t), com.ipodemu.library.offline.OfflineReason.MANUAL); app.ui.say(if (n > 0) "Downloading \"${t.title}\"" else "Already offline") }
        else items += SheetItem(when (off.state) { com.ipodemu.library.offline.OfflineState.DONE -> "Remove download"; com.ipodemu.library.offline.OfflineState.FAILED -> "Download failed: try again"; else -> "Cancel download" }, if (off.state == com.ipodemu.library.offline.OfflineState.FAILED) Glyph.DOWN else Glyph.CLOSE) {
            if (off.state == com.ipodemu.library.offline.OfflineState.FAILED) app.offline.enqueue(listOf(t), com.ipodemu.library.offline.OfflineReason.MANUAL) else app.offline.remove(t.path)
        }
    }
    items += extra
    if (t.isMusic) items += SheetItem("Not for me…", Glyph.CLOSE) {
        nav.sheet = SheetSpec("Not for me", t.title, listOf(
            SheetItem("Less like this", Glyph.MORE) { app.listened(com.ipodemu.library.ListenKind.LESS_LIKE_THIS, t); app.ui.say("You'll hear less like \"${t.title}\"") },
            SheetItem("Never pick this song", Glyph.CLOSE) { thumbsDown(app, t) },
        ))
    }
    items += SheetItem("More…", Glyph.MORE) { nav.sheet = trackMoreSheet(app, nav, t) }
    nav.sheet = SheetSpec(t.title, t.artist.ifEmpty { null }, items)
}

/** A song menu's rarer choices: the rating, the DJ's choice for it, its MusicBrainz match, and (administrators) a Hi-Res copy. */
private fun trackMoreSheet(app: App, nav: PlayerNav, t: Track): SheetSpec {
    val items = ArrayList<SheetItem>()
    items += SheetItem("Rate" + (app.userData.rating(t).takeIf { it > 0 }?.let { "  (" + RatingStore.label(it) + ")" } ?: "") + "…", Glyph.STAR) { nav.sheet = ratingSheet(app, t) }
    // the DJ's choice for this song (kept on the account by title and artist): the one in force is ticked
    if (t.isMusic && app.player.djAvailable && com.ipodemu.playback.DjRules.songKey(t.title, t.artist).isNotEmpty())
        for ((label, choice, on) in com.ipodemu.playback.DjRules.songMenu(app.player.djSettings, t.title, t.artist))
            items += SheetItem(label, Glyph.MORE, selected = on, group = "DJ") {
                app.player.setDjSong(t.title, t.artist, choice) { app.ui.say("Couldn't save the DJ choice: $it") }
                app.ui.say(when (choice) { true -> "The DJ will always introduce \"${t.title}\""; false -> "The DJ won't talk over \"${t.title}\""; null -> "The DJ treats \"${t.title}\" as usual" })
            }
    if (t.isMusic) items += SheetItem("MusicBrainz match…", Glyph.INFO) { musicBrainzSheet(app, nav, t) }
    // administrators only: a Hi-Res copy of a song already in the library (saved beside it; nothing is downloaded instead if none exists)
    if (app.ui.isAdmin && t.isMusic && app.prefs.slskdUrl.isNotBlank() && app.prefs.slskdApiKey.isNotBlank() && app.prefs.fileMoverUrl.isNotBlank() && !t.path.contains("[Hi-Res]", ignoreCase = true)) {
        items += SheetItem("✦ Hi-Res version…", Glyph.DOWN) {
            nav.sheet = SheetSpec("Get a Hi-Res copy?", "Hi-Res files are 2 to 5 times bigger (roughly 40 to 200 MB). It is saved next to this one. If no Hi-Res copy exists, nothing is changed.", listOf(
                SheetItem("Yes, download Hi-Res", Glyph.DOWN) { app.library.requestDownload(t.artist, t.title, t.album, t.durationMs, hiRes = true, fallbackToNormal = false) },
                SheetItem("Cancel", Glyph.CLOSE) { },
            ))
        }
    }
    return SheetSpec(t.title, "More", items)
}

/**
 * The person does not want this song: the server is told (Autoplay and Home stop suggesting it), it leaves Up next and Autoplay's list, and if
 * it is playing the next song starts (with nothing after it, the player stops).
 */
internal fun thumbsDown(app: App, t: Track) {
    app.listened(com.ipodemu.library.ListenKind.THUMB_DOWN, t)
    app.autoplay.dislike(t)
    hideOnHome(t)
    app.player.thumbsDown(t)
    app.ui.say("Thumbs down: \"${t.title}\" won't be suggested again")
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
private fun NameDialog(onDone: (String) -> Unit, renaming: String?, onDismiss: () -> Unit) {
    val sc = LocalScheme.current
    var text by remember { mutableStateOf(renaming ?: "") }
    val fr = remember { FocusRequester() }
    LaunchedEffect(Unit) { try { fr.requestFocus() } catch (_: Exception) {} }
    // a short screen with the keyboard up leaves about 180 dp: the dialog sits at the top, is tighter, and scrolls, so Cancel and Create are never hidden behind the keyboard
    val shortScreen = androidx.compose.ui.platform.LocalConfiguration.current.screenHeightDp < 500
    Box(Modifier.fillMaxSize().background(Color(0xAA000000)).imePadding().pointerInput(Unit) { detectTapGestures { onDismiss() } }, contentAlignment = if (shortScreen) Alignment.TopCenter else Alignment.Center) {
        Column(
            Modifier.padding(if (shortScreen) 12.dp else 24.dp).fillMaxWidth().clip(RoundedCornerShape(20.dp)).background(sc.top.mix(Color.Black, .4f))
                .pointerInput(Unit) { detectTapGestures { } }.verticalScroll(rememberScrollState()).padding(if (shortScreen) 14.dp else 20.dp),
            verticalArrangement = Arrangement.spacedBy(if (shortScreen) 8.dp else 14.dp),
        ) {
            Txt(if (renaming != null) "Rename playlist" else "New playlist", size = if (shortScreen) 18f else 20f, weight = FontWeight.Bold)
            BasicTextField(
                text, { text = it }, singleLine = true, cursorBrush = SolidColor(sc.accent),
                textStyle = TextStyle(color = sc.onBg, fontSize = 18.sp),
                keyboardOptions = androidx.compose.foundation.text.KeyboardOptions(capitalization = androidx.compose.ui.text.input.KeyboardCapitalization.Words, imeAction = androidx.compose.ui.text.input.ImeAction.Done),
                keyboardActions = androidx.compose.foundation.text.KeyboardActions(onDone = { if (text.isNotBlank()) { onDone(text.trim()); onDismiss() } }),
                modifier = Modifier.fillMaxWidth().focusRequester(fr).clip(RoundedCornerShape(12.dp)).background(Color(0x22FFFFFF)).padding(if (shortScreen) 10.dp else 14.dp),
            )
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.align(Alignment.End)) {
                GlossPill("Cancel", onDismiss)
                GlossPill(if (renaming != null) "Save" else "Create", { if (text.isNotBlank()) { onDone(text.trim()); onDismiss() } }, primary = true)
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
    // the shelves of the last run are on disk (HomeSnapshot): a cold start draws Home from them at once
    var home by remember { androidx.compose.runtime.mutableStateOf(HomeCache.last ?: app.homeSnapshot?.let { HomeData(it.shelves, it.moods) }) }
    // shelves already on screen never change under the person's thumb (they used to swap two or three times as the app opened: saved, phone, server):
    // newer ones are kept and show the next time Home opens
    // newer shelves get their covers now (disk and memory), so the next Home shows them at once instead of loading them in (owner, 9 Nov)
    fun offer(d: HomeData) {
        HomeCache.last = d; if (home == null) home = d
        // only the first screen's covers are decoded into memory; the rest of the shelves' covers are only fetched to the disk (b126: memory)
        try { val hd = com.ipodemu.library.HomeSnapshot.Data(d.shelves, d.moods); app.art.keepReady(com.ipodemu.library.HomeSnapshot.firstViewKeys(hd), com.ipodemu.library.HomeSnapshot.coverKeys(hd)) } catch (_: Exception) {}
    }
    LaunchedEffect(libRev, hour, recentsKey) {
        // ...and works them out again only once the whole library has loaded, not after every step of loading it (each pass shuffles and sorts all of it)
        if (home != null && !lib.loaded) return@LaunchedEffect
        // a burst of library changes (each merge notifies) restarts this effect: only the last one builds
        if (home != null) delay(300)
        val d = kotlinx.coroutines.withContext(Dispatchers.Default) {
            try { com.ipodemu.Boot.span("HomeShelves.build") { HomeData(com.ipodemu.library.HomeShelves.build(lib, app.userData, System.currentTimeMillis(), com.ipodemu.library.TasteData.jf, com.ipodemu.library.TasteData.hits), com.ipodemu.library.HomeShelves.moodsAvailable(lib)) } } catch (e: Exception) { android.util.Log.w("FLACie", "Home shelves failed", e); null }
        }
        if (d != null) {
            offer(d)
            if (d.shelves.isNotEmpty()) kotlinx.coroutines.withContext(Dispatchers.IO) {
                try { com.ipodemu.library.HomeSnapshot.write(java.io.File(app.filesDir, "home.json"), com.ipodemu.library.HomeSnapshot.Data(d.shelves, d.moods)) } catch (_: Exception) {}
            }
        }
        // the server's engine knows the whole listening log: its song shelves replace the phone's own when it answers (no signal or an error: the phone's stay)
        val web = com.ipodemu.library.FlacieWebClient(app.prefs)
        val served = web.available && d != null && run {
            val server = kotlinx.coroutines.withContext(Dispatchers.IO) { try { web.recommendHome() } catch (e: Exception) { android.util.Log.w("FLACie", "server Home failed: ${e.message}"); null } }
            val mixed = server?.let { sv -> kotlinx.coroutines.withContext(Dispatchers.Default) { try { com.ipodemu.library.HomeShelves.fromServer(sv, { p -> lib.resolve(p.path ?: "", p.title to p.artist) }, d!!.shelves) { a -> serverAlbum(lib, a) } } catch (_: Exception) { null } } }
            if (mixed != null) {
                val d3 = HomeData(mixed, d!!.moods); offer(d3)
                kotlinx.coroutines.withContext(Dispatchers.IO) { try { com.ipodemu.library.HomeSnapshot.write(java.io.File(app.filesDir, "home.json"), com.ipodemu.library.HomeSnapshot.Data(d3.shelves, d3.moods)) } catch (_: Exception) {} }
            }
            mixed != null
        }
        if (served) return@LaunchedEffect
        // then, once an hour, learn from Jellyfin's play counts and the artists' best-known songs, and build the shelves again with them
        val before = com.ipodemu.library.TasteData.hits.size + com.ipodemu.library.TasteData.jf.size
        kotlinx.coroutines.withContext(Dispatchers.IO) { com.ipodemu.library.TasteData.refresh(app, lib) }
        if (com.ipodemu.library.TasteData.hits.size + com.ipodemu.library.TasteData.jf.size != before) {
            val d2 = kotlinx.coroutines.withContext(Dispatchers.Default) {
                try { HomeData(com.ipodemu.library.HomeShelves.build(lib, app.userData, System.currentTimeMillis(), com.ipodemu.library.TasteData.jf, com.ipodemu.library.TasteData.hits), com.ipodemu.library.HomeShelves.moodsAvailable(lib)) } catch (e: Exception) { android.util.Log.w("FLACie", "Home shelves failed", e); null }
            }
            if (d2 != null) offer(d2)
        }
    }
    val gone = HomeCache.gone.toSet()
    val shelves = (home?.shelves ?: emptyList()).let { l -> if (gone.isEmpty()) l else l.map { sh -> if (sh.tracks.none { it.matchKey in gone }) sh else com.ipodemu.library.HomeShelf(sh.id, sh.title, sh.subtitle, sh.tracks.filter { it.matchKey !in gone }, sh.albums) } }
    androidx.compose.runtime.SideEffect { com.ipodemu.Boot.once(if (shelves.isNotEmpty()) "home: shelves drawn" else "home: first composition (no shelves yet)") }
    val moodList = home?.moods ?: emptyList()
    var mood by remember { androidx.compose.runtime.mutableStateOf<String?>(null) }
    app.userData.rev
    app.ui.rev
    val clock by androidx.compose.runtime.produceState("") { while (true) { value = java.text.SimpleDateFormat("h:mm a", java.util.Locale.getDefault()).format(java.util.Date()); delay(15000) } }
    val online = rememberOnline()
    // the chips and the first shelves come in with a short rise as Home opens (as the web's page does), not again on scrolling
    val entrance = rememberEntrance()
    Column(Modifier.fillMaxSize()) {
        val modern = LocalStyle.current.modern
        if (!modern) TopBar(if (app.prefs.timeInTitle) clock else "FLACie", nav, showBack = false) {
            TopAction(Glyph.SEARCH, "Search") { nav.push(Screen.Search) }
            TopAction(Glyph.GEAR, "Settings") { nav.push(Screen.Settings) }
        }
        LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 24.dp)) {
            if (modern) item(key = "hero") {
                val last = snap.track ?: recentsKey?.let { lib.byPath()[it] ?: lib.resolve(it, app.userData.meta[it]) }
                HomeHero(last, nav, onShuffleAll = { val s = lib.songs(); if (s.isNotEmpty()) app.player.shuffleAll(s) }) { AccountAction() }
            }
            if (!online) item { OfflineChip(Modifier.padding(horizontal = 16.dp, vertical = 6.dp)) }
            // the first visit: where things are, once (as on the web); Got it puts it away for good
            if (modern && !welcomed(app).value) item(key = "welcome") { WelcomeCard { welcomed(app).value = true; app.prefs.welcomed = true } }
            if (!modern) item { NowPlayingCard(snap, nav) { val s = lib.songs(); if (s.isNotEmpty()) { app.player.shuffleAll(s) } } }
            if (com.ipodemu.PermissionPlan.showMediaNotice(app.ui.loginMode, app.ui.mediaAccess)) item { MediaAccessNotice(Modifier.padding(horizontal = 16.dp, vertical = 8.dp)) }
            // design I: Favourites, Recent, Downloads, Recently added and the moods as outlined chips (the chosen mood filled); Shuffle all is in the hero
            // the chips wrap onto more lines rather than run off the right edge (owner, 9 Nov: nothing may hang off the screen; "Offline music" was
            // cut in half at the edge of a phone). Each chip has its own 6 dp above and below, so the lines need no extra gap
            if (modern) item {
                @OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
                androidx.compose.foundation.layout.FlowRow(Modifier.fillMaxWidth().padding(horizontal = 16.dp).entranceRise(entrance, 1), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    // the moods first, as on the web ("All" filled while no mood is picked), then the shortcuts the old Home had
                    DesignChip("All", mood == null, { mood = null })
                    moodList.forEach { m -> DesignChip(m, mood == m, { mood = if (mood == m) null else m }) }
                    DesignChip("Favourites", false, { nav.push(Screen.Detail(DetailKind.FAVORITES)) }, icon = Glyph.HEART)
                    DesignChip("Recent", false, { nav.push(Screen.Detail(DetailKind.RECENT)) }, icon = Glyph.CLOCK)
                    // "Downloads" is the songs kept for listening without signal; "Recently added" is new music fetched into the library
                    DesignChip("Offline music", false, { nav.push(Screen.Downloads) }, icon = Glyph.DOWNLOADED)
                    DesignChip("Recently added", false, { nav.push(Screen.Detail(DetailKind.DOWNLOADS)) }, icon = Glyph.PLUS)
                }
            } else item {
                Row(Modifier.horizontalScroll(rememberScrollState()).padding(horizontal = 12.dp, vertical = 4.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    GlossPill("Shuffle All", { val s = lib.songs(); if (s.isNotEmpty()) { app.player.shuffleAll(s) } }, icon = Glyph.SHUFFLE, height = 32.dp)
                    // shuffles what is queued after the song playing; off puts the queue back in order (Shuffle All starts a new shuffled queue)
                    GlossPill(if (snap.shuffle) "Shuffle on" else "Shuffle off", { app.prefs.shuffle = !app.prefs.shuffle; app.player.applyModes() }, icon = Glyph.SHUFFLE, height = 32.dp, primary = snap.shuffle)
                    GlossPill("Favorites", { nav.push(Screen.Detail(DetailKind.FAVORITES)) }, icon = Glyph.HEART, height = 32.dp)
                    GlossPill("Recent", { nav.push(Screen.Detail(DetailKind.RECENT)) }, icon = Glyph.CLOCK, height = 32.dp)
                    GlossPill("Offline music", { nav.push(Screen.Downloads) }, icon = Glyph.DOWNLOADED, height = 32.dp)
                    GlossPill("Recently added", { nav.push(Screen.Detail(DetailKind.DOWNLOADS)) }, icon = Glyph.PLUS, height = 32.dp)
                    moodList.forEach { m -> GlossPill(m, { mood = if (mood == m) null else m }, height = 32.dp, primary = mood == m) }
                }
            }
            // YT Music style shelves (see library/Recommend.kt); the Daily Mix moves on every hour
            mood?.let { mname ->
                val ts = com.ipodemu.library.HomeShelves.moodTracks(lib, mname)
                item { SectionHeader(mname) }
                item {
                    LazyRow(contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(14.dp)) {
                        itemsIndexed(ts) { i, t -> TrackCard(t, Modifier.width(130.dp)) { app.player.play(ts, i, null, mname, from = "home") } }
                    }
                }
            }
            // playlists are one tap away here as well as in the Playlists tab: near the top, before the other shelves, as on the web (at the foot of
            // Home the owner never saw them)
            val lists = shownPlaylists(app); val by0 = lib.byPath()
            if (lists.isNotEmpty() && mood == null) {
                item { SectionHeader("Your playlists") }
                item {
                    LazyRow(contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(14.dp)) {
                        items(lists.take(14), key = { it.id }) { pl ->
                            val ts = remember(pl.paths.size, libRev) { pl.paths.asSequence().take(8).mapNotNull { by0[it] ?: lib.resolve(it, app.userData.meta[it]) }.toList() }   // only the first few: the cover needs one, and a 350-song playlist resolved whole on every Home build was a stutter
                            AlbumCard(com.ipodemu.library.Group("${pl.name} (${pl.paths.size})", ts, ts.firstNotNullOfOrNull { it.artKey }), Modifier.width(150.dp)) { nav.push(Screen.Detail(DetailKind.USER, pl.id)) }
                        }
                    }
                }
            }
            if (mood == null) itemsIndexed(shelves, key = { _, sh -> sh.id }) { i, sh ->
                Column(Modifier.entranceRise(entrance, i + 2)) {
                    if (sh.id == "quick-picks") {
                        Row(Modifier.fillMaxWidth().padding(end = 12.dp), verticalAlignment = Alignment.CenterVertically) {
                            Box(Modifier.weight(1f)) { SectionHeader(sh.title) }
                            GlossPill("Play all", { app.player.play(sh.tracks.filterNot { com.ipodemu.library.HomeShelves.isNew(it) }, 0, null, sh.title, from = "home") }, icon = Glyph.PLAY, height = 28.dp)
                        }
                        QuickPicks(sh.tracks, nav) { i -> sh.tracks.getOrNull(i)?.let { playShelf(app, sh.tracks, it, sh.title) } }
                    } else {
                    SectionHeader(sh.title)
                    LazyRow(contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(14.dp)) {
                        // keyed by the song, so when the shelf is rebuilt a card stays with its own song and a press lands on the one drawn under the finger
                        if (sh.tracks.isNotEmpty()) items(sh.tracks.distinctBy { it.path }, key = { it.path }) { t -> TrackCard(t, Modifier.width(130.dp)) { playShelf(app, sh.tracks, t, sh.title) } }
                        else items(sh.albums, key = { it.tracks.first().albumKey }) { g -> AlbumCard(g, Modifier.width(150.dp)) { nav.push(Screen.Detail(DetailKind.ALBUM, g.tracks.first().albumKey)) } }
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

/**
 * Home's top in design I (J-Home-phone): the last-played cover full width, fading into the ground; over it Search and the account;
 * then a DM Mono greeting, the serif headline with its last words in italics, and Shuffle all in the cover's colour, with
 * Resume (tinted) beside it when there is a song to go back to. It is at the top of the screen, well clear of the bottom 100 dp.
 */
@Composable
private fun HomeHero(last: Track?, nav: PlayerNav, onShuffleAll: () -> Unit, account: @Composable () -> Unit) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val cols = rememberCoverColours(last?.artKey)
    val hour = remember { java.util.Calendar.getInstance().get(java.util.Calendar.HOUR_OF_DAY) }
    // on its side the hero is shorter (it filled a landscape phone), and the headline is smaller when its two lines would not fit (HomeFit)
    val cfg = androidx.compose.ui.platform.LocalConfiguration.current
    val hero = remember(cfg.screenWidthDp, cfg.screenHeightDp, cfg.fontScale) { HomeFit.hero(cfg.screenWidthDp.toFloat(), cfg.screenHeightDp.toFloat(), cfg.fontScale) }
    Column {
        Box(Modifier.fillMaxWidth().height(hero.height.dp)) {
            if (last?.artKey != null) {
                ArtImage(last.artKey, Modifier.fillMaxSize(), corner = 0.dp)
                Box(Modifier.fillMaxSize().background(Brush.verticalGradient(0f to Color(0x55000000), .3f to Color.Transparent, .55f to Color.Transparent, 1f to Palette.bg)))
            } else Box(Modifier.fillMaxSize().background(Brush.verticalGradient(listOf(Color(cols.tone), Palette.bg))))
            // the web's top bar: the search box at the left (a tap opens Search), then Settings and the account
            Row(Modifier.align(Alignment.TopStart).fillMaxWidth().padding(start = 14.dp, end = 4.dp, top = 6.dp), verticalAlignment = Alignment.CenterVertically) {
                Row(
                    Modifier.weight(1f).height(48.dp).clip(RoundedCornerShape(50)).background(Color(0x59000000)).border(1.dp, Color(0x33FFFFFF), RoundedCornerShape(50))
                        .semantics { contentDescription = "Search" }.clickable { nav.push(Screen.Search) }.padding(horizontal = 14.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    GlyphIcon(Glyph.SEARCH, Modifier.size(18.dp), Color.White.copy(alpha = .8f))
                    Txt("Search songs, albums, artists", Modifier.padding(start = 10.dp), size = 14f, color = Color.White.copy(alpha = .7f), maxLines = 1)
                }
                // (no Settings cog here: Settings is always a tab at the bottom, and two cogs on one screen was one icon too many: owner, 7 Nov)
                account()
            }
            Column(Modifier.align(Alignment.BottomStart).padding(horizontal = 20.dp)) {
                val who = remember { app.prefs.accountUserName.ifBlank { if (app.prefs.accountKind == "nas") app.prefs.nasUsername else "" } }
                MonoLabel(HomeWords.greeting(hour) + if (who.isNotBlank()) ", $who" else "", color = sc.onBg.copy(alpha = .75f))
                SerifTitle(HomeWords.headline(last != null), Modifier.padding(top = 6.dp, bottom = 8.dp), size = hero.headlineSp, maxLines = 2, italicWords = if (last != null) 4 else 1)
            }
        }
        // Shuffle all and Resume wrap onto two lines rather than run off a narrow phone at big text (they needed about 350 dp at font scale 1.5)
        @OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
        androidx.compose.foundation.layout.FlowRow(Modifier.padding(start = 20.dp, end = 20.dp, top = 14.dp, bottom = 12.dp), horizontalArrangement = Arrangement.spacedBy(10.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            AccentPill("Shuffle all", Glyph.SHUFFLE, cols, onShuffleAll)
            if (last != null) TonePill("Resume", Glyph.PLAY, cols, {
                // the song still loaded opens the player (resuming it); one from history starts again
                if (app.player.current?.path == last.path) { if (!app.player.isPlaying) app.player.toggle(); nav.nowPlaying = true }
                else app.player.play(listOf(last), 0, null, "Recently Played")
            })
        }
    }
}

@Composable
fun SectionHeader(text: String) {
    // design I: shelf and section titles are serif headings (last word italic); the iPod skins keep their small caps
    if (LocalStyle.current.modern) { SerifTitle(text, Modifier.padding(start = 20.dp, end = 16.dp, top = 24.dp, bottom = 10.dp), size = 26f); return }
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
    val spoken = remember(g.name) { g.name + (g.tracks.firstOrNull()?.let { t -> t.albumArtist.ifEmpty { t.artist } }?.takeIf { it.isNotEmpty() }?.let { ", $it" } ?: "") }
    Column(modifier.clip(RoundedCornerShape(14.dp)).semantics(mergeDescendants = true) { contentDescription = spoken }.clickable(src, null, onClick = onClick).padding(4.dp)) {
        // rounded covers with a soft shadow, as on the web's new look
        ArtImage(g.artKey, Modifier.fillMaxWidth().aspectRatio(1f).coverShadow(12.dp, 8.dp).border(if (focused) 3.dp else 0.dp, if (focused) sc.accent else Color.Transparent, RoundedCornerShape(12.dp)), corner = 12.dp)
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
        ArtImage(t.artKey, Modifier.fillMaxWidth().aspectRatio(1f).coverShadow(12.dp, 8.dp).border(if (focused) 3.dp else 0.dp, if (focused) sc.accent else Color.Transparent, RoundedCornerShape(12.dp)), thumb = true, corner = 12.dp)
        Txt(t.title, Modifier.padding(top = 8.dp), size = 13f, weight = FontWeight.SemiBold)
        Txt(t.artist, size = 12f, color = sc.onBgDim)
        if (com.ipodemu.library.HomeShelves.isNew(t)) Txt("New · tap to get it", size = 11f, color = sc.accent)
        DownloadTagLine(t, size = 11f, compact = true, showNew = !(Tweaks.badges && LocalApp.current.library.mixedSources))
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
                // Offline only: just the songs kept on this phone (they play without a connection)
                var offlineOnly by androidx.compose.runtime.saveable.rememberSaveable { androidx.compose.runtime.mutableStateOf(false) }
                val offlineRev = app.offline.rev
                val songs = remember(allSongs, sourceFilter, offlineOnly, offlineRev) {
                    val bySource = sourceFilter?.let { f -> allSongs.filter { it.source == f } } ?: allSongs
                    if (offlineOnly) bySource.filter { app.offline.isDownloaded(it.path) } else bySource
                }
                SongList(songs, nav, snap, showArt = true, sections = if (sort == 0) 1 else if (sort == 1) 2 else 0,
                    header = {
                        ShuffleHeader(songs, nav, SORTS[sort]) { sort = (sort + 1) % SORTS.size }
                        SourceFilterChips(allSongs, sourceFilter) { sourceFilter = it }
                        if (app.offline.doneCount > 0) Row(Modifier.padding(horizontal = 16.dp, vertical = 4.dp)) { DesignChip("Offline only", offlineOnly, { offlineOnly = !offlineOnly }, icon = Glyph.DOWNLOADED) }
                    })
            }
            LibKind.MEMOS -> { val memos = lib.memos; SongList(memos, nav, snap, showArt = false) }
            LibKind.ALBUMS -> AlbumGrid(lib.albums(), nav)
            LibKind.ARTISTS -> GroupList(lib.artists(), nav, circle = true) { Screen.Detail(DetailKind.ARTIST, it.name) }
            LibKind.GENRES -> GroupList(lib.genres(), nav, circle = false) { Screen.Detail(DetailKind.GENRE, it.name) }
            LibKind.PLAYLISTS -> PlaylistsList(nav)
            LibKind.JELLYFIN -> SongList(remember(LocalLibRev.current) { lib.jellyfinTracks.sortedBy { sortKey(it.title) } }, nav, snap, showArt = true, sections = 1)
            LibKind.PLEX -> SongList(remember(LocalLibRev.current) { lib.plexTracks.sortedBy { sortKey(it.title) } }, nav, snap, showArt = true, sections = 1)
            LibKind.NAS -> SongList(remember(LocalLibRev.current) { lib.nasOnlyTracks().sortedBy { sortKey(it.title) } }, nav, snap, showArt = true, sections = 1)
        }
    }
}

@Composable
private fun ShuffleHeader(tracks: List<Track>, nav: PlayerNav, sortLabel: String, onSort: () -> Unit) {
    val app = LocalApp.current
    // wraps rather than squeezing: on a 360 dp phone Play, Shuffle and the sort pill fill the line, and the count used to be pressed to nothing
    // against the right edge; now it drops to the next line instead (owner, 9 Nov: nothing may hang off or overlap)
    @OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
    androidx.compose.foundation.layout.FlowRow(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 12.dp), horizontalArrangement = Arrangement.spacedBy(10.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        GlossPill("Play", { app.player.play(tracks, 0, null) }, icon = Glyph.PLAY, primary = true)
        GlossPill("Shuffle", { app.player.play(tracks, tracks.indices.random(), true) }, icon = Glyph.SHUFFLE)
        GlossPill(sortLabel, onSort, height = 32.dp)
        Txt(songCount(tracks.size), Modifier.align(Alignment.CenterVertically).padding(start = 4.dp), size = 13f, color = LocalScheme.current.onBgDim)
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
            // design I filters: outlined chips, the chosen one filled
            if (LocalStyle.current.modern) DesignChip("$label  $count", current == value, { onChange(value) })
            else GlossPill("$label ($count)", { onChange(value) }, height = 30.dp, primary = current == value)
        }
        chip("All", null, all.size)
        for (s in com.ipodemu.library.TrackSource.entries) {
            val n = counts[s] ?: continue
            chip(com.ipodemu.library.SourceLabel.badge(s), s, n)
        }
    }
}

@Composable
@OptIn(androidx.compose.foundation.ExperimentalFoundationApi::class)
fun SongList(
    tracks: List<Track>, nav: PlayerNav, snap: PlayerSnap, showArt: Boolean, numbered: Boolean = false,
    sheetExtra: List<SheetItem> = emptyList(), sheetExtraFor: ((Track) -> List<SheetItem>)? = null, header: (@Composable () -> Unit)? = null, sections: Int = 0, missing: List<Pair<String, String>> = emptyList(),
    footer: (@Composable () -> Unit)? = null,
) {
    val app = LocalApp.current
    if (tracks.isEmpty() && missing.isEmpty()) { EmptyState("Nothing here yet"); return }
    val sc = LocalScheme.current
    // choosing several songs: Select in a song's sheet starts it; a tap then ticks a row; the bar at the bottom acts on them all (as on the web)
    var chosen by remember(tracks) { mutableStateOf<Set<Int>?>(null) }
    androidx.activity.compose.BackHandler(enabled = chosen != null) { chosen = null }
    // the first rows rise in one after another as the list opens (the web's j-row), and just appear when scrolled to later
    val entrance = rememberEntrance()
    val row: @Composable (Int, Track) -> Unit = { i, t ->
        val c = chosen
        Box(Modifier.entranceRise(entrance, i)) {
        TrackRow(t, nav, snap, showArt = showArt, index = if (numbered) i + 1 else null, sheetExtra = sheetExtra + SheetItem("Select", Glyph.CHECK) { chosen = setOf(i) }, sheetExtraFor = sheetExtraFor, onPlay = {
            app.player.play(tracks, i, null, nav.playContext, nav.playContextId?.takeIf { it.first == nav.playContext }?.second)
        }, picked = c?.contains(i), onPick = c?.let { cur -> { chosen = if (i in cur) cur - i else cur + i } })
        }
    }
    Box(Modifier.fillMaxSize()) {
    val groups = remember(tracks, sections) { if (sections > 0) tracks.withIndex().groupBy { letterOf(sortKey(if (sections == 2) it.value.artist.ifEmpty { "#" } else it.value.title)) } else emptyMap() }
    LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 24.dp)) {
        if (header != null) item(contentType = "header") { header() }
        // a plain button to start choosing (the song's menu has Select too), so ticking songs to download or queue is easy to find
        if (chosen == null && tracks.size > 1) item(contentType = "select") {
            Row(Modifier.padding(horizontal = 16.dp, vertical = 2.dp)) { DesignChip("Select", false, { chosen = emptySet() }, icon = Glyph.CHECK) }
        }
        if (sections > 0) {
            groups.forEach { (l, list) ->
                stickyHeader(key = "h$l", contentType = "letter") { LetterHeader(l) }
                items(list, key = { "${it.index}${it.value.path}" }, contentType = { "song" }) { row(it.index, it.value) }
            }
        } else itemsIndexed(tracks, key = { i, t -> "$i${t.path}" }, contentType = { _, _ -> "song" }) { i, t -> row(i, t) }
        item { CountFooter(songCount(tracks.size) + totalTime(tracks)) }
        if (missing.isNotEmpty()) {
            item { SectionHeader("Not in your library") }
            item {
                Row(Modifier.padding(horizontal = 16.dp, vertical = 4.dp)) { GlossPill("Download all ${missing.size}", { missing.forEach { (t, a) -> app.library.requestDownload(a, t, "", 0) } }, icon = Glyph.DOWNLOADED, height = 34.dp) }
            }
            itemsIndexed(missing, key = { i, m -> "m$i${m.first}" }) { _, (t, a) ->
                val st = app.library.downloads[app.library.songKey(a, t)]
                IpodRow({ app.library.requestDownload(a, t, "", 0) }, height = 56.dp, trailing = { Txt(st?.message?.take(18) ?: "Download", size = 13f, color = sc.accent) }) { _ ->
                    Column { Txt(t, size = 16f, color = sc.onBgDim, maxLines = 1); Txt(a, size = 13f, color = sc.onBgDim, maxLines = 1) }
                }
            }
        }
        if (footer != null) item(contentType = "footer") { footer() }
        if (chosen != null) item { Spacer(Modifier.height(140.dp)) }
    }
    chosen?.let { cur -> SelectBar(cur.sorted().map { tracks[it] }, nav, Modifier.align(Alignment.BottomCenter), allPicked = cur.size == tracks.size, onAll = { chosen = if (cur.size == tracks.size) emptySet() else tracks.indices.toSet() }) { chosen = null } }
    }
}

/** The bar for the songs chosen in a list: Select all, Download N (kept on the phone), Play next, Add to queue, Add to a playlist, Cancel. */
@Composable
private fun SelectBar(songs: List<Track>, nav: PlayerNav, modifier: Modifier, allPicked: Boolean, onAll: () -> Unit, done: () -> Unit) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    fun n(k: Int) = if (k == 1) "1 song" else "$k songs"
    Column(modifier.fillMaxWidth().padding(12.dp).clip(RoundedCornerShape(20.dp)).background(Color(0xFF221D19)).padding(14.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Txt("${songs.size} selected", size = 15f, weight = FontWeight.SemiBold, color = sc.onBg)
        // Cancel first, so it is never off the edge of a narrow screen (it was the last chip, out of sight until the row was scrolled); Share as on the web
        Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            DesignChip("Cancel", false, done)
            DesignChip(if (allPicked) "Select none" else "Select all", false, onAll)
            // songs from the server are kept on the phone for listening without signal; the others are already on it
            DesignChip(PickSet.downloadLabel(songs.size), false, {
                if (songs.isEmpty()) return@DesignChip
                val k = app.offline.keep(songs, com.ipodemu.library.offline.OfflineReason.MANUAL)
                app.ui.say(if (k > 0) PickSet.summary(k, 0) else "Already on this phone, or not downloadable", warn = k == 0); done()
            }, icon = Glyph.DOWNLOADED)
            DesignChip("Play next", false, { if (songs.isNotEmpty()) { songs.asReversed().forEach { app.player.addNext(it) }; app.ui.say("${n(songs.size)} play next"); done() } })
            DesignChip("Add to queue", false, { if (songs.isNotEmpty()) { songs.forEach { app.player.addToQueue(it) }; app.ui.say("Added ${n(songs.size)} to the queue"); done() } })
            DesignChip("Add to playlist", false, {
                if (songs.isEmpty()) return@DesignChip
                nav.sheet = SheetSpec("Add ${n(songs.size)} to", null, shownPlaylists(app).map { pl ->
                    SheetItem(pl.name, Glyph.LIST) { songs.forEach { app.userData.addToPlaylist(pl.id, it.path) }; app.ui.say("Added ${n(songs.size)} to ${pl.name}"); done() }
                })
            })
            DesignChip("Share", false, { if (songs.isNotEmpty()) { shareSongs(app, if (songs.size == 1) "song" else "songs", if (songs.size == 1) songs[0].title else n(songs.size), songs); done() } })
        }
    }
}

@Composable
internal fun AlbumGrid(albums: List<Group>, nav: PlayerNav) {
    if (albums.isEmpty()) { EmptyState("No albums yet"); return }
    // the first covers rise in one after another as the grid opens (the web's .grid > .card), not again on scrolling
    val entrance = rememberEntrance()
    // two columns on any phone (GridFit): a 320 dp phone got one column of 296 dp covers
    BoxWithConstraints(Modifier.fillMaxSize()) {
    LazyVerticalGrid(GridCells.Fixed(GridFit.columns(maxWidth.value)), Modifier.fillMaxSize(), contentPadding = PaddingValues(12.dp, 12.dp, 12.dp, 24.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        gridItemsIndexed(albums, key = { _, g -> g.tracks.first().albumKey }) { i, g ->
            AlbumCard(g, Modifier.entranceRise(entrance, i)) { nav.push(Screen.Detail(DetailKind.ALBUM, g.tracks.first().albumKey)) }
        }
    }
    }
}

@OptIn(androidx.compose.foundation.ExperimentalFoundationApi::class)
@Composable
internal fun GroupList(groups: List<Group>, nav: PlayerNav, circle: Boolean, target: (Group) -> Screen) {
    val sc = LocalScheme.current
    if (groups.isEmpty()) { EmptyState("Nothing here yet"); return }
    val sections = remember(groups) { groups.groupBy { letterOf(sortKey(it.name)) } }
    // artists (round pictures) and genres: the first rows rise in as the list opens, as the songs do
    val entrance = rememberEntrance()
    LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 24.dp)) {
        sections.forEach { (l, list) ->
            stickyHeader(key = "h$l", contentType = "letter") { LetterHeader(l) }
            itemsIndexed(list, key = { _, g -> g.name + "|" + (g.tracks.firstOrNull()?.path ?: "") }, contentType = { _, _ -> "group" }) { i, g ->
            Box(Modifier.entranceRise(entrance, i)) {
            IpodRow({ nav.push(target(g)) }, height = 56.dp,
                leading = { ArtImage(g.artKey, Modifier.size(44.dp), thumb = true, corner = 10.dp, circle = circle) },
                // an album (or any group) with every song on the phone carries the downloaded mark
                trailing = { Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) { if (!circle) DownloadedListMark(remember(g) { g.tracks.map { it.path } }); CountChevron(g.tracks.size) } }) { hi ->
                Txt(g.name, size = 17f, weight = FontWeight.Medium, color = if (hi) Color.White else sc.onBg)
            }
            }
            }
        }
        item { CountFooter("${groups.size} ${if (circle) "artists" else "items"}") }
    }
}

/**
 * The top of Playlists as on the web (Playlists.razor): a "New playlist name" field with Create beside it, then "Import a playlist", which sends a
 * playlist file to FLACie Web to find and fetch its songs (the Dashboard's import, one tap from here). Without [withField] only the import buttons:
 * the Playlists grid has the name field in its first tile instead (NewPlaylistTile).
 */
@Composable
private fun NewPlaylistRow(nav: PlayerNav, withField: Boolean = true) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val ctx = androidx.compose.ui.platform.LocalContext.current
    var name by remember { mutableStateOf("") }
    val scope = androidx.compose.runtime.rememberCoroutineScope()
    val web = remember { com.ipodemu.library.FlacieWebClient(app.prefs) }
    val pick = androidx.activity.compose.rememberLauncherForActivityResult(androidx.activity.result.contract.ActivityResultContracts.OpenDocument()) { uri ->
        if (uri != null) scope.launch(Dispatchers.IO) {
            val m = try {
                val file = ctx.contentResolver.query(uri, arrayOf(android.provider.OpenableColumns.DISPLAY_NAME), null, null, null)?.use { if (it.moveToFirst()) it.getString(0) else null } ?: "playlist.txt"
                val j = web.submit(file, ctx.contentResolver.openInputStream(uri)!!.use { it.readBytes() })
                "Sent ${j.file}: ${j.total} songs. The server is fetching them."
            } catch (e: Exception) { e.message ?: "Couldn't send the file" }
            withContext(Dispatchers.Main) { app.ui.say(m) }
        }
    }
    fun create() { val n = name.trim(); if (n.isNotEmpty()) { app.userData.createPlaylist(n); app.ui.say("Made \"$n\""); name = "" } }
    Column(Modifier.padding(horizontal = 16.dp, vertical = 8.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        if (withField) Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            androidx.compose.foundation.text.BasicTextField(
                name, { name = it }, singleLine = true,
                textStyle = androidx.compose.ui.text.TextStyle(color = sc.onBg, fontSize = 16.sp, fontFamily = DesignFonts.sans),
                cursorBrush = androidx.compose.ui.graphics.SolidColor(sc.accent),
                keyboardOptions = androidx.compose.foundation.text.KeyboardOptions(imeAction = androidx.compose.ui.text.input.ImeAction.Done),
                keyboardActions = androidx.compose.foundation.text.KeyboardActions(onDone = { create() }),
                modifier = Modifier.weight(1f).height(52.dp).semantics { contentDescription = "New playlist name" },
                decorationBox = { inner ->
                    Box(Modifier.fillMaxSize().clip(RoundedCornerShape(14.dp)).background(Palette.surface).border(1.dp, Palette.line, RoundedCornerShape(14.dp)).padding(horizontal = 16.dp), contentAlignment = Alignment.CenterStart) {
                        if (name.isEmpty()) Txt("New playlist name", size = 16f, color = sc.onBgDim)
                        inner()
                    }
                },
            )
            Box(
                Modifier.height(52.dp).clip(RoundedCornerShape(14.dp)).background(if (name.isBlank()) sc.accent.copy(alpha = .35f) else sc.accent)
                    .semantics { contentDescription = "Create" }.clickable(enabled = name.isNotBlank()) { create() }.padding(horizontal = 18.dp),
                contentAlignment = Alignment.Center,
            ) { Txt("Create", size = 15f, weight = FontWeight.Bold, color = if (name.isBlank()) sc.onBg.copy(alpha = .5f) else sc.accent.readableInk()) }
        }
        // a link shared to FLACie (Spotify's or Deezer's Share) opens the link box with it filled in; nothing is imported until Import is pressed
        val shared = app.ui.sharedLink
        var linkOpen by remember { mutableStateOf(shared != null) }
        LaunchedEffect(shared) { if (shared != null) linkOpen = true }
        // the two chips wrap at big text ("Paste a link" ran off a 320 dp phone at font scale 1.5)
        @OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
        androidx.compose.foundation.layout.FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            DesignChip("Import a playlist", false, {
                if (web.available) pick.launch(arrayOf("*/*")) else app.ui.say(com.ipodemu.library.LinkImport.NEEDS_WEB, warn = true)
            }, icon = Glyph.DOWN)
            DesignChip("Paste a link", linkOpen, { linkOpen = !linkOpen })
        }
        if (linkOpen) {
            LinkImportBox(startWith = shared, onStarted = { linkOpen = false })
            LaunchedEffect(shared) { if (shared != null) app.ui.sharedLink = null }
        }
    }
}

/**
 * The first tile of the Playlists grid, as on the web (Playlists.razor: "a new playlist is the first tile, where the playlists are", owner, overhaul
 * 9 Nov): an outlined square with a plus where a cover would be, then the name field as a round pill and Create under it. A tap on the square puts
 * the cursor in the field.
 */
@Composable
private fun NewPlaylistTile() {
    val app = LocalApp.current
    val sc = LocalScheme.current
    var name by remember { mutableStateOf("") }
    val fr = remember { androidx.compose.ui.focus.FocusRequester() }
    fun create() { val n = name.trim(); if (n.isNotEmpty()) { app.userData.createPlaylist(n); app.ui.say("Made \"$n\""); name = "" } }
    Column(Modifier.padding(4.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Box(
            Modifier.fillMaxWidth().aspectRatio(1f).clip(RoundedCornerShape(12.dp)).border(1.5.dp, sc.onBg.copy(alpha = .28f), RoundedCornerShape(12.dp))
                .clickable { try { fr.requestFocus() } catch (_: Exception) {} },
            contentAlignment = Alignment.Center,
        ) { GlyphIcon(Glyph.PLUS, Modifier.size(34.dp), sc.onBgDim) }
        androidx.compose.foundation.text.BasicTextField(
            name, { name = it }, singleLine = true,
            textStyle = androidx.compose.ui.text.TextStyle(color = sc.onBg, fontSize = 14.sp, fontFamily = DesignFonts.sans),
            cursorBrush = androidx.compose.ui.graphics.SolidColor(sc.accent),
            keyboardOptions = androidx.compose.foundation.text.KeyboardOptions(imeAction = androidx.compose.ui.text.input.ImeAction.Done),
            keyboardActions = androidx.compose.foundation.text.KeyboardActions(onDone = { create() }),
            modifier = Modifier.fillMaxWidth().height(44.dp).focusRequester(fr).semantics { contentDescription = "New playlist name" },
            decorationBox = { inner ->
                Box(Modifier.fillMaxSize().clip(RoundedCornerShape(50)).background(Palette.surface).border(1.dp, Palette.line, RoundedCornerShape(50)).padding(horizontal = 14.dp), contentAlignment = Alignment.CenterStart) {
                    if (name.isEmpty()) Txt("New playlist name", size = 14f, color = sc.onBgDim)
                    inner()
                }
            },
        )
        Box(
            Modifier.height(44.dp).clip(RoundedCornerShape(50)).background(if (name.isBlank()) sc.accent.copy(alpha = .35f) else sc.accent)
                .semantics { contentDescription = "Create" }.clickable(enabled = name.isNotBlank()) { create() }.padding(horizontal = 20.dp),
            contentAlignment = Alignment.Center,
        ) { Txt("Create", size = 15f, weight = FontWeight.SemiBold, color = if (name.isBlank()) sc.onBg.copy(alpha = .5f) else sc.accent.readableInk()) }
    }
}

/** A playlist in the grid: its cover as a rounded square (a heart for Favourites), the name and the song count; a long press opens its menu. */
@OptIn(androidx.compose.foundation.ExperimentalFoundationApi::class)
@Composable
private fun PlaylistCard(name: String, sub: String, art: String?, modifier: Modifier = Modifier, heart: Boolean = false, onLong: (() -> Unit)? = null,
                         mark: (@Composable () -> Unit)? = null, onClick: () -> Unit) {
    val sc = LocalScheme.current
    Column(modifier.clip(RoundedCornerShape(14.dp)).semantics(mergeDescendants = true) { contentDescription = "$name, $sub" }
        .combinedClickable(onClick = onClick, onLongClick = onLong).padding(4.dp)) {
        if (heart) Box(
            Modifier.fillMaxWidth().aspectRatio(1f).coverShadow(12.dp, 8.dp).clip(RoundedCornerShape(12.dp)).background(Brush.linearGradient(listOf(Color(0xFFE5486B), Color(0xFF9E2A4A)))),
            contentAlignment = Alignment.Center,
        ) { GlyphIcon(Glyph.HEART_FILLED, Modifier.fillMaxSize(.34f), Color.White) }
        else ArtImage(art, Modifier.fillMaxWidth().aspectRatio(1f).coverShadow(12.dp, 8.dp), thumb = true, corner = 12.dp)
        Txt(name, Modifier.padding(top = 8.dp), size = 14f, weight = FontWeight.SemiBold)
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            Txt(sub, Modifier.weight(1f, fill = false), size = 12f, color = sc.onBgDim)
            mark?.invoke()
        }
    }
}

/** A playlist's long-press menu: share it with someone on the same Jellyfin, or delete it (asked first). */
private fun playlistSheet(app: com.ipodemu.App, nav: PlayerNav, p: com.ipodemu.library.UserPlaylist): SheetSpec {
    val ud = app.userData
    return SheetSpec(p.name, songCount(p.paths.size), listOfNotNull(
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
        },
    ))
}

/**
 * Playlists in design I as a grid of covers, as on the web: the New playlist tile first, then Favourites, your playlists and the folder playlists.
 * The import buttons stay above the grid. The cards rise in one after another as the page opens.
 */
@Composable
private fun PlaylistsGrid(nav: PlayerNav) {
    val app = LocalApp.current
    val ud = app.userData
    ud.rev
    val lib = app.library
    val entrance = rememberEntrance()
    Column(Modifier.fillMaxSize()) {
        NewPlaylistRow(nav, withField = false)
        // two columns on any phone (GridFit): a 320 dp phone had one column, the New playlist tile alone filling the screen
        BoxWithConstraints(Modifier.fillMaxWidth().weight(1f)) {
        LazyVerticalGrid(GridCells.Fixed(GridFit.columns(maxWidth.value)), Modifier.fillMaxSize(), contentPadding = PaddingValues(12.dp, 4.dp, 12.dp, 24.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            item(key = "new") { Box(Modifier.entranceRise(entrance, 0)) { NewPlaylistTile() } }
            item(key = "favs") {
                PlaylistCard("Favourites", songCount(ud.favorites.size), null, Modifier.entranceRise(entrance, 1), heart = true) { nav.push(Screen.Detail(DetailKind.FAVORITES)) }
            }
            gridItemsIndexed(shownPlaylists(app), key = { _, p -> p.id }) { i, p ->
                val by = lib.byPath()
                val keys = p.paths.asSequence().take(20).mapNotNull { (by[it] ?: lib.resolve(it, ud.meta[it]))?.artKey }.toList()
                // a cover already on disk, else one a server can supply; a local key with no file behind it shows nothing
                val first = keys.firstOrNull { app.art.has(it) } ?: keys.firstOrNull { it.startsWith("jf") || it.startsWith("px") || it.startsWith("it") }
                PlaylistCard(p.name, songCount(p.paths.size), first, Modifier.entranceRise(entrance, i + 2), onLong = { nav.sheet = playlistSheet(app, nav, p) },
                    mark = { DownloadedListMark(p.paths) }) { nav.push(Screen.Detail(DetailKind.USER, p.id)) }
            }
            items(shownFolderPlaylists(app), key = { "f" + it.name + "|" + (it.tracks.firstOrNull()?.path ?: "") }) { g ->
                PlaylistCard(g.name, songCount(g.tracks.size), g.artKey, mark = { DownloadedListMark(remember(g) { g.tracks.map { it.path } }) }) { nav.push(Screen.Detail(DetailKind.FOLDER, g.name)) }
            }
        }
        }
    }
}

@Composable
private fun PlaylistsList(nav: PlayerNav) {
    if (LocalStyle.current.modern) { PlaylistsGrid(nav); return }
    val app = LocalApp.current
    val sc = LocalScheme.current
    val ud = app.userData
    ud.rev
    val lib = app.library
    LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 24.dp)) {
        item {
            if (LocalStyle.current.modern) NewPlaylistRow(nav)
            else IpodRow({ nav.nameDialog = { ud.createPlaylist(it) } }, height = 52.dp, leading = { IconTile(Glyph.PLUS, size = 44.dp) }) { hi ->
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
                trailing = { Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) { DownloadedListMark(p.paths); CountChevron(p.paths.size) } }) { hi ->
                Txt(p.name, size = 17f, weight = FontWeight.Medium, color = if (hi) Color.White else sc.onBg)
            }
        }
        items(shownFolderPlaylists(app), key = { "f" + it.name + "|" + (it.tracks.firstOrNull()?.path ?: "") }) { g ->
            IpodRow({ nav.push(Screen.Detail(DetailKind.FOLDER, g.name)) }, height = 56.dp,
                leading = { ArtImage(g.artKey, Modifier.size(44.dp), thumb = true, corner = 10.dp) },
                trailing = { Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) { DownloadedListMark(remember(g) { g.tracks.map { it.path } }); CountChevron(g.tracks.size) } }) { hi ->
                Txt(g.name, size = 17f, weight = FontWeight.Medium, color = if (hi) Color.White else sc.onBg)
            }
        }
    }
}

/** What an empty list shows: the list itself, grey placeholders while the library is still loading, or "nothing here" once it has loaded (or a filter matches nothing). */
enum class ListLoad {
    LIST, PLACEHOLDERS, EMPTY;
    companion object { fun of(empty: Boolean, filtering: Boolean, loaded: Boolean) = if (!empty) LIST else if (!filtering && !loaded) PLACEHOLDERS else EMPTY }
}

/** Grey shapes where song rows will be, while the library loads (the web's Skeleton.razor): still, never shimmering, with the words for TalkBack. */
@Composable
fun LoadingRows(text: String = "Loading your library…") {
    val sc = LocalScheme.current
    val shade = sc.onBg.copy(alpha = .09f)
    Column(Modifier.fillMaxSize().padding(horizontal = 16.dp, vertical = 12.dp).semantics(mergeDescendants = true) { contentDescription = text }, verticalArrangement = Arrangement.spacedBy(14.dp)) {
        Txt(text.uppercase(), size = 11f, color = sc.onBgDim)
        repeat(8) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(14.dp)) {
                Box(Modifier.size(44.dp).clip(RoundedCornerShape(10.dp)).background(shade))
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Box(Modifier.fillMaxWidth(.62f).height(11.dp).clip(RoundedCornerShape(6.dp)).background(shade))
                    Box(Modifier.fillMaxWidth(.38f).height(11.dp).clip(RoundedCornerShape(6.dp)).background(shade.copy(alpha = .06f)))
                }
            }
        }
    }
}

@Composable
fun EmptyState(text: String) {
    val sc = LocalScheme.current
    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Column(Modifier.padding(horizontal = 32.dp), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(12.dp)) {
            GlyphIcon(Glyph.NOTE, Modifier.size(56.dp), sc.onBg.copy(alpha = .3f))
            Txt(text, size = 16f, color = sc.onBgDim, maxLines = 6, align = TextAlign.Center)
        }
    }
}

// ---- detail pages -------------------------------------------------------------------------------------------------

/** The contents of a [DetailScreen], kept between redraws. */
private class DetailPage { var filled = false; var title = ""; var subtitle = ""; var art: String? = null; var tracks: List<Track> = emptyList(); var userId: String? = null }

@Composable
private fun DetailScreen(d: Screen.Detail, nav: PlayerNav, snap: PlayerSnap) {
    LocalLibRev.current
    val app = LocalApp.current
    val lib = app.library
    val ud = app.userData
    ud.rev
    val libRev = LocalLibRev.current
    val by = lib.byPath()
    // what the page shows is worked out once per change of the library, the playlists or the downloads, not on every redraw (the page redraws with the
    // player; an album page searched every album each time)
    val page = remember(d.kind, d.id, libRev, ud.rev) { DetailPage() }
    var title by page::title; var subtitle by page::subtitle; var art by page::art
    val mix = if (d.kind == DetailKind.MIX) remember(d.id, libRev) { com.ipodemu.library.Recommender.find(d.id, lib, ud) } else null
    var tracks by page::tracks
    var userId by page::userId
    // a hand-made playlist's songs, worked out once per change of the library or the playlists (not on every redraw), with the ones this library hasn't got
    val userSongs = remember(d.kind, d.id, libRev, ud.rev) {
        val pl = if (d.kind == DetailKind.USER) ud.playlists.firstOrNull { it.id == d.id } else null
        if (pl == null) emptyList<Track>() to emptyList<Pair<String, String>>()
        else ud.visiblePaths(pl.id).mapNotNull { p -> lib.resolve(p, ud.meta[p]) } to
            (if (lib.loaded) pl.paths.filter { lib.resolve(it, ud.meta[it]) == null }.mapNotNull { ud.meta[it] }.filter { it.first.isNotBlank() } else emptyList())
    }
    if (!page.filled) { page.filled = true; when (d.kind) {
        DetailKind.ALBUM -> lib.albums().firstOrNull { it.tracks.firstOrNull()?.albumKey == d.id }?.let { title = it.name; tracks = it.tracks; art = it.artKey; subtitle = it.tracks.firstOrNull()?.artist ?: "" }
        DetailKind.ARTIST -> lib.artists().firstOrNull { it.name == d.id }?.let { title = it.name; tracks = it.tracks; art = it.artKey }
        DetailKind.GENRE -> lib.genres().firstOrNull { it.name == d.id }?.let { title = it.name; tracks = it.tracks; art = it.artKey }
        DetailKind.FOLDER -> lib.playlists().firstOrNull { it.name == d.id }?.let { title = it.name; tracks = it.tracks; art = it.artKey }
        DetailKind.USER -> ud.playlists.firstOrNull { it.id == d.id }?.let { title = it.name; tracks = userSongs.first; art = tracks.firstNotNullOfOrNull { t -> t.artKey }; userId = it.id }
        DetailKind.FAVORITES -> { title = "Favourites"; tracks = ud.favorites.mapNotNull { lib.resolve(it, ud.meta[it]) }.distinctBy { it.path }; art = tracks.firstNotNullOfOrNull { it.artKey } }
        DetailKind.RECENT -> { title = "Recently Played"; tracks = ud.recents.mapNotNull { by[it] ?: lib.resolve(it, ud.meta[it]) }; art = tracks.firstNotNullOfOrNull { it.artKey } }
        DetailKind.DOWNLOADS -> { title = "Recently Added"; tracks = recentDownloads(app); art = tracks.firstNotNullOfOrNull { it.artKey } }
        DetailKind.MIX -> mix?.let { title = it.title; subtitle = it.subtitle; tracks = it.tracks; art = it.artKey }
    } }
    BackdropArt(nav, art)
    // the small mono label over the serif title (design I), saying what kind of page this is
    val kindLabel = when (d.kind) {
        DetailKind.ALBUM -> "Album"; DetailKind.ARTIST -> "Artist"; DetailKind.GENRE -> "Genre"; DetailKind.FOLDER, DetailKind.USER -> "Playlist"
        DetailKind.FAVORITES -> "Your favourites"; DetailKind.RECENT -> "History"; DetailKind.DOWNLOADS -> "New in your library"; DetailKind.MIX -> "Mix"
    }
    val total = tracks.sumOf { it.durationMs }
    // the web's facts line: the year (an album's), how many songs, how long, and the file formats in it ("2020 · 8 songs · 16 min · FLAC / MP3")
    val info = DetailFacts.line(if (d.kind == DetailKind.ALBUM) tracks.firstOrNull()?.year ?: 0 else 0, tracks.size, total,
        if (d.kind == DetailKind.ALBUM || d.kind == DetailKind.ARTIST) tracks.map { it.filePath.ifEmpty { it.path } } else emptyList())
    // a hand-made playlist's key for the DJ's per-playlist switch: its Jellyfin id when it is mirrored there (the web and Windows know it by that), else the app's own
    val djKey = userId?.let { id -> ud.playlists.firstOrNull { it.id == id }?.let { it.jfId ?: it.id } }
    androidx.compose.runtime.DisposableEffect(title, djKey) {
        nav.playContext = title.ifEmpty { null }; nav.playContextId = djKey?.let { title to it }
        onDispose { if (nav.playContext == title) nav.playContext = null; if (nav.playContextId?.first == title) nav.playContextId = null }
    }
    val extra = if (userId != null) listOf(SheetItem("Remove from playlist", Glyph.CLOSE) { }) else emptyList()
    val missing = if (userId != null) userSongs.second else emptyList()
    // kept here, outside the list, so the cover and title rise in once as the page opens and not again when scrolled back to
    val entrance = rememberEntrance()
    Column(Modifier.fillMaxSize()) {
        val heroPage = LocalStyle.current.modern && (d.kind == DetailKind.ALBUM || d.kind == DetailKind.ARTIST || d.kind == DetailKind.USER || d.kind == DetailKind.FOLDER)
        TopBar(if (heroPage) "" else title.ifEmpty { "Playlist" }, nav, showBack = true)
        if (tracks.isEmpty() && missing.isEmpty()) {
            // an emptied playlist keeps its Play / Rename / Delete buttons (the only way to rename or delete it from its own page)
            if (userId != null) Column(Modifier.padding(16.dp).verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(14.dp)) { DetailHeader(art, title, subtitle, info, tracks, nav, userId, kindLabel, djKey, since = entrance) }
            Box(Modifier.weight(1f).fillMaxWidth()) { EmptyState(when (d.kind) {
                DetailKind.FAVORITES -> "Tap the heart on a song to add it here"
                DetailKind.RECENT -> "Songs you play show up here"
                DetailKind.DOWNLOADS -> "New music you get shows up here. Search for something you don't have yet to get it"
                else -> "Nothing in this playlist yet. Use a song's menu and pick Add to playlist"
            }) }
            return@Column
        }
        // "Recommended songs" below the songs, only on the person's own playlists (PlaylistRecsUi.kt)
        val recs: (@Composable () -> Unit)? = userId?.takeIf { d.kind == DetailKind.USER }?.let { id -> @Composable { PlaylistRecsSection(id, tracks, title) } }
        BoxWithConstraints(Modifier.fillMaxSize()) {
            // two panes from 560 dp (a landscape phone beside the rail), the header's column scrolling: it was cut off at the bottom (DetailFit)
            val wide = DetailFit.twoPane(maxWidth.value, maxHeight.value)
            if (wide) {
                Row(Modifier.fillMaxSize()) {
                    Column(Modifier.width(DetailFit.sideWidth(maxWidth.value).dp).fillMaxHeight().verticalScroll(rememberScrollState()).padding(20.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
                        DetailHeader(art, title, subtitle, info, tracks, nav, userId, kindLabel, djKey, since = entrance)
                    }
                    SongList(tracks, nav, snap, showArt = false, numbered = true, sheetExtraFor = userId?.let { id -> { t: Track -> playlistSongItems(app, id, t) } }, missing = missing, footer = recs)
                }
            } else {
                SongList(tracks, nav, snap, showArt = false, numbered = true, sheetExtraFor = userId?.let { id -> { t: Track -> playlistSongItems(app, id, t) } }, missing = missing, footer = recs, header = {
                    Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
                        DetailHeader(art, title, subtitle, info, tracks, nav, userId, kindLabel, djKey, since = entrance)
                    }
                })
            }
        }
    }
}


/** A song from the online catalog before it is downloaded (owner, 6 Nov): the big cover, its whole title, artist, album, year and length, Download (or Play
 *  once it is here), and its other versions, each opening its own page. Nothing downloads until Download is pressed. */
@Composable
private fun CatalogSongScreen(d: Screen.CatalogSong, nav: PlayerNav, snap: PlayerSnap) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val s = d.song
    val libRev = rememberLibRev(app.library)
    val owned = remember(libRev) { app.library.songs().firstOrNull { it.matchKey == com.ipodemu.library.matchKey(s.title, s.artist) } }
    val status = remember(libRev) { app.library.downloads[app.library.songKey(s.artist, s.title)] }
    var year by remember(s) { mutableStateOf(s.year) }
    LaunchedEffect(s) { if (year.isBlank()) year = withContext(Dispatchers.IO) { try { WebCatalog.yearOf(s) } catch (_: Exception) { "" } } }
    Column(Modifier.fillMaxSize()) {
        TopBar("", nav, showBack = true)
        LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 24.dp)) {
            item(key = "head") {
                Column(Modifier.fillMaxWidth().padding(horizontal = 24.dp), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    // on its side the cover leaves Download on the first screen (DetailFit); the whole title, up to three lines
                    val cfg = androidx.compose.ui.platform.LocalConfiguration.current
                    RemoteArtImage(s.artUrl, Modifier.fillMaxWidth(.8f).widthIn(max = DetailFit.coverCap(cfg.screenWidthDp.toFloat(), cfg.screenHeightDp.toFloat(), 340f).dp).aspectRatio(1f), corner = 18.dp)
                    Txt(s.title, Modifier.padding(top = 10.dp), size = 22f, weight = FontWeight.Bold, color = sc.onBg, maxLines = 3, align = androidx.compose.ui.text.style.TextAlign.Center)
                    Txt(s.artist, size = 16f, color = sc.onBg, maxLines = 2, align = androidx.compose.ui.text.style.TextAlign.Center)
                    val facts = listOfNotNull(s.album.ifBlank { null }, year.ifBlank { null }, if (s.durationMs > 0) "%d:%02d".format(s.durationMs / 60000, s.durationMs / 1000 % 60) else null)
                    if (facts.isNotEmpty()) Txt(facts.joinToString(" · "), size = 14f, color = sc.onBgDim, align = androidx.compose.ui.text.style.TextAlign.Center)
                    val busy = status != null && status.stage != DownloadStage.DONE && status.stage != DownloadStage.FAILED
                    Box(Modifier.padding(top = 8.dp)) {
                        when {
                            owned != null -> GlossPill("Play", { app.player.play(listOf(owned), 0, null, from = "search") }, height = 40.dp)
                            busy -> Txt(com.ipodemu.library.DownloadTags.runningText(com.ipodemu.library.DownloadTags.stageText(status!!.stage, status.message)), size = 14f, color = sc.onBgDim)
                            else -> GlossPill(if (status?.stage == DownloadStage.FAILED) "Retry download" else "Download", {
                                app.library.requestDownload(s.artist, s.title, s.album, s.durationMs, artUrl = s.artUrl); app.ui.say("Getting \"${s.title}\"…")
                            }, icon = Glyph.DOWNLOADED, height = 40.dp)
                        }
                    }
                }
            }
            if (d.versions.isNotEmpty()) {
                item { SectionHeader("Other versions") }
                items(d.versions, key = { "v" + it.artist + it.title + it.album }) { v ->
                    val vOwned = app.library.songs().firstOrNull { it.matchKey == com.ipodemu.library.matchKey(v.title, v.artist) }
                    val vStatus = app.library.downloads[app.library.songKey(v.artist, v.title)]
                    WebRow(v.title, "${v.artist} · ${v.album}", v.artUrl, vOwned, vStatus,
                        onPlay = { vOwned?.let { app.player.play(listOf(it), 0, null, from = "search") } },
                        onDownload = { app.library.requestDownload(v.artist, v.title, v.album, v.durationMs, artUrl = v.artUrl); app.ui.say("Getting \"${v.title}\"…") },
                        onOpen = { nav.push(Screen.CatalogSong(v, listOf(s) + d.versions.filter { it != v })) })
                }
            }
        }
    }
}

@Composable
private fun CatalogAlbumScreen(d: Screen.CatalogAlbum, nav: PlayerNav, snap: PlayerSnap) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val a = d.album
    val libRev = rememberLibRev(app.library)
    val status = remember(libRev) { app.library.downloads[app.library.albumKey(a)] }
    var tracks by remember(a.id) { mutableStateOf<List<WebCatalog.Song>?>(null) }
    var year by remember(a.id) { mutableStateOf(a.year) }
    LaunchedEffect(a.id) {
        tracks = withContext(Dispatchers.IO) { try { WebCatalog.tracks(a) } catch (_: Exception) { emptyList() } }
        // the catalogue leaves some albums' year out: its first song's year then
        if (year.isBlank()) tracks?.firstOrNull()?.let { s -> year = withContext(Dispatchers.IO) { try { s.year.ifBlank { WebCatalog.yearOf(s) } } catch (_: Exception) { "" } } }
    }
    Column(Modifier.fillMaxSize()) {
        TopBar("", nav, showBack = true)
        LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 24.dp)) {
            item(key = "head") {
                Column(Modifier.fillMaxWidth().padding(horizontal = 24.dp), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    val cfg = androidx.compose.ui.platform.LocalConfiguration.current
                    RemoteArtImage(a.artUrl, Modifier.fillMaxWidth(.8f).widthIn(max = DetailFit.coverCap(cfg.screenWidthDp.toFloat(), cfg.screenHeightDp.toFloat(), 340f).dp).aspectRatio(1f), corner = 18.dp)
                    Txt(a.cleanTitle, Modifier.padding(top = 10.dp), size = 22f, weight = FontWeight.Bold, color = sc.onBg, maxLines = 3, align = androidx.compose.ui.text.style.TextAlign.Center)
                    Txt(a.artist, size = 16f, color = sc.onBg, maxLines = 2, align = androidx.compose.ui.text.style.TextAlign.Center)
                    val n = tracks?.size?.takeIf { it > 0 } ?: a.trackCount
                    Txt(listOfNotNull(a.kind, year.ifBlank { null }, if (n > 0) "$n song${if (n == 1) "" else "s"}" else null).joinToString(" · "), size = 14f, color = sc.onBgDim)
                    val busy = status != null && status.stage != DownloadStage.DONE && status.stage != DownloadStage.FAILED
                    Box(Modifier.padding(top = 8.dp)) {
                        if (busy) Txt(status!!.message, size = 14f, color = sc.onBgDim)
                        else GlossPill(if (status?.stage == DownloadStage.FAILED) "Retry download" else "Download ${a.kind.lowercase()}", {
                            app.library.requestAlbum(a); app.ui.say("Getting \"${a.cleanTitle}\"…")
                        }, icon = Glyph.DOWNLOADED, height = 40.dp)
                    }
                }
            }
            val list = tracks
            if (list == null) item { Txt("Loading the songs...", Modifier.padding(24.dp), size = 14f, color = sc.onBgDim) }
            else {
                item { SectionHeader("Songs") }
                items(list, key = { "t" + it.trackNo + it.discNo + it.title }) { s ->
                    // each song opens its own page (with its own Download), never downloads from a tap
                    IpodRow({ nav.push(Screen.CatalogSong(s, emptyList())) }) {
                        Column(Modifier.padding(horizontal = 16.dp)) {
                            Txt(s.title, size = 15f, color = sc.onBg, maxLines = 1)
                            if (s.durationMs > 0) Txt("%d:%02d".format(s.durationMs / 60000, s.durationMs / 1000 % 60), size = 12f, color = sc.onBgDim)
                        }
                    }
                }
            }
        }
    }
}

/** A ready-made playlist (owner, 2 Nov): its songs as this phone has them, Play, Shuffle and "Add to my playlists". */
@Composable
private fun MadeScreen(d: Screen.Made, nav: PlayerNav, snap: PlayerSnap) {
    val app = LocalApp.current
    val lib = app.library
    val libRev = rememberLibRev(lib)
    var full by remember(d.id) { mutableStateOf(com.ipodemu.library.MadeLists.opened[d.id]) }
    var failed by remember(d.id) { mutableStateOf(false) }
    var saving by remember(d.id) { mutableStateOf(false) }
    val madeScope = androidx.compose.runtime.rememberCoroutineScope()
    var added by remember(d.id) { mutableStateOf(d.id in com.ipodemu.library.MadeLists.saved) }
    LaunchedEffect(d.id) {
        val web = com.ipodemu.library.FlacieWebClient(app.prefs)
        try { full = withContext(Dispatchers.IO) { web.made(d.id) }.also { com.ipodemu.library.MadeLists.opened[d.id] = it }; failed = false }
        catch (e: Exception) { android.util.Log.w("FLACie", "made list failed: ${e.message}"); failed = full == null }
    }
    val tracks = remember(full, libRev) { full?.tracks.orEmpty().mapNotNull { p -> lib.resolve(p.path ?: "", p.title to p.artist) }.distinctBy { it.path } }
    // a list from outside (Spotify's, Deezer's...): the songs the phone lacks, greyed with Download, as an imported playlist shows them
    val missing = remember(full, libRev) { full?.let { f -> com.ipodemu.library.MadeLists.missing(f) { p -> lib.resolve(p.path ?: "", p.title to p.artist) != null } } ?: emptyList() }
    val outside = com.ipodemu.library.MadeLists.isOutside(d.id)
    val art = tracks.firstNotNullOfOrNull { it.artKey }
    BackdropArt(nav, art)
    val title = full?.name ?: d.name
    val info = DetailFacts.line(0, tracks.size, tracks.sumOf { it.durationMs }, emptyList())
    Column(Modifier.fillMaxSize()) {
        TopBar(if (LocalStyle.current.modern) "" else title, nav, showBack = true)
        if (tracks.isEmpty() && missing.isEmpty()) {
            Box(Modifier.weight(1f).fillMaxWidth()) { EmptyState(if (failed) "Couldn't load this playlist. Check the connection and try again" else if (full == null) "Loading..." else "None of this playlist's songs are on this phone yet") }
            return@Column
        }
        SongList(tracks, nav, snap, showArt = false, numbered = true, missing = missing, header = {
            Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
                DetailHeader(art, title, full?.subtitle.orEmpty(), info, tracks, nav, null, if (outside) "Playlist · ${com.ipodemu.library.MadeLists.source(d.id)}" else "Made for you", null)
                DesignChip(if (added) "Added to my playlists" else if (saving) "Adding..." else "Add to my playlists", added, {
                    if (!added && !saving) {
                        saving = true
                        madeScope.launch {
                            try {
                                withContext(Dispatchers.IO) { com.ipodemu.library.FlacieWebClient(app.prefs).saveMade(d.id) }
                                com.ipodemu.library.MadeLists.saved.add(d.id); added = true
                                // the server wrote it to the account; a sync brings it into the phone's playlists, as an import does
                                app.account.sync()
                                app.ui.say("Added \"$title\" to your playlists")
                            } catch (e: Exception) { app.ui.say(com.ipodemu.library.FriendlyError.of(e, "Couldn't add the playlist"), warn = true) }
                            saving = false
                        }
                    }
                }, icon = if (added) Glyph.CHECK else Glyph.PLUS)
            }
        })
    }
}

@Composable
private fun DetailHeader(art: String?, title: String, line1: String, line2: String, tracks: List<Track>, nav: PlayerNav, playlistId: String? = null, kind: String = "", djKey: String? = null, since: Long = 0L) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    if (LocalStyle.current.modern) {
        // design I (the web's detail-album): the cover large with a soft shadow, a DM Mono kind label, the serif title with its last word in
        // italics, the artist, a mono facts line, then Play in the cover's colour and Shuffle tinted with its tone
        val cols = rememberCoverColours(art)
        // on its side the cover is at most 45% of the height, so Play and Shuffle are on the first screen; the title stays under 50 sp as drawn (DetailFit)
        val cfg = androidx.compose.ui.platform.LocalConfiguration.current
        val cap = DetailFit.coverCap(cfg.screenWidthDp.toFloat(), cfg.screenHeightDp.toFloat())
        // an artist gets a round picture, as on the web; everything else the rounded square cover. The cover, the words and the buttons rise in
        // one after another as the page opens ([since]: when it opened; 0 leaves them still)
        if (kind == "Artist") ArtImage(art, Modifier.entranceRise(since, 0).fillMaxWidth(.62f).widthIn(max = (cap * .87f).dp).aspectRatio(1f).coverShadow(200.dp, 18.dp), corner = 200.dp, circle = true)
        else ArtImage(art, Modifier.entranceRise(since, 0).fillMaxWidth(.7f).widthIn(max = cap.dp).aspectRatio(1f).coverShadow(22.dp, 18.dp), corner = 22.dp)
        Column(Modifier.entranceRise(since, 2), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            if (kind.isNotEmpty()) MonoLabel(kind)
            SerifTitle(title, size = DetailFit.titleSp(title.length > SleeveText.LONG_TITLE, cfg.fontScale), maxLines = 3)
            if (line1.isNotEmpty()) Txt(line1, size = 16f, weight = FontWeight.Bold, maxLines = 2)
            if (line2.isNotEmpty()) MonoLabel(line2, Modifier.padding(top = 2.dp), maxLines = 2)
        }
        // Play is the list in order and Shuffle is shuffled, whatever the shuffle switch was (two buttons, two meanings); they wrap at big text
        @OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
        androidx.compose.foundation.layout.FlowRow(Modifier.entranceRise(since, 4), horizontalArrangement = Arrangement.spacedBy(10.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            if (tracks.isNotEmpty()) AccentPill("Play", Glyph.PLAY, cols, { app.player.play(tracks, 0, false, title, djKey) })
            if (tracks.isNotEmpty()) TonePill("Shuffle", Glyph.SHUFFLE, cols, { app.player.play(tracks, tracks.indices.random(), true, title, djKey) })
        }
    } else {
    Row(horizontalArrangement = Arrangement.spacedBy(14.dp), verticalAlignment = Alignment.CenterVertically) {
        ArtImage(art, Modifier.size(104.dp), corner = 12.dp)
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(3.dp)) {
            Txt(title, size = 20f, weight = FontWeight.Bold, maxLines = 2)
            if (line1.isNotEmpty()) Txt(line1, size = 13f, color = sc.onBgDim)
            if (line2.isNotEmpty()) Txt(line2, size = 12f, color = sc.onBgDim)
        }
    }
    // below the art row rather than beside the title, so both buttons fit in the narrow side column of wide layouts; they wrap at big text
    @OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
    androidx.compose.foundation.layout.FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        GlossPill("Play", { app.player.play(tracks, 0, false, title, djKey) }, icon = Glyph.PLAY, primary = true, height = 40.dp)
        GlossPill("Shuffle", { app.player.play(tracks, tracks.indices.random(), true, title, djKey) }, icon = Glyph.SHUFFLE, height = 40.dp)
    }
    }
    // an album or a playlist can be kept on the phone as a whole; a playlist then also keeps itself up to date
    if ((kind == "Album" || kind == "Playlist") && tracks.isNotEmpty()) KeepOfflineChip(tracks, nav, if (kind == "Playlist") (playlistId ?: "folder:$title") else null, title)
    // Favourites: Clear all, asked first, with Undo (other devices follow: each removal is recorded)
    if (kind == "Your favourites" && tracks.isNotEmpty()) {
        val app = LocalApp.current
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            DesignChip("Clear all", false, {
                nav.sheet = SheetSpec("Clear all favourites?", "${tracks.size} songs. Every device you're signed in on follows.", listOf(
                    SheetItem("Yes, clear them", Glyph.CLOSE) {
                        val was = app.userData.clearFavorites()
                        app.ui.say("Cleared ${was.size} favourites", "Undo") { app.userData.restoreFavorites(was) }
                    },
                    SheetItem("Keep them", Glyph.CHECK) { },
                ))
            })
        }
    }
    if (playlistId != null) {
        // the playlist's own actions behind one More button (songs have Move and Remove in their menu): Rename used to sit where a second tap on the
        // playlist landed while its page opened, and renamed it by accident (owner, 1 Nov)
        DesignChip("More", false, {
            nav.sheet = SheetSpec(title, "Playlist", listOf(
                SheetItem("Share", Glyph.LIST) { shareSongs(app, "playlist", title, tracks) },
                SheetItem("Rename", Glyph.NOTE) { nav.nameDialogRename = title; nav.nameDialog = { n -> app.userData.rename(playlistId, n); app.ui.say("Renamed to \"$n\"") } },
                SheetItem("Remove duplicates", Glyph.CHECK) {
                    val ud = app.userData
                    val n = ud.removeDuplicates(playlistId) { p -> app.library.resolve(p, ud.meta[p])?.matchKey ?: p }
                    app.ui.say(if (n == 0) "No duplicates in this playlist" else "Removed $n duplicate${if (n == 1) "" else "s"}", warn = n == 0)
                },
                SheetItem("Delete playlist", Glyph.CLOSE) { nav.sheet = SheetSpec("Delete this playlist?", null, listOf(SheetItem("Delete", Glyph.CLOSE) { app.userData.deletePlaylist(playlistId); nav.pop() }, SheetItem("Cancel", Glyph.BACK) {})) },
            ))
        }, icon = Glyph.MORE)
        if (djKey != null && app.player.djAvailable) PlaylistDjChip(djKey)
    }
}

/**
 * The AI DJ on this playlist, kept on the server for the account (the web and Windows follow): On (even with the DJ off), Off (quiet here for good,
 * a bedtime list), or Auto (no entry: the DJ's own settings decide). Taps go round Auto, On, Off. [key]: the Jellyfin playlist id, else the app's own.
 */
@Composable
private fun PlaylistDjChip(key: String) {
    val app = LocalApp.current
    val scope = androidx.compose.runtime.rememberCoroutineScope()
    val web = remember { com.ipodemu.library.FlacieWebClient(app.prefs) }
    var on by remember(key) { mutableStateOf(app.player.djSettings.playlists[key]) }
    var busy by remember { mutableStateOf(false) }
    // what the server has now (changed on the web, say), not only what this phone saw last
    LaunchedEffect(key) {
        val s = try { kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) { web.djSettings() } } catch (_: Exception) { null } ?: return@LaunchedEffect
        app.player.djSettingsFetched(s); if (!busy) on = s.playlists[key]
    }
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        DesignChip("DJ: " + when (on) { true -> "On"; false -> "Off"; null -> "Auto" }, on != null, {
            if (!busy) {
                val was = on; val next = when (was) { null -> true; true -> false; false -> null }
                on = next; busy = true
                scope.launch {
                    try {
                        val s = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) { web.setDjPlaylist(key, next) }
                        app.player.djSettingsChanged(s); on = s.playlists[key]
                        app.ui.say(when (on) { true -> "The DJ talks on this playlist"; false -> "The DJ stays quiet on this playlist"; null -> "This playlist follows your DJ settings" })
                    } catch (_: Exception) { on = was; app.ui.say("Couldn't reach FLACie Web", warn = true) } finally { busy = false }
                }
            }
        }, icon = Glyph.DJ)
    }
}

// ---- search -------------------------------------------------------------------------------------------------------

private class Results(val songs: List<Track>, val albums: List<Group>)

/** A small "you already have this" tag, so an owned result reads as obviously different from a
 * catalog hit at a glance without needing to compare sections. */
@Composable
private fun OwnedTag() {
    val sc = LocalScheme.current
    // a round pill in the sans, like every chip (owner, 9 Nov)
    Box(Modifier.clip(RoundedCornerShape(50)).background(Color(0xFF2E7D4F)).padding(horizontal = 9.dp, vertical = 3.dp)) {
        Txt("Owned", size = 11f, weight = FontWeight.SemiBold, color = Color.White)
    }
}

@Composable
private fun SongLinkCard(link: com.ipodemu.library.SongLinks.Song) {
    val app = LocalApp.current
    val scope = androidx.compose.runtime.rememberCoroutineScope()
    var said by remember(link.url) { mutableStateOf<String?>(null) }
    Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp).clip(RoundedCornerShape(18.dp)).background(LocalScheme.current.onBg.copy(alpha = .07f)).padding(14.dp),
        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
        Column(Modifier.weight(1f)) {
            Txt("A ${link.service} song", size = 16f, weight = FontWeight.Bold)
            Txt(said ?: link.url.removePrefix("https://"), size = 13f, color = LocalScheme.current.onBg.copy(alpha = .65f), maxLines = 3)
        }
        if (said == null) Box(Modifier.heightIn(min = 48.dp).clip(RoundedCornerShape(24.dp)).background(LocalScheme.current.accent)
            .semantics { role = androidx.compose.ui.semantics.Role.Button }.clickable {
            said = "Asking FLACie Web…"
            scope.launch {
                said = try {
                    kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) { com.ipodemu.library.FlacieWebClient(app.prefs).startLink(link.url) }
                    "Getting it on FLACie Web: it joins your library when it's done."
                } catch (e: com.ipodemu.library.FlacieWebClient.Refused) { e.message ?: "FLACie Web said no" }
                catch (e: Exception) { "Couldn't reach FLACie Web. Try again in a moment." }
            }
        }.padding(horizontal = 18.dp, vertical = 12.dp), contentAlignment = Alignment.Center) {
            Txt("Get this song", size = 14f, weight = FontWeight.Bold, color = Color(DesignColour.readableOn(LocalScheme.current.accent.toArgb())))
        }
    }
}

@Composable
private fun SearchScreen(nav: PlayerNav, snap: PlayerSnap) {
    val appR = LocalApp.current
    // remembered once the person stops typing for a moment
    LaunchedEffect(appR.ui.searchQuery) { val q = appR.ui.searchQuery.trim(); if (q.length >= 2) { delay(2500); appR.prefs.recentSearches = RecentSearches.add(appR.prefs.recentSearches, q) } }
    val app = LocalApp.current
    val libRev = rememberLibRev(app.library)
    // the focused text field used to swallow the first Back press; leave the screen straight away
    BackHandler(enabled = !app.ui.pickerOpen && nav.sheet == null && nav.nameDialog == null && nav.stack.size > 1) { nav.pop() }
    val sc = LocalScheme.current
    var query by app.ui::searchQuery
    var results by remember { mutableStateOf(Results(emptyList(), emptyList())) }
    var web by remember { mutableStateOf(WebResults(emptyList(), emptyList())) }
    var webLoading by remember { mutableStateOf(false) }
    // "Playlists for you": the ready-made lists matching the query (owner, 2 Nov)
    var made by remember { mutableStateOf<List<com.ipodemu.library.MadeLists.Summary>>(emptyList()) }
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
        // the covers of the first results are asked for at once, as rows on screen are, before the rows are drawn
        val art = app.library.art
        (results.songs.take(10).map { it.artKey } + results.albums.take(6).map { it.artKey }).filterNotNull().distinct()
            .forEach { art.prefetch(it, priority = com.ipodemu.library.Priority.VISIBLE) }
    }
    // Songs and albums that aren't owned yet, most popular first (iTunes ranks by popularity, so a hit song comes before
    // anyone else's track with the same name). Debounced longer than the local filter: these are network calls.
    LaunchedEffect(query) {
        val q = query.trim()
        // guests have no servers or downloads, so the online catalog has nothing to offer them
        if (q.isEmpty() || app.ui.guest) { web = WebResults(emptyList(), emptyList()); webLoading = false; return@LaunchedEffect }
        delay(450)
        webLoading = true
        // one row a song, its versions under it (owner, 6 Nov: "filter out the 30 versions and list like Spotify would")
        val groups = withContext(Dispatchers.IO) { WebCatalog.songGroups(q) }
        val songs = groups.map { it.first }
        val versions = groups.associate { versionKey(it.first) to it.second }
        // their covers start now, before the rows are drawn (owner, 6 Nov: covers came slowly in search)
        RemoteArt.prefetch(app, songs.take(12).mapNotNull { it.artUrl })
        web = WebResults(songs, emptyList(), versions)
        val albums = withContext(Dispatchers.IO) { WebCatalog.albumsWithTop(q, songs.firstOrNull()) }
        RemoteArt.prefetch(app, albums.take(8).mapNotNull { it.artUrl })
        web = WebResults(songs, albums, versions)
        webLoading = false
    }
    LaunchedEffect(query) {
        val q = query.trim()
        if (app.ui.guest || !com.ipodemu.library.MadeLists.worthAsking(q)) { made = emptyList(); return@LaunchedEffect }
        val web = com.ipodemu.library.FlacieWebClient(app.prefs)
        if (!web.available) { made = emptyList(); return@LaunchedEffect }
        // the lists saved on FLACie Web first, at once (owner, 9 Nov: not after the online look-up), then the full answer once typing settles
        delay(150)
        val saved = withContext(Dispatchers.IO) { try { web.madeSearch(q, quick = true) } catch (e: Exception) { emptyList() } }
        if (saved.isNotEmpty()) { made = saved; RemoteArt.prefetch(app, saved.mapNotNull { it.picture }) }
        delay(350)
        val full = withContext(Dispatchers.IO) { try { web.madeSearch(q) } catch (e: Exception) { android.util.Log.w("FLACie", "made search failed: ${e.message}"); null } }
        if (full != null) made = full
        RemoteArt.prefetch(app, made.mapNotNull { it.picture })
    }
    Column(Modifier.fillMaxSize().imePadding()) {
        // the big title goes while typing, so more results fit above the keyboard
        val imeUp = androidx.compose.foundation.layout.WindowInsets.ime.getBottom(androidx.compose.ui.platform.LocalDensity.current) > 0
        // a short screen (the 360 dp RG Rotate) drops the big title when Search is a tab of its own, so the field and the results get the room
        val shortNoTitle = androidx.compose.ui.platform.LocalConfiguration.current.screenHeightDp < 500 && nav.stack.size <= 1
        if (shortNoTitle) Spacer(Modifier.height(4.dp))
        else if (!imeUp) TopBar("Search your music", nav, showBack = true)
        else Spacer(Modifier.windowInsetsTopHeight(androidx.compose.foundation.layout.WindowInsets.statusBars))
        SearchPill(query, { query = it }, "Songs, albums, artists", Modifier.padding(16.dp), shape = RoundedCornerShape(50), background = Color(0x26FFFFFF), borderColor = sc.cardBorder,
            height = 46.dp, textSize = 16f, onSubmit = { searchFocus.clearFocus() }, trailing = {
                if (query.isNotEmpty()) IconAction(Glyph.CLOSE, "Clear search", { query = "" }, tint = sc.onBgDim, size = 44.dp, iconScale = 0.45f)
                // speak to search; a request to the DJ ("play my pop playlist") is carried out instead
                else DjMicButton(size = 38.dp, tint = sc.onBg, glass = Color.Transparent, onSearch = { query = it }, touch = 46.dp)
            })
        val online = rememberOnline()
        if (!online) OfflineChip(Modifier.padding(start = 16.dp, end = 16.dp, bottom = 8.dp), "Offline: searching only the music on this device")
        val r = results
        val w = web
        val q = query.trim()
        // owned copies of web hits: a song you have plays instead of offering a download
        // a copy per library change, so rows recompose as download progress arrives
        val dl = remember(libRev) { HashMap(app.library.downloads) }
        val ownedSongs = remember(libRev) { app.library.songs().associateBy { com.ipodemu.library.matchKey(it.title, it.artist) } }
        val ownedAlbums = remember(libRev) { app.library.albums().associateBy { ownedAlbumKey(it.tracks.firstOrNull()?.artist.orEmpty(), it.name) } }
        val nothingAtAll = made.isEmpty() && r.songs.isEmpty() && r.albums.isEmpty() && w.songs.isEmpty() && w.albums.isEmpty() && !webLoading
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
        // F6.3: a search naming an artist is laid out like their page (Top result with their top 5 songs, then Popular, Albums, Singles & EPs, Playlists, similar names)
        val artistTop = remember(q, libRev) { if (q.length < 2) null else com.ipodemu.library.SearchRankTop.artist(app.library.artists(), q, app.userData.plays) }
        val artistPage = remember(artistTop, libRev) { artistTop?.let { com.ipodemu.library.ArtistPage.of(it, app.library.albums(), app.userData.plays) } }
        val similarArtists = remember(artistTop, libRev) { if (artistTop == null) emptyList() else SearchArtists.similar(app.library.artists(), q, artistTop) }
        // "Lists with <artist> in": lists the server has already read that hold the artist's songs
        var featuring by remember(artistTop?.name) { mutableStateOf<List<com.ipodemu.library.MadeLists.Summary>>(emptyList()) }
        LaunchedEffect(artistTop?.name) {
            val name = artistTop?.name ?: return@LaunchedEffect
            if (app.ui.guest) return@LaunchedEffect
            delay(500)
            val c = com.ipodemu.library.FlacieWebClient(app.prefs)
            if (c.available) featuring = withContext(Dispatchers.IO) { try { c.featuring(name) } catch (e: Exception) { emptyList() } }
        }
        val restSongs = if (topSong != null) rankedSongs.drop(1) else rankedSongs
        val byPath = remember(libRev) { app.library.byPath() }
        val ownLists = remember(q, libRev, app.userData.rev) {
            com.ipodemu.library.PlaylistHits.find(shownPlaylists(app), { it.name }, { pl -> pl.paths.mapNotNull { byPath[it] }.map { com.ipodemu.library.PlaylistHits.Song(it.title, it.artist) } }, q)
        }
        var songsExpanded by remember(q) { mutableStateOf(false) }
        var albumsExpanded by remember(q) { mutableStateOf(false) }
        var webSongsExpanded by remember(q) { mutableStateOf(false) }
        var only by remember(q) { mutableStateOf("") }
        // the Playlists chip's lists (null while asking) and its genre and decade
        var listGenre by remember { mutableStateOf("") }
        var listDecade by remember { mutableStateOf("") }
        val listFilters = remember(libRev) { com.ipodemu.library.ListFilters.of(app.library.songs()) }
        var allLists by remember(q) { mutableStateOf<List<com.ipodemu.library.MadeLists.Summary>?>(null) }
        val webLists = !app.ui.guest && remember { com.ipodemu.library.FlacieWebClient(app.prefs) }.available
        LaunchedEffect(q, only, listGenre, listDecade) {
            if (only != "playlists" || !com.ipodemu.library.MadeLists.worthAsking(q)) return@LaunchedEffect
            allLists = null
            delay(250)
            allLists = withContext(Dispatchers.IO) {
                try { com.ipodemu.library.FlacieWebClient(app.prefs).madeSearch(q, "playlists", listGenre, listDecade) } catch (e: Exception) { android.util.Log.w("FLACie", "playlists filter failed: ${e.message}"); emptyList() }
            }
        }
        // the row keeps its scroll offset by item key; when a new result list arrives (or a download reshuffles it) it could stay scrolled
        // a few pixels, clipping the first card, so it snaps back to the start whenever the first album changes
        val albumRowState = androidx.compose.foundation.lazy.rememberLazyListState()
        androidx.compose.runtime.LaunchedEffect(q, w.albums.firstOrNull()?.id) { albumRowState.scrollToItem(0) }
        // Select: tick songs (a long press on one starts it, or the Select button) and Download them all at once. A song in the library is kept on the phone;
        // a catalogue result that is not in the library yet is asked for. Ids: "L" + path for a library song, "W" + artist and title for a catalogue one.
        var picks by remember { mutableStateOf<PickSet?>(null) }
        androidx.activity.compose.BackHandler(enabled = picks != null) { picks = null }
        val libSongs = if ((only == "songs" || picks != null) && topSong != null) listOf(topSong) + restSongs else restSongs
        fun webId(s: WebCatalog.Song) = ownedSongs[com.ipodemu.library.matchKey(s.title, s.artist)]?.let { "L" + it.path } ?: ("W" + s.artist + "|" + s.title)
        val tracksById = remember(libSongs, w.songs, ownedSongs) { HashMap<String, Track>().also { m -> libSongs.forEach { m["L" + it.path] = it }; w.songs.forEach { s -> ownedSongs[com.ipodemu.library.matchKey(s.title, s.artist)]?.let { m["L" + it.path] = it } } } }
        val webById = remember(w.songs) { w.songs.associateBy { "W" + it.artist + "|" + it.title } }
        val allIds = remember(libSongs, w.songs, ownedSongs) { (w.songs.map { webId(it) } + libSongs.map { "L" + it.path }).distinct() }
        if (query.isBlank()) SearchIdle(nav)
        else if (nothingAtAll) {
            Column(Modifier.fillMaxSize()) {
                Box(Modifier.weight(1f)) { EmptyState("No results") }
                if (!app.ui.guest) DownloadRequestBar(app, q)
            }
        }
        else Box(Modifier.fillMaxSize()) {
        LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = if (picks != null) 150.dp else 24.dp)) {
            // a SoundCloud or YouTube song link pasted into search (owner, 8 Nov): FLACie Web gets just that song
            com.ipodemu.library.SongLinks.of(query)?.let { link -> item(key = "songlink") { SongLinkCard(link) } }
            // Show only: All, Songs or Albums (as on the web), when there are both
            val hasSongs = w.songs.isNotEmpty() || rankedSongs.isNotEmpty(); val hasAlbums = w.albums.isNotEmpty() || r.albums.isNotEmpty()
            if (hasSongs && picks == null && only != "playlists") item(key = "select") {
                Row(Modifier.padding(horizontal = 16.dp, vertical = 2.dp)) { DesignChip("Select", false, { picks = PickSet() }, icon = Glyph.CHECK) }
            }
            // one choice, as on the web: All, Songs, Albums or Playlists (Playlists only with FLACie Web, which has them)
            if (picks == null && (hasSongs || hasAlbums || webLists)) item(key = "kinds") {
                Row(Modifier.horizontalScroll(rememberScrollState()).padding(horizontal = 16.dp, vertical = 4.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    val kinds = listOf("" to "All", "songs" to "Songs", "albums" to "Albums") + (if (webLists) listOf("playlists" to "Playlists") else emptyList())
                    for ((k, label) in kinds) DesignChip(label, only == k, { only = k })
                }
            }
            fun shows(kind: String) = only == "" || only == kind
            val artistMode = artistTop != null && artistPage != null && only == "" && picks == null
            // the person's own playlists holding what was typed, before anything recommended (owner, 7 Nov: "dreamer" should show the playlists Dreamer is in)
            if (ownLists.isNotEmpty() && picks == null && (only == "" || only == "playlists")) {
                item { SectionHeader("Your playlists") }
                item(key = "ownlists") {
                    androidx.compose.foundation.lazy.LazyRow(contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                        items(ownLists, key = { "own" + it.list.id }) { h ->
                            val pl = h.list
                            val cover = remember(pl.id, libRev) { pl.paths.firstNotNullOfOrNull { byPath[it]?.artKey } }
                            SearchCover(cover, pl.name, "Your playlist · " + (h.holds ?: "${pl.paths.size} ${if (pl.paths.size == 1) "song" else "songs"}"), "Open playlist ${pl.name}") {
                                searchFocus.clearFocus(); nav.push(Screen.Detail(DetailKind.USER, pl.id))
                            }
                        }
                    }
                }
            }
            // the web's Search playlists (6 Nov): ours, Spotify's, Deezer's and SoundCloud's, under the top of the results
            if (made.isNotEmpty() && only == "" && picks == null && !artistMode) {
                item { SectionHeader("Playlists") }
                item(key = "made") { ListRow(made, nav) { searchFocus.clearFocus() } }
            }
            // the Playlists chip (F26.3, one choice): every list that matches, narrowed by genre and decade as the web's Filters panel does
            if (only == "playlists") {
                item(key = "listfilters") {
                    Column(Modifier.padding(vertical = 4.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        Row(Modifier.horizontalScroll(rememberScrollState()).padding(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            for (g in listFilters.genres) DesignChip(g, listGenre == g, { listGenre = if (listGenre == g) "" else g })
                        }
                        Row(Modifier.horizontalScroll(rememberScrollState()).padding(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            for (d in listFilters.decades) DesignChip(d, listDecade == d, { listDecade = if (listDecade == d) "" else d })
                        }
                    }
                }
                val shownLists = allLists
                if (shownLists == null) item { Row(Modifier.fillMaxWidth().padding(16.dp), horizontalArrangement = Arrangement.Center) { Txt("Finding playlists...", size = 13f, color = sc.onBgDim) } }
                else if (shownLists.isEmpty()) item { Txt("No playlists for this yet.", Modifier.padding(16.dp), size = 14f, color = sc.onBgDim) }
                // two covers a row
                else items(shownLists.chunked(2), key = { row -> "pl" + row.first().id }) { row ->
                    Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 6.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                        for (m in row) ListCard(m, app, nav) { searchFocus.clearFocus() }
                    }
                }
            }
            // albums as one swipeable row of covers near the top, so they're visible with the keyboard up; a query naming an
            // album ("short n sweet") puts that row first, like YT Music
            val albumNamed = w.albums.firstOrNull()?.takeIf { it.kind != "Single" }?.let { a -> a.cleanTitle.lowercase().filter { it.isLetterOrDigit() } == q.lowercase().filter { it.isLetterOrDigit() } } == true
            val webAlbums: androidx.compose.foundation.lazy.LazyListScope.() -> Unit = {
                if (w.albums.isNotEmpty() && shows("albums")) {
                    item { SectionHeader("Albums and EPs") }
                    item(key = "walbums") {
                        androidx.compose.foundation.lazy.LazyRow(state = albumRowState, contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                            items(w.albums, key = { it.id }) { a ->
                                val owned = ownedAlbums[ownedAlbumKey(a.artist, a.cleanTitle)]
                                AlbumCard(a, owned != null, dl[app.library.albumKey(a)],
                                    onOpen = { searchFocus.clearFocus(); owned?.let { nav.push(Screen.Detail(DetailKind.ALBUM, it.tracks.first().albumKey)) } ?: nav.push(Screen.CatalogAlbum(a)) },
                                    onDownload = { app.library.requestAlbum(a); app.ui.say("Getting \"${a.cleanTitle}\"…") })
                            }
                        }
                    }
                }
            }
            if (albumNamed) webAlbums()
            if (artistMode && artistTop != null && artistPage != null) ArtistResults(artistTop, artistPage, made, similarArtists, nav, snap, featuring) { searchFocus.clearFocus() }
            if (topSong != null && only == "" && picks == null && !artistMode) {
                item { SectionHeader("Top result") }
                item(key = "top${topSong.path}") {
                    TopResultCard(topSong) { searchFocus.clearFocus(); if (snap.track?.path == topSong.path) { if (!snap.playing) app.player.toggle(); nav.nowPlaying = true } else { app.player.play(listOf(topSong), 0, null, from = "search") } }
                }
            }
            if (!albumNamed) webAlbums()
            if (w.songs.isNotEmpty() && shows("songs")) {
                item { SectionHeader("Songs") }
                val shown = if (webSongsExpanded || only == "songs" || picks != null) w.songs else w.songs.take(8)
                itemsIndexed(shown, key = { i, s -> "ws$i${s.artist}${s.title}" }) { _, s ->
                    val owned = ownedSongs[com.ipodemu.library.matchKey(s.title, s.artist)]
                    val status = dl[app.library.songKey(s.artist, s.title)]
                    val more = w.versions[versionKey(s)].orEmpty()
                    WebRow(s.title, status?.takeIf { it.stage != DownloadStage.DONE }?.let { st -> if (st.stage == DownloadStage.FAILED) st.message else com.ipodemu.library.DownloadTags.runningText(com.ipodemu.library.DownloadTags.stageText(st.stage, st.message)) }
                        ?: ("Song · ${s.artist} · ${s.album}" + if (more.isNotEmpty()) " · +${more.size} version${if (more.size == 1) "" else "s"}" else ""), s.artUrl, owned, status,
                        onOpen = { searchFocus.clearFocus(); nav.push(Screen.CatalogSong(s, more)) },
                        onPlay = { searchFocus.clearFocus(); owned?.let { if (snap.track?.path == it.path) { if (!snap.playing) app.player.toggle(); nav.nowPlaying = true } else { app.player.play(listOf(it), 0, null, from = "search") } } },
                        // said at once: on a weak signal the row's status can take a while to change, and the tap looked like it did nothing (owner, 1 Nov)
                        onDownload = { searchFocus.clearFocus(); app.library.requestDownload(s.artist, s.title, s.album, s.durationMs, artUrl = s.artUrl); app.ui.say("Getting \"${s.title}\"…") },
                        picked = picks?.contains(webId(s)), onPick = { picks = (picks ?: PickSet()).toggle(webId(s)) },
                        // the three-line menu: Download, Hi-Res (administrators, per song, after a confirmation: the files are several times the size), Find on YouTube
                        onHiRes = {
                            val items = ArrayList<SheetItem>()
                            items += SheetItem("Download", Glyph.DOWN) { app.library.requestDownload(s.artist, s.title, s.album, s.durationMs, artUrl = s.artUrl); app.ui.say("Getting \"${s.title}\"…") }
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
                if (!webSongsExpanded && only != "songs" && w.songs.size > 8) item { ShowMoreRow(w.songs.size - 8) { webSongsExpanded = true } }
            }
            // with only songs shown (or while choosing), the top song joins the library list (the Top result card is for All)
            if (libSongs.isNotEmpty() && shows("songs") && !artistMode) {
                item { SectionHeader("In your library") }
                val shown = if (songsExpanded || only == "songs" || picks != null) libSongs else libSongs.take(4)
                itemsIndexed(shown, key = { i, t -> "s$i${t.path}" }) { i, t ->
                    val id = "L" + t.path
                    TrackRow(t, nav, snap, onPlay = { app.player.play(listOf(t), 0, null, from = "search") }, sheetExtra = listOf(SheetItem("Select", Glyph.CHECK) { picks = PickSet().toggle(id) }),
                        picked = picks?.contains(id), onPick = { picks = (picks ?: PickSet()).toggle(id) }, kind = "Song")
                }
                if (!songsExpanded && only != "songs" && libSongs.size > 4) item { ShowMoreRow(libSongs.size - 4) { songsExpanded = true } }
            }
            if (r.albums.isNotEmpty() && shows("albums") && !artistMode) {
                item { SectionHeader("Your albums") }
                val shownAlbums = if (albumsExpanded || only == "albums") r.albums else r.albums.take(3)
                items(shownAlbums, key = { "b" + it.tracks.first().albumKey }) { g ->
                    IpodRow({ nav.push(Screen.Detail(DetailKind.ALBUM, g.tracks.first().albumKey)) }, height = 56.dp, leading = { ArtImage(g.artKey, Modifier.size(48.dp), thumb = true, corner = 8.dp) },
                        trailing = { OwnedTag() }) { hi ->
                        Column { Txt(g.name, size = 16f, color = if (hi) Color.White else sc.onBg); Txt("Album · " + (g.tracks.firstOrNull()?.artist ?: ""), size = 13f, color = if (hi) Color(0xDDFFFFFF) else sc.onBgDim) }
                    }
                }
                if (!albumsExpanded && only != "albums" && r.albums.size > 3) item { ShowMoreRow(r.albums.size - 3) { albumsExpanded = true } }
            }
            if (webLoading) item { Row(Modifier.fillMaxWidth().padding(16.dp), horizontalArrangement = Arrangement.Center) { Txt("Searching for more...", size = 13f, color = sc.onBgDim) } }
            if (!webLoading && w.songs.isEmpty() && !app.ui.guest && only != "playlists") item { DownloadRequestBar(app, q) }
        }
        picks?.let { p ->
            SearchSelectBar(p.count, p.allOf(allIds), Modifier.align(Alignment.BottomCenter), onCancel = { picks = null }, onAll = { picks = p.selectAllOrNone(allIds) }) {
                val split = PickSet.split(p.asList()) { tracksById[it] }
                val kept = if (split.keep.isEmpty()) 0 else app.offline.keep(split.keep, com.ipodemu.library.offline.OfflineReason.MANUAL)
                var asked = 0
                for (id in split.fetch) webById[id]?.let { s -> app.library.requestDownload(s.artist, s.title, s.album, s.durationMs, artUrl = s.artUrl); asked++ }
                app.ui.say(if (kept + asked > 0) PickSet.summary(kept, asked) else "Already on this phone", warn = kept + asked == 0)
                picks = null
            }
        }
        }
    }
}

/** Artists whose names also match the search, besides the top one (the small row at the bottom of an artist search). */
object SearchArtists {
    fun similar(artists: List<com.ipodemu.library.Group>, q: String, top: com.ipodemu.library.Group): List<com.ipodemu.library.Group> {
        val words = com.ipodemu.library.SearchRank.words(q)
        if (words.isEmpty()) return emptyList()
        return artists.filter { it.name != top.name && com.ipodemu.library.SearchRank.words(it.name).joinToString("").let { n -> words.all { w -> n.contains(w) } } }
            .sortedByDescending { it.tracks.size }.take(12)
    }
}

/** The artist search layout: Top result card over the artist's top 5 songs, then Popular, Albums, Singles & EPs, Playlists and similar names. */
private fun androidx.compose.foundation.lazy.LazyListScope.ArtistResults(
    artist: com.ipodemu.library.Group, page: com.ipodemu.library.ArtistPage, made: List<com.ipodemu.library.MadeLists.Summary>,
    similar: List<com.ipodemu.library.Group>, nav: PlayerNav, snap: PlayerSnap, featuring: List<com.ipodemu.library.MadeLists.Summary> = emptyList(), unfocus: () -> Unit,
) {
    item { SectionHeader("Top result") }
    item(key = "artistTop") {
        val app = LocalApp.current
        val sc = LocalScheme.current
        Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 4.dp).clip(RoundedCornerShape(16.dp)).background(sc.accent.copy(alpha = 0.12f))
            .clickable { unfocus(); nav.push(Screen.Detail(DetailKind.ARTIST, artist.name)) }.padding(12.dp)
            .semantics { contentDescription = "Open artist ${artist.name}" }, verticalAlignment = Alignment.CenterVertically) {
            ArtImage(artist.artKey, Modifier.size(64.dp), thumb = true, circle = true)
            Column(Modifier.weight(1f).padding(start = 14.dp)) {
                Txt(artist.name, size = 18f, weight = FontWeight.Bold, color = sc.onBg, maxLines = 1)
                Txt("Artist · ${artist.tracks.size} ${if (artist.tracks.size == 1) "song" else "songs"}", size = 13f, color = sc.onBgDim, maxLines = 1)
            }
            IconAction(Glyph.PLAY, "Play ${artist.name}", { unfocus(); app.player.play(artist.tracks, 0, null, from = "search") }, tint = sc.accent, size = 48.dp)
        }
    }
    // the top 5 songs sit right under the card, so there is no big empty card
    itemsIndexed(page.top, key = { i, t -> "at$i${t.path}" }) { i, t ->
        val app = LocalApp.current
        TrackRow(t, nav, snap, onPlay = { unfocus(); app.player.play(page.top, i, null, from = "search") })
    }
    if (page.popular.isNotEmpty()) {
        item { SectionHeader("Popular") }
        item(key = "apopular") {
            val app = LocalApp.current
            androidx.compose.foundation.lazy.LazyRow(contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                itemsIndexed(page.popular, key = { i, t -> "ap$i${t.path}" }) { i, t ->
                    SearchCover(t.artKey, t.title, t.album.ifEmpty { "Song" }, "Play ${t.title}") { unfocus(); app.player.play(page.popular, i, null, from = "search") }
                }
            }
        }
    }
    for ((label, rows) in listOf("Albums" to page.albums, "Singles & EPs" to page.singles)) {
        if (rows.isEmpty()) continue
        item { SectionHeader(label) }
        item(key = "a$label") {
            androidx.compose.foundation.lazy.LazyRow(contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                items(rows, key = { it.tracks.first().albumKey }) { g ->
                    val year = g.tracks.maxOf { it.year }
                    SearchCover(g.artKey, g.name, (if (year > 0) "$year · " else "") + com.ipodemu.library.ArtistPage.kind(g), "Open album ${g.name}") { unfocus(); nav.push(Screen.Detail(DetailKind.ALBUM, g.tracks.first().albumKey)) }
                }
            }
        }
    }
    if (made.isNotEmpty()) {
        item { SectionHeader("Playlists") }
        item(key = "amade") { ListRow(made, nav, unfocus) }
    }
    // "Lists with <artist> in" (F26.2, the web's take on Spotify's Featuring)
    if (featuring.isNotEmpty()) {
        item { SectionHeader("Lists with ${artist.name} in") }
        item(key = "afeaturing") { ListRow(featuring, nav, unfocus) }
    }
    if (similar.isNotEmpty()) {
        item { Txt("Other artists with similar names", Modifier.padding(start = 16.dp, top = 16.dp, bottom = 4.dp), size = 13f, color = LocalScheme.current.onBgDim) }
        item(key = "asimilar") {
            Row(Modifier.horizontalScroll(rememberScrollState()).padding(horizontal = 16.dp, vertical = 4.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                for (a in similar) DesignChip(a.name, false, { unfocus(); nav.push(Screen.Detail(DetailKind.ARTIST, a.name)) })
            }
        }
    }
}

/** A row of playlists as the web's search shows them: our own, Spotify's, Deezer's and SoundCloud's; an outside one shows where it is from. */
@Composable
private fun ListRow(lists: List<com.ipodemu.library.MadeLists.Summary>, nav: PlayerNav, unfocus: () -> Unit) {
    val app = LocalApp.current
    androidx.compose.foundation.lazy.LazyRow(contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
        items(lists, key = { it.id }) { m -> ListCard(m, app, nav, unfocus) }
    }
}

@Composable
private fun ListCard(m: com.ipodemu.library.MadeLists.Summary, app: com.ipodemu.App, nav: PlayerNav, unfocus: () -> Unit) {
    val cover = m.first?.let { f -> app.library.resolve(f.path ?: "", f.title to f.artist)?.artKey }
    val what = if (com.ipodemu.library.MadeLists.isOutside(m.id)) "playlist from ${com.ipodemu.library.MadeLists.source(m.id)}" else "playlist for you"
    SearchCover(cover, m.name, m.subtitle, "${m.name}, $what. ${m.subtitle}", picture = m.picture) { unfocus(); nav.push(Screen.Made(m.id, m.name)) }
}

/** A square cover with a title and a line under it, for the search rows. */
@Composable
private fun SearchCover(artKey: String?, title: String, sub: String, description: String, picture: String? = null, onClick: () -> Unit) {
    Column(Modifier.width(132.dp).clip(RoundedCornerShape(10.dp)).clickable(onClick = onClick).semantics { contentDescription = description }) {
        // an outside list the phone has none of the songs of shows its own picture
        if (artKey == null && picture != null) RemoteArtImage(picture, Modifier.size(132.dp), corner = 10.dp) else ArtImage(artKey, Modifier.size(132.dp), corner = 10.dp)
        Txt(title, Modifier.padding(top = 6.dp), size = 14f, weight = FontWeight.SemiBold, maxLines = 2)
        Txt(sub, size = 12f, color = LocalScheme.current.onBgDim, maxLines = 2)
    }
}

/** The tick on a cover while choosing songs: filled when picked, an empty ring when not. */
@Composable
private fun PickTick(picked: Boolean, modifier: Modifier) {
    val sc = LocalScheme.current
    Box(modifier.offset(4.dp, 4.dp).size(24.dp).clip(CircleShape).background(if (picked) sc.accent else Color(0xCC000000)).border(2.dp, if (picked) sc.accent else Color.White, CircleShape)
        .semantics { stateDescription = if (picked) "Selected" else "Not selected" }, contentAlignment = Alignment.Center) {
        if (picked) GlyphIcon(Glyph.CHECK, Modifier.size(14.dp), Color.Black)
    }
}

/** The bar for songs ticked in Search: how many, Select all, and Download N (library songs are kept on the phone, catalogue ones are asked for). */
@Composable
private fun SearchSelectBar(count: Int, allPicked: Boolean, modifier: Modifier, onCancel: () -> Unit, onAll: () -> Unit, onDownload: () -> Unit) {
    val sc = LocalScheme.current
    Column(modifier.fillMaxWidth().padding(12.dp).clip(RoundedCornerShape(20.dp)).background(Color(0xFF221D19)).padding(14.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Txt(if (count == 0) "Tap songs to tick them" else "$count selected", size = 15f, weight = FontWeight.SemiBold, color = sc.onBg)
        Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            DesignChip("Cancel", false, onCancel)
            DesignChip(if (allPicked) "Select none" else "Select all", false, onAll)
            if (count > 0) DesignChip(PickSet.downloadLabel(count), true, onDownload, icon = Glyph.DOWNLOADED)
        }
    }
}


/** An album/EP/single in Search's swipeable row: cover with a Download (or progress, or Open) button on it, title, kind and artist. */
@Composable
private fun AlbumCard(a: WebCatalog.Album, owned: Boolean, status: DownloadStatus?, onOpen: () -> Unit, onDownload: () -> Unit) {
    val sc = LocalScheme.current
    val busy = status != null && status.stage != DownloadStage.DONE && status.stage != DownloadStage.FAILED
    val failed = status?.stage == DownloadStage.FAILED
    // a tap opens the album (its page when the library lacks it); only the badge on the cover downloads (owner, 6 Nov: a tap downloaded it)
    Column(Modifier.width(132.dp).clip(RoundedCornerShape(10.dp)).clickable(onClick = onOpen)
        .semantics { contentDescription = "${a.cleanTitle}, ${a.kind} by ${a.artist}. " + if (owned) "In your library" else if (busy) status!!.message else "Download" }) {
        Box {
            RemoteArtImage(a.artUrl, Modifier.size(132.dp), corner = 10.dp)
            Box(Modifier.align(Alignment.BottomEnd).padding(6.dp).height(32.dp).widthIn(min = 32.dp).clip(RoundedCornerShape(50))
                .background(if (owned || busy) Color(0xCC000000) else sc.accent).clickable(onClick = { if (owned) onOpen() else if (!busy) onDownload() })
                .semantics { contentDescription = if (owned) "Open ${a.cleanTitle}" else "Download ${a.cleanTitle}" }.padding(horizontal = 8.dp), contentAlignment = Alignment.Center) {
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
/** [versions]: each song's other versions, by [versionKey] (search shows one row a song, as Spotify does). */
private class WebResults(val songs: List<WebCatalog.Song>, val albums: List<WebCatalog.Album>, val versions: Map<String, List<WebCatalog.Song>> = emptyMap())
private fun versionKey(s: WebCatalog.Song) = s.artist + "|" + s.title

/** "Espresso", "Espresso EP", "Espresso - Single" and "Espresso (Deluxe)" are one release when checking what's owned. */
private fun ownedAlbumKey(artist: String, album: String) = com.ipodemu.library.primaryArtist(artist) + "|" +
    album.lowercase().replace(Regex("""\s*[(\[](deluxe|expanded|anniversary|remaster)[^)\]]*[)\]]"""), "").replace(Regex("""\s*-?\s*\b(ep|single)\s*$"""), "").filter { it.isLetterOrDigit() }

/**
 * A song or album from the web catalog: cover, title, a second line that turns into live progress while downloading, and
 * one action: Play (or Open) when it's already in the library, otherwise Download, a percentage while it runs, then Play.
 */
@Composable
private fun WebRow(title: String, subtitle: String, imageUrl: String?, owned: Track?, status: DownloadStatus?, onPlay: () -> Unit, onDownload: () -> Unit, actionLabel: String = "Download $title", onHiRes: (() -> Unit)? = null,
                   picked: Boolean? = null, onPick: (() -> Unit)? = null,
                   /** A tap on a song not in the library opens its page; it downloads only from the Download button (owner, 6 Nov). */
                   onOpen: (() -> Unit)? = null) {
    val sc = LocalScheme.current
    val busy = status != null && status.stage != DownloadStage.DONE && status.stage != DownloadStage.FAILED
    val failed = status?.stage == DownloadStage.FAILED
    val tagged = owned != null && isTagged(owned)   // a song fetched just now: "Recently downloaded · 5s ago" under the line
    // choosing several songs: a tick in place of the cover, and a tap ticks the row; a long press starts choosing
    IpodRow({ if (picked != null && onPick != null) onPick() else if (owned != null) onPlay() else if (onOpen != null) onOpen() else if (!busy) onDownload() }, onLong = if (picked == null) onPick else null, height = if (tagged) 76.dp else 60.dp,
        leading = {
            // choosing keeps the cover, with the tick on it (the bare ticks made a cramped list you couldn't tell apart; owner, 1 Nov)
            Box(Modifier.size(48.dp)) {
                RemoteArtImage(imageUrl, Modifier.size(48.dp), corner = 8.dp)
                if (picked != null) PickTick(picked, Modifier.align(Alignment.BottomEnd))
            }
        },
        trailing = {
            // a narrow phone or big text: Play and Download as 48 dp icons, so the title keeps its room (SearchFit); the menu is 44 dp (it was 36)
            val cfg = androidx.compose.ui.platform.LocalConfiguration.current
            val compact = SearchFit.compactActions(cfg.screenWidthDp.toFloat(), cfg.fontScale)
            if (picked == null) when {
                owned != null -> if (compact) IconAction(Glyph.PLAY, "Play $title", onPlay, tint = sc.accent) else GlossPill("Play", onPlay, height = 32.dp)
                busy -> Txt(Regex("""\d+%""").find(status!!.message)?.value ?: "...", size = 13f, weight = FontWeight.Medium, color = sc.accent)
                else -> Row(horizontalArrangement = Arrangement.spacedBy(if (compact) 0.dp else 6.dp), verticalAlignment = Alignment.CenterVertically) {
                    if (onHiRes != null) IconAction(Glyph.LIST, "More download options for $title", onHiRes, size = 44.dp)
                    if (compact) IconAction(Glyph.DOWN, if (failed) "Retry $title" else actionLabel, onDownload, tint = if (failed) Color(0xFFFF9C9C) else sc.accent)
                    else GlossPill(if (failed) "Retry" else "Download", onDownload, Modifier.semantics { contentDescription = actionLabel }, height = 32.dp)
                }
            }
        },
    ) { hi ->
        Column {
            Txt(title, size = 15f, color = if (hi) Color.White else sc.onBg, maxLines = 1)
            Txt(subtitle, size = 12f, color = if (failed && !hi) Color(0xFFFF9C9C) else if (hi) Color(0xDDFFFFFF) else sc.onBgDim, maxLines = 1)
            if (tagged && owned != null) DownloadTagLine(owned, hi = hi, size = 11f)
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
            Txt("Song · " + t.artist, size = 14f, color = sc.onBgDim, maxLines = 1)
            DownloadTagLine(t, size = 12f, compact = true, showNew = !(Tweaks.badges && LocalApp.current.library.mixedSources))
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
        TopBar("Up Next", nav, showBack = true) { if (q.isNotEmpty()) GlossPill("Clear", { clearQueueWithUndo(app); nav.pop() }, height = 30.dp) }
        if (q.isEmpty()) { EmptyState("The queue is empty"); return@Column }
        // opens on the song playing now (what already played stays above it)
        val list = androidx.compose.foundation.lazy.rememberLazyListState(initialFirstVisibleItemIndex = snap.index.coerceIn(0, q.size - 1))
        // the songs Autoplay added, and the suggestions it is still fetching (display only, after the queue's own rows)
        val upcoming = q.withIndex().filter { it.index > snap.index }.map { it.index to it.value }
        val plan = rememberUpNextPlan(upcoming, snap.count, snap.rev, snap.track?.path)
        LazyColumn(Modifier.fillMaxSize(), state = list, contentPadding = PaddingValues(bottom = 24.dp)) {
            // keyed by the song (not its place), so Move up/down and Remove glide the rows instead of redrawing them
            val keys = QueueKeys.of(q.map { it.path })
            itemsIndexed(q, key = { i, _ -> keys[i] }) { i, t ->
                Column(Modifier.fillMaxWidth().glide(this@itemsIndexed)) {
                if (i == plan.dividerBefore) AutoplayDivider()
                val cur = i == snap.index
                val remove = remember(i, t) { SwipeAction("Remove", Glyph.CLOSE, { Color(0xFFD9423F) }) { removeFromQueueWithUndo(app, i) } }
                SwipeRow(right = remove, left = remove) {
                val tagged = isTagged(t)
                IpodRow({ app.player.skipTo(i) }, height = if (tagged) 74.dp else 56.dp, onLong = { nav.sheet = queueRowSheet(app, t, i, snap.index, q.size) },
                    leading = { Box(Modifier.width(30.dp), contentAlignment = Alignment.Center) { if (cur) EqualizerBars(Modifier.size(18.dp), snap.playing, sc.accent) else Txt("${i + 1}", size = 14f, color = sc.onBgDim) } },
                    trailing = {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            DownloadedMark(t.path, 15.dp)
                            Box(Modifier.size(44.dp).clip(RoundedCornerShape(50)).semantics { contentDescription = "More for ${t.title}" }.clickable { nav.sheet = queueRowSheet(app, t, i, snap.index, q.size) }, contentAlignment = Alignment.Center) { GlyphIcon(Glyph.MORE, Modifier.size(18.dp), sc.onBgDim) }
                            Box(Modifier.size(44.dp).clip(RoundedCornerShape(50)).semantics { contentDescription = "Remove ${t.title}" }.clickable { removeFromQueueWithUndo(app, i) }, contentAlignment = Alignment.Center) { GlyphIcon(Glyph.CLOSE, Modifier.size(18.dp), sc.onBgDim) }
                        }
                    }) { hi ->
                    Column {
                        Txt(t.title, size = 16f, weight = if (cur) FontWeight.Bold else FontWeight.Medium, color = if (hi) Color.White else if (cur) sc.accent else sc.onBg)
                        Txt(t.artist, size = 13f, color = if (hi) Color(0xDDFFFFFF) else sc.onBgDim)
                        if (tagged) DownloadTagLine(t, hi = hi, compact = androidx.compose.ui.platform.LocalConfiguration.current.screenWidthDp < 520)
                    }
                }
                }
                }
            }
            if (plan.dividerAtEnd) item(key = "ap-divider") { AutoplayDivider() }
            items(plan.pending, key = { "ap-" + it.pending.key }) { PendingDrawerRow(it) }
        }
    }
}

/** The Up Next screen lists the queue in its stored order: its row actions move a song in that order (the playing song keeps playing). */
private fun queueRowSheet(app: App, t: Track, i: Int, current: Int, count: Int): SheetSpec {
    val items = ArrayList<SheetItem>()
    if (app.player.canEditQueue) {
        if (com.ipodemu.playback.QueueEdit.playNextTarget(current, i) != null) items += SheetItem("Play next", Glyph.CHEVRON) { app.player.playNextInQueue(i) }
        if (i > 0) items += SheetItem("Move up", Glyph.CHEVRON) { app.player.moveInQueue(i, i - 1) }
        if (i < count - 1) items += SheetItem("Move down", Glyph.CHEVRON) { app.player.moveInQueue(i, i + 1) }
    }
    items += SheetItem("Remove from queue", Glyph.CLOSE) { removeFromQueueWithUndo(app, i) }
    return SheetSpec(t.title, t.artist.ifEmpty { null }, items)
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
    Box(Modifier.fillMaxWidth().heightIn(min = 24.dp).background(Brush.verticalGradient(listOf(sc.top.copy(alpha = .96f), sc.bottom.copy(alpha = .96f)))).padding(horizontal = 16.dp), contentAlignment = Alignment.CenterStart) {
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
internal fun AccountAction() {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val nav = LocalPlayerNav.current
    val ctx = androidx.compose.ui.platform.LocalContext.current
    val name = if (app.prefs.accountKind == "nas") app.prefs.nasUsername.ifBlank { "guest" } else app.prefs.accountUserName   // the same name the Account screen shows
    var open by remember { mutableStateOf(false) }
    Box {
        // as on the web: the account button opens a small menu (it opened the Account page); Settings is in it, so it is a tap away on every screen
        Box(Modifier.size(48.dp).clip(CircleShape).semantics { contentDescription = if (name.isNotEmpty()) "Account: $name" else "Account"; stateDescription = if (open) "Menu open" else "" }
            .clickable { if (nav == null) app.ui.accountOpen = true else open = !open }, contentAlignment = Alignment.Center) {
            Box(Modifier.size(34.dp).clip(CircleShape).background(if (name.isNotEmpty()) sc.accent else Color(0x22FFFFFF)), contentAlignment = Alignment.Center) {
                if (name.isNotEmpty()) Txt(name.take(1).uppercase(), size = 16f, weight = FontWeight.Bold, color = Color.White)
                else GlyphIcon(Glyph.ARTIST, Modifier.size(20.dp), sc.onBg)
            }
        }
        if (open && nav != null) AccountMenu(name, onClose = { open = false }) { pick ->
            open = false
            when (pick) {
                AccountPick.ACCOUNT -> app.ui.accountOpen = true
                AccountPick.DASHBOARD -> { nav.nowPlaying = false; nav.push(Screen.Dashboard) }
                AccountPick.DOWNLOADS -> app.ui.downloadsOpen = true
                AccountPick.RETRO -> openRetro(ctx)
                AccountPick.SETTINGS -> { nav.nowPlaying = false; nav.push(Screen.Settings) }
                AccountPick.SIGN_OUT -> { app.account.signOut(); app.ui.setLogin("") }
            }
        }
    }
}

/** What the account menu offers, in the web's order (TopBar.razor): Account, Dashboard and Downloads (administrators), Retro, Settings, Sign out. */
enum class AccountPick { ACCOUNT, DASHBOARD, DOWNLOADS, RETRO, SETTINGS, SIGN_OUT }

/** The menu's items for this person: the administrators' two only for administrators; Sign out only when signed in. Pure, unit-tested. */
fun accountPicks(admin: Boolean, signedIn: Boolean): List<AccountPick> = buildList {
    add(AccountPick.ACCOUNT)
    if (admin) { add(AccountPick.DASHBOARD); add(AccountPick.DOWNLOADS) }
    add(AccountPick.RETRO); add(AccountPick.SETTINGS)
    if (signedIn) add(AccountPick.SIGN_OUT)
}

/** The account menu: a small card under the button, as on the web; a tap outside or Back closes it. */
@Composable
private fun AccountMenu(name: String, onClose: () -> Unit, onPick: (AccountPick) -> Unit) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val kind = when (app.prefs.accountKind) { "nas" -> "NAS"; "" -> "Not signed in"; else -> "Jellyfin" }
    androidx.compose.ui.window.Popup(alignment = Alignment.TopEnd, offset = androidx.compose.ui.unit.IntOffset(0, 140), onDismissRequest = onClose,
        properties = androidx.compose.ui.window.PopupProperties(focusable = true)) {
        // at most the screen less a margin, scrolling: on a phone on its side at big text Sign out was below the screen (SheetFit)
        val cfg = androidx.compose.ui.platform.LocalConfiguration.current
        Column(Modifier.padding(end = 8.dp).widthIn(min = 220.dp, max = 280.dp).heightIn(max = SheetFit.popupMaxHeight(cfg.screenHeightDp.toFloat()).dp)
            .clip(RoundedCornerShape(16.dp)).background(sc.top.mix(Color.Black, .45f))
            .border(1.dp, sc.cardBorder, RoundedCornerShape(16.dp)).verticalScroll(rememberScrollState()).padding(vertical = 6.dp)) {
            Row(Modifier.padding(horizontal = 14.dp, vertical = 10.dp), verticalAlignment = Alignment.CenterVertically) {
                Box(Modifier.size(40.dp).clip(CircleShape).background(sc.accent), contentAlignment = Alignment.Center) {
                    Txt(name.take(1).uppercase().ifEmpty { "?" }, size = 18f, weight = FontWeight.Bold, color = Color.White)
                }
                Column(Modifier.padding(start = 12.dp)) {
                    Txt(name.ifEmpty { "Guest" }, size = 16f, weight = FontWeight.Bold)
                    Txt(kind, size = 12.5f, color = sc.onBgDim)
                }
            }
            Box(Modifier.fillMaxWidth().height(1.dp).background(sc.onBg.copy(alpha = .1f)))
            for (pick in accountPicks(app.ui.isAdmin && !app.ui.guest, app.prefs.accountKind.isNotEmpty())) {
                val (label, glyph) = when (pick) {
                    AccountPick.ACCOUNT -> "Account" to Glyph.ARTIST
                    AccountPick.DASHBOARD -> "Server admin" to Glyph.INFO
                    AccountPick.DOWNLOADS -> "Download log" to Glyph.DOWN
                    AccountPick.RETRO -> "Retro" to Glyph.CASSETTE
                    AccountPick.SETTINGS -> "Settings" to Glyph.GEAR
                    AccountPick.SIGN_OUT -> "Sign out" to Glyph.CLOSE
                }
                Row(Modifier.fillMaxWidth().heightIn(min = 48.dp).clickable(role = androidx.compose.ui.semantics.Role.Button) { onPick(pick) }.padding(horizontal = 16.dp),
                    verticalAlignment = Alignment.CenterVertically) {
                    GlyphIcon(glyph, Modifier.size(20.dp), sc.onBg)
                    Txt(label, Modifier.padding(start = 14.dp), size = 15f)
                }
            }
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
        // not gone until the Undo message has run out: Undo just cancels it
        val path = ud.removeLater(id, i)
        if (path != null) app.ui.say("Removed \"${t.title}\" from ${p.name}", "Undo") { ud.cancelRemoval() }
    }
    return items
}

/** An album on a server shelf, found in this library: by one of its songs, else by name and artist. */
private fun serverAlbum(lib: com.ipodemu.library.Library, a: com.ipodemu.library.RecPick.Album): com.ipodemu.library.Group? {
    val albums = lib.albums()
    a.songs.firstNotNullOfOrNull { lib.resolve(it, null) }?.let { t -> albums.firstOrNull { g -> g.tracks.firstOrNull()?.albumKey == t.albumKey }?.let { return it } }
    val name = a.name.lowercase().replace(com.ipodemu.library.editionRe, "").filter { it.isLetterOrDigit() }
    return albums.firstOrNull { g -> val t = g.tracks.firstOrNull() ?: return@firstOrNull false
        t.albumNormC == name && com.ipodemu.library.primaryArtist(t.albumArtist.ifEmpty { t.artist }) == com.ipodemu.library.primaryArtist(a.artist) }
}

/**
 * Plays song [i] of a Home shelf. A "New" song is not in the library yet: the tap asks for it instead (Autoplay's request path; it joins the
 * queue when it lands), and the shelf's other new songs are left out of what plays.
 */
private fun playShelf(app: App, tracks: List<Track>, t: Track, title: String) {
    // the card hands over its own song, which is found again by its path in the queue (HomeShelves.startOf), not by its place on the shelf
    val start = com.ipodemu.library.HomeShelves.startOf(tracks, t)
    if (start == null) {
        app.ui.say(if (app.autoplay.fetch(t)) "Getting \"${t.title}\": it plays when it arrives" else "Downloads aren't set up on this phone", warn = !app.library.downloadsConfigured())
        return
    }
    app.player.play(start.queue, start.index, null, title, from = "home")
}

private class HomeData(val shelves: List<com.ipodemu.library.HomeShelf>, val moods: List<String>)
private object HomeCache {
    @Volatile var last: HomeData? = null
    /** Songs thumbed down while Home is up: they leave the shelves at once (the next build leaves them out on its own). */
    val gone = androidx.compose.runtime.mutableStateListOf<String>()
}
/** The phone Home's Quick picks (the server's when it answered, else its own), for the car: the songs on this phone, minus ones thumbed down. */
internal fun quickPicksForCar(app: com.ipodemu.App): List<Track> = carQuickPicks(HomeCache.last?.shelves ?: app.homeSnapshot?.shelves, HomeCache.gone)
internal fun carQuickPicks(shelves: List<com.ipodemu.library.HomeShelf>?, gone: Collection<String>): List<Track> =
    shelves?.firstOrNull { it.id == "quick-picks" }?.tracks.orEmpty().filter { !com.ipodemu.library.HomeShelves.isNew(it) && it.matchKey !in gone }
internal fun hideOnHome(t: Track) { if (t.matchKey !in HomeCache.gone) HomeCache.gone.add(t.matchKey) }

/** What the Search tab shows before anything is typed: shortcuts and genres to tap, instead of an empty page. */
@OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
@Composable
private fun SearchIdle(nav: PlayerNav) {
    val app = LocalApp.current
    val genres = remember(LocalLibRev.current) { app.library.genres().filter { it.name.lowercase() !in setOf("genre", "other", "unknown", "") }.sortedByDescending { it.tracks.size }.take(16) }
    Column(Modifier.fillMaxSize().then(Modifier.verticalScroll(rememberScrollState())).padding(bottom = 24.dp)) {
        val lib = app.library
        MonoLabel("${lib.songs().size} songs · ${lib.albums().size} albums · ${lib.artists().size} artists. Type above to search; anything you don't have shows up under more music, ready to download.",
            Modifier.padding(horizontal = 20.dp, vertical = 4.dp), size = 11f, maxLines = 4)
        var recent by remember { mutableStateOf(app.prefs.recentSearches) }
        if (recent.isNotEmpty()) {
            Row(Modifier.fillMaxWidth().padding(start = 20.dp, end = 8.dp, top = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                Txt("Recent searches", Modifier.weight(1f), size = 17f, weight = FontWeight.SemiBold)
                DesignChip("Clear", false, { app.prefs.recentSearches = emptyList(); recent = emptyList() })
            }
            Row(Modifier.horizontalScroll(rememberScrollState()).padding(horizontal = 16.dp, vertical = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                recent.forEach { r -> DesignChip(r, false, { app.ui.searchQuery = r }) }
            }
        }
        Row(Modifier.horizontalScroll(rememberScrollState()).padding(horizontal = 16.dp, vertical = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            DesignChip("Overview", true, { })
            DesignChip("Songs", false, { nav.push(Screen.Lib(LibKind.SONGS)) })
            DesignChip("Albums", false, { nav.push(Screen.Lib(LibKind.ALBUMS)) })
            DesignChip("Artists", false, { nav.push(Screen.Lib(LibKind.ARTISTS)) })
            DesignChip("Genres", false, { nav.push(Screen.Lib(LibKind.GENRES)) })
            DesignChip("Explore", false, { nav.push(Screen.Library) })
        }
        SectionHeader("Jump to")
        androidx.compose.foundation.layout.FlowRow(Modifier.padding(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            GlossPill("Favorites", { nav.push(Screen.Detail(DetailKind.FAVORITES)) }, icon = Glyph.HEART, height = 36.dp)
            GlossPill("Recent", { nav.push(Screen.Detail(DetailKind.RECENT)) }, icon = Glyph.CLOCK, height = 36.dp)
            GlossPill("Offline music", { nav.push(Screen.Downloads) }, icon = Glyph.DOWNLOADED, height = 36.dp)
            GlossPill("Recently added", { nav.push(Screen.Detail(DetailKind.DOWNLOADS)) }, icon = Glyph.PLUS, height = 36.dp)
            GlossPill("Albums", { nav.push(Screen.Lib(LibKind.ALBUMS)) }, icon = Glyph.ALBUM, height = 36.dp)
            GlossPill("Artists", { nav.push(Screen.Lib(LibKind.ARTISTS)) }, icon = Glyph.ARTIST, height = 36.dp)
        }
        if (genres.isNotEmpty()) {
            SectionHeader("Browse by genre")
            androidx.compose.foundation.layout.FlowRow(Modifier.padding(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                genres.forEach { g -> GlossPill(g.name, { nav.push(Screen.Detail(DetailKind.GENRE, g.name)) }, height = 36.dp) }
            }
        }
    }
}

/** The song's match on MusicBrainz (the open music database): asked only when chosen here; the choice is kept beside the library and never written into the file or Jellyfin. */
fun musicBrainzSheet(app: App, nav: PlayerNav, t: Track) {
    val mb = app.mb; val key = mb.keyOf(t)
    val chosen = mb.overlay.get(key)
    if (chosen == null) { mbSearch(app, nav, t); return }
    val items = ArrayList<SheetItem>()
    items += SheetItem("Find a different match", Glyph.SEARCH) { mbSearch(app, nav, t) }
    if (chosen.releaseMbid != null || chosen.releaseGroupMbid != null) items += SheetItem("Use the cover from the Cover Art Archive", Glyph.ALBUM) {
        app.ui.say("Looking for a cover...")
        mb.useFrontCover(t, chosen) { r -> app.ui.say(when (r) { 1 -> "Cover replaced. It shows from the next screen you open."; 0 -> "The Cover Art Archive has no picture for this release."; else -> "The Cover Art Archive did not answer." }) }
    }
    if (t.artKey != null && mb.pins.has(t.artKey!!)) items += SheetItem("Use the original cover", Glyph.CLOSE) { mb.originalCover(t); app.ui.say("Original cover back. It shows from the next screen you open.") }
    items += SheetItem("Forget this match", Glyph.CLOSE) { mb.overlay.clear(key); app.ui.say("Match forgotten") }
    nav.sheet = SheetSpec("MusicBrainz", chosen.title + " - " + chosen.artist + (if (chosen.album.isNotEmpty()) " (" + chosen.album + ")" else ""), items)
}

private fun mbSearch(app: App, nav: PlayerNav, t: Track) {
    nav.sheet = SheetSpec("MusicBrainz", "Looking it up...", emptyList())
    app.mb.search(t) { ranked ->
        val items = ArrayList<SheetItem>()
        if (ranked == null) items += SheetItem("MusicBrainz did not answer. Try again in a minute.", Glyph.CLOSE) { }
        else if (ranked.isEmpty()) items += SheetItem("MusicBrainz knows nothing like this song.", Glyph.CLOSE) { }
        else for ((c, score) in ranked) items += SheetItem("${score.toInt()}%  ${c.title} - ${c.artist}" + (if (c.album.isNotEmpty()) " (${c.album})" else "") + (c.year?.let { "  $it" } ?: ""), Glyph.NOTE) {
            app.mb.overlay.set(app.mb.keyOf(t), c); app.ui.say("Matched: ${c.title}")
        }
        nav.sheet = SheetSpec("MusicBrainz match", "Pick the right one; nothing is changed in your files", items)
    }
}

/** The stars of a song: five to one, and clear. Kept in the profile (every device sees them) and written to Jellyfin. */
fun ratingSheet(app: App, t: Track): SheetSpec {
    val cur = app.userData.rating(t)
    val items = ArrayList<SheetItem>()
    for (n in 5 downTo 1) items += SheetItem(RatingStore.label(n), Glyph.STAR, selected = n == cur) { app.userData.setRating(t, n); app.ui.say("Rated \"${t.title}\": " + RatingStore.label(n)) }
    if (cur > 0) items += SheetItem("Clear the rating", Glyph.CLOSE) { app.userData.setRating(t, 0) }
    return SheetSpec("Rate", t.title, items)
}

/** Search's recent searches, by the web's rules (UserSession.AddRecentSearch). */
object RecentSearches {
    fun add(list: List<String>, query: String): List<String> {
        val q = query.trim()
        if (q.length < 2) return list
        return listOf(q) + list.filter { !it.equals(q, ignoreCase = true) && !q.startsWith(it, ignoreCase = true) }.take(9)
    }
}

/** Clears the queue and offers it back: Undo puts the same songs back, on the same song at the same second, paused. */
fun clearQueueWithUndo(app: com.ipodemu.App) {
    val p = app.player
    val list = p.queueTracks(); val at = p.exo.currentMediaItemIndex; val ms = p.positionMs
    p.clearQueue()
    if (list.isEmpty()) return
    app.ui.say("Cleared Up next", "Undo") { p.playFrom(list, at.coerceIn(0, list.size - 1), ms, paused = true) }
}

/** Home's one-time welcome: where Home, Search, Library and Retro are, in a few lines. */
@Composable
private fun WelcomeCard(onDone: () -> Unit) {
    val sc = LocalScheme.current
    Column(Modifier.padding(horizontal = 16.dp, vertical = 8.dp).fillMaxWidth().clip(RoundedCornerShape(20.dp)).background(Color(0x14FFFFFF)).padding(18.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        SerifTitle("Welcome to FLACie", size = 28f, italicWords = 1)
        Txt("Home has what you played last and new picks every day.", size = 14.5f, color = sc.onBg, maxLines = 3)
        Txt("Search finds anything in your library, and anything you don't have yet, ready to download.", size = 14.5f, color = sc.onBg, maxLines = 3)
        Txt("Library holds your favourites, playlists and Jam. Retro (the cassette in the player) plays your music on a classic-style music player.", size = 14.5f, color = sc.onBg, maxLines = 4)
        DesignChip("Got it", true, onDone)
    }
}

/** Whether Home's welcome has been put away: one observed value for the whole app (read from the settings once), so Got it hides it everywhere at
 * once and it never comes back when the list is scrolled or the tab changed. */
private var welcomedState: androidx.compose.runtime.MutableState<Boolean>? = null
private fun welcomed(app: com.ipodemu.App): androidx.compose.runtime.MutableState<Boolean> =
    welcomedState ?: androidx.compose.runtime.mutableStateOf(app.prefs.welcomed).also { welcomedState = it }
