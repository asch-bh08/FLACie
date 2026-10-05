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
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import kotlinx.coroutines.launch
import com.ipodemu.library.testJamendoClientId
import com.ipodemu.library.OpenSourceSettings
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
 * Settings > Lidarr: lets the app request a track it doesn't have, by talking to Lidarr directly
 * (same no-PC-needed pattern as Jellyfin/Plex/NAS). An optional Soulseek section underneath lets
 * slskd race Lidarr for a faster download when a peer already has the file online -- Lidarr alone
 * is a complete, working setup, so this section can be left blank.
 */
@Composable
fun LidarrSetupScreen() {
    val app = LocalApp.current
    val ui = app.ui
    val lib = app.library
    rememberLibRev(lib)
    BackHandler { ui.lidarrSetupOpen = false }
    var url by remember { mutableStateOf(app.prefs.lidarrUrl) }
    var apiKey by remember { mutableStateOf(app.prefs.lidarrApiKey) }
    var slskdUrl by remember { mutableStateOf(app.prefs.slskdUrl) }
    var slskdKey by remember { mutableStateOf(app.prefs.slskdApiKey) }
    var slskdPath by remember { mutableStateOf(app.prefs.slskdDownloadPath) }
    var moverUrl by remember { mutableStateOf(app.prefs.fileMoverUrl) }
    var moverKey by remember { mutableStateOf(app.prefs.fileMoverApiKey) }
    var jamendoId by remember { mutableStateOf(app.prefs.jamendoClientId) }
    var jamendoStatus by remember { mutableStateOf<Pair<Boolean, String>?>(null) }
    var jamendoBusy by remember { mutableStateOf(false) }
    var order by remember { mutableStateOf(app.prefs.openSources.fullOrder()) }
    var openRev by remember { mutableStateOf(0) }
    @Suppress("UNUSED_EXPRESSION") openRev
    val openArchive = app.prefs.openArchive; val openAudius = app.prefs.openAudius; val openJamendo = app.prefs.openJamendo; val openYtdl = app.prefs.openYtdl
    val scope = rememberCoroutineScope()
    val fr = remember { FocusRequester() }

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
                GlossPill("Back", { ui.lidarrSetupOpen = false }, icon = Glyph.BACK, height = 36.dp)
                Box(Modifier.padding(start = 14.dp)) { Txt("Lidarr", size = 20f, weight = FontWeight.Bold) }
            }
            Txt("Lets a search's \"Download\" option request a track you don't have yet.", size = 13f, maxLines = 3)

            field("Server URL (e.g. https://myserver.example/lidarr)", url, { url = it }, focus = true)
            field("API key (Lidarr > Settings > General)", apiKey, { apiKey = it })
            GlossPill(if (lib.lidarrConnecting) "Connecting…" else "Connect", {
                if (url.isNotBlank() && apiKey.isNotBlank()) lib.connectLidarr(url.trim(), apiKey.trim())
            }, primary = true)
            when {
                lib.lidarrConnecting -> Txt("Connecting to $url ...", size = 13f)
                lib.lidarrConnected -> Txt(lib.lidarrStatus ?: "Connected", size = 13f, color = Color(0xFF7CE0A0))
                lib.lidarrStatus != null -> Txt(lib.lidarrStatus ?: "", size = 13f, color = Color(0xFFFF8080))
            }

            Box(Modifier.padding(top = 8.dp)) { Txt("Soulseek (optional)", size = 16f, weight = FontWeight.Bold) }
            Txt("Lidarr won't import a single track on its own, so a completed download is filed straight into the library via the file mover below, so set that up too. Leave blank to use Lidarr only.", size = 12f, maxLines = 8)
            field("slskd URL (e.g. https://myserver.example/slskd)", slskdUrl, { slskdUrl = it })
            field("slskd API key", slskdKey, { slskdKey = it })
            field("Download path (relative to the shared root, e.g. downloads/slskd-inbox)", slskdPath, { slskdPath = it })
            GlossPill(if (lib.slskdConnecting) "Connecting…" else "Connect Soulseek", {
                if (slskdUrl.isNotBlank() && slskdKey.isNotBlank()) lib.connectSlskd(slskdUrl.trim(), slskdKey.trim(), slskdPath.trim())
            })
            when {
                lib.slskdConnecting -> Txt("Connecting to $slskdUrl ...", size = 13f)
                lib.slskdConnected -> Txt(lib.slskdStatus ?: "Connected", size = 13f, color = Color(0xFF7CE0A0))
                lib.slskdStatus != null -> Txt(lib.slskdStatus ?: "", size = 13f, color = Color(0xFFFF8080))
            }

            Box(Modifier.padding(top = 8.dp)) { Txt("File mover", size = 16f, weight = FontWeight.Bold) }
            Txt("Moves finished Soulseek downloads into your music folder, and fetches songs from the open sources below.", size = 12f, maxLines = 4)
            field("File mover URL (e.g. https://myserver.example/filemove)", moverUrl, { moverUrl = it })
            field("File mover API key", moverKey, { moverKey = it })
            GlossPill(if (lib.fileMoverConnecting) "Connecting…" else "Connect File Mover", {
                if (moverUrl.isNotBlank() && moverKey.isNotBlank()) lib.connectFileMover(moverUrl.trim(), moverKey.trim())
            })
            when {
                lib.fileMoverConnecting -> Txt("Connecting to $moverUrl ...", size = 13f)
                lib.fileMoverConnected -> Txt(lib.fileMoverStatus ?: "Connected", size = 13f, color = Color(0xFF7CE0A0))
                lib.fileMoverStatus != null -> Txt(lib.fileMoverStatus ?: "", size = 13f, color = Color(0xFFFF8080))
            }

            Box(Modifier.padding(top = 8.dp)) { Txt("Open sources", size = 16f, weight = FontWeight.Bold) }
            Txt("A song Soulseek doesn't have is also looked up here before Lidarr, and fetched through the file mover. The free sources look while Soulseek searches and only download if it found nothing. The first source on the list that has the song is used: move one up to prefer it.", size = 12f, maxLines = 8)
            for (id in order) {
                val pos = order.indexOf(id)
                val on = when (id) { OpenSourceSettings.ARCHIVE -> openArchive; OpenSourceSettings.AUDIUS -> openAudius; OpenSourceSettings.YTDL -> openYtdl; else -> openJamendo }
                Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp)).background(Color(0x14FFFFFF)).padding(14.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Txt("${pos + 1}. " + when (id) { OpenSourceSettings.ARCHIVE -> "Internet Archive"; OpenSourceSettings.AUDIUS -> "Audius"; OpenSourceSettings.YTDL -> "YouTube (yt-dlp)"; else -> "Jamendo" }, size = 16f, weight = FontWeight.SemiBold)
                    Txt(when (id) {
                        OpenSourceSettings.ARCHIVE -> "Live Music Archive and netlabel collections only, never a general search. Needs nothing."
                        OpenSourceSettings.AUDIUS -> "The free Audius catalogue. Needs nothing."
                        OpenSourceSettings.YTDL -> "Last resort for songs nothing else has: finds the official audio on YouTube and saves it (AAC, not lossless). Against YouTube's terms of service, so it is off until you switch it on. Needs the yt-dlp service next to the file mover."
                        else -> "Only tracks whose artist allows downloads. Needs the client id below."
                    }, size = 12f, maxLines = 7)
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        GlossPill(if (on) "On" else "Off", {
                            when (id) { OpenSourceSettings.ARCHIVE -> app.prefs.openArchive = !on; OpenSourceSettings.AUDIUS -> app.prefs.openAudius = !on; OpenSourceSettings.YTDL -> app.prefs.openYtdl = !on; else -> app.prefs.openJamendo = !on }
                            openRev++; app.library.onServicesChanged?.invoke()
                        }, primary = on, height = 38.dp)
                        if (pos > 0) GlossPill("Move up", { order = order.toMutableList().also { java.util.Collections.swap(it, pos, pos - 1) }; app.prefs.openOrder = order.joinToString(","); app.library.onServicesChanged?.invoke() }, height = 38.dp)
                        if (pos < order.size - 1) GlossPill("Move down", { order = order.toMutableList().also { java.util.Collections.swap(it, pos, pos + 1) }; app.prefs.openOrder = order.joinToString(","); app.library.onServicesChanged?.invoke() }, height = 38.dp)
                    }
                }
            }
            field("Jamendo client id (free from devportal.jamendo.com; Jamendo stays off without one)", jamendoId, { jamendoId = it; jamendoStatus = null })
            GlossPill(if (jamendoBusy) "Checking…" else if (jamendoId.isBlank()) "Turn Jamendo off" else "Save Jamendo", {
                if (jamendoBusy) return@GlossPill
                val id = jamendoId.trim()
                if (id.isEmpty()) { app.prefs.jamendoClientId = ""; app.library.onServicesChanged?.invoke(); jamendoStatus = true to "Jamendo is off" }
                else {
                    jamendoBusy = true
                    scope.launch {
                        val (ok, why) = testJamendoClientId(id)
                        if (ok) { app.prefs.jamendoClientId = id; app.library.onServicesChanged?.invoke(); jamendoStatus = true to "Saved. Jamendo is on" }
                        else jamendoStatus = false to "Not saved: $why"
                        jamendoBusy = false
                    }
                }
            })
            jamendoStatus?.let { (ok, msg) -> Txt(msg, size = 13f, color = if (ok) Color(0xFF7CE0A0) else Color(0xFFFF8080)) }
        }
    }
}
