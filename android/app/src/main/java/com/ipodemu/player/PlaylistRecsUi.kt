package com.ipodemu.player

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.ipodemu.library.FlacieWebClient
import com.ipodemu.library.PlaylistRecs
import com.ipodemu.library.Track
import com.ipodemu.library.matchKey
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/**
 * "Recommended songs" under one of the person's own playlists (the web's PlaylistRecsSection.razor): about ten songs from FLACie Web that go with it,
 * loaded after the songs show, each with a "+" that adds it to the end of the playlist (the row shows a tick, then leaves). Refresh loads the next set.
 * Hidden for an empty playlist, without FLACie Web, and when the server fails.
 */
@Composable
fun PlaylistRecsSection(playlistId: String, songs: List<Track>, title: String) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    if (songs.isEmpty()) return
    val ud = app.userData
    val web = remember { FlacieWebClient(app.prefs) }
    if (!web.available) return
    val scope = androidx.compose.runtime.rememberCoroutineScope()
    var picks by remember(playlistId) { mutableStateOf<List<Track>?>(null) }
    var failed by remember(playlistId) { mutableStateOf(false) }
    var skip by remember(playlistId) { mutableIntStateOf(0) }
    val ticked = remember(playlistId) { androidx.compose.runtime.mutableStateListOf<String>() }
    val inList = songs.mapTo(HashSet()) { it.matchKey }
    LaunchedEffect(playlistId, skip) {
        picks = null
        val key = ud.playlists.firstOrNull { it.id == playlistId }?.let { it.jfId ?: it.id } ?: playlistId
        val got = try {
            withContext(Dispatchers.IO) {
                val raw = web.recommendPlaylist(key, PlaylistRecs.N, skip)
                PlaylistRecs.onPhone(raw, { p -> app.library.resolve(p.path, p.title to p.artist) }, inList)
            }
        } catch (_: Exception) { null }
        if (got.isNullOrEmpty()) failed = true else picks = got
    }
    if (failed) return
    // on a playlist page: a glass card of its own, as on the web (PlaylistLookUi.kt)
    val glass = LocalPlaylistLook.current != null
    val card = RoundedCornerShape(22.dp)
    Column(if (glass) Modifier.padding(start = 12.dp, end = 12.dp, top = 18.dp, bottom = 8.dp).clip(card).background(GlassFill).border(1.dp, GlassRim, card).padding(bottom = 6.dp)
        else Modifier.padding(bottom = 8.dp)) {
        SectionHeader("Recommended songs")
        Txt("Based on the songs in this playlist", Modifier.padding(start = 20.dp, bottom = 8.dp), size = 13f, color = sc.onBgDim)
        val shown = picks
        if (shown == null) { LoadingRows("Finding songs…"); return@Column }
        for (t in shown.filter { it.path in ticked || it.matchKey !in inList }) {
            val done = t.path in ticked
            IpodRow({ app.player.play(listOf(t), 0, null, title) }, height = 56.dp,
                leading = { ArtImage(t.artKey, Modifier.size(44.dp), thumb = true, corner = 10.dp) },
                trailing = {
                    // a round outlined "+" (the Devices buttons), green once added
                    val ring = if (done) androidx.compose.ui.graphics.Color(0x8C4EE0A1) else sc.onBg.copy(alpha = .35f)
                    Box(Modifier.size(40.dp).clip(CircleShape).then(if (glass) Modifier.border(1.dp, ring, CircleShape) else Modifier).clickable(enabled = !done) {
                        if (PlaylistRecs.add(ud, playlistId, t)) {
                            ticked.add(t.path)
                            app.ui.say("Added “${t.title}” to $title")
                            scope.launch { delay(900); ticked.remove(t.path); picks = picks?.filter { it.path != t.path } }
                        }
                    }.semantics { contentDescription = if (done) "Added ${t.title}" else "Add ${t.title} to this playlist" }, contentAlignment = Alignment.Center) {
                        GlyphIcon(if (done) Glyph.CHECK else Glyph.PLUS, Modifier.size(if (glass) 18.dp else 22.dp), if (!glass) sc.accent else if (done) androidx.compose.ui.graphics.Color(0xFF4EE0A1) else sc.onBg)
                    }
                }) { hi ->
                Column { Txt(t.title, size = 16f, maxLines = 1, color = if (hi) androidx.compose.ui.graphics.Color.White else sc.onBg); Txt(t.artist, size = 13f, color = sc.onBgDim, maxLines = 1) }
            }
        }
        Row(Modifier.padding(horizontal = 16.dp, vertical = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            GlossPill("Refresh", { skip += PlaylistRecs.N }, icon = Glyph.SHUFFLE, height = 34.dp)
        }
    }
}
