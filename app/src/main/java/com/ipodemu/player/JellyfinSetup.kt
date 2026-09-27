package com.ipodemu.player

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
 * Settings > Jellyfin: connect this app directly to a Jellyfin server (no PC/ipodsync in the
 * loop) so its tracks merge into the local library and stream straight from Jellyfin. Same
 * full-screen-overlay pattern as [SyncSetupScreen]/[PickerScreen].
 */
@Composable
fun JellyfinSetupScreen() {
    val app = LocalApp.current
    val ui = app.ui
    val lib = app.library
    rememberLibRev(lib)
    BackHandler { ui.jellyfinSetupOpen = false }
    var url by remember { mutableStateOf(app.prefs.jellyfinUrl) }
    var apiKey by remember { mutableStateOf(app.prefs.jellyfinApiKey) }
    val fr = remember { FocusRequester() }

    LaunchedEffect(lib.jellyfinConnected) { if (lib.jellyfinConnected) ui.jellyfinSetupOpen = false }

    Box(Modifier.fillMaxSize().background(Color(0xFF07080B)).pointerInput(Unit) { detectTapGestures { } }) {
        Column(Modifier.fillMaxSize().statusBarsPadding().imePadding().padding(20.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                GlossPill("Back", { ui.jellyfinSetupOpen = false }, icon = Glyph.BACK, height = 36.dp)
                Box(Modifier.padding(start = 14.dp)) { Txt("Jellyfin", size = 20f, weight = FontWeight.Bold) }
            }
            Txt("Streams straight from your Jellyfin server -- no PC needs to be running for this.", size = 13f, maxLines = 3)

            Txt("Server URL (e.g. http://192.168.1.183:8096)", size = 12f)
            BasicTextField(
                url, { url = it }, singleLine = true, cursorBrush = SolidColor(Color.White),
                textStyle = TextStyle(color = Color.White, fontSize = 16.sp),
                modifier = Modifier.fillMaxWidth().focusRequester(fr).clip(RoundedCornerShape(12.dp)).background(Color(0x22FFFFFF)).padding(14.dp),
            )
            Txt("API key (Jellyfin dashboard > Advanced > API Keys)", size = 12f)
            BasicTextField(
                apiKey, { apiKey = it }, singleLine = true, cursorBrush = SolidColor(Color.White),
                textStyle = TextStyle(color = Color.White, fontSize = 16.sp),
                modifier = Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp)).background(Color(0x22FFFFFF)).padding(14.dp),
            )
            GlossPill(if (lib.jellyfinConnecting) "Connecting…" else "Connect", {
                if (url.isNotBlank() && apiKey.isNotBlank()) lib.connectJellyfin(url.trim(), apiKey.trim())
            }, primary = true)

            when {
                lib.jellyfinConnecting -> Txt("Connecting to $url ...", size = 13f)
                lib.jellyfinConnected -> Txt(lib.jellyfinStatus ?: "Connected", size = 13f, color = Color(0xFF7CE0A0))
                lib.jellyfinStatus != null -> Txt(lib.jellyfinStatus ?: "", size = 13f, color = Color(0xFFFF8080))
            }
        }
    }
}
