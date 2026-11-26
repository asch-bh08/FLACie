package com.ipodemu.player

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.ipodemu.library.JellyfinConnect
import kotlinx.coroutines.delay

/**
 * Remote playback, like Spotify Connect: when another of this user's devices (the web, Windows, another phone) is playing and this phone is not,
 * a bar at the bottom shows what it plays; tapping it opens that device's Now Playing, drawn as this phone's own player is (the web's design),
 * with "Play here" (takes the queue over, same second).
 */

/**
 * Which other device's bar to show. One that is playing; when none is, the one the bar was last showing, if it is still on a song: paused from this bar, it
 * stays with its Play button until that device stops or another takes over (owner, 5 Nov: pausing made the bar vanish, so it could not be resumed).
 * [others] are (id, paused) of the other controllable devices on a song; [sticky] is the id shown last.
 */
internal object RemoteBarRule {
    fun pick(others: List<Pair<String, Boolean>>, sticky: String?): String? =
        others.firstOrNull { !it.second }?.first ?: sticky?.takeIf { id -> others.any { it.first == id } }
}

/** The device whose bar to show (see [RemoteBarRule]); a paused one that was not just playing is only offered inside the full-screen remote. */
fun JellyfinConnect.remoteNow(includePaused: Boolean = false): JellyfinConnect.Session? {
    val others = sessions.filter { !it.isSelf && it.controllable && it.itemId != null }
    // this phone is playing itself: the bar steps aside and forgets the device
    if (sessions.any { it.isSelf && !it.paused && it.itemId != null }) remoteSticky = null
    val id = RemoteBarRule.pick(others.map { it.id to it.paused }, remoteSticky)
    remoteSticky = id
    return others.firstOrNull { it.id == id } ?: if (includePaused) others.firstOrNull() else null
}
@Volatile private var remoteSticky: String? = null

/** Where the song is on the other device right now: its last reported spot plus the time since (while it plays). */
internal fun JellyfinConnect.Session.estimatedMs(now: Long): Long =
    if (paused) positionMs else (positionMs + (now - at)).coerceAtMost(if (durationMs > 0) durationMs else Long.MAX_VALUE)

/**
 * The bar for another device's song, as this phone's own mini player draws one (design I: tinted with the cover's tone, the serif title, play in the
 * cover's colour) with what the web's strip says (RemoteNowPlaying.razor): "Listening on <device>", the title, "artist · 1:02 / 3:45", and
 * previous | play | next, previous only from 400 dp and next from 340 dp wide, as the web drops them. A tap opens that device's Now Playing.
 */
@Composable
fun RemoteMiniPlayer(id: String) {
    val app = LocalApp.current
    val s = app.connect.sessions.firstOrNull { it.id == id } ?: return
    val sc = LocalScheme.current
    var now by remember { mutableLongStateOf(System.currentTimeMillis()) }
    LaunchedEffect(s.id, s.paused) { while (true) { now = System.currentTimeMillis(); delay(500) } }
    val est = s.estimatedMs(now)
    val frac = if (s.durationMs > 0) (est.toFloat() / s.durationMs).coerceIn(0f, 1f) else 0f
    val cfg = androidx.compose.ui.platform.LocalConfiguration.current
    val short = cfg.screenHeightDp < 500
    val art = s.artKey ?: "jf${s.itemId}"
    val cols = rememberCoverColours(art)
    val tone = Color(cols.tone); val main = Color(cols.main)
    val shape = RoundedCornerShape(18.dp)
    Box(
        Modifier.fillMaxWidth().padding(start = 12.dp, end = 12.dp, top = if (short) 4.dp else 6.dp, bottom = if (short) 4.dp else 10.dp).height(if (short) 58.dp else 66.dp)
            .coverShadow(18.dp, 8.dp).clip(shape)
            .background(Brush.horizontalGradient(listOf(tone, Palette.surface))).border(1.dp, Color(cols.toneFg).copy(alpha = .14f), shape)
            .focusRing(shape)
            .clickable(onClickLabel = "Open Now Playing on ${s.device}") { app.ui.remoteId = s.id; app.ui.remoteOpen = true },
    ) {
        Row(Modifier.fillMaxSize().padding(start = 8.dp, end = 6.dp), verticalAlignment = Alignment.CenterVertically) {
            ArtImage(art, Modifier.size(if (short) 44.dp else 50.dp).popIn(s.itemId, 0.7f), thumb = true, corner = 10.dp)
            Column(Modifier.weight(1f).padding(horizontal = 12.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    GlyphIcon(Glyph.DEVICES, Modifier.size(12.dp), main)
                    Txt("Listening on ${s.device}", Modifier.padding(start = 4.dp), size = 11f, weight = FontWeight.Bold, color = main, maxLines = 1)
                }
                SerifTitle(s.title, size = 18f)
                Txt(SleeveText.remoteLine(s.artist, est, s.durationMs, ::fmtTime), size = 12f, color = sc.onBgDim, maxLines = 1)
            }
            if (cfg.screenWidthDp >= 400) IconAction(Glyph.PREV, "Previous on ${s.device}", { app.connect.command(s, "PreviousTrack") })
            Box(Modifier.size(48.dp), contentAlignment = Alignment.Center) {
                Box(Modifier.size(42.dp).clip(CircleShape).background(main)) {
                    IconAction(if (s.paused) Glyph.PLAY else Glyph.PAUSE, if (s.paused) "Play on ${s.device}" else "Pause ${s.device}", { app.connect.command(s, "PlayPause") },
                        tint = Color(DesignColour.readableOn(main.toArgb())), size = 42.dp, iconScale = .46f)
                }
            }
            if (cfg.screenWidthDp >= 340) IconAction(Glyph.NEXT, "Next on ${s.device}", { app.connect.command(s, "NextTrack") })
        }
        Box(Modifier.align(Alignment.BottomStart).padding(horizontal = 14.dp).fillMaxWidth().height(2.dp).background(sc.onBg.copy(alpha = .14f))) {
            Box(Modifier.fillMaxWidth(frac).fillMaxHeight().background(main))
        }
    }
}

/**
 * Another device's Now Playing: the same player as this phone's own (LivingSleeve.kt [RemoteSleeveBody]), showing the device picked in Devices,
 * else the one playing. Devices (top right) lists every device, and a tap on one there comes back here showing it.
 */
@Composable
fun RemoteScreen() {
    val app = LocalApp.current
    val ui = app.ui
    val c = app.connect
    val close = { ui.remoteOpen = false; ui.remoteId = null }
    BackHandler { close() }
    // keep the other devices' state fresh while this is open
    LaunchedEffect(Unit) { while (true) { c.loadSessions(); delay(2_500) } }
    val s = c.sessions.firstOrNull { !it.isSelf && it.id == ui.remoteId && it.itemId != null } ?: c.remoteNow(includePaused = true)
    if (s != null) { RemoteSleeveBody(s, onClose = close, onDevices = { ui.devicesOpen = true }, onPlayHere = { c.takeOver(s); close() }); return }
    Box(Modifier.fillMaxSize().safeArea(), contentAlignment = Alignment.TopCenter) {
        Column(Modifier.widthIn(max = 560.dp).fillMaxSize().padding(horizontal = 22.dp, vertical = 8.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
            Row(Modifier.fillMaxWidth().height(52.dp), verticalAlignment = Alignment.CenterVertically) {
                IconAction(Glyph.DOWN, "Close", close, tint = Color.White)
                SerifTitle("Remote", Modifier.padding(start = 4.dp), size = 30f, color = Color.White)
            }
            Txt("Nothing is playing on your other devices right now. Start a song on the web, on Windows or on another phone and it shows up here.", size = 15f, color = Color(0xCCFFFFFF), maxLines = 4)
            GlossPill("Open Devices", { ui.devicesOpen = true }, primary = true)
        }
    }
}
