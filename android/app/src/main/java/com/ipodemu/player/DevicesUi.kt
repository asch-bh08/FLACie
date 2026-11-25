package com.ipodemu.player

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.blur
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.ipodemu.library.WhereYouWere
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/**
 * Devices, redone (owner, 25 Nov): the same page as the web's (Devices.razor, devices.css). This device first, then the account's other devices as glass
 * cards tinted by their covers, with labelled actions (Play here, Send to, Previous/Pause/Next), devices that are off now with where they left off, and
 * "Where you were": each device's last day from FLACie Web, so the person can go back to where a device was an hour ago and carry on here.
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun DevicesScreen() {
    val app = LocalApp.current
    val ui = app.ui
    val c = app.connect
    val sc = LocalScheme.current
    val scope = rememberCoroutineScope()
    BackHandler { ui.devicesOpen = false }
    var where by remember { mutableStateOf<List<WhereYouWere.Device>>(emptyList()) }
    var whereFailed by remember { mutableStateOf(false) }
    var filter by remember { mutableStateOf<String?>(null) }
    var status by remember { mutableStateOf<String?>(null) }
    val expanded = remember { mutableStateListOf<String>() }
    var now by remember { mutableStateOf(System.currentTimeMillis()) }
    val web = remember { com.ipodemu.library.FlacieWebClient(app.prefs) }
    // refresh while open: other devices' positions move, and new moments are kept
    LaunchedEffect(Unit) { while (true) { c.loadSessions(); now = System.currentTimeMillis(); delay(4_000) } }
    LaunchedEffect(Unit) {
        while (true) {
            if (web.available) try { where = withContext(Dispatchers.IO) { web.where() }; whereFailed = false } catch (_: Exception) { whereFailed = true }
            delay(10_000)
        }
    }
    val here = app.player.current
    val others = c.sessions.filter { !it.isSelf }.sortedWith(compareBy({ !(it.itemId != null && !it.paused) }, { it.itemId == null }, { it.device }))
    val liveIds = others.mapNotNull { it.deviceId }.toSet()
    val away = where.filter { it.deviceId !in liveIds && it.deviceId != app.prefs.deviceId && !it.deviceId.startsWith("web:") }
    val dim = Color(0x99FFFFFF)

    fun carryOn(m: WhereYouWere.Moment) {
        status = "Finding the songs..."
        scope.launch {
            val ok = try { c.carryOn(withContext(Dispatchers.IO) { web.whereOne(m.id) }) } catch (_: Exception) { false }
            status = if (ok) "Carrying on: ${m.title}, ${WhereYouWere.where(m.positionMs, m.context)}." else "\"${m.title}\" can't be found here right now."
        }
    }

    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.TopCenter) {
        Column(
            Modifier.widthIn(max = 640.dp).fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = 20.dp, vertical = 8.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                IconAction(Glyph.BACK, "Back", { ui.devicesOpen = false }, tint = Color.White)
                SerifTitle("Your devices", Modifier.padding(start = 4.dp), size = 34f, color = Color.White)
            }
            Txt("Everything signed in to this account. Move the music between them, or go back to where any of them was today.", size = 14.5f, color = dim, maxLines = 4)

            SectionTitle("This phone")
            DeviceCard(
                name = android.os.Build.MODEL, kind = "Phone", chips = listOf("This device" to ChipTone.YOU, (if (here == null) "Idle" else if (app.player.wantsToPlay) "Playing" else "Paused") to if (here != null && app.player.wantsToPlay) ChipTone.ON else ChipTone.PLAIN),
                song = here?.title, artist = listOfNotNull(here?.artist, app.player.contextName).joinToString(" · "), artKey = here?.artKey,
                progress = here?.let { if (it.durationMs > 0) (app.player.positionMs.toFloat() / it.durationMs).coerceIn(0f, 1f) else null },
                time = here?.let { "${WhereYouWere.clock(app.player.positionMs)} / ${WhereYouWere.clock(it.durationMs)}" },
                live = here != null && app.player.wantsToPlay,
                note = if (here == null) "Nothing playing here. Use Play here on another device, or carry on from where you were below." else "To move it, use Send to on another device's card.",
            ) {}

            SectionTitle("Other devices")
            if (!c.available) Empty("Connect needs a Jellyfin sign-in.", "Sign in with Jellyfin to see and control your other devices.")
            else {
                if (!c.connected) Txt("Connecting to Jellyfin...", size = 13.5f, color = dim)
                for (s in others) {
                    val playing = s.itemId != null
                    val pos = s.estimatedMs(now)
                    DeviceCard(
                        name = s.device, kind = WhereYouWere.kind(s.device),
                        chips = listOf(WhereYouWere.kind(s.device) to ChipTone.PLAIN, (if (!playing) "Idle" else if (s.paused) "Paused" else "Playing") to if (playing && !s.paused) ChipTone.ON else ChipTone.PLAIN,
                            (if (s.lastSeen > 0) "Seen " + WhereYouWere.ago(s.lastSeen, now) else "Online") to ChipTone.MONO),
                        song = s.title.takeIf { playing }, artist = s.artist, artKey = s.artKey,
                        progress = if (playing && s.durationMs > 0) (pos.toFloat() / s.durationMs).coerceIn(0f, 1f) else null,
                        time = if (playing) "${WhereYouWere.clock(pos)} / ${WhereYouWere.clock(s.durationMs)}" else null,
                        live = playing && !s.paused, note = if (playing) null else "Nothing playing.",
                    ) {
                        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                            if (playing) GlossPill("Play here", { c.takeOver(s) }, icon = Glyph.PLAY, primary = true, height = 40.dp)
                            if (here != null && s.controllable) GlossPill("Send to " + short(s.device), { c.sendTo(s) }, icon = Glyph.DEVICES, height = 40.dp)
                            if (playing && s.controllable) {
                                GlossPill("Previous", { c.command(s, "PreviousTrack") }, icon = Glyph.PREV, height = 40.dp)
                                GlossPill(if (s.paused) "Play" else "Pause", { c.command(s, "PlayPause") }, icon = if (s.paused) Glyph.PLAY else Glyph.PAUSE, height = 40.dp)
                                GlossPill("Next", { c.command(s, "NextTrack") }, icon = Glyph.NEXT, height = 40.dp)
                            }
                        }
                    }
                }
                for (d in away) {
                    val last = d.moments.first()
                    DeviceCard(
                        name = d.device, kind = d.kind, chips = listOf(d.kind to ChipTone.PLAIN, "Offline" to ChipTone.OFF, ("Seen " + WhereYouWere.ago(d.lastSeen, now)) to ChipTone.MONO),
                        song = last.title, artist = last.artist, artKey = artOf(app, last), progress = null, time = "Left " + WhereYouWere.where(last.positionMs, last.context),
                        live = false, note = null, faded = true,
                    ) { GlossPill("Carry on here", { carryOn(last) }, icon = Glyph.PLAY, primary = true, height = 40.dp) }
                }
                if (others.isEmpty() && away.isEmpty()) Empty("No other device right now.", "Open FLACie on the web, on Windows or on another phone with this account and it shows up here.")
            }

            SectionTitle("Where you were")
            Txt("The last 24 hours on each device, newest first. Pick a moment to carry on from there, here.", size = 13.5f, color = dim, maxLines = 3)
            when {
                !web.available -> Empty("Needs FLACie Web.", "Add your FLACie Web address in Settings to keep where each device was.")
                where.isEmpty() -> Empty(if (whereFailed) "Can't reach FLACie Web just now." else "Nothing yet.", "As you play music on any device signed in to this account, FLACie keeps where you were for a day.")
                else -> {
                    if (where.size > 1) Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        DesignChip("All devices", filter == null, { filter = null })
                        for (d in where) DesignChip(short(named(app, d)), filter == d.deviceId, { filter = d.deviceId })
                    }
                    for (d in where.filter { filter == null || it.deviceId == filter }) {
                        val open = d.deviceId in expanded
                        Glass {
                            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                                KindBadge(d.kind, 28.dp)
                                Txt(named(app, d), size = 15f, weight = FontWeight.Bold, color = Color.White)
                                MonoLabel("${d.moments.size} ${if (d.moments.size == 1) "moment" else "moments"} · last ${WhereYouWere.ago(d.lastSeen, now)}", size = 10.5f, color = dim)
                            }
                            for (m in if (open) d.moments else d.moments.take(5)) MomentRow(m, artOf(app, m), now) { carryOn(m) }
                            if (d.moments.size > 5) GlossPill(if (open) "Show fewer" else "Show all ${d.moments.size}", { if (!expanded.remove(d.deviceId)) expanded.add(d.deviceId) }, height = 36.dp)
                        }
                    }
                }
            }
            status?.let { MonoLabel(it, color = sc.accent, upper = false, maxLines = 3, size = 12f) }
            GlossPill("Jam together", { ui.devicesOpen = false; ui.jamOpen = true }, icon = Glyph.JAM, height = 40.dp)
            Box(Modifier.height(24.dp))
        }
    }
}

private enum class ChipTone { PLAIN, YOU, ON, OFF, MONO }

private fun short(name: String) = if (name.length > 18) name.take(17) + "…" else name

/** This phone's own past, named so it is clear which one it is. */
private fun named(app: com.ipodemu.App, d: WhereYouWere.Device) = if (d.deviceId == app.prefs.deviceId) d.device + " (this one)" else d.device

/** The picture for a moment: the song in this phone's library has the key this phone knows; a browser's key is the server's own. */
private fun artOf(app: com.ipodemu.App, m: WhereYouWere.Moment): String? =
    m.itemId?.let { app.library.jellyfinTrack(it)?.artKey ?: "jf$it" } ?: m.artKey

@Composable
private fun SectionTitle(text: String) = SerifTitle(text, Modifier.padding(top = 10.dp), size = 28f, color = Color.White)

@Composable
private fun KindBadge(kind: String, size: androidx.compose.ui.unit.Dp = 34.dp) {
    Box(Modifier.size(size).clip(CircleShape).background(Color(0x14FFFFFF)).border(1.dp, Color(0x24FFFFFF), CircleShape), contentAlignment = Alignment.Center) {
        GlyphIcon(if (kind == "Browser" || kind == "Computer") Glyph.IPOD else Glyph.DEVICES, Modifier.size(size * .5f), Color.White)
    }
}

/** The rounded glass the Now Playing sheet uses: a faint fill and a hairline rim. */
@Composable
private fun Glass(modifier: Modifier = Modifier, content: @Composable () -> Unit) {
    Column(
        modifier.fillMaxWidth().clip(RoundedCornerShape(22.dp)).background(Color(0x0FFFFFFF)).border(1.dp, Color(0x24FFFFFF), RoundedCornerShape(22.dp)).padding(14.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) { content() }
}

@Composable
private fun Empty(title: String, text: String) {
    Row(
        Modifier.fillMaxWidth().clip(RoundedCornerShape(22.dp)).border(1.dp, Color(0x24FFFFFF), RoundedCornerShape(22.dp)).padding(18.dp),
        horizontalArrangement = Arrangement.spacedBy(14.dp), verticalAlignment = Alignment.CenterVertically,
    ) {
        GlyphIcon(Glyph.DEVICES, Modifier.size(26.dp), Color(0xB3FFFFFF))
        Column { Txt(title, size = 14.5f, weight = FontWeight.Bold, color = Color.White, maxLines = 2); Txt(text, size = 13.5f, color = Color(0x99FFFFFF), maxLines = 4) }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun Chips(chips: List<Pair<String, ChipTone>>) {
    val sc = LocalScheme.current
    FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        for ((text, tone) in chips) {
            val col = when (tone) { ChipTone.YOU -> sc.accent; ChipTone.ON -> Color(0xFF4EE0A1); else -> Color(0xB3FFFFFF) }
            val m = Modifier.clip(RoundedCornerShape(50)).border(1.dp, col.copy(alpha = if (tone == ChipTone.PLAIN || tone == ChipTone.MONO || tone == ChipTone.OFF) .35f else .6f), RoundedCornerShape(50))
                .padding(horizontal = 10.dp, vertical = 3.dp)
            if (tone == ChipTone.MONO) MonoLabel(text, m, size = 10f, color = col) else Txt(text, m, size = 11.5f, weight = FontWeight.SemiBold, color = col)
        }
    }
}

/** A device: kind, name and chips, then its song (cover, serif title, artist, progress, time) washed in the cover's colours, then its actions. */
@Composable
private fun DeviceCard(
    name: String, kind: String, chips: List<Pair<String, ChipTone>>, song: String?, artist: String?, artKey: String?, progress: Float?, time: String?,
    live: Boolean, note: String?, faded: Boolean = false, actions: @Composable () -> Unit,
) {
    val sc = LocalScheme.current
    val shape = RoundedCornerShape(22.dp)
    Box(Modifier.fillMaxWidth().clip(shape).background(Color(0x0FFFFFFF)).border(1.dp, if (live) sc.accent.copy(alpha = .55f) else Color(0x24FFFFFF), shape)) {
        // the cover's colours, blurred behind the card (a plain faint fill on phones too old to blur)
        if (song != null) ArtImage(artKey, Modifier.matchParentSize().blur(48.dp).alpha(if (faded) .14f else .3f), thumb = true, corner = 0.dp)
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                KindBadge(kind)
                Txt(name, size = 16f, weight = FontWeight.Bold, color = Color.White, maxLines = 1)
            }
            Chips(chips)
            if (song != null) Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(14.dp)) {
                ArtImage(artKey, Modifier.size(64.dp), thumb = true, corner = 12.dp)
                Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                    SerifTitle(song, size = 22f, color = Color.White, italicWords = -1)
                    if (!artist.isNullOrBlank()) Txt(artist, size = 13.5f, color = Color(0xB3FFFFFF), maxLines = 1)
                    if (progress != null) Box(Modifier.padding(top = 4.dp).fillMaxWidth().height(3.dp).clip(RoundedCornerShape(2.dp)).background(Color(0x24FFFFFF))) {
                        Box(Modifier.fillMaxWidth(progress).height(3.dp).background(sc.accent))
                    }
                    if (time != null) MonoLabel(time, size = 11f, color = Color(0x99FFFFFF), upper = false)
                }
            }
            if (note != null) Txt(note, size = 13.5f, color = Color(0x99FFFFFF), maxLines = 3)
            actions()
        }
    }
}

/** One moment in "Where you were": when (relative, then the clock), the cover, the song and where in it, and Carry on. */
@Composable
private fun MomentRow(m: WhereYouWere.Moment, art: String?, now: Long, onCarryOn: () -> Unit) {
    val sc = LocalScheme.current
    Column(Modifier.fillMaxWidth().padding(vertical = 4.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Column(Modifier.width(64.dp)) {
                MonoLabel(WhereYouWere.ago(m.at, now), size = 10.5f, color = sc.accent, maxLines = 2)
                MonoLabel(java.text.SimpleDateFormat("HH:mm", java.util.Locale.ROOT).format(java.util.Date(m.at)), size = 10.5f, color = Color(0x99FFFFFF), upper = false)
            }
            ArtImage(art, Modifier.size(44.dp), thumb = true, corner = 8.dp)
            Column(Modifier.weight(1f)) {
                Txt(m.title, size = 15f, weight = FontWeight.SemiBold, color = Color.White, maxLines = 1)
                Txt(m.artist + " · " + WhereYouWere.where(m.positionMs, m.context) + if (m.paused) " · paused" else "", size = 12.5f, color = Color(0x99FFFFFF), maxLines = 2)
            }
        }
        Row(Modifier.padding(start = 76.dp)) { GlossPill("Carry on", onCarryOn, icon = Glyph.PLAY, height = 36.dp) }
    }
}
