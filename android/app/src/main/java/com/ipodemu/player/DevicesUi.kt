package com.ipodemu.player

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
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
            DeviceCard("This device", here?.let { "${it.title} · ${it.artist}" } ?: "Nothing playing", Glyph.DEVICES, highlight = true) {}
            if (!c.connected) Txt("Connecting to Jellyfin...", size = 14f, color = Color(0x99FFFFFF))
            else if (others.isEmpty()) Txt("No other devices are signed in to ${app.prefs.accountUserName} right now. Open FLACie on the web, on Windows or on another phone, and it shows up here.", size = 14f, color = Color(0x99FFFFFF), maxLines = 4)
            for (s in others) {
                val playing = s.itemId != null
                DeviceCard("${s.device} · ${s.client}", if (playing) "${s.title} · ${s.artist}${if (s.paused) " (paused)" else ""}" else "Nothing playing", Glyph.DEVICES) {
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

@Composable
private fun DeviceCard(title: String, subtitle: String, glyph: Glyph, highlight: Boolean = false, actions: @Composable () -> Unit) {
    val sc = LocalScheme.current
    Column(
        Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(if (highlight) sc.accent.copy(alpha = 0.16f) else Color(0x14FFFFFF)).padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            GlyphIcon(glyph, Modifier.size(26.dp), if (highlight) sc.accent else Color.White)
            Column(Modifier.weight(1f)) {
                Txt(title, size = 16f, weight = FontWeight.SemiBold, color = Color.White, maxLines = 1)
                Txt(subtitle, size = 13f, color = Color(0xBBFFFFFF), maxLines = 2)
            }
        }
        actions()
    }
}
