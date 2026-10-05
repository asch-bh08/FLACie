package com.ipodemu.player

import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.add
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
    val bg = Color(0xFF0B0B0E); val surface = Color(0xFF15151A); val surface2 = Color(0xFF1E1E25); val surface3 = Color(0xFF282832)
    val line = Color(0xFF2A2A33); val ink = Color(0xFFF4F3F7); val dim = Color(0xFFA9A6B3); val faint = Color(0xFF6F6C78); val pink = Color(0xFFFF4D73)
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
        Txt(title, Modifier.weight(1f).padding(start = 4.dp), size = 22f, weight = FontWeight.Bold)
        Row(horizontalArrangement = Arrangement.spacedBy(4.dp), verticalAlignment = Alignment.CenterVertically) { actions() }
    }
}

/** A 48dp icon button (Material minimum touch target) -- the Modern theme's top-bar and toolbar action. */
@Composable
fun IconAction(g: Glyph, label: String, onClick: () -> Unit, tint: Color = LocalScheme.current.onBg, size: Dp = 48.dp, iconScale: Float = 0.5f) {
    val src = remember { MutableInteractionSource() }
    val focused by src.collectIsFocusedAsState()
    val pressed by src.collectIsPressedAsState()
    Box(
        Modifier.size(size).pressSpring(pressed, 0.82f).clip(CircleShape).background(if (focused || pressed) Color(0x33FFFFFF) else Color.Transparent)
            .semanticsLabel(label).wheelTracked().clickable(src, null, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) { GlyphIcon(g, Modifier.size(size * iconScale).bumpOnChange(g), tint) }   // a heart turning solid bounces
}

private fun Modifier.semanticsLabel(label: String) = this.semantics { contentDescription = label; role = androidx.compose.ui.semantics.Role.Button }

// ---- navigation ----------------------------------------------------------------------------------------------------

enum class Tab(val label: String, val glyph: Glyph, val root: Screen) {
    HOME("Home", Glyph.NOTE, Screen.Home), SEARCH("Search", Glyph.SEARCH, Screen.Search),
    PLAYLISTS("Playlists", Glyph.LIST, Screen.Lib(LibKind.PLAYLISTS)), LIBRARY("Explore", Glyph.ALBUM, Screen.Library), SETTINGS("Settings", Glyph.GEAR, Screen.Settings),
}

fun PlayerNav.currentTab(): Tab = Tab.entries.firstOrNull { it.root == stack.first() } ?: Tab.HOME
fun PlayerNav.selectTab(t: Tab) {
    nowPlaying = false; queueFromPlayer = false
    if (stack.size == 1 && stack.first() == t.root) return
    stack.clear(); stack.add(t.root)
}

/** Bottom navigation bar for phones and the Fold's cover screen. */
@Composable
fun BottomNav(nav: PlayerNav) {
    val sc = LocalScheme.current
    val cur = nav.currentTab()
    Row(Modifier.fillMaxWidth().height(72.dp).background(Palette.bg).drawBehind { drawLine(Palette.line, androidx.compose.ui.geometry.Offset(0f, 0f), androidx.compose.ui.geometry.Offset(size.width, 0f), 1f) }.padding(horizontal = 8.dp), verticalAlignment = Alignment.CenterVertically) {
        Tab.entries.forEach { t -> NavItem(t, t == cur, Modifier.weight(1f).fillMaxHeight()) { nav.selectTab(t) } }
    }
}

/** Navigation rail for wide screens: the unfolded Fold, tablets, landscape, and the square RG Rotate panel (where a
 * bottom bar would eat too much of the short height). */
@Composable
fun NavRail(nav: PlayerNav) {
    val cur = nav.currentTab()
    Column(
        Modifier.width(80.dp).fillMaxHeight().background(Palette.bg).verticalScroll(androidx.compose.foundation.rememberScrollState()).padding(vertical = 12.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp, Alignment.CenterVertically), horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Tab.entries.forEach { t -> NavItem(t, t == cur, Modifier.width(80.dp).height(68.dp)) { nav.selectTab(t) } }
    }
}

@Composable
private fun NavItem(t: Tab, selected: Boolean, modifier: Modifier, onClick: () -> Unit) {
    val sc = LocalScheme.current
    val src = remember { MutableInteractionSource() }
    val focused by src.collectIsFocusedAsState()
    val col = if (selected) sc.accent else Palette.dim
    Column(modifier.wheelTracked().clickable(src, null, onClick = onClick), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.Center) {
        Box(
            Modifier.size(width = 64.dp, height = 32.dp).clip(RoundedCornerShape(16.dp))
                .background(if (selected) sc.accent.copy(alpha = .16f) else if (focused) Color(0x22FFFFFF) else Color.Transparent),
            contentAlignment = Alignment.Center,
        ) { GlyphIcon(t.glyph, Modifier.size(22.dp), col) }
        Txt(t.label, Modifier.padding(top = 6.dp), size = 12f, weight = if (selected) FontWeight.SemiBold else FontWeight.Normal, color = col, align = TextAlign.Center)
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
        ModernTopBar("Library", null)
        LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 16.dp)) {
            item { LibRow("Favorites", Glyph.HEART_FILLED, "${ud.favorites.size}", Color(0xFFE5486B)) { nav.push(Screen.Detail(DetailKind.FAVORITES)) } }
            item { LibRow("Recently Played", Glyph.CLOCK, "${ud.recents.size}") { nav.push(Screen.Detail(DetailKind.RECENT)) } }
            item { LibRow("Recently Downloaded", Glyph.DOWN, "${ud.downloads.size}") { nav.push(Screen.Detail(DetailKind.DOWNLOADS)) } }
            item { LibRow("Playlists", Glyph.LIST, "${shownPlaylists(app).size + shownFolderPlaylists(app).size}") { nav.push(Screen.Lib(LibKind.PLAYLISTS)) } }
            item { LibRow("Artists", Glyph.ARTIST, "${lib.artists().size}") { nav.push(Screen.Lib(LibKind.ARTISTS)) } }
            item { LibRow("Albums", Glyph.ALBUM, "${lib.albums().size}") { nav.push(Screen.Lib(LibKind.ALBUMS)) } }
            item { LibRow("Songs", Glyph.NOTE, "${lib.songs().size}") { nav.push(Screen.Lib(LibKind.SONGS)) } }
            item { LibRow("Genres", Glyph.STAR, "${lib.genres().size}") { nav.push(Screen.Lib(LibKind.GENRES)) } }
            if (lib.memos.isNotEmpty()) item { LibRow("Voice Memos", Glyph.MIC, "${lib.memos.size}") { nav.push(Screen.Lib(LibKind.MEMOS)) } }
            if (lib.jellyfinTracks.isNotEmpty()) item { LibRow("Jellyfin", Glyph.JELLYFIN, "${lib.jellyfinTracks.size}") { nav.push(Screen.Lib(LibKind.JELLYFIN)) } }
            if (lib.plexTracks.isNotEmpty()) item { LibRow("Plex", Glyph.PLEX, "${lib.plexTracks.size}") { nav.push(Screen.Lib(LibKind.PLEX)) } }
            lib.nasOnlyTracks().takeIf { it.isNotEmpty() }?.let { n -> item { LibRow("NAS only", Glyph.NAS, "${n.size}") { nav.push(Screen.Lib(LibKind.NAS)) } } }
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
