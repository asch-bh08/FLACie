package com.ipodemu.player

import androidx.compose.ui.graphics.graphicsLayer

import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.add
import androidx.compose.foundation.layout.asPaddingValues
import androidx.compose.foundation.layout.navigationBars
import androidx.compose.foundation.layout.displayCutout
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.systemBars
import androidx.compose.foundation.layout.union
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.ui.draw.drawBehind
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp

// ---- safe area -----------------------------------------------------------------------------------------------------

/** Debug aid only (`am start --es fakecutout 32`): pretends the display has a cutout this tall at the top, so the
 * safe-area handling can be checked on a device without a punch-hole. 0 in normal use. */
val LocalDebugCutout = staticCompositionLocalOf { 0.dp }

/** Everything the UI must stay clear of: the camera cutout / punch-hole (the window is laid out edge-to-edge into it,
 * `shortEdges`), plus the status/navigation bars when they are showing (they are normally hidden). */
@Composable
fun safeInsets(): WindowInsets = WindowInsets.displayCutout.union(WindowInsets.systemBars).add(WindowInsets(top = LocalDebugCutout.current))

/** Pads content into the safe area; backgrounds drawn by the caller before this still fill the whole screen. */
@Composable
fun Modifier.safeArea(): Modifier = this.windowInsetsPadding(safeInsets())

// ---- modern theme --------------------------------------------------------------------------------------------------

private fun hsvOf(c: Color): FloatArray { val o = FloatArray(3); android.graphics.Color.colorToHSV(c.toArgb(), o); return o }
private fun hsvColor(h: Float, s: Float, v: Float) = Color(android.graphics.Color.HSVToColor(floatArrayOf(h, s.coerceIn(0f, 1f), v.coerceIn(0f, 1f))))

/** The web's own palette (app.css :root), so the phone and the browser look like one app: flat near-black, one pink accent (or the one chosen in
 * Settings), raised surfaces with a hairline. Only the full-screen player takes a wash of the cover's colour, like the web's blurred backdrop. */
object Palette {
    // the kit's tokens (Kit.kt) under the old names; the ground is the web's slate now (plan decision 1)
    val bg = Kit.bg; val surface = Kit.surface; val surface2 = Kit.surface2; val surface3 = Kit.surface3
    val line = Kit.line; val ink = Kit.ink; val dim = Kit.dim; val faint = Kit.faint; val pink = Kit.pink
}

fun buildModernScheme(art: ArtColors?): Scheme {
    val accent = Tweaks.accent ?: Palette.pink
    val top = if (art == null) Palette.bg else hsvOf(art.primary).let { p -> hsvColor(p[0], p[1] * 0.55f, 0.27f) }
    return Scheme(top, Palette.bg, accent, Palette.ink, Palette.dim, Palette.surface, Palette.line, true)
}

// ---- top bar -------------------------------------------------------------------------------------------------------

/** Modern title bar: 64dp tall, 48dp touch targets, back arrow + left-aligned title, actions on the right. */
@Composable
fun ModernTopBar(title: String, onBack: (() -> Unit)?, actions: @Composable () -> Unit = {}) {
    Row(Modifier.fillMaxWidth().height(64.dp).padding(horizontal = 4.dp), verticalAlignment = Alignment.CenterVertically) {
        if (onBack != null) IconAction(Glyph.BACK, "Back", onBack) else Box(Modifier.width(12.dp))
        // design I: the screen's name in the serif, last word in italics
        SerifTitle(title, Modifier.weight(1f).padding(start = 4.dp), size = 30f)
        // the account button on every page, as on Home (it was Home only): the account, sign-in and sync are one tap from anywhere
        Row(horizontalArrangement = Arrangement.spacedBy(4.dp), verticalAlignment = Alignment.CenterVertically) { actions(); if (title != "Settings") AccountAction() }
    }
}

/** A 48dp icon button (Material minimum touch target) -- the Modern theme's top-bar and toolbar action. */
@Composable
fun IconAction(g: Glyph, label: String, onClick: () -> Unit, tint: Color = LocalScheme.current.onBg, size: Dp = 48.dp, iconScale: Float = 0.5f, dot: Boolean = false) {
    val src = remember { MutableInteractionSource() }
    val focused by src.collectIsFocusedAsState()
    val pressed by src.collectIsPressedAsState()
    Box(
        Modifier.size(size).pressSpring(pressed, 0.82f).clip(CircleShape).background(if (focused || pressed) Color(0x33FFFFFF) else Color.Transparent).ringWhen(focused, CircleShape)
            .semanticsLabel(label).wheelTracked().clickable(src, null, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        GlyphIcon(g, Modifier.size(size * iconScale).bumpOnChange(g), tint)   // a heart turning solid bounces
        // a toggle that is on says so with more than colour: a dot under the glyph
        if (dot) Box(Modifier.align(Alignment.BottomCenter).padding(bottom = size * 0.12f).size(4.dp).clip(CircleShape).background(tint))
    }
}

private fun Modifier.semanticsLabel(label: String) = this.semantics { contentDescription = label; role = androidx.compose.ui.semantics.Role.Button }

// ---- navigation ----------------------------------------------------------------------------------------------------

/**
 * The bottom navigation: four roomy places, Home, Search, Library and Devices. Library holds Favourites, Playlists, Jam and the rest of the
 * collection (six tabs were too cramped to hit); Devices opens its panel over whatever is on screen. Explore and Settings are on Search (its chips)
 * and on Home's top bar, so nothing went away.
 */
enum class Tab(val label: String, val glyph: Glyph, val root: Screen?) {
    HOME("Home", Glyph.NOTE, Screen.Home), SEARCH("Search", Glyph.SEARCH, Screen.Search),
    LIBRARY("Library", Glyph.LIST, Screen.Sources), SETTINGS("Settings", Glyph.GEAR, Screen.Settings), DEVICES("Devices", Glyph.DEVICES, null),
}

fun PlayerNav.currentTab(): Tab = Tab.entries.firstOrNull { it.root != null && it.root == stack.first() } ?: Tab.HOME
fun PlayerNav.selectTab(t: Tab) {
    val root = t.root ?: return
    nowPlaying = false; queueFromPlayer = false
    if (stack.size == 1 && stack.first() == root) return
    stack.clear(); stack.add(root)
}

/** The tabs this person sees: Settings always; Devices only while another of the account's devices is playing (it is in the player's More menu
 * the rest of the time), and never for a guest. */
@Composable
private fun shownTabs(): List<Tab> {
    val app = LocalApp.current
    val elsewhere by remember { androidx.compose.runtime.derivedStateOf { app.connect.remoteNow() != null } }
    return Tab.entries.filter { it != Tab.DEVICES || (!app.ui.guest && elsewhere) }
}

/** A tab's press: a page tab switches page; Devices opens its panel. */
@Composable
private fun tabPress(nav: PlayerNav): (Tab) -> Unit {
    val app = LocalApp.current
    return { t -> when (t) { Tab.DEVICES -> app.ui.devicesOpen = true; else -> nav.selectTab(t) } }
}

/** Bottom navigation bar for phones and the Fold's cover screen. */
@Composable
fun BottomNav(nav: PlayerNav) {
    val sc = LocalScheme.current
    val cur = nav.currentTab()
    // above Android's own navigation: the three buttons (Samsung's default) sat on top of the tabs; the gesture bar is short enough to keep the old look
    val navBar = androidx.compose.foundation.layout.WindowInsets.navigationBars.asPaddingValues().calculateBottomPadding()
    val lift = if (navBar > 32.dp) navBar else 0.dp
    Row(Modifier.fillMaxWidth().background(Palette.bg).drawBehind { drawLine(Palette.line, androidx.compose.ui.geometry.Offset(0f, 0f), androidx.compose.ui.geometry.Offset(size.width, 0f), 1f) }.padding(bottom = lift).height(76.dp).padding(horizontal = 8.dp), verticalAlignment = Alignment.CenterVertically) {
        val press = tabPress(nav)
        shownTabs().forEach { t -> NavItem(t, t == cur && t.root != null, Modifier.weight(1f).fillMaxHeight()) { press(t) } }
    }
}

/** Navigation rail for wide screens: the unfolded Fold, tablets, landscape, and the square RG Rotate panel (where a
 * bottom bar would eat too much of the short height). */
@Composable
fun NavRail(nav: PlayerNav) {
    val cur = nav.currentTab()
    androidx.compose.foundation.layout.BoxWithConstraints(Modifier.width(80.dp).fillMaxHeight().background(Palette.bg)) {
        // the items share the height there is (a landscape phone is only about 400dp tall): they shrink to fit, and still scroll if even that is too much
        val itemH = ((maxHeight - 16.dp) / Tab.entries.size - 2.dp).coerceIn(50.dp, 68.dp)
        Column(
            Modifier.fillMaxSize().verticalScroll(androidx.compose.foundation.rememberScrollState()).padding(vertical = 8.dp),
            verticalArrangement = Arrangement.spacedBy(2.dp, Alignment.CenterVertically), horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            val press = tabPress(nav)
            shownTabs().forEach { t -> NavItem(t, t == cur && t.root != null, Modifier.width(80.dp).height(itemH)) { press(t) } }
        }
    }
}

@Composable
private fun NavItem(t: Tab, selected: Boolean, modifier: Modifier, onClick: () -> Unit) {
    val sc = LocalScheme.current
    val src = remember { MutableInteractionSource() }
    val focused by src.collectIsFocusedAsState()
    val col = if (selected) sc.accent else Palette.dim
    Column(modifier.wheelTracked().clickable(src, null, onClick = onClick), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.Center) {
        // the chosen tab's pill grows out from the icon (a spring, transform and alpha only), and the icon gives a small bounce as it is chosen
        val k = chosenAmount(selected, "tab")
        Box(
            Modifier.size(width = 64.dp, height = 32.dp).clip(RoundedCornerShape(16.dp))
                .background(if (focused && !selected) Color(0x22FFFFFF) else Color.Transparent).ringWhen(focused, RoundedCornerShape(16.dp)),
            contentAlignment = Alignment.Center,
        ) {
            Box(Modifier.matchParentSize().graphicsLayer { scaleX = .4f + .6f * k; scaleY = .7f + .3f * k; alpha = k }.clip(RoundedCornerShape(16.dp)).background(sc.accent.copy(alpha = .16f)))
            GlyphIcon(t.glyph, Modifier.size(24.dp).bumpOnChange(selected), col)
        }
        Txt(t.label, Modifier.padding(top = 4.dp), size = 12f, maxLines = 1, weight = if (selected) FontWeight.SemiBold else FontWeight.Normal, color = col, align = TextAlign.Center)
    }
}

// ---- library tab ---------------------------------------------------------------------------------------------------

@Composable
fun LibraryHome(nav: PlayerNav) {
    LocalLibRev.current
    val app = LocalApp.current
    val lib = app.library
    val ud = app.userData
    ud.rev
    Column(Modifier.fillMaxSize()) {
        LazyColumn(Modifier.fillMaxSize().statusBarsPadding(), contentPadding = PaddingValues(bottom = 16.dp)) {
            // design I: the serif heading, as on the web; a tab's root has no back button
            item {
                Row(Modifier.fillMaxWidth().padding(end = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                    SerifTitle("Your library", Modifier.weight(1f).padding(start = 20.dp, top = 20.dp, bottom = 12.dp), size = 34f)
                    AccountAction()
                }
            }
            // design I, as Home and the web (owner, 25 Nov: "the Library page on mobile doesn't match the app or web": it was a long list of the same
            // pink rows). Your things as four tiles, your playlists as covers, then the library itself as four tiles; the sources as chips
            item(key = "mine") {
                LibTiles(listOf(
                    LibTile("Favourites", Glyph.HEART_FILLED, "${ud.favorites.size} songs") { nav.push(Screen.Detail(DetailKind.FAVORITES)) },
                    LibTile("Recent", Glyph.CLOCK, "${ud.recents.size} played") { nav.push(Screen.Detail(DetailKind.RECENT)) },
                    // the songs kept for listening without signal, apart from the new music fetched into the library
                    LibTile("Offline", Glyph.DOWNLOADED, "${app.offline.doneCount} on this phone") { nav.push(Screen.Downloads) },
                    LibTile("New songs", Glyph.PLUS, "${ud.downloads.size} recently added") { nav.push(Screen.Detail(DetailKind.DOWNLOADS)) },
                ))
            }
            val lists = shownPlaylists(app)
            val nLists = lists.size + shownFolderPlaylists(app).size
            item(key = "pl-head") {
                Row(Modifier.fillMaxWidth().clickable { nav.push(Screen.Lib(LibKind.PLAYLISTS)) }.padding(start = 20.dp, end = 16.dp, top = 18.dp, bottom = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                    SerifTitle("Your playlists", Modifier.weight(1f), size = 24f)
                    DesignChip(if (nLists > 0) "All $nLists" else "New playlist", false, { nav.push(Screen.Lib(LibKind.PLAYLISTS)) })
                }
            }
            if (lists.isNotEmpty()) item(key = "pl-row") {
                val by0 = lib.byPath()
                androidx.compose.foundation.lazy.LazyRow(contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    items(lists.take(12).size) { i ->
                        val pl = lists[i]
                        val ts = remember(pl.id, pl.paths.size) { pl.paths.asSequence().take(6).mapNotNull { by0[it] ?: lib.resolve(it, app.userData.meta[it]) }.toList() }
                        AlbumCard(com.ipodemu.library.Group("${pl.name} (${pl.paths.size})", ts, ts.firstNotNullOfOrNull { it.artKey }), Modifier.width(140.dp)) { nav.push(Screen.Detail(DetailKind.USER, pl.id)) }
                    }
                }
            }
            item(key = "browse-head") { SerifTitle("Browse", Modifier.padding(start = 20.dp, top = 18.dp, bottom = 8.dp), size = 24f) }
            item(key = "browse") {
                LibTiles(listOf(
                    LibTile("Artists", Glyph.ARTIST, "${lib.artists().size}") { nav.push(Screen.Lib(LibKind.ARTISTS)) },
                    LibTile("Albums", Glyph.ALBUM, "${lib.albums().size}") { nav.push(Screen.Lib(LibKind.ALBUMS)) },
                    LibTile("Songs", Glyph.NOTE, "${lib.songs().size}") { nav.push(Screen.Lib(LibKind.SONGS)) },
                    LibTile("Genres", Glyph.STAR, "${lib.genres().size}") { nav.push(Screen.Lib(LibKind.GENRES)) },
                ))
            }
            // where the songs come from, when there is more than one place
            val sources = buildList {
                if (lib.jellyfinTracks.isNotEmpty()) add(Triple("Jellyfin ${lib.jellyfinTracks.size}", Glyph.JELLYFIN, LibKind.JELLYFIN))
                if (lib.plexTracks.isNotEmpty()) add(Triple("Plex ${lib.plexTracks.size}", Glyph.PLEX, LibKind.PLEX))
                lib.nasOnlyTracks().takeIf { it.isNotEmpty() }?.let { add(Triple("NAS only ${it.size}", Glyph.NAS, LibKind.NAS)) }
                if (lib.memos.isNotEmpty()) add(Triple("Voice memos ${lib.memos.size}", Glyph.MIC, LibKind.MEMOS))
            }
            if (sources.isNotEmpty()) item(key = "sources") {
                @OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
                androidx.compose.foundation.layout.FlowRow(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 14.dp), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    for ((label, g, kind) in sources) DesignChip(label, false, { nav.push(Screen.Lib(kind)) }, icon = g)
                }
            }
        }
    }
}

private class LibTile(val title: String, val glyph: Glyph, val sub: String, val onClick: () -> Unit)

/** Tiles two to a row (the Library tab): a glyph in the accent, the name in bold, a short line under it; one tap opens it. */
@Composable
private fun LibTiles(tiles: List<LibTile>) {
    val sc = LocalScheme.current
    val shape = RoundedCornerShape(18.dp)
    Column(Modifier.padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        for (row in tiles.chunked(2)) Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            for (t in row) Row(
                Modifier.weight(1f).height(72.dp).clip(shape).background(Palette.surface).border(1.dp, Palette.line, shape)
                    .semantics { contentDescription = t.title; stateDescription = t.sub; role = androidx.compose.ui.semantics.Role.Button }.clickable(onClick = t.onClick)
                    .padding(horizontal = 14.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Box(Modifier.size(38.dp).clip(RoundedCornerShape(12.dp)).background(sc.accent.copy(alpha = .16f)), contentAlignment = Alignment.Center) { GlyphIcon(t.glyph, Modifier.size(20.dp), sc.accent) }
                Column(Modifier.padding(start = 12.dp)) {
                    Txt(t.title, size = 15.5f, weight = FontWeight.SemiBold, maxLines = 1)
                    Txt(t.sub, size = 12.5f, color = sc.onBgDim, maxLines = 1)
                }
            }
            if (row.size == 1) Box(Modifier.weight(1f))
        }
    }
}

@Composable
private fun LibRow(title: String, g: Glyph, count: String, tint: Color? = null, onClick: () -> Unit) {
    val sc = LocalScheme.current
    IpodRow(onClick, height = 64.dp, leading = {
        Box(Modifier.size(48.dp).clip(RoundedCornerShape(10.dp)).background((tint ?: sc.accent).copy(alpha = .18f)), contentAlignment = Alignment.Center) {
            GlyphIcon(g, Modifier.size(24.dp), tint ?: sc.accent)
        }
    }, trailing = { Txt(count, size = 14f, color = rowDim()) }) {
        Txt(title, size = 17f, weight = FontWeight.Medium)
    }
}
