package com.ipodemu.player

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.delay

/**
 * Devices (Connect): this user's other FLACie and Jellyfin sessions and what each is playing. Control one from here,
 * take its music over to this device (same queue, same second), or send what plays here to it.
 */
@Composable
fun DevicesScreen() {
    val app = LocalApp.current
    val ui = app.ui
    val c = app.connect
    val sc = LocalScheme.current
    BackHandler { ui.devicesOpen = false }
    // refresh while open: other devices' positions move
    LaunchedEffect(Unit) { while (true) { c.loadSessions(); delay(4_000) } }
    val here = app.player.current
    val others = c.sessions.filter { !it.isSelf }

    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.TopCenter) {
        Column(
            Modifier.widthIn(max = 560.dp).fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = 20.dp, vertical = 8.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                IconAction(Glyph.BACK, "Back", { ui.devicesOpen = false }, tint = Color.White)
                Txt("Devices", Modifier.padding(start = 4.dp), size = 22f, weight = FontWeight.Bold, color = Color.White)
            }
            if (!c.available) {
                Txt("Sign in with Jellyfin to see and control your other devices.", size = 15f, color = Color(0xCCFFFFFF), maxLines = 3)
                return@Column
            }
            DeviceCard("This phone", listOf("This device", if (here != null) "Playing" else "Idle"), here?.title, here?.artist, here?.artKey, null, Glyph.DEVICES, highlight = true) {}
            if (!c.connected) Txt("Connecting to Jellyfin...", size = 14f, color = Color(0x99FFFFFF))
            else if (others.isEmpty()) Txt("No other devices are signed in to ${app.prefs.accountUserName} right now. Open FLACie on the web, on Windows or on another phone, and it shows up here.", size = 14f, color = Color(0x99FFFFFF), maxLines = 4)
            for (s in others) {
                val playing = s.itemId != null
                val kind = deviceKind(s.device)
                DeviceCard(s.device, listOf(kind, if (!playing) "Idle" else if (s.paused) "Paused" else "Playing"), s.title.takeIf { playing }, s.artist.takeIf { playing }, s.artKey, if (playing && s.durationMs > 0) (s.estimatedMs(System.currentTimeMillis()).toFloat() / s.durationMs).coerceIn(0f, 1f) else null, if (kind == "Phone") Glyph.DEVICES else Glyph.DEVICES) {
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        if (playing && s.controllable) {
                            IconAction(if (s.paused) Glyph.PLAY else Glyph.PAUSE, if (s.paused) "Play on ${s.device}" else "Pause ${s.device}", { c.command(s, "PlayPause") }, tint = Color.White, size = 40.dp, iconScale = 0.5f)
                            IconAction(Glyph.NEXT, "Next on ${s.device}", { c.command(s, "NextTrack") }, tint = Color.White, size = 40.dp, iconScale = 0.5f)
                        }
                    }
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        if (playing) GlossPill("Play here", { c.takeOver(s) }, primary = true, height = 36.dp)
                        if (here != null && s.controllable) GlossPill("Send here", { c.sendTo(s) }, height = 36.dp)
                    }
                }
            }
            Txt("Songs from Jellyfin move between devices. Files stored only on one device stay there.", size = 13f, color = Color(0x80FFFFFF), maxLines = 3)
        }
    }
}

/** Jellyfin only gives a device name and the app name (the same for every FLACie player), so the kind is guessed from the name. */
private fun deviceKind(name: String): String {
    val n = name.lowercase()
    return when {
        n.contains("flacie web") || n.contains("browser") || n.contains("chrome") || n.contains("firefox") || n.contains("edge") || n.contains("safari") -> "Browser"
        n.startsWith("sm-") || n.startsWith("sdk_") || n.contains("pixel") || n.contains("phone") || n.contains("galaxy") -> "Phone"
        n.contains("desktop") || n.contains("windows") || n.contains("laptop") -> "Computer"
        else -> "Player"
    }
}

/** A device: its cover (or icon), name, what it is and whether it is playing, the song with a progress line, then the actions. */
@Composable
private fun DeviceCard(title: String, chips: List<String>, song: String?, artist: String?, artKey: String?, progress: Float?, glyph: Glyph, highlight: Boolean = false, actions: @Composable () -> Unit) {
    val sc = LocalScheme.current
    val live = chips.contains("Playing")
    Column(
        Modifier.fillMaxWidth().clip(RoundedCornerShape(18.dp)).background(if (highlight) sc.accent.copy(alpha = 0.14f) else Color(0x14FFFFFF)).padding(14.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(14.dp)) {
            Box(Modifier.size(64.dp).clip(RoundedCornerShape(12.dp)).background(Color(0x1FFFFFFF)), contentAlignment = Alignment.Center) {
                if (song != null) ArtImage(artKey, Modifier.fillMaxSize(), thumb = true, corner = 12.dp) else GlyphIcon(glyph, Modifier.size(28.dp), if (highlight) sc.accent else Color.White)
            }
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                Txt(title, size = 16f, weight = FontWeight.Bold, color = Color.White, maxLines = 1)
                Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    chips.forEach { c ->
                        val on = c == "Playing"; val you = c == "This device"
                        Txt(c, Modifier.clip(RoundedCornerShape(50)).background(if (you) sc.accent.copy(alpha = .22f) else if (on) Color(0x2E4EE0A1) else Color(0x1FFFFFFF)).padding(horizontal = 9.dp, vertical = 2.dp), size = 11.5f, weight = FontWeight.Bold, color = if (you) sc.accent else if (on) Color(0xFF4EE0A1) else Color(0xB3FFFFFF), maxLines = 1)
                    }
                }
                if (song != null) Txt(song + if (!artist.isNullOrBlank()) " · $artist" else "", size = 13f, color = Color(0xCCFFFFFF), maxLines = 1)
                else Txt("Nothing playing", size = 13f, color = Color(0x80FFFFFF), maxLines = 1)
            }
        }
        if (progress != null) Box(Modifier.fillMaxWidth().height(3.dp).clip(RoundedCornerShape(2.dp)).background(Color(0x22FFFFFF))) { Box(Modifier.fillMaxWidth(progress).height(3.dp).background(if (live) sc.accent else Color(0x99FFFFFF))) }
        actions()
    }
}
