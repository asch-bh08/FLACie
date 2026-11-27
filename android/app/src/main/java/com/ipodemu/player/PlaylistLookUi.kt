package com.ipodemu.player

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.compositionLocalOf
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.blur
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.Role
import androidx.compose.runtime.getValue
import androidx.compose.runtime.setValue
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp

/*
 * A playlist (and Favourites) in the look of the Devices page and Now Playing, the same page as the web's (design-i-playlists.css, .pl-page):
 * rounded glass cards with the cover blurred behind the hero (DevicesUi.kt's DeviceCard), Play and Shuffle as full pills, the quieter actions as
 * Now Playing's row of equal slots, the songs on one glass card with the playing row tinted in the cover's colour, and tags as the Devices chip
 * (an outlined round pill), only the ones that say something: Hi-Res, Streaming and the download state.
 */

/** The Devices page's glass: the kit's faint fill and hairline rim. */
internal val GlassFill = Kit.glassFill
internal val GlassRim = Kit.glassRim

/** The first cover's colours, for the page's tinted parts. Rows read it from [LocalPlaylistLook]; null everywhere else keeps the plain rows. */
class PlaylistLook(val colours: SleeveColours) {
    val accent = Color(colours.main)
}

val LocalPlaylistLook = compositionLocalOf<PlaylistLook?> { null }


/** One song's Edit-mode buttons: download, remove, and the ≡ handle to press and drag it up or down (owner, 26 Nov: as in Spotify). [handle] carries the drag. */
@Composable
fun PlaylistEditButtons(downloaded: Boolean, handle: Modifier, onDownload: () -> Unit, onRemove: () -> Unit) {
    val sc = LocalScheme.current
    // 48 dp targets in the kit's rounded square, as the web's Edit page (owner, 27 Nov: bigger ✕ and ≡)
    @Composable fun b(icon: Glyph, desc: String, enabled: Boolean, tint: Color = sc.onBg, onClick: () -> Unit) {
        Box(Modifier.size(Kit.touch).clip(RoundedCornerShape(Kit.rButtonSmall)).semantics { role = Role.Button; contentDescription = desc }
            .clickable(enabled = enabled, onClick = onClick), contentAlignment = Alignment.Center) {
            GlyphIcon(icon, Modifier.size(20.dp), tint)
        }
    }
    Row(verticalAlignment = Alignment.CenterVertically) {
        b(Glyph.DOWNLOADED, if (downloaded) "On this phone" else "Download", !downloaded, tint = if (downloaded) Color(0xFF6BD58A) else sc.onBg, onClick = onDownload)
        b(Glyph.CLOSE, "Remove from playlist", true, onClick = onRemove)
        // the handle reads as one: grip lines on a glass square
        Box(handle.size(Kit.touch).clip(RoundedCornerShape(Kit.rButtonSmall)).background(Kit.glassFill).semantics { contentDescription = "Hold and drag to move" }, contentAlignment = Alignment.Center) {
            GlyphIcon(Glyph.LIST, Modifier.size(22.dp), sc.onBg)
        }
    }
}

@Composable
fun rememberPlaylistLook(art: String?): PlaylistLook {
    val cols = rememberCoverColours(art)
    return remember(cols) { PlaylistLook(cols) }
}

/** What a tag says, which picks its ink (the web's .badge.fmt.hires, .badge.cloud). */
enum class TagKind { PLAIN, HIRES, CLOUD }

/** One tag: the kit's StatusTag (the Devices chip). [small] trims it to fit in front of the artist on a narrow row. */
@Composable
fun PlTag(text: String, kind: TagKind = TagKind.PLAIN, small: Boolean = false) =
    StatusTag(text, when (kind) { TagKind.HIRES -> StatusTone.HIRES; TagKind.CLOUD -> StatusTone.CLOUD; TagKind.PLAIN -> StatusTone.PLAIN }, compact = small)

/** A quieter action of the page: the kit's Slot, the icon over its word, so four fit a phone's row with their words whole (owner, 26 Nov: the
 *  cut-off pills looked odd); [on] fills it with the ink as every slot does now. [accent] is kept for old callers. */
@Composable
fun ToolSlot(label: String, icon: Glyph, onClick: () -> Unit, modifier: Modifier = Modifier, on: Boolean = false, @Suppress("UNUSED_PARAMETER") accent: Color = LocalScheme.current.accent) =
    Slot(label, icon, onClick, modifier, on = on)

/** The hero: a glass card with the cover blurred behind it (a Devices card), holding the cover, the title, the facts and the buttons. */
@Composable
fun PlaylistHeroCard(look: PlaylistLook, art: String?, content: @Composable ColumnScope.() -> Unit) {
    val shape = RoundedCornerShape(22.dp)
    Box(Modifier.fillMaxWidth().clip(shape).background(GlassFill).border(1.dp, GlassRim, shape)) {
        // the cover's colours behind the card (a plain faint fill on phones too old to blur), and the accent faintly when there is no cover
        if (art != null) ArtImage(art, Modifier.matchParentSize().blur(48.dp).alpha(.4f), thumb = true, corner = 0.dp)
        else Box(Modifier.matchParentSize().background(look.accent.copy(alpha = .14f)))
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) { content() }
    }
}

/** The songs' glass card is drawn a row at a time (the rows are a lazy list): the first rounds the top, the last the bottom (the web's ol.tracks card). */
fun Modifier.playlistCardRow(index: Int, count: Int): Modifier {
    val top = if (index == 0) 22.dp else 0.dp
    val bottom = if (index == count - 1) 22.dp else 0.dp
    return this.padding(horizontal = 12.dp).clip(RoundedCornerShape(top, top, bottom, bottom)).background(GlassFill)
        .padding(start = 4.dp, end = 4.dp, top = if (index == 0) 4.dp else 0.dp, bottom = if (index == count - 1) 4.dp else 0.dp)
}

/** The playing row: tinted in the cover's colour, as Now Playing's queue marks it. */
fun Modifier.playlistNowRow(look: PlaylistLook?, now: Boolean): Modifier =
    if (look != null && now) this.clip(RoundedCornerShape(16.dp)).background(look.accent.copy(alpha = .20f)) else this
