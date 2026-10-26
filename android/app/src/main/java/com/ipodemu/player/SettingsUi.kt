package com.ipodemu.player

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.width
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
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.semantics.ProgressBarRangeInfo
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.progressBarRangeInfo
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.setProgress
import kotlin.math.roundToInt
import com.ipodemu.library.AudioFacts
import com.ipodemu.theme.Themes

private val VIEW_NAMES = listOf("iPod Player", "iPod Emulator", "Click Wheel Fullscreen", "iPod Emulator")

@Composable
fun SettingsScreen(nav: PlayerNav, dashboard: Boolean = false) {
    // Settings is drawn on the flat dark palette in every mode, so its text must come from that palette too (the light iPod Player skin has dark ink: unreadable on these cards)
    androidx.compose.runtime.CompositionLocalProvider(LocalScheme provides remember { buildModernScheme(null) }) { SettingsBody(nav, dashboard) }
}

@Composable
private fun SettingsBody(nav: PlayerNav, dashboard: Boolean) {
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
    fun SettingRow(title: String, value: String? = null, chevron: Boolean = false, toggle: Boolean = false, desc: String? = null, onClick: () -> Unit) {
        if (desc != null) {
            // the web's switch row: the name in bold and what it does under it, the switch on the right
            Row(Modifier.fillMaxWidth().clickable { onClick(); rev++ }.padding(horizontal = 16.dp, vertical = 12.dp), verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f).padding(end = 12.dp)) {
                    Txt(title, size = 16f, weight = androidx.compose.ui.text.font.FontWeight.Bold, color = sc.onBg)
                    Txt(desc, Modifier.padding(top = 2.dp), size = 13f, color = sc.onBgDim, maxLines = 4)
                }
                if (toggle) SwitchPill(value == "On") else if (value != null) Txt(value, size = 15f, color = sc.onBgDim)
            }
            return
        }
        IpodRow({ onClick(); rev++ }, height = 58.dp, trailing = {
            Row(Modifier.widthIn(max = 150.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                if (toggle) SwitchPill(value == "On")
                else if (value != null) Txt(value, Modifier.weight(1f, fill = false), size = 15f, color = rowDim(), maxLines = 1)
                if (chevron) GlyphIcon(Glyph.CHEVRON, Modifier.size(18.dp), rowDim())
            }
        }) { hi -> Txt(title, size = 16f, weight = androidx.compose.ui.text.font.FontWeight.Medium, color = if (hi) Color.White else sc.onBg) }
    }

    val ctx = androidx.compose.ui.platform.LocalContext.current
    // administrators of the server (FLACie Web's Jellyfin admins) get the Dashboard; without FLACie Web the phone's own download setup lives there too
    val adminWeb = remember { com.ipodemu.library.FlacieWebClient(prefs) }
    var isAdmin by remember { mutableStateOf(false) }
    androidx.compose.runtime.LaunchedEffect(adminWeb.available) {
        if (adminWeb.available) isAdmin = try { kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) { adminWeb.account().admin } } catch (_: Exception) { false }
    }
    val canAdmin = !ui.guest && (isAdmin || !adminWeb.available)

    Column(Modifier.fillMaxSize().background(Palette.bg)) {
        TopBar(if (dashboard) "Dashboard" else "Settings", nav, showBack = true)
        Box(Modifier.fillMaxSize(), contentAlignment = Alignment.TopCenter) {
        val list = androidx.compose.foundation.lazy.rememberLazyListState()
        val scope = androidx.compose.runtime.rememberCoroutineScope()
        val at = remember { HashMap<String, Int>() }
        // opened for one section (the DJ button's long press): there once the list has been laid out
        androidx.compose.runtime.LaunchedEffect(Unit) {
            val s = nav.settingsSection ?: return@LaunchedEffect
            nav.settingsSection = null
            androidx.compose.runtime.withFrameNanos { }
            at[s]?.let { list.scrollToItem(it) }
        }
        LazyColumn(Modifier.widthIn(max = 620.dp).fillMaxSize(), state = list, contentPadding = PaddingValues(bottom = 30.dp)) {
            // design I, the web's Settings page (Settings.razor): a row of section chips, then each section as one panel with a serif heading and a
            // DM Mono hint, in the web's order: Playback, Music sources, Look and feel, then what only the phone has, Account and About
            if (!dashboard) {
                item(key = "subnav") {
                    Row(Modifier.horizontalScroll(rememberScrollState()).padding(horizontal = 16.dp, vertical = 4.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        for ((id, label) in listOf("playback" to "Playback", "dj" to "DJ", "look" to "Appearance", "sources" to "Library", "account" to "Account", "about" to "About"))
                            DesignChip(label, false, { val n = list.layoutInfo.totalItemsCount; val i = when (id) { "account" -> n - 2; "about" -> n - 1; else -> at[id] ?: 0 }; scope.launch { list.animateScrollToItem(i.coerceAtLeast(0)) } })
                        if (!ui.guest) DesignChip("Download log", false, { ui.downloadsOpen = true })
                        if (canAdmin) DesignChip("Dashboard", false, { nav.push(Screen.Dashboard) })
                    }
                }
                at["playback"] = 1
                item(key = "playback") {
                    SetPanel("Playback", "How this phone plays and what comes after the last song.") {
                        SettingRow("Autoplay", if (prefs.autoplay) "On" else "Off", toggle = true, desc = "When the queue ends, carry on with different songs that go with what was playing") { prefs.autoplay = !prefs.autoplay }
                        SettingRow("Fetch ahead", if (prefs.autoplayFetch) "On" else "Off", toggle = true, desc = "Download the next few Autoplay songs you don't have yet, so they are ready when needed") { prefs.autoplayFetch = !prefs.autoplayFetch }
                        SettingRow("Offline and storage", if (app.offline.doneCount > 0) "${com.ipodemu.library.offline.OfflineMath.songs(app.offline.doneCount)}, ${com.ipodemu.library.offline.OfflineMath.formatBytes(app.offline.usedBytes)}" else "None", chevron = true) { nav.push(Screen.Offline) }
                        SettingRow("Streaming quality", listOf("Automatic", "Always full", "Data saver")[prefs.streamQuality.coerceIn(0, 2)]) { prefs.streamQuality = (prefs.streamQuality + 1) % 3 }
                        SettingRow("Equalizer", prefs.eq, chevron = true) {
                            nav.sheet = SheetSpec("Equalizer", prefs.eq, com.ipodemu.playback.PlayerController.EQ_NAMES.map { n ->
                                SheetItem(if (prefs.eq == n) "$n  (on)" else n, if (prefs.eq == n) Glyph.CHECK else Glyph.LIST) { prefs.eq = n; app.player.applyEq() }
                            })
                        }
                        SettingRow("Fade between songs", if (prefs.fadeSecs == 0) "Off" else "${prefs.fadeSecs} s") {
                            prefs.fadeSecs = com.ipodemu.playback.FadeBetween.CHOICES[(com.ipodemu.playback.FadeBetween.CHOICES.indexOf(prefs.fadeSecs).coerceAtLeast(0) + 1) % com.ipodemu.playback.FadeBetween.CHOICES.size]; app.player.applyFade()
                        }
                        SettingRow("Even out volume", if (prefs.levelVolume) "On" else "Off", toggle = true) { prefs.levelVolume = !prefs.levelVolume; app.player.applyLeveling() }
                        SettingRow("Volume limit", if (prefs.volumeLimit == 100) "Off" else "${prefs.volumeLimit}%") {
                            prefs.volumeLimit = limits[(limits.indexOf(prefs.volumeLimit).coerceAtLeast(0) + 1) % limits.size]; app.player.applyVolumeLimit()
                        }
                        val sleepLeft = rememberSleepLeft(app)
                        SettingRow("Sleep timer", sleepLeft ?: "Off", chevron = true) { nav.sheet = sleepSheet(app) }
                    }
                }
                at["dj"] = 2
                item(key = "dj") {
                    // the AI DJ (docs/spec/ai-dj.md): the settings are the account's, kept on FLACie Web, so the web and Windows follow them. Loaded when
                    // this opens, saved on every change; the phone keeps the last copy for the Now Playing and car buttons
                    SetPanel("DJ", "Short spoken intros between songs, like a radio DJ. Saved on your account, so FLACie Web and Windows follow.") {
                        val web = remember { com.ipodemu.library.FlacieWebClient(prefs) }
                        val djScope = androidx.compose.runtime.rememberCoroutineScope()
                        val available = app.player.djAvailable
                        var dj by remember { mutableStateOf(app.player.djSettings) }
                        var loaded by remember { mutableStateOf(false) }
                        var djMsg by remember { mutableStateOf<String?>(null) }
                        androidx.compose.runtime.LaunchedEffect(available) {
                            if (available) try {
                                val s = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) { web.djSettings() }
                                dj = s; loaded = true; djMsg = null; app.player.djSettingsFetched(s)
                            } catch (_: Exception) { djMsg = "Couldn't reach FLACie Web: these are the settings this phone saw last." }
                        }
                        // shown at once, then as the server has it (or put back, with the reason, when it can't be saved)
                        fun save(patch: org.json.JSONObject, shown: com.ipodemu.playback.DjSettings) {
                            val was = dj; dj = shown
                            djScope.launch {
                                try {
                                    val s = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) { web.saveDjSettings(patch) }
                                    dj = s; djMsg = null; app.player.djSettingsChanged(s)
                                } catch (e: Exception) { dj = was; djMsg = e.message ?: "Couldn't reach FLACie Web" }
                            }
                        }
                        fun where(phone: Boolean, auto: Boolean, webOn: Boolean, windows: Boolean) =
                            save(org.json.JSONObject().put("enabled", true).put("phone", phone).put("auto", auto).put("web", webOn).put("windows", windows),
                                dj.copy(enabled = true, phone = phone, auto = auto, web = webOn, windows = windows))
                        if (!available) {
                            Txt(if (prefs.flacieWebUrl.isBlank()) "The DJ needs FLACie Web. Open FLACie Web in a browser once, signed in to the same Jellyfin account, and this phone finds it by itself."
                                else "The DJ needs a Jellyfin sign-in: FLACie Web knows you by it.", Modifier.padding(horizontal = 16.dp, vertical = 8.dp), size = 13f, color = sc.onBgDim, maxLines = 5)
                        } else {
                            SettingRow("DJ", if (dj.enabled) "On" else "Off", toggle = true, desc = "Now and then, a few words about the next song before it starts. The DJ button on Now Playing turns it on or off for one queue.") {
                                save(org.json.JSONObject().put("enabled", !dj.enabled), dj.copy(enabled = !dj.enabled))
                            }
                            // Rare, Often, Every song: a tap moves to the next
                            SettingRow("How chatty", dj.chatty.label, desc = "Rare: about 1 song in 7. Often: about 1 in 3. Every song: before each song (not songs under a minute, spoken word or ones marked never); it uses the most of the day's DJ allowance.") {
                                val n = dj.chatty.next()
                                save(org.json.JSONObject().put("chatty", n.wire), dj.copy(chatty = n))
                            }
                            // Classic radio, Chill, Hype: a tap moves to the next
                            SettingRow("Style", dj.style.label, desc = "Classic radio is a friendly presenter. Chill is softer and talks a little less; Hype is livelier and talks a little more.") {
                                val n = dj.style.next()
                                save(org.json.JSONObject().put("style", n.wire), dj.copy(style = n))
                            }
                            SettingRow("Talk after skips", if (dj.talkAfterSkips) "On" else "Off", toggle = true, desc = "Keep talking when you skip songs. Off: the DJ stays quiet for a couple of songs after a skip.") {
                                save(org.json.JSONObject().put("talkAfterSkips", !dj.talkAfterSkips), dj.copy(talkAfterSkips = !dj.talkAfterSkips))
                            }
                            MonoLabel("Where", Modifier.padding(start = 16.dp, end = 16.dp, top = 10.dp))
                            Row(Modifier.horizontalScroll(rememberScrollState()).padding(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                val all = dj.phone && dj.auto && dj.web && dj.windows
                                DesignChip("Everywhere", dj.enabled && all, { where(true, true, true, true) })
                                DesignChip("Only in the car", dj.enabled && dj.auto && !dj.phone && !dj.web && !dj.windows, { where(false, true, false, false) })
                                DesignChip("Only on web", dj.enabled && dj.web && !dj.phone && !dj.auto && !dj.windows, { where(false, false, true, false) })
                            }
                            SettingRow("Phone", if (dj.phone) "On" else "Off", toggle = true) { save(org.json.JSONObject().put("phone", !dj.phone), dj.copy(phone = !dj.phone)) }
                            SettingRow("Android Auto", if (dj.auto) "On" else "Off", toggle = true) { save(org.json.JSONObject().put("auto", !dj.auto), dj.copy(auto = !dj.auto)) }
                            SettingRow("Web", if (dj.web) "On" else "Off", toggle = true) { save(org.json.JSONObject().put("web", !dj.web), dj.copy(web = !dj.web)) }
                            SettingRow("Windows", if (dj.windows) "On" else "Off", toggle = true) { save(org.json.JSONObject().put("windows", !dj.windows), dj.copy(windows = !dj.windows)) }
                            MonoLabel("Sound", Modifier.padding(start = 16.dp, end = 16.dp, top = 10.dp))
                            DjLevelRow("DJ volume", "How loud the DJ's voice is", dj.volume) { v -> save(org.json.JSONObject().put("volume", v), dj.copy(volume = v)) }
                            DjLevelRow("Music under the DJ", "How loud the song stays while the DJ talks", dj.duck) { v -> save(org.json.JSONObject().put("duck", v), dj.copy(duck = v)) }
                            SettingRow("Device voice", if (dj.deviceVoice) "On" else "Off", toggle = true, desc = "Use this phone's own voice for the DJ instead of the DJ's voice. Off: if the DJ's voice can't be made, the DJ stays quiet.") {
                                save(org.json.JSONObject().put("deviceVoice", !dj.deviceVoice), dj.copy(deviceVoice = !dj.deviceVoice))
                            }
                            if (loaded && !dj.voiceReady) Txt("Cloudflare isn't set up yet; until it is, the DJ stays quiet unless Device voice is on.", Modifier.padding(horizontal = 16.dp, vertical = 8.dp), size = 13f, color = sc.onBgDim, maxLines = 4)
                        }
                        djMsg?.let { Txt(it, Modifier.padding(horizontal = 16.dp, vertical = 8.dp), size = 13f, color = sc.onBgDim, maxLines = 4) }
                    }
                }
                at["sources"] = 3
                item(key = "sources") {
                    SetPanel("Library", "Where the songs come from. Your profile is kept in your ${if (prefs.accountKind == "nas") "NAS" else "Jellyfin account"}, so signing in on another device brings everything back.") {
                        // the web's facts first: who is signed in where, and how big the library is
                        val lib = app.library
                        SetFacts(listOf(
                            "Jellyfin" to (if (lib.jellyfinConnected || prefs.accountKind == "jellyfin") "${prefs.accountUserName.ifBlank { "Signed in" }} on ${prefs.accountServer.ifBlank { prefs.jellyfinUrl }}" else "Not connected"),
                            "NAS" to (if (lib.nasConnected) "${prefs.nasHost}/${prefs.nasShare}" else "Not connected"),
                            "Library" to "${lib.songs().size} songs · ${lib.albums().size} albums",
                        ))
                        // guests use this device's files only
                        if (!ui.guest) SettingRow("Jellyfin", if (lib.jellyfinConnected) "Connected" else "Not connected", chevron = true) { ui.jellyfinSetupOpen = true }
                        if (!ui.guest) SettingRow("Plex", if (lib.plexConnected) "Connected" else "Not connected", chevron = true) { ui.plexSetupOpen = true }
                        if (!ui.guest) SettingRow("NAS", if (lib.nasConnected) "Connected" else "Not connected", chevron = true) { ui.nasSetupOpen = true }
                        SettingRow("Reload library", if (lib.scanning) "Scanning ${lib.scanCount}..." else null, desc = "Reads this phone's music and the servers' song lists again") { lib.rescan() }
                        if (!ui.guest) SettingRow("Download log", "Open", chevron = true) { ui.downloadsOpen = true }
                        SettingRow("Headphone test", "Tones, bass, 3D", chevron = true) { ui.headphonesOpen = true }
                        SettingRow("iPod sync", lib.syncDeviceLabel ?: "Off", chevron = true) { ui.syncSetupOpen = true }
                    }
                }
                at["look"] = 4
                item(key = "look") {
                    SetPanel("Appearance", "Personal tweaks, saved on this phone only (the web has its own in its Settings).") {
                        val accName = Tweaks.ACCENTS.firstOrNull { it.second == prefs.accentColor }?.first ?: "Custom"
                        SettingRow("Accent colour", accName, chevron = true) {
                            nav.sheet = SheetSpec("Accent colour", "Over the theme's own colour", Tweaks.ACCENTS.map { (n, c) ->
                                SheetItem(if (c == prefs.accentColor) "$n  (on)" else n, if (c == prefs.accentColor) Glyph.CHECK else Glyph.NOTE) { prefs.accentColor = c; ui.refreshFromPrefs() }
                            })
                        }
                        SettingRow("Cover colours in the player", if (ui.dynamicColor) "On" else "Off", toggle = true, desc = "The full-screen player takes its background from the song's cover. Off gives a plain dark background.") { ui.changeDynamic(!ui.dynamicColor) }
                        SettingRow("Compact lists", if (prefs.compactLists) "On" else "Off", toggle = true, desc = "Tighter rows in song lists, so more fit on the screen.") { prefs.compactLists = !prefs.compactLists; ui.refreshFromPrefs() }
                        SettingRow("Format and source badges", if (prefs.showBadges) "On" else "Off", toggle = true, desc = "The small FLAC / MP3 and NAS / Streaming labels next to songs.") { prefs.showBadges = !prefs.showBadges; ui.refreshFromPrefs() }
                        SettingRow("Animations", if (prefs.motion) "On" else "Off", toggle = true, desc = "Fades, the pulsing download dot and moving highlights. Turn off for a calmer, lighter app.") { prefs.motion = !prefs.motion; ui.refreshFromPrefs() }
                        // the phone's own looks: the mode (an explicit chooser, never a one-tap toggle: a stray tap used to flip the whole app into the iPod theme) and Retro
                        SettingRow("Mode", if (ui.ipodTheme) "iPod" else "Modern", chevron = true) {
                            nav.sheet = SheetSpec("Mode", null, listOf(
                                SheetItem("Modern", if (!ui.ipodTheme) Glyph.CHECK else Glyph.NOTE) { ui.changeTheme(0) },
                                SheetItem("iPod (classic skins and click wheel)", if (ui.ipodTheme) Glyph.CHECK else Glyph.IPOD) { ui.changeTheme(1) },
                                SheetItem("Sync mode (edit a connected iPod)", Glyph.LIST) { ui.changeTheme(2) },
                                SheetItem("Retro (iPods, the desk and listening scenes)", Glyph.CASSETTE) { openRetro(ctx) },
                            ))
                        }
                        SettingRow("Retro", "iPods, desk, scenes", chevron = true) { openRetro(ctx) }
                        if (ui.ipodTheme) SettingRow("iPod appearance", modeName, chevron = true) { ui.pickerOpen = true }
                        if (ui.ipodTheme) SettingRow("Light or dark", listOf("Dark", "Light", "System")[prefs.appearance.coerceIn(0, 2)]) { prefs.appearance = (prefs.appearance + 1) % 3; ui.refreshFromPrefs() }
                    }
                }
            }
            if (dashboard) item(key = "dash-intro") {
                MonoLabel("What is in the library, how the downloads went, and the server's settings", Modifier.padding(horizontal = 20.dp, vertical = 4.dp), size = 11f, maxLines = 3)
            }
            if (dashboard && canAdmin) {
                item {
                    SetPanel("Library", "Every song by the real format of its file") {
                        // what is in the library, by the real format of each file (AudioFacts, read by the server), and how the last downloads went
                        AudioFacts.version
                        val counts = remember(AudioFacts.version, app.library.songs().size) {
                            val all = app.library.songs(); var hi = 0; var lossless = 0; var lossy = 0; var unknown = 0
                            for (t in all) { val f = AudioFacts.of(t); if (f == null) unknown++ else if (f.hiRes) hi++ else if (f.lossless) lossless++ else lossy++ }
                            listOf(all.size, hi, lossless, lossy, unknown)
                        }
                        val tiles = listOf("%,d".format(counts[0]) to "Songs in your library", "%,d".format(counts[1]) to "Hi-Res", "%,d".format(counts[2]) to "Lossless (CD quality)", "%,d".format(counts[3]) to "Lossy (MP3, AAC...)") +
                            (if (counts[4] > 0) listOf("%,d".format(counts[4]) to "Format not read yet") else emptyList())
                        Column(Modifier.padding(horizontal = 16.dp, vertical = 8.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                            for (pair in tiles.chunked(2)) Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                                for ((v, k) in pair) Column(Modifier.weight(1f).clip(RoundedCornerShape(18.dp)).border(1.dp, Palette.line, RoundedCornerShape(18.dp)).background(Palette.surface2).padding(16.dp)) {
                                    SerifTitle(v, size = 34f, italicWords = -1)
                                    MonoLabel(k, Modifier.padding(top = 6.dp), size = 10.5f, maxLines = 2)
                                }
                                if (pair.size == 1) Box(Modifier.weight(1f))
                            }
                        }
                        val dl = remember { com.ipodemu.library.FlacieWebClient(prefs) }
                        var recent by remember { mutableStateOf<List<com.ipodemu.library.DownloadRecord>?>(null) }
                        androidx.compose.runtime.LaunchedEffect(dl.available) {
                            if (dl.available) recent = try { kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) { dl.downloads().log.take(100) } } catch (_: Exception) { null }
                        }
                        recent?.let { r ->
                            SettingRow("Last ${r.size} downloads", "${r.count { it.done }} worked, ${r.count { !it.done }} failed") { ui.downloadsOpen = true }
                            for ((src, n) in r.filter { it.done }.groupingBy { it.source ?: "other" }.eachCount().entries.sortedByDescending { it.value })
                                SettingRow("  from ${com.ipodemu.library.OpenSourceNames.of(src).let { if (src == "soulseek") "Soulseek" else if (src == "lidarr") "Lidarr" else it }}", "$n") { }
                        }
                    }
                }
            }
            if (dashboard) {
                item {
                    SetPanel("Download services", "Where missing songs are fetched from") {
                        if (canAdmin) SettingRow("Download services", if (app.library.lidarrConnected) "Set up" else "Not set up", chevron = true) { ui.lidarrSetupOpen = true }
                        SettingRow("Download log", "Open", chevron = true) { ui.downloadsOpen = true }
                        if (adminWeb.available) SettingRow("Web dashboard", "Open", chevron = true) {
                            ctx.startActivity(android.content.Intent(android.content.Intent.ACTION_VIEW, android.net.Uri.parse(prefs.flacieWebUrl.trimEnd('/') + "/dashboard")).addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK))
                        }
                    }
                }
                if (!canAdmin) item { Txt("The Dashboard is for the server's administrators.", Modifier.padding(horizontal = 16.dp, vertical = 8.dp), size = 13f, color = sc.onBgDim, maxLines = 3) }
            }
            if (dashboard && canAdmin) {
                item {
                    SetPanel("Background downloads", "Playlists sent to the server and its daily charts") {
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
                        srv?.takeIf { it.admin }?.let { s ->
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
            if (!dashboard) {
            item(key = "storage") {
                SetPanel("Storage", "Covers and audio kept on this phone so they show and play at once.") {
                    // covers (one 240 px picture per album, kept until the phone runs low on space), the saved song list, and audio kept for replay
                    val scope2 = androidx.compose.runtime.rememberCoroutineScope()
                    var bytes by remember { mutableStateOf(-1L) }
                    var clearing by remember { mutableStateOf(false) }
                    var cleared by remember { mutableIntStateOf(0) }
                    androidx.compose.runtime.LaunchedEffect(rev, cleared) {
                        bytes = try { kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) { app.cacheBytes() } } catch (_: Exception) { -1L }
                    }
                    SettingRow("Cache", if (bytes < 0) "Counting..." else cacheSizeLabel(bytes)) { }
                    SettingRow("Clear cache", if (clearing) "Clearing..." else "Covers and saved audio") {
                        if (!clearing) {
                            clearing = true
                            scope2.launch {
                                try { kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) { app.clearCaches() } } finally { clearing = false; cleared++ }
                            }
                        }
                    }
                }
            }
            item(key = "controls") {
                SetPanel("Controls", "Swipes and game controllers.") {
                    SettingRow("Swipe to go back", listOf("Anywhere", "Left edge", "Off")[prefs.swipeBack.coerceIn(0, 2)]) { prefs.swipeBack = (prefs.swipeBack + 1) % 3; ui.refreshFromPrefs() }
                    SettingRow("Swipe track right", SWIPE_ACTIONS[prefs.swipeRowRight.coerceIn(0, 3)]) { prefs.swipeRowRight = (prefs.swipeRowRight + 1) % 4; ui.refreshFromPrefs() }
                    SettingRow("Swipe track left", SWIPE_ACTIONS[prefs.swipeRowLeft.coerceIn(0, 3)]) { prefs.swipeRowLeft = (prefs.swipeRowLeft + 1) % 4; ui.refreshFromPrefs() }
                    SettingRow("Face buttons", if (prefs.swapFaceButtons) "Nintendo" else "Xbox") { prefs.swapFaceButtons = !prefs.swapFaceButtons }
                }
            }
            // what FLACie may ask the Android system for: music on this phone, notifications (Android 13+). Only shown while something is off.
            if (ui.mediaAccess != com.ipodemu.Access.GRANTED || ui.notifAccess == com.ipodemu.Access.DENIED || ui.notifAccess == com.ipodemu.Access.ASK) {
                item(key = "permissions") {
                    SetPanel("Permissions", "What FLACie may ask the phone for.") {
                        if (ui.mediaAccess != com.ipodemu.Access.GRANTED) SettingRow("Music on this phone", if (ui.mediaAccess == com.ipodemu.Access.DENIED) "Open settings" else "Allow", chevron = true) {
                            if (ui.mediaAccess == com.ipodemu.Access.DENIED) ui.permissions?.openAppSettings() else ui.permissions?.request(com.ipodemu.Need.MEDIA)
                        }
                        if (ui.notifAccess == com.ipodemu.Access.DENIED || ui.notifAccess == com.ipodemu.Access.ASK) SettingRow("Notifications", if (ui.notifAccess == com.ipodemu.Access.DENIED) "Open settings" else "Allow", chevron = true) {
                            if (ui.notifAccess == com.ipodemu.Access.DENIED) ui.permissions?.openAppSettings() else ui.permissions?.request(com.ipodemu.Need.NOTIFICATIONS)
                        }
                    }
                }
            }
            item(key = "account") {
                SetPanel("Account", null) {
                    val acct = app.account
                    if (acct.signedIn) {
                        Txt("Signed in with ${if (prefs.accountKind == "nas") "a NAS" else "Jellyfin"} as ${if (prefs.accountKind == "nas") prefs.nasUsername.ifBlank { "guest" } else prefs.accountUserName.ifEmpty { "you" }}.",
                            Modifier.padding(horizontal = 16.dp, vertical = 6.dp), size = 15f, maxLines = 3)
                        if (prefs.accountKind == "nas") SettingRow("Account details", "${prefs.nasHost}/${prefs.nasShare}", chevron = true) { ui.accountOpen = true }
                        else SettingRow("Account details", prefs.accountServer.removePrefix("https://").removePrefix("http://"), chevron = true) { ui.accountOpen = true }
                        SettingRow("Sync now", acct.status ?: syncedLabel(prefs.accountSyncedAt)) { acct.sync() }
                    } else SettingRow("Sign in", if (ui.guest) "Guest" else "Not signed in", chevron = true) { ui.accountOpen = true }
                }
            }
            item(key = "about") {
                SetPanel("About", "What is running here.") {
                    SetFacts(listOf(
                        "Version" to remember { ctx.packageManager.getPackageInfo(ctx.packageName, 0).versionName ?: "" },
                        "Server" to prefs.flacieWebUrl.ifBlank { "Not linked to FLACie Web" },
                    ))
                    if (canAdmin) SettingRow("Dashboard", "Library, downloads, server", chevron = true) { nav.push(Screen.Dashboard) }
                }
            }
            }
        }
        }
    }
}

/** A Settings section as the web draws it: one rounded panel with a serif heading and a DM Mono hint, the rows inside. */
@Composable
internal fun SetPanel(title: String, hint: String?, content: @Composable () -> Unit) {
    Column(Modifier.padding(horizontal = 16.dp, vertical = 8.dp).fillMaxWidth().clip(RoundedCornerShape(20.dp)).background(Palette.surface).border(1.dp, Palette.line, RoundedCornerShape(20.dp)).padding(vertical = 14.dp)) {
        SerifTitle(title, Modifier.padding(horizontal = 16.dp), size = 26f)
        if (hint != null) MonoLabel(hint, Modifier.padding(start = 16.dp, end = 16.dp, top = 4.dp, bottom = 6.dp), size = 10.5f, upper = false, maxLines = 4)
        content()
    }
}

/** Label and value pairs (the web's dl.kv): the label in DM Mono capitals, the value beside it. */
@Composable
internal fun SetFacts(rows: List<Pair<String, String>>) {
    Column(Modifier.padding(horizontal = 16.dp, vertical = 6.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        for ((k, v) in rows) Row(verticalAlignment = Alignment.Top) {
            MonoLabel(k, Modifier.width(84.dp).padding(top = 3.dp), size = 10.5f)
            Txt(v, Modifier.weight(1f), size = 15f, maxLines = 3)
        }
    }
}

@Composable
internal fun Card(content: @Composable () -> Unit) {
    val sc = LocalScheme.current
    Column(Modifier.padding(horizontal = 16.dp).clip(RoundedCornerShape(14.dp)).background(Palette.surface).border(1.dp, Palette.line, RoundedCornerShape(14.dp))) { content() }
}
val SWIPE_ACTIONS = listOf("Off", "Play next", "Favorite", "Add to queue")

/** "0 KB", "812 KB", "12.4 MB", "1.3 GB": the Cache line of Settings. */
fun cacheSizeLabel(bytes: Long): String = when {
    bytes < 1024L * 1024 -> "${(bytes.coerceAtLeast(0) + 1023) / 1024} KB"
    bytes < 1024L * 1024 * 1024 -> "%.1f MB".format(java.util.Locale.US, bytes / 1048576.0)
    else -> "%.1f GB".format(java.util.Locale.US, bytes / 1073741824.0)
}

fun brightnessLabel(b: Float) = if (b < 0f) "System" else "${(b * 100).toInt()}%"

fun syncedLabel(at: Long): String = if (at <= 0L) "Not synced yet" else {
    val m = (System.currentTimeMillis() - at) / 60000
    when { m < 1 -> "Synced just now"; m < 60 -> "Synced $m min ago"; m < 1440 -> "Synced ${m / 60} hr ago"; else -> "Synced ${m / 1440} d ago" }
}

/** The On/Off of a setting drawn as a switch (the web's), instead of the word. */
@Composable
fun SwitchPill(on: Boolean) {
    val sc = LocalScheme.current
    val x by androidx.compose.animation.core.animateDpAsState(if (on) 20.dp else 2.dp, androidx.compose.animation.core.tween(160), label = "knob")
    Box(Modifier.size(width = 44.dp, height = 26.dp).clip(androidx.compose.foundation.shape.RoundedCornerShape(13.dp)).background(if (on) sc.accent else Palette.surface3)) {
        Box(Modifier.padding(start = x, top = 2.dp).size(22.dp).clip(androidx.compose.foundation.shape.CircleShape).background(if (on) sc.accent.readableInk() else Palette.dim))
    }
}

/** A DJ level (0-100 %) as a plain drag slider: shown as it moves, saved once when the finger lifts (or on a tap). */
@Composable
private fun DjLevelRow(title: String, desc: String, value: Int, onSet: (Int) -> Unit) {
    val sc = LocalScheme.current
    var shown by remember(value) { mutableStateOf(value) }
    var width by remember { mutableStateOf(1f) }
    fun at(x: Float) = ((x / width).coerceIn(0f, 1f) * 20).roundToInt() * 5   // steps of 5 %
    Column(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Txt(title, size = 15f)
                Txt(desc, size = 13f, color = sc.onBgDim, maxLines = 2)
            }
            Txt("$shown%", size = 14f, color = sc.onBgDim)
        }
        androidx.compose.foundation.layout.Box(
            Modifier.fillMaxWidth().height(40.dp)
                .semantics {
                    contentDescription = title
                    progressBarRangeInfo = ProgressBarRangeInfo(shown.toFloat(), 0f..100f, 19)
                    setProgress { v -> onSet((v / 5f).roundToInt().coerceIn(0, 20) * 5); true }
                }
                .pointerInput(Unit) { detectTapGestures { o -> shown = at(o.x); onSet(shown) } }
                .pointerInput(Unit) { detectDragGestures(onDragEnd = { onSet(shown) }) { change, _ -> change.consume(); shown = at(change.position.x) } },
            contentAlignment = Alignment.CenterStart,
        ) {
            androidx.compose.foundation.Canvas(Modifier.fillMaxWidth().height(40.dp)) {
                width = size.width
                val y = size.height / 2; val f = shown / 100f
                drawLine(sc.onBgDim.copy(alpha = 0.3f), androidx.compose.ui.geometry.Offset(0f, y), androidx.compose.ui.geometry.Offset(size.width, y), strokeWidth = 5.dp.toPx())
                drawLine(sc.accent, androidx.compose.ui.geometry.Offset(0f, y), androidx.compose.ui.geometry.Offset(size.width * f, y), strokeWidth = 5.dp.toPx())
                drawCircle(sc.accent, 10.dp.toPx(), androidx.compose.ui.geometry.Offset((size.width * f).coerceIn(10.dp.toPx(), size.width - 10.dp.toPx()), y))
            }
        }
    }
}
