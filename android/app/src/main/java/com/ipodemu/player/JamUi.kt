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
import androidx.compose.foundation.shape.CircleShape
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

/** Jam: start one, join one on this Jellyfin server, see who's listening, or leave. */
@Composable
fun JamScreen() {
    val app = LocalApp.current
    val ui = app.ui
    val jam = app.jam
    val sc = LocalScheme.current
    BackHandler { ui.jamOpen = false }
    LaunchedEffect(jam.inJam) { while (!jam.inJam) { jam.loadGroups(); delay(5_000) } }

    // ONE APP 1k: a Standard page of the frame (the shared top bar, the tab bar, the mini player); its own header only in the iPod theme's overlay
    val frameNav = LocalPlayerNav.current
    FramePage(frameNav, "Jam") {
    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.TopCenter) {
        Column(
            Modifier.widthIn(max = 560.dp).fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = 20.dp, vertical = 8.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            if (frameNav == null) Row(verticalAlignment = Alignment.CenterVertically) {
                IconAction(Glyph.BACK, "Back", { ui.jamOpen = false }, tint = Color.White)
                Title("Jam", Modifier.padding(start = 4.dp), color = Color.White)   // design I: the serif heading, as on the web
            }
            if (!app.connect.available) {
                Body("Sign in with Jellyfin to listen together with other people on your server.", color = Color(0xCCFFFFFF), maxLines = 3)
                return@Column
            }
            if (jam.inJam) {
                Txt(jam.groupName, size = 20f, weight = FontWeight.SemiBold, color = Color.White)
                Small("Everyone hears the same song at the same moment. Anyone can play, pause, skip or add songs.", color = Color(0xCCFFFFFF), maxLines = 3)
                Txt("LISTENING", size = 12f, weight = FontWeight.Bold, color = Color(0x99FFFFFF))
                for (p in jam.people) Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    Box(Modifier.size(36.dp).clip(CircleShape).background(sc.accent.copy(alpha = 0.3f)), contentAlignment = Alignment.Center) {
                        Body(p.take(1).uppercase(), weight = FontWeight.Bold, color = Color.White)
                    }
                    Body(p, color = Color.White)
                }
                GlossPill("Leave the Jam", { jam.leave() }, height = 40.dp)
            } else {
                Small("Listen together with anyone on this Jellyfin server. Songs stored only on one device can't be shared.", color = Color(0xCCFFFFFF), maxLines = 3)
                GlossPill("Start a Jam", { jam.start() }, primary = true, icon = Glyph.JAM, height = 44.dp)
                Txt("JAMS ON THIS SERVER", size = 12f, weight = FontWeight.Bold, color = Color(0x99FFFFFF))
                if (jam.groups.isEmpty()) Small("No Jams right now.", color = Color(0x99FFFFFF))
                for (g in jam.groups) Column(
                    Modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp)).background(Color(0x14FFFFFF)).padding(14.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    Body(g.name, weight = FontWeight.SemiBold, color = Color.White)
                    Small(g.people.joinToString(", ").ifBlank { "Nobody yet" }, color = Color(0xBBFFFFFF), maxLines = 2)
                    GlossPill("Join", { jam.join(g) }, primary = true, height = 36.dp)
                }
            }
            jam.status?.let { Small(it, color = Color(0xFFFFB0B0), maxLines = 4) }
        }
    }
    }
}
