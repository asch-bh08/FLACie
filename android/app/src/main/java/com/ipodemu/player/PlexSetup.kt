package com.ipodemu.player

import androidx.compose.foundation.verticalScroll
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/**
 * Settings > Plex: connect this app directly to a Plex Media Server so its music merges into the
 * local library and streams straight from Plex. Same full-screen-overlay pattern as
 * [JellyfinSetupScreen]/[SyncSetupScreen].
 */
@Composable
fun PlexSetupScreen() {
    val app = LocalApp.current
    val ui = app.ui
    val lib = app.library
    rememberLibRev(lib)
    BackHandler { ui.plexSetupOpen = false }
    var url by remember { mutableStateOf(app.prefs.plexUrl) }
    var token by remember { mutableStateOf(app.prefs.plexToken) }
    val fr = remember { FocusRequester() }

    LaunchedEffect(lib.plexConnected) { if (lib.plexConnected) ui.plexSetupOpen = false }

    Box(Modifier.fillMaxSize().background(Color(0xFF07080B)).pointerInput(Unit) { detectTapGestures { } }) {
        Column(Modifier.fillMaxSize().statusBarsPadding().imePadding().verticalScroll(androidx.compose.foundation.rememberScrollState()).padding(20.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                GlossPill("Back", { ui.plexSetupOpen = false }, icon = Glyph.BACK, height = 36.dp)
                Box(Modifier.padding(start = 14.dp)) { Txt("Plex", size = 20f, weight = FontWeight.Bold) }
            }
            Txt("Streams straight from your Plex server. Nothing else needs to be running.", size = 13f, maxLines = 3)

            Txt("Server URL (e.g. http://192.168.1.183:32400)", size = 12f)
            SetupTextField(
                url, { url = it }, singleLine = true, cursorBrush = SolidColor(Color.White),
                textStyle = TextStyle(color = Color.White, fontSize = 16.sp),
                modifier = Modifier.fillMaxWidth().focusRequester(fr).clip(RoundedCornerShape(12.dp)).background(Color(0x22FFFFFF)).padding(14.dp),
            )
            Txt("Plex token (Settings > ... in a Plex web session's network tab, or plex.tv/claim)", size = 12f)
            SetupTextField(
                token, { token = it }, singleLine = true, cursorBrush = SolidColor(Color.White),
                textStyle = TextStyle(color = Color.White, fontSize = 16.sp),
                modifier = Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp)).background(Color(0x22FFFFFF)).padding(14.dp),
            )
            GlossPill(if (lib.plexConnecting) "Connecting…" else "Connect", {
                if (url.isNotBlank() && token.isNotBlank()) lib.connectPlex(url.trim(), token.trim())
            }, primary = true)

            when {
                lib.plexConnecting -> Txt("Connecting to $url ...", size = 13f)
                lib.plexConnected -> Txt(lib.plexStatus ?: "Connected", size = 13f, color = Color(0xFF7CE0A0))
                lib.plexStatus != null -> Txt(lib.plexStatus ?: "", size = 13f, color = Color(0xFFFF8080))
            }
        }
    }
}
