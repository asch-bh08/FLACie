package com.ipodemu.player

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.ipodemu.library.Library

/**
 * Settings > Sync: point the app at a PC running ipodsync (host:port over LAN/Tailscale), list the
 * real iPod(s) it sees, and browse one read-only. Same full-screen-overlay pattern as [PickerScreen].
 */
@Composable
fun SyncSetupScreen() {
    val app = LocalApp.current
    val ui = app.ui
    val lib = app.library
    val rev = rememberLibRev(lib)
    BackHandler { ui.syncSetupOpen = false }
    var host by remember { mutableStateOf(app.prefs.syncHost) }
    val fr = remember { androidx.compose.ui.focus.FocusRequester() }

    LaunchedEffect(lib.source, rev) { if (lib.source == Library.Source.SYNC) ui.syncSetupOpen = false }

    Box(Modifier.fillMaxSize().background(Color(0xFF07080B)).pointerInput(Unit) { detectTapGestures { } }) {
        Column(Modifier.fillMaxSize().statusBarsPadding().imePadding().padding(20.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                GlossPill("Back", { ui.syncSetupOpen = false }, icon = Glyph.BACK, height = 36.dp)
                Box(Modifier.padding(start = 14.dp)) { Txt("Sync", size = 20f, weight = FontWeight.Bold) }
            }
            Txt("Browse a real iPod plugged into a PC running FLACie for Windows, over Wi-Fi.", size = 13f, maxLines = 3)

            Txt("PC address (e.g. 192.168.1.50:5070)", size = 12f)
            SetupTextField(
                host, { host = it }, singleLine = true, cursorBrush = SolidColor(Color.White),
                textStyle = TextStyle(color = Color.White, fontSize = 16.sp),
                modifier = Modifier.fillMaxWidth().focusRequester(fr).clip(RoundedCornerShape(12.dp)).background(Color(0x22FFFFFF)).padding(14.dp),
            )
            GlossPill("Find iPods", {
                app.prefs.syncHost = host
                if (host.isNotBlank()) lib.findSyncDevices(host)
            }, primary = true)

            when {
                lib.discoveringDevices -> Txt("Searching $host ...", size = 13f)
                lib.syncError != null -> Txt(lib.syncError ?: "", size = 13f, color = Color(0xFFFF8080))
                lib.syncing -> Txt("Loading library...", size = 13f)
                lib.lastSyncSearch != null && lib.syncDevices.isEmpty() -> Txt("No iPods found at ${lib.lastSyncSearch}", size = 13f)
            }

            if (lib.syncDevices.isNotEmpty()) LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 24.dp)) {
                items(lib.syncDevices, key = { it.rootPath }) { d ->
                    Row(
                        Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp)).background(Color(0x14FFFFFF))
                            .pointerInput(d.rootPath) { detectTapGestures { if (d.hasDatabase) lib.loadSync(host, d.rootPath) } }
                            .padding(14.dp),
                    ) {
                        Column {
                            Txt(d.volumeLabel?.takeIf { it.isNotBlank() } ?: d.rootPath, size = 15f, weight = FontWeight.SemiBold)
                            Txt(if (d.hasDatabase) d.rootPath else "${d.rootPath}: no iTunes library found", size = 12f)
                        }
                    }
                }
            }
        }
    }
}
