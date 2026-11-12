package com.ipodemu.player

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.pager.HorizontalPager
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.media3.common.util.UnstableApi
import com.ipodemu.playback.LiveAnalysis

/**
 * Car mode (owner, 12 Nov: "my dad likes the normal Now Playing for car mode, just make it a bit more car friendly"; "don't skimp the features because
 * it's car mode"; "car mode should have all the graphs, just scrollable"): it is the normal full-screen player with every button, its controls bigger,
 * and where the cover is, a strip that swipes sideways from the cover through every live graph. A swipe there changes the picture, never the song.
 * Switched on from Home, Library or the player's More, off with the Car chip at the top (UiState.carOpen). The screen stays on (MainActivity).
 */

/** The graphs car mode swipes through after the cover: every live one the Info card has (the bit rate is not live). */
internal fun carGraphs(): List<Pair<String, String>> = infoGraphs(0, 2).filter { it.first != "rate" }

/** How much bigger the controls are in car mode. */
internal const val CarScale = 1.3f

/** The cover's square in car mode: page 0 is the cover in its frame ([cover]), then one page per graph, with its name, and dots to show where you are. */
@OptIn(UnstableApi::class)
@Composable
internal fun CarPager(side: Dp, accent: Color, tone: Color, ground: Color, playing: Boolean, cover: @Composable () -> Unit) {
    val app = LocalApp.current
    val ctx = androidx.compose.ui.platform.LocalContext.current
    val graphs = remember { carGraphs() }
    val analysis = remember {
        // the song itself, at full level whatever the volume (as the web's graphs read it; owner, 12 Nov: the values were wrong: they sank with the volume)
        LiveAnalysis(app.player.live)
    }
    val state = rememberPagerState { graphs.size + 1 }
    val h = side - 22.dp
    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        HorizontalPager(state, Modifier.size(side, h)) { page ->
            if (page == 0) Box(Modifier.size(side, h), contentAlignment = Alignment.Center) { cover() }
            else {
                val (mode, name) = graphs[page - 1]
                Column(Modifier.size(side, h).clip(RoundedCornerShape(24.dp)).background(tone.copy(alpha = .45f)).padding(12.dp)
                    .semantics { contentDescription = "$name graph" }) {
                    MonoLabel(name, size = 13f, color = Color(0xFFF4EFE6))
                    // only the page on screen draws and listens to the sound
                    if (state.currentPage == page) LiveGraph(mode, analysis, playing, h - 48.dp, accent, tone, ground)
                }
            }
        }
        Row(Modifier.padding(top = 8.dp).height(10.dp), horizontalArrangement = Arrangement.spacedBy(6.dp), verticalAlignment = Alignment.CenterVertically) {
            repeat(graphs.size + 1) { i ->
                Box(Modifier.size(if (i == state.currentPage) 9.dp else 6.dp).clip(CircleShape).background(if (i == state.currentPage) accent else Color(0x66F4EFE6)))
            }
        }
    }
}
