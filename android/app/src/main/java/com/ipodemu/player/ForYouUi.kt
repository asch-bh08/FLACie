package com.ipodemu.player

import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyListScope
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.background
import androidx.compose.runtime.Composable
import androidx.compose.runtime.compositionLocalOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.ipodemu.library.Mix

/**
 * True when the app is showing a faithful device recreation ("Player in an iPod body"): there the UI sticks to what the
 * real hardware had (the classic menu list, no recommendations, no swipe shortcuts). The modern Player keeps everything.
 */
val LocalHardware = compositionLocalOf { false }

/** A generated mix: cover of its first track, an iPod-style glossy badge, title and one-line reason. */
@Composable
fun MixCard(mix: Mix, modifier: Modifier = Modifier, onClick: () -> Unit) {
    val sc = LocalScheme.current
    val src = remember { MutableInteractionSource() }
    val focused by src.collectIsFocusedAsState()
    Column(modifier.clip(RoundedCornerShape(14.dp)).clickable(src, null, onClick = onClick).padding(4.dp)) {
        Box {
            // the first song with a cover already here, so a mix opening on an uncovered song still has a picture
            val app = LocalApp.current
            val key = remember(mix.id, mix.tracks.size, LocalLibRev.current) { mix.tracks.mapNotNull { it.artKey }.distinct().take(12).firstOrNull { app.art.has(it) } ?: mix.artKey }
            ArtImage(key, Modifier.fillMaxWidth().aspectRatio(1f).border(if (focused) 3.dp else 0.dp, if (focused) sc.accent else Color.Transparent, RoundedCornerShape(12.dp)), corner = 12.dp)
            Box(
                Modifier.align(Alignment.BottomStart).padding(8.dp).size(30.dp).clip(CircleShape).background(sc.accent),
                contentAlignment = Alignment.Center,
            ) { GlyphIcon(Glyph.PLAY, Modifier.size(14.dp), sc.accent.readableInk()) }
        }
        Txt(mix.title, Modifier.padding(top = 8.dp), size = 14f, weight = FontWeight.SemiBold, maxLines = 1)
        Txt(mix.subtitle, size = 12f, color = sc.onBgDim, maxLines = 2)
    }
}

/** "Made for You" (taste-driven) and "Explore" (genre / decade / discover) shelves for the modern Home. */
fun LazyListScope.forYouShelves(mixes: List<Mix>, nav: PlayerNav) {
    val personal = mixes.filter { it.id.startsWith("daily") || it.id in setOf("suggested", "repeat", "rediscover") }
    val explore = mixes - personal.toSet()
    fun shelf(title: String, list: List<Mix>) {
        if (list.isEmpty()) return
        item { SectionHeader(title) }
        item {
            LazyRow(contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                items(list, key = { it.id }) { m -> MixCard(m, Modifier.width(140.dp)) { nav.push(Screen.Detail(DetailKind.MIX, m.id)) } }
            }
        }
    }
    shelf("Made for You", personal)
    shelf("Explore", explore)
}

/** The classic iPod main menu, used in the faithful body view. */
@Composable
fun HardwareHome(nav: PlayerNav, snap: PlayerSnap) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    Column(Modifier.fillMaxSize()) {
        TopBar("iPod", nav, showBack = false)
        LazyColumn(Modifier.fillMaxSize()) {
            item { HwRow("Music", true) { nav.push(Screen.Music) } }
            item { HwRow("Voice Memos", true) { nav.push(Screen.Lib(LibKind.MEMOS)) } }
            item { HwRow("Settings", true) { nav.push(Screen.Settings) } }
            item { HwRow("Shuffle Songs", false) { val s = app.library.songs(); if (s.isNotEmpty()) { app.player.shuffleAll(s) } } }
            if (snap.track != null) item { HwRow("Now Playing", true) { nav.nowPlaying = true } }
        }
    }
}

@Composable
fun MusicMenu(nav: PlayerNav) {
    Column(Modifier.fillMaxSize()) {
        TopBar("Music", nav, showBack = true)
        LazyColumn(Modifier.fillMaxSize()) {
            listOf(LibKind.PLAYLISTS, LibKind.ARTISTS, LibKind.ALBUMS, LibKind.SONGS, LibKind.GENRES).forEach { k ->
                item { HwRow(k.title, true) { nav.push(Screen.Lib(k)) } }
            }
        }
    }
}

@Composable
private fun HwRow(title: String, chevron: Boolean, onClick: () -> Unit) {
    IpodRow(onClick, height = 52.dp, trailing = { if (chevron) GlyphIcon(Glyph.CHEVRON, Modifier.size(18.dp), rowDim()) }) { hi ->
        Txt(title, size = 17f, weight = FontWeight.Medium, color = if (hi) Color.White else LocalScheme.current.onBg)
    }
}

/**
 * "Quick picks": small cover, title and artist in rows of four, a few songs wide, swiped sideways (the YouTube Music shelf). A tap plays that song
 * with the rest of the shelf queued behind it.
 */
@Composable
fun QuickPicks(tracks: List<com.ipodemu.library.Track>, onPlay: (Int) -> Unit) {
    val sc = LocalScheme.current
    val pageW = (androidx.compose.ui.platform.LocalConfiguration.current.screenWidthDp * 0.86f).coerceAtMost(420f).dp
    val pages = remember(tracks) { tracks.chunked(4) }
    androidx.compose.foundation.lazy.LazyRow(contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
        itemsIndexed(pages) { pi, page ->
            Column(Modifier.width(pageW)) {
                page.forEachIndexed { ri, t ->
                    val idx = pi * 4 + ri
                    Row(
                        Modifier.fillMaxWidth().height(60.dp).clip(RoundedCornerShape(10.dp)).clickable { onPlay(idx) }.padding(horizontal = 4.dp, vertical = 5.dp),
                        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp),
                    ) {
                        ArtImage(t.artKey, Modifier.size(50.dp), thumb = true, corner = 6.dp)
                        Column(Modifier.weight(1f)) {
                            Txt(t.title, size = 15f, weight = FontWeight.SemiBold, maxLines = 1)
                            Txt(t.artist, size = 12.5f, color = sc.onBgDim, maxLines = 1)
                        }
                    }
                }
            }
        }
    }
}
