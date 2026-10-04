package com.ipodemu.player

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import kotlinx.coroutines.launch
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import com.ipodemu.theme.Themes

private val VIEW_NAMES = listOf("iPod Player", "iPod Emulator", "Click Wheel Fullscreen", "iPod Emulator")

@Composable
fun SettingsScreen(nav: PlayerNav) {
    LocalLibRev.current
    val app = LocalApp.current
    val sc = LocalScheme.current
    val prefs = app.prefs
    val ui = app.ui
    val modeName = VIEW_NAMES[ui.viewMode.coerceIn(0, 3)]   // read here so the row follows mode changes
    var rev by remember { mutableIntStateOf(0) }
    @Suppress("UNUSED_EXPRESSION") rev
    val model = Themes.model(ui.model)
    val limits = listOf(100, 85, 70, 50)
    val sleeps = listOf(0, 15, 30, 60, 90, 120)

    @Composable
    fun SettingRow(title: String, value: String? = null, chevron: Boolean = false, onClick: () -> Unit) {
        IpodRow({ onClick(); rev++ }, height = 58.dp, trailing = {
            Row(Modifier.widthIn(max = 150.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                if (value != null) Txt(value, Modifier.weight(1f, fill = false), size = 15f, color = rowDim(), maxLines = 1)
                if (chevron) GlyphIcon(Glyph.CHEVRON, Modifier.size(18.dp), rowDim())
            }
        }) { hi -> Txt(title, size = 16f, weight = androidx.compose.ui.text.font.FontWeight.Medium, color = if (hi) Color.White else sc.onBg) }
    }

    val ctx = androidx.compose.ui.platform.LocalContext.current
    Column(Modifier.fillMaxSize()) {
        TopBar("Settings", nav, showBack = true)
        LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 30.dp)) {
            item { SectionHeader("Appearance") }
            item {
                Card {
                    // an explicit chooser, not a one-tap toggle: the Account rows above change count on sign-in/out, and a tap meant
                    // for them used to land here and silently flip the whole app into the iPod theme
                    SettingRow("Mode", if (ui.ipodTheme) "iPod" else "Modern", chevron = true) {
                        nav.sheet = SheetSpec("Mode", null, listOf(
                            SheetItem("Modern", if (!ui.ipodTheme) Glyph.CHECK else Glyph.NOTE) { ui.changeTheme(0) },
                            SheetItem("iPod (classic skins and click wheel)", if (ui.ipodTheme) Glyph.CHECK else Glyph.IPOD) { ui.changeTheme(1) },
                            SheetItem("Sync mode (edit a connected iPod)", Glyph.LIST) { ui.changeTheme(2) },
                        ))
                    }
                    if (ui.ipodTheme) SettingRow("iPod appearance", modeName, chevron = true) { ui.pickerOpen = true }
                    SettingRow("Colour from album art", if (ui.dynamicColor) "On" else "Off") { ui.changeDynamic(!ui.dynamicColor) }
                    if (ui.ipodTheme) SettingRow("Light or dark", listOf("Dark", "Light", "System")[prefs.appearance.coerceIn(0, 2)]) { prefs.appearance = (prefs.appearance + 1) % 3; ui.refreshFromPrefs() }
                }
            }
            // shuffle and repeat live on Now Playing, where they're used; settings holds what's set once
            item { SectionHeader("Playback") }
            item {
                Card {
                    SettingRow("Autoplay", if (prefs.autoplay) "On" else "Off") { prefs.autoplay = !prefs.autoplay }
                    SettingRow("Fetch ahead", if (prefs.autoplayFetch) "On" else "Off") { prefs.autoplayFetch = !prefs.autoplayFetch }
                    SettingRow("Streaming quality", listOf("Automatic", "Always full", "Data saver")[prefs.streamQuality.coerceIn(0, 2)]) { prefs.streamQuality = (prefs.streamQuality + 1) % 3 }
                    SettingRow("Equalizer", prefs.eq, chevron = true) {
                        nav.sheet = SheetSpec("Equalizer", prefs.eq, com.ipodemu.playback.PlayerController.EQ_NAMES.map { n ->
                            SheetItem(if (prefs.eq == n) "$n  (on)" else n, if (prefs.eq == n) Glyph.CHECK else Glyph.LIST) { prefs.eq = n; app.player.applyEq() }
                        })
                    }
                    SettingRow("Volume limit", if (prefs.volumeLimit == 100) "Off" else "${prefs.volumeLimit}%") {
                        prefs.volumeLimit = limits[(limits.indexOf(prefs.volumeLimit).coerceAtLeast(0) + 1) % limits.size]; app.player.applyVolumeLimit()
                    }
                    SettingRow("Sleep timer", if (app.player.sleepMinutes == 0) "Off" else "${app.player.sleepMinutes} min") {
                        app.player.setSleepTimer(sleeps[(sleeps.indexOf(app.player.sleepMinutes).coerceAtLeast(0) + 1) % sleeps.size])
                    }
                }
            }
            item { SectionHeader("Music sources") }
            item {
                Card {
                    // guests use this device's files only
                    if (!ui.guest) SettingRow("Jellyfin", if (app.library.jellyfinConnected) "Connected" else "Not connected", chevron = true) { ui.jellyfinSetupOpen = true }
                    if (!ui.guest) SettingRow("Plex", if (app.library.plexConnected) "Connected" else "Not connected", chevron = true) { ui.plexSetupOpen = true }
                    if (!ui.guest) SettingRow("NAS", if (app.library.nasConnected) "Connected" else "Not connected", chevron = true) { ui.nasSetupOpen = true }
                    if (!ui.guest) SettingRow("Downloads", if (app.library.lidarrConnected) "Set up" else "Not set up", chevron = true) { ui.lidarrSetupOpen = true }
                    SettingRow("iPod sync", app.library.syncDeviceLabel ?: "Off", chevron = true) { ui.syncSetupOpen = true }
                    SettingRow("Rescan this device", if (app.library.scanning) "Scanning ${app.library.scanCount}..." else null) { app.library.rescan() }
                }
            }
            if (!ui.guest) {
                item { SectionHeader("Background downloads") }
                item {
                    Card {
                        val web = remember { com.ipodemu.library.FlacieWebClient(prefs) }
                        val scope = androidx.compose.runtime.rememberCoroutineScope()
                        var msg by remember { mutableStateOf<String?>(null) }
                        var jobs by remember { mutableStateOf<List<com.ipodemu.library.FlacieWebClient.Job>>(emptyList()) }
                        androidx.compose.runtime.LaunchedEffect(Unit) {
                            while (true) {
                                if (web.available) jobs = try { kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) { web.jobs() } } catch (_: Exception) { jobs }
                                kotlinx.coroutines.delay(5000)
                            }
                        }
                        val pick = androidx.activity.compose.rememberLauncherForActivityResult(androidx.activity.result.contract.ActivityResultContracts.OpenDocument()) { uri ->
                            if (uri != null) scope.launch(kotlinx.coroutines.Dispatchers.IO) {
                                try {
                                    val name = ctx.contentResolver.query(uri, arrayOf(android.provider.OpenableColumns.DISPLAY_NAME), null, null, null)?.use { if (it.moveToFirst()) it.getString(0) else null } ?: "playlist.txt"
                                    val bytes = ctx.contentResolver.openInputStream(uri)!!.use { it.readBytes() }
                                    val j = web.submit(name, bytes)
                                    msg = "Sent ${j.file}: ${j.total} songs. The server is fetching them."
                                    jobs = web.jobs()
                                } catch (e: Exception) { msg = e.message ?: "Couldn't send the file" }
                            }
                        }
                        SettingRow("Import a playlist file", if (web.available) "Choose" else "Needs FLACie Web", chevron = web.available) {
                            if (web.available) pick.launch(arrayOf("*/*")) else msg = "Open FLACie Web in a browser, signed in to the same Jellyfin account, and this phone finds it by itself."
                        }
                        jobs.take(3).forEach { j ->
                            SettingRow(j.file, when (j.state) { "done" -> "Done ${j.total - j.failed}/${j.total}"; "waiting" -> "Waiting for space"; "cancelled" -> "Stopped"; else -> "${j.done}/${j.total}" }) { }
                        }
                        // the server's own settings, the same ones as FLACie Web's Settings page (they live on the server, so they apply on every device)
                        var srv by remember { mutableStateOf<com.ipodemu.library.FlacieWebClient.ServerSettings?>(null) }
                        androidx.compose.runtime.LaunchedEffect(web.available) {
                            if (web.available) srv = try { kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) { web.settings() } } catch (_: Exception) { null }
                        }
                        fun change(block: () -> com.ipodemu.library.FlacieWebClient.ServerSettings) {
                            scope.launch(kotlinx.coroutines.Dispatchers.IO) { try { srv = block() } catch (e: Exception) { msg = e.message ?: "Couldn't reach FLACie Web" } }
                        }
                        fun chartsSheet(s: com.ipodemu.library.FlacieWebClient.ServerSettings) {
                            nav.sheet = SheetSpec("Charts to follow", "Tap to add or remove", s.available.map { (id, name) ->
                                val on = id in s.lists
                                SheetItem(name, if (on) Glyph.CHECK else Glyph.LIST) {
                                    scope.launch(kotlinx.coroutines.Dispatchers.IO) {
                                        try {
                                            val n = web.saveSettings(lists = if (on) s.lists - id else s.lists + id); srv = n
                                            kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.Main) { chartsSheet(n) }
                                        } catch (e: Exception) { msg = e.message }
                                    }
                                }
                            })
                        }
                        fun size(b: Long) = when { b >= 1L shl 40 -> "%.1f TB".format(b / 1099511627776.0); b >= 1L shl 30 -> "%.0f GB".format(b / 1073741824.0); else -> "%.0f MB".format(b / 1048576.0) }
                        srv?.let { s ->
                            val gbs = listOf(5, 10, 25, 50, 100, 200)
                            SettingRow("Keep free space", "${s.minFreeGb} GB") { val n = gbs.firstOrNull { it > s.minFreeGb } ?: gbs[0]; change { web.saveSettings(minFreeGb = n) } }
                            SettingRow("Daily charts", if (s.chartsOn) "On" else "Off") { change { web.saveSettings(chartsOn = !s.chartsOn) } }
                            if (s.chartsOn) {
                                val per = listOf(5, 10, 15, 25)
                                SettingRow("Charts to follow", "${s.lists.size} chosen", chevron = true) { chartsSheet(s) }
                                SettingRow("New songs per chart", "${s.perList} a day") { val n = per.firstOrNull { it > s.perList } ?: per[0]; change { web.saveSettings(perList = n) } }
                                SettingRow("Run now", if (s.lastRun.isNotEmpty()) "Last ${s.lastRun}" else "Not run yet") { scope.launch(kotlinx.coroutines.Dispatchers.IO) { try { web.runCharts(); msg = "Started. New songs are added to your library on the server." } catch (e: Exception) { msg = e.message } } }
                                Txt("About ${"%.1f".format(s.lists.size * s.perList * 30 / 1024.0)} GB a day with these settings, only while there is room. Nothing already stored is deleted." + (if (s.lastNote.isNotEmpty()) " Last run: ${s.lastNote}" else ""),
                                    Modifier.padding(horizontal = 16.dp, vertical = 8.dp), size = 13f, color = sc.onBgDim, maxLines = 5)
                            }
                            SettingRow("Music storage", if (s.musicTotal > 0) "${size(s.musicFree)} free of ${size(s.musicTotal)}" else "Unknown") { }
                        }
                        msg?.let { Txt(it, Modifier.padding(horizontal = 16.dp, vertical = 10.dp), size = 13f, color = sc.onBgDim, maxLines = 4) }
                    }
                }
            }
            item { SectionHeader("Controls") }
            item {
                Card {
                    SettingRow("Swipe to go back", listOf("Anywhere", "Left edge", "Off")[prefs.swipeBack.coerceIn(0, 2)]) { prefs.swipeBack = (prefs.swipeBack + 1) % 3; ui.refreshFromPrefs() }
                    SettingRow("Swipe track right", SWIPE_ACTIONS[prefs.swipeRowRight.coerceIn(0, 3)]) { prefs.swipeRowRight = (prefs.swipeRowRight + 1) % 4; ui.refreshFromPrefs() }
                    SettingRow("Swipe track left", SWIPE_ACTIONS[prefs.swipeRowLeft.coerceIn(0, 3)]) { prefs.swipeRowLeft = (prefs.swipeRowLeft + 1) % 4; ui.refreshFromPrefs() }
                    SettingRow("Face buttons", if (prefs.swapFaceButtons) "Nintendo" else "Xbox") { prefs.swapFaceButtons = !prefs.swapFaceButtons }
                }
            }
            item { SectionHeader("Account") }
            item {
                Card {
                    val acct = app.account
                    if (acct.signedIn) {
                        if (prefs.accountKind == "nas") SettingRow("NAS: ${prefs.nasUsername.ifBlank { "guest" }}", "${prefs.nasHost}/${prefs.nasShare}", chevron = true) { ui.accountOpen = true }
                        else SettingRow(prefs.accountUserName.ifEmpty { "Signed in" }, prefs.accountServer.removePrefix("https://").removePrefix("http://"), chevron = true) { ui.accountOpen = true }
                        SettingRow("Sync now", acct.status ?: syncedLabel(prefs.accountSyncedAt)) { acct.sync() }
                    } else SettingRow("Sign in", if (ui.guest) "Guest" else "Not signed in", chevron = true) { ui.accountOpen = true }
                }
            }
            item { SectionHeader("About") }
            item { Card { SettingRow("Version", remember { ctx.packageManager.getPackageInfo(ctx.packageName, 0).versionName ?: "" }) { } } }
        }
    }
}

@Composable
private fun Card(content: @Composable () -> Unit) {
    val sc = LocalScheme.current
    Column(Modifier.padding(horizontal = 16.dp).clip(RoundedCornerShape(16.dp)).background(sc.card).border(1.dp, sc.cardBorder, RoundedCornerShape(16.dp))) { content() }
}
val SWIPE_ACTIONS = listOf("Off", "Play next", "Favorite", "Add to queue")

fun brightnessLabel(b: Float) = if (b < 0f) "System" else "${(b * 100).toInt()}%"

fun syncedLabel(at: Long): String = if (at <= 0L) "Not synced yet" else {
    val m = (System.currentTimeMillis() - at) / 60000
    when { m < 1 -> "Synced just now"; m < 60 -> "Synced $m min ago"; m < 1440 -> "Synced ${m / 60} hr ago"; else -> "Synced ${m / 1440} d ago" }
}
