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
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.verticalScroll
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
 * Settings > NAS: browse a plain SMB/CIFS network share directly (no media server needed -- just
 * a folder of music files on a NAS or another PC's shared folder). Same full-screen-overlay
 * pattern as [JellyfinSetupScreen]/[PlexSetupScreen], with a few more fields since a raw share has
 * no server-side account system to authenticate against, only the share's own credentials.
 */
@Composable
fun NasSetupScreen() {
    val app = LocalApp.current
    val ui = app.ui
    val lib = app.library
    rememberLibRev(lib)
    BackHandler { ui.nasSetupOpen = false }
    var host by remember { mutableStateOf(app.prefs.nasHost) }
    var share by remember { mutableStateOf(app.prefs.nasShare) }
    var folder by remember { mutableStateOf(app.prefs.nasFolder) }
    var username by remember { mutableStateOf(app.prefs.nasUsername) }
    var password by remember { mutableStateOf(app.prefs.nasPassword) }
    val fr = remember { FocusRequester() }

    // close once a connection is made here; already connected on opening, it stays open so the server can be changed
    val connectedOnOpen = remember { lib.nasConnected }
    LaunchedEffect(lib.nasConnected) { if (lib.nasConnected && !connectedOnOpen) ui.nasSetupOpen = false }

    @Composable
    fun field(label: String, value: String, onChange: (String) -> Unit, focus: Boolean = false, secret: Boolean = label.startsWith("Password")) {
        Txt(label, size = 12f)
        SetupTextField(
            value, onChange, singleLine = true, cursorBrush = SolidColor(Color.White), secret = secret,
            textStyle = TextStyle(color = Color.White, fontSize = 16.sp),
            modifier = Modifier.fillMaxWidth().let { if (focus) it.focusRequester(fr) else it }
                .clip(RoundedCornerShape(12.dp)).background(Color(0x22FFFFFF)).padding(14.dp),
        )
    }

    Box(Modifier.fillMaxSize().background(Color(0xFF07080B)).pointerInput(Unit) { detectTapGestures { } }) {
        Column(
            Modifier.fillMaxSize().statusBarsPadding().imePadding().verticalScroll(rememberScrollState()).padding(20.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                GlossPill("Back", { ui.nasSetupOpen = false }, icon = Glyph.BACK, height = 36.dp)
                Box(Modifier.padding(start = 14.dp)) { Txt("NAS", size = 20f, weight = FontWeight.Bold) }
            }
            Txt("Browses a network share's music files directly over SMB. No media server needed.", size = 13f, maxLines = 3)

            field("Server (e.g. 192.168.1.50)", host, { host = it }, focus = true)
            field("Share name (e.g. Music)", share, { share = it })
            field("Folder within the share (optional, e.g. Flac)", folder, { folder = it })
            field("Username (blank = guest/anonymous)", username, { username = it })
            field("Password", password, { password = it })

            GlossPill(if (lib.nasConnecting) "Connecting…" else "Connect", {
                if (host.isNotBlank() && share.isNotBlank()) lib.connectNas(host.trim(), share.trim(), folder.trim(), username.trim(), password, app.prefs.nasDomain)
            }, primary = true)

            when {
                lib.nasConnecting -> Txt("Scanning $share ...", size = 13f)
                lib.nasConnected -> Txt(lib.nasStatus ?: "Connected", size = 13f, color = Color(0xFF7CE0A0))
                lib.nasStatus != null -> Txt(lib.nasStatus ?: "", size = 13f, color = Color(0xFFFF8080))
            }
        }
    }
}
