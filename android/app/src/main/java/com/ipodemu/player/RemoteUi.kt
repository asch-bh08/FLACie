package com.ipodemu.player

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.height
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.ipodemu.library.JellyfinConnect
import kotlinx.coroutines.delay

/**
 * Remote playback, like Spotify Connect: when another of this user's devices (the web, Windows, another phone) is playing and this phone is not,
 * a bar at the bottom shows what it plays with play/pause, next and a Devices shortcut; tapping it opens a full-screen remote with the cover,
 * a seek bar, the transport, "Play here" (takes the queue over, same second) and the list of devices.
 */

/** The device whose bar to show: one that is playing (a paused one is only offered inside the full-screen remote). */
fun JellyfinConnect.remoteNow(includePaused: Boolean = false): JellyfinConnect.Session? {
    val others = sessions.filter { !it.isSelf && it.controllable && it.itemId != null }
    return others.firstOrNull { !it.paused } ?: if (includePaused) others.firstOrNull() else null
}

/** Where the song is on the other device right now: its last reported spot plus the time since (while it plays). */
private fun JellyfinConnect.Session.estimatedMs(now: Long): Long =
    if (paused) positionMs else (positionMs + (now - at)).coerceAtMost(if (durationMs > 0) durationMs else Long.MAX_VALUE)

@Composable
fun RemoteMiniPlayer(id: String) {
    val app = LocalApp.current
    val s = app.connect.sessions.firstOrNull { it.id == id } ?: return
    val sc = LocalScheme.current
    var now by remember { mutableLongStateOf(System.currentTimeMillis()) }
    LaunchedEffect(s.id, s.paused) { while (true) { now = System.currentTimeMillis(); delay(500) } }
    val frac = if (s.durationMs > 0) (s.estimatedMs(now).toFloat() / s.durationMs).coerceIn(0f, 1f) else 0f
    Box(
        Modifier.fillMaxWidth().padding(start = 12.dp, end = 12.dp, top = 6.dp, bottom = 10.dp).height(66.dp).clip(RoundedCornerShape(14.dp))
            .background(Palette.surface).border(1.dp, Palette.line, RoundedCornerShape(14.dp))
            .clickable { app.ui.remoteOpen = true },
    ) {
        Row(Modifier.fillMaxSize().padding(start = 8.dp, end = 4.dp), verticalAlignment = Alignment.CenterVertically) {
            ArtImage(s.artKey ?: "jf${s.itemId}", Modifier.size(50.dp).popIn(s.itemId, 0.7f), thumb = true, corner = 6.dp)
            Column(Modifier.weight(1f).padding(horizontal = 12.dp)) {
                Txt("Listening on ${s.device}", size = 11f, weight = FontWeight.Bold, color = sc.accent, maxLines = 1)
                Txt(s.title, size = 15f, weight = FontWeight.SemiBold, maxLines = 1)
                Txt(s.artist, size = 12f, color = sc.onBgDim, maxLines = 1)
            }
            IconAction(Glyph.DEVICES, "Devices", { app.ui.devicesOpen = true }, tint = sc.accent)
            IconAction(if (s.paused) Glyph.PLAY else Glyph.PAUSE, if (s.paused) "Play on ${s.device}" else "Pause ${s.device}", { app.connect.command(s, "PlayPause") })
        }
        Box(Modifier.align(Alignment.BottomStart).padding(horizontal = 8.dp).fillMaxWidth().height(2.dp).background(sc.onBg.copy(alpha = .18f))) {
            Box(Modifier.fillMaxWidth(frac).fillMaxHeight().background(sc.accent))
        }
    }
}

@Composable
fun RemoteScreen() {
    val app = LocalApp.current
    val ui = app.ui
    val c = app.connect
    val sc = LocalScheme.current
    BackHandler { ui.remoteOpen = false }
    // keep the other devices' state fresh while this is open
    LaunchedEffect(Unit) { while (true) { c.loadSessions(); delay(2_500) } }
    var pick by remember { mutableStateOf<String?>(null) }
    val others = c.sessions.filter { !it.isSelf }
    val s = others.firstOrNull { it.id == pick && it.itemId != null } ?: c.remoteNow(includePaused = true)
    var now by remember { mutableLongStateOf(System.currentTimeMillis()) }
    LaunchedEffect(Unit) { while (true) { now = System.currentTimeMillis(); delay(400) } }

    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.TopCenter) {
        Column(
            Modifier.widthIn(max = 560.dp).fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = 22.dp, vertical = 8.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            Row(Modifier.fillMaxWidth().height(52.dp), verticalAlignment = Alignment.CenterVertically) {
                IconAction(Glyph.DOWN, "Close", { ui.remoteOpen = false }, tint = Color.White)
                Txt(if (s != null) "Listening on ${s.device}" else "Remote", Modifier.weight(1f), size = 14f, weight = FontWeight.SemiBold, color = sc.accent, align = androidx.compose.ui.text.style.TextAlign.Center)
                IconAction(Glyph.DEVICES, "Devices", { ui.devicesOpen = true }, tint = Color.White)
            }
            if (s == null) {
                Txt("Nothing is playing on your other devices right now. Start a song on the web, on Windows or on another phone and it shows up here.", size = 15f, color = Color(0xCCFFFFFF), maxLines = 4)
                GlossPill("Open Devices", { ui.devicesOpen = true }, primary = true)
                return@Column
            }
            Box(Modifier.fillMaxWidth()) {
                ArtImage(s.artKey ?: "jf${s.itemId}", Modifier.align(Alignment.Center).fillMaxWidth(0.86f).aspectRatio(1f).popIn(s.itemId, 0.9f), thumb = false, corner = 14.dp)
            }
            Column(Modifier.fillMaxWidth().riseIn(80, s.itemId), verticalArrangement = Arrangement.spacedBy(14.dp)) {
                Column {
                    Txt(s.title, size = 24f, weight = FontWeight.Bold, maxLines = 2)
                    Txt(s.artist, Modifier.padding(top = 2.dp), size = 16f, color = sc.onBgDim, maxLines = 1)
                }
                val est = s.estimatedMs(now)
                RemoteSeek(if (s.durationMs > 0) (est.toFloat() / s.durationMs).coerceIn(0f, 1f) else 0f, s.controllable && s.durationMs > 0) { f -> c.seek(s, (f * s.durationMs).toLong()) }
                Row(Modifier.fillMaxWidth()) {
                    Txt(fmtTime(est), size = 12f, color = sc.onBgDim)
                    Box(Modifier.weight(1f))
                    Txt(fmtTime(s.durationMs), size = 12f, color = sc.onBgDim)
                }
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceEvenly, verticalAlignment = Alignment.CenterVertically) {
                    IconAction(Glyph.PREV, "Previous on ${s.device}", { c.command(s, "PreviousTrack") }, tint = Color.White, size = 56.dp, iconScale = 0.5f)
                    GlossButton({ c.command(s, "PlayPause") }, size = 72.dp, primary = true, label = if (s.paused) "Play on ${s.device}" else "Pause ${s.device}") {
                        GlyphIcon(if (s.paused) Glyph.PLAY else Glyph.PAUSE, Modifier.size(34.dp), Color.White)
                    }
                    IconAction(Glyph.NEXT, "Next on ${s.device}", { c.command(s, "NextTrack") }, tint = Color.White, size = 56.dp, iconScale = 0.5f)
                }
                // Spotify-style: bring the music to this phone, same song and same second
                GlossPill("Play here", { c.takeOver(s); ui.remoteOpen = false }, Modifier.fillMaxWidth(), icon = Glyph.PLAY, primary = true, height = 50.dp)
            }
            Txt("DEVICES", Modifier.padding(top = 6.dp), size = 11f, weight = FontWeight.ExtraBold, color = sc.onBgDim)
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                DeviceRow("This phone", app.player.current?.let { "${it.title} · ${it.artist}" } ?: "Nothing playing here", false) { }
                for (o in others) {
                    val sub = if (o.itemId != null) "${o.title} · ${o.artist}${if (o.paused) " (paused)" else ""}" else "Nothing playing"
                    DeviceRow("${o.device} · ${o.client}", sub, o.id == s.id) { if (o.itemId != null) pick = o.id }
                }
            }
        }
    }
}


@Composable
private fun DeviceRow(title: String, subtitle: String, selected: Boolean, onClick: () -> Unit) {
    val sc = LocalScheme.current
    Row(
        Modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp)).background(if (selected) sc.accent.copy(alpha = 0.16f) else Color(0x14FFFFFF)).clickable(onClick = onClick).padding(14.dp),
        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        GlyphIcon(Glyph.DEVICES, Modifier.size(22.dp), if (selected) sc.accent else Color.White)
        Column(Modifier.weight(1f)) {
            Txt(title, size = 15f, weight = FontWeight.SemiBold, color = Color.White, maxLines = 1)
            Txt(subtitle, size = 12.5f, color = Color(0xBBFFFFFF), maxLines = 1)
        }
        if (selected) Box(Modifier.size(8.dp).clip(CircleShape).background(sc.accent))
    }
}

/** A seek line you can tap or drag; reports the new place (0..1) when you let go. */
@Composable
private fun RemoteSeek(frac: Float, enabled: Boolean, onSeek: (Float) -> Unit) {
    val sc = LocalScheme.current
    var drag by remember { mutableFloatStateOf(-1f) }
    var width by remember { mutableFloatStateOf(1f) }
    val shown = if (drag >= 0f) drag else frac
    Box(
        Modifier.fillMaxWidth().height(28.dp)
            .pointerInput(enabled) {
                if (!enabled) return@pointerInput
                detectTapGestures { o -> onSeek((o.x / width).coerceIn(0f, 1f)) }
            }
            .pointerInput(enabled) {
                if (!enabled) return@pointerInput
                detectDragGestures(onDragStart = { drag = (it.x / width).coerceIn(0f, 1f) }, onDragEnd = { if (drag >= 0f) onSeek(drag); drag = -1f }, onDragCancel = { drag = -1f }) { ch, _ -> drag = (ch.position.x / width).coerceIn(0f, 1f) }
            }
            .onSizeChanged { width = it.width.toFloat().coerceAtLeast(1f) },
        contentAlignment = Alignment.CenterStart,
    ) {
        Box(Modifier.fillMaxWidth().height(4.dp).clip(RoundedCornerShape(2.dp)).background(Color(0x33FFFFFF))) {
            Box(Modifier.fillMaxWidth(shown).fillMaxHeight().background(sc.accent))
        }
        Box(Modifier.padding(start = 0.dp).fillMaxWidth(shown.coerceAtLeast(0.001f)), contentAlignment = Alignment.CenterEnd) {
            Box(Modifier.size(14.dp).clip(CircleShape).background(Color.White))
        }
    }
}

