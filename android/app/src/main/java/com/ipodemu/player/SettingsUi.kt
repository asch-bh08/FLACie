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
            item { SectionHeader("Account") }
            item {
                Card {
                    val acct = app.account
                    if (acct.signedIn) {
                        SettingRow(prefs.accountUserName.ifEmpty { "Signed in" }, prefs.accountServer.removePrefix("https://").removePrefix("http://"), chevron = true) { ui.accountOpen = true }
                        SettingRow("Sync now", acct.status ?: syncedLabel(prefs.accountSyncedAt)) { acct.sync() }
                    } else SettingRow("Sign in", "Not signed in", chevron = true) { ui.accountOpen = true }
                }
            }
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
                }
            }
            item { SectionHeader("Playback") }
            item {
                Card {
                    SettingRow("Shuffle", if (prefs.shuffle) "On" else "Off") { prefs.shuffle = !prefs.shuffle; app.player.applyModes() }
                    SettingRow("Repeat", listOf("Off", "All", "One")[prefs.repeat.coerceIn(0, 2)]) { prefs.repeat = (prefs.repeat + 1) % 3; app.player.applyModes() }
                    SettingRow("Equalizer", prefs.eq, chevron = true) {
                        nav.sheet = SheetSpec("Equalizer", prefs.eq, com.ipodemu.playback.PlayerController.EQ_NAMES.map { n ->
                            SheetItem(if (prefs.eq == n) "$n  (on)" else n, if (prefs.eq == n) Glyph.CHECK else Glyph.LIST) { prefs.eq = n; app.player.applyEq() }
                        })
                    }
                    SettingRow("Volume Limit", if (prefs.volumeLimit == 100) "Off" else "${prefs.volumeLimit}%") {
                        prefs.volumeLimit = limits[(limits.indexOf(prefs.volumeLimit).coerceAtLeast(0) + 1) % limits.size]; app.player.applyVolumeLimit()
                    }
                    SettingRow("Sleep Timer", if (app.player.sleepMinutes == 0) "Off" else "${app.player.sleepMinutes} min") {
                        app.player.setSleepTimer(sleeps[(sleeps.indexOf(app.player.sleepMinutes).coerceAtLeast(0) + 1) % sleeps.size])
                    }
                }
            }
            item { SectionHeader("Library") }
            item {
                Card {
                    SettingRow("Songs", "${app.library.songs().size}") { }
                    SettingRow("Rescan Library", if (app.library.scanning) "Scanning ${app.library.scanCount}..." else null) { app.library.rescan() }
                    SettingRow("Sync", app.library.syncDeviceLabel ?: "Local Library", chevron = true) { ui.syncSetupOpen = true }
                    SettingRow("Jellyfin", if (app.library.jellyfinConnected) "Connected" else "Not connected", chevron = true) { ui.jellyfinSetupOpen = true }
                    SettingRow("Plex", if (app.library.plexConnected) "Connected" else "Not connected", chevron = true) { ui.plexSetupOpen = true }
                    SettingRow("NAS", if (app.library.nasConnected) "Connected" else "Not connected", chevron = true) { ui.nasSetupOpen = true }
                    SettingRow("Lidarr", if (app.library.lidarrConnected) "Connected" else "Not connected", chevron = true) { ui.lidarrSetupOpen = true }
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
