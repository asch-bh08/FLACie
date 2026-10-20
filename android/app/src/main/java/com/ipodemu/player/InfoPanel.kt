package com.ipodemu.player

import android.graphics.Bitmap
import android.graphics.Paint
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.clickable
import androidx.compose.foundation.border
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.graphics.drawscope.clipPath
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.nativeCanvas
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.media3.common.C
import androidx.media3.common.util.UnstableApi
import com.ipodemu.library.AudioFacts
import com.ipodemu.library.Track
import com.ipodemu.playback.LiveAnalysis
import kotlin.math.max
import kotlin.math.min
import kotlin.math.pow
import kotlin.math.sqrt

/** What the player knows about the file it is playing right now. */
internal data class AudioInfo(val codec: String, val lossless: Boolean, val sampleRate: Int, val channels: Int, val kbps: Int, val bits: Int) {
    /** Lossless and better than CD: more than 16 bits, or above 44.1 kHz (the one rule, [com.ipodemu.library.QualityRule]); unknown values never make a file Hi-Res. */
    val hiRes: Boolean get() = com.ipodemu.library.QualityRule.isHiRes(lossless, sampleRate, bits)
    val tier: String get() = when {
        hiRes -> "Hi-Res"
        lossless -> "Lossless"
        kbps >= 256 -> "High"
        kbps > 0 -> "Standard"
        else -> ""
    }
    val sampling: String get() = listOfNotNull(bits.takeIf { it > 0 }?.let { "$it-bit" }, sampleRate.takeIf { it > 0 }?.let { "%.1f kHz".format(it / 1000.0) }).joinToString(" / ")
}

/** What the player's audio renderer reports for the item it is playing right now (taken only when that item is the song asked about). */
internal class DecoderFormat(val mime: String, val pcmBits: Int, val sampleRate: Int, val channels: Int, val kbps: Int)

/**
 * The format of one song, from one place with one rule. Real metadata comes first: the facts FLACie Web (ffprobe / Jellyfin) or the tag reader found for
 * THIS file ([facts]). The renderer's format ([decoder]) is used only when the caller has checked it belongs to this very song (the current item, playing),
 * because between two songs the renderer still reports the previous song's rate and a Hi-Res song would lend its badge to the next one. What is not known
 * stays 0, and 0 never makes a file Hi-Res.
 */
internal fun buildAudioInfo(facts: AudioFacts.Fact?, ext: String, decoder: DecoderFormat?, liveRate: Int, liveChannels: Int, size: Long, durationMs: Long): AudioInfo {
    val mime = decoder?.mime ?: ""
    val factCodec = facts?.codec?.lowercase() ?: ""
    // the file's own facts name its codec; the renderer's mime type and the extension are only for when the facts are not known (never the other way round)
    val codec = if (factCodec.isNotEmpty()) when {
        factCodec == "flac" -> "FLAC"
        factCodec == "alac" -> "ALAC"
        factCodec in setOf("mp3", "mpeg", "mpegaudio") -> "MP3"
        factCodec == "aac" -> "AAC"
        factCodec == "opus" -> "Opus"
        factCodec == "vorbis" -> "Vorbis"
        factCodec in setOf("wav", "pcm", "aiff") || factCodec.startsWith("pcm_") -> "WAV"
        else -> factCodec.uppercase()
    } else when {
        mime.contains("flac") || ext == "flac" -> "FLAC"
        mime.contains("alac") || ext == "alac" -> "ALAC"
        mime.contains("mpeg") || ext == "mp3" -> "MP3"
        mime.contains("mp4a") || mime.contains("aac") || ext in setOf("m4a", "aac") -> "AAC"
        mime.contains("opus") || ext == "opus" -> "Opus"
        mime.contains("vorbis") || ext == "ogg" -> "Vorbis"
        mime.contains("raw") || ext in setOf("wav", "aiff") -> "WAV"
        else -> ext.uppercase()
    }
    val lossless = codec in setOf("FLAC", "ALAC", "WAV") || (factCodec.isNotEmpty() && com.ipodemu.library.QualityRule.isLossless(factCodec))
    val rate = facts?.rate?.takeIf { it > 0 } ?: decoder?.sampleRate?.takeIf { it > 0 } ?: 0
    val bits = facts?.depth?.takeIf { it > 0 } ?: decoder?.pcmBits?.takeIf { it > 0 } ?: 0
    var kbps = facts?.kbps?.takeIf { it > 0 } ?: decoder?.kbps?.takeIf { it > 0 } ?: 0
    if (kbps == 0 && size > 0 && durationMs > 0) kbps = (size * 8 / durationMs).toInt()   // display only; never used for the Hi-Res decision
    val channels = decoder?.channels?.takeIf { it > 0 } ?: if (decoder != null) liveChannels else 0
    // the live analysis rate (the spectrum axis) belongs to what is playing now, so it too only counts for the playing song
    val axisRate = if (rate > 0) rate else if (decoder != null) liveRate else 0
    return AudioInfo(codec, lossless, axisRate, channels, kbps, bits)
}

@OptIn(UnstableApi::class)
@Suppress("DEPRECATION")
internal fun audioInfo(app: com.ipodemu.App, t: Track): AudioInfo {
    val exo = app.player.exo
    // the renderer's format counts only while THIS song is the current item and playing: during a skip it is still the previous song's
    val own = exo.playbackState == androidx.media3.common.Player.STATE_READY && (exo.currentMediaItem?.localConfiguration?.tag as? Track)?.path == t.path
    val f = if (own) exo.audioFormat else null
    val bits = when (f?.pcmEncoding) { C.ENCODING_PCM_16BIT -> 16; C.ENCODING_PCM_24BIT -> 24; C.ENCODING_PCM_32BIT, C.ENCODING_PCM_FLOAT -> 0; else -> 0 }   // 32-bit / float is the OUTPUT format of the audio pipeline, not the file's depth
    val kbps = ((f?.bitrate ?: -1).takeIf { it > 0 } ?: (f?.peakBitrate ?: -1).takeIf { it > 0 } ?: 0) / 1000
    val dec = if (f != null) DecoderFormat(f.sampleMimeType ?: "", bits, f.sampleRate.takeIf { it > 0 } ?: 0, f.channelCount.takeIf { it > 0 } ?: 0, kbps) else null
    val ext = t.filePath.substringAfterLast('.', "").lowercase().takeIf { it.length in 2..5 } ?: ""
    val live = app.player.live
    return buildAudioInfo(AudioFacts.of(t), ext, dec, live.sampleRate, live.channels, t.size, t.durationMs)
}

/** The song's [AudioInfo], refreshed while it is on screen: the facts can arrive after the song starts and the renderer's format only once it is playing, and a skip must never leave the previous song's value behind. */
@androidx.compose.runtime.Composable
internal fun rememberAudioInfo(app: com.ipodemu.App, t: Track): AudioInfo {
    @Suppress("UNUSED_VARIABLE") val factsVersion = AudioFacts.version
    var info by remember(t.path) { mutableStateOf(audioInfo(app, t)) }
    androidx.compose.runtime.LaunchedEffect(t.path, factsVersion) {
        var logged: AudioInfo? = null
        while (true) {
            val n = audioInfo(app, t)
            if (n != info) info = n
            if (n != logged) { logged = n; android.util.Log.d("FLACieBadge", "${t.title}: ${n.codec} ${n.bits}/${n.sampleRate} lossless=${n.lossless} hiRes=${n.hiRes}") }
            kotlinx.coroutines.delay(400)
        }
    }
    return info
}
/** Where an MP3 at this file's bitrate usually stops (0 = not an MP3, no marker); set by the panel, read by the spectrum drawing. */
private var cutHz = 0
private var cutLabel = ""

/** The graphs in the web's order (TrackInfoPanel.razor Picks); the bit rate only when the file says it. */
internal fun infoGraphs(kbps: Int, channels: Int): List<Pair<String, String>> = buildList {
    add("curve" to "Spectrum"); add("leds" to "Visualizer"); add("wall" to "Spectrogram"); add("loud" to "Loudness")
    add("stereo" to if (channels == 1) "Mono" else "Stereo")
    if (kbps > 0) add("rate" to "Bit rate")
}

private fun graphTitle(mode: String) = when (mode) { "rate" -> "Bit rate"; "curve" -> "Spectrum"; "leds" -> "Visualizer"; "wall" -> "Spectrogram"; "loud" -> "Loudness"; else -> "Stereo" }

private fun hint(mode: String, sampleRate: Int = 0) = when (mode) {
    "rate" -> "How much data each second of the song uses, next to common formats."
    "curve" -> "How loud each pitch is right now: deep sounds on the left, high sounds on the right, up to ${if (sampleRate > 0) "%.1f".format(sampleRate / 2000.0) else "22"} kHz (half this file's sample rate). A lossless file usually has sound out to the right edge; an MP3 is cut off by its encoder (about 16 kHz at 128 kbps, about 20 kHz at 320 kbps: the dashed line marks it)."
    "leds" -> "The song as it plays, deep sounds on the left and high ones on the right."
    "wall" -> "Pitch runs up the side (deep at the bottom, high at the top) and brighter means louder. The newest sound is on the right and scrolls left."
    "loud" -> "How loud the song is right now, in dB (0 is the loudest a file can go), and the last ten seconds of it."
    else -> "The dots show what left and right do together: a thin vertical line is the same sound in both, a wide cloud is a wide stereo picture. The bars are the level of each side."
}

private val InfoInk = Color(0xFFF4EFE6)
private val InfoDim = Color(0xA6F4EFE6)
private val InfoOnMain = Color(0xFF1A120C)

/** A soft card: a little of the cover's tone over the ground, rounded, no hard border (design I's .kv-sec and .graph-box). */
private fun Modifier.softCard(tone: Color): Modifier = this.fillMaxWidth().clip(RoundedCornerShape(20.dp)).background(tone.copy(alpha = .45f))

/**
 * The Info view of Now Playing, the same as the web's (TrackInfoPanel.razor): the GRAPH first (Spectrum, Visualizer, Spectrogram, Loudness, Stereo,
 * Bit rate, drawn in the cover's colours with DM Mono labels), then one card with the format badge, the quality tier and the sampling over number tiles
 * (kbps, kHz rate, bit, channels), then the song's and the file's details on soft cards.
 */
@OptIn(ExperimentalLayoutApi::class, UnstableApi::class)
@Composable
fun TrackInfoPanel(t: Track, snap: PlayerSnap, modifier: Modifier, tint: Color? = null, tone: Color? = null, ground: Color? = null) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    val accent = tint ?: sc.accent
    val toneC = tone ?: Color(0xFF24525A)
    val groundC = ground ?: Color(0xFF101416)
    var mode by rememberSaveable { mutableStateOf("curve") }
    val info = rememberAudioInfo(app, t)
    val ctx = androidx.compose.ui.platform.LocalContext.current
    val analysis = remember {
        val am = ctx.getSystemService(android.content.Context.AUDIO_SERVICE) as android.media.AudioManager
        LiveAnalysis(app.player.live) { app.player.volume * (am.getStreamVolume(android.media.AudioManager.STREAM_MUSIC).toFloat() / am.getStreamMaxVolume(android.media.AudioManager.STREAM_MUSIC).coerceAtLeast(1)) }
    }
    cutHz = if (info.codec.equals("MP3", true) && info.kbps > 0) (if (info.kbps <= 96) 15000 else if (info.kbps <= 128) 16000 else if (info.kbps <= 160) 17500 else if (info.kbps <= 192) 19000 else if (info.kbps <= 256) 20000 else 20500) else 16000
    cutLabel = if (info.codec.equals("MP3", true) && info.kbps > 0) "${info.kbps} kbps MP3 usually ends here" else "MP3 usually ends here"
    val graphs = infoGraphs(info.kbps, info.channels)
    if (graphs.none { it.first == mode }) mode = "curve"
    androidx.compose.foundation.layout.BoxWithConstraints(modifier) {
        val graphH = (maxHeight * .34f).coerceIn(150.dp, if (info.hiRes) 280.dp else 230.dp)
        Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(bottom = 24.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            // 1. the graph, picked from outlined tabs (the chosen one filled)
            Column(Modifier.softCard(toneC).padding(14.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    for ((id, label) in graphs) {
                        val on = mode == id
                        val shape = RoundedCornerShape(12.dp)
                        Box(
                            Modifier.height(48.dp).padding(vertical = 4.dp).clip(shape).background(if (on) InfoInk else Color.Transparent)
                                .border(1.dp, if (on) InfoInk else InfoInk.copy(alpha = .28f), shape)
                                .semantics { contentDescription = label; role = androidx.compose.ui.semantics.Role.Tab; selected = on }
                                .clickable { mode = id }.padding(horizontal = 14.dp),
                            contentAlignment = Alignment.Center,
                        ) { Txt(label, size = 14f, weight = FontWeight.SemiBold, color = if (on) InfoOnMain else InfoInk) }
                    }
                }
                Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                    SerifTitle(graphTitle(mode), Modifier.weight(1f), size = 28f, color = InfoInk)
                    Box(Modifier.clip(RoundedCornerShape(50)).background(if (mode == "rate") Color(0x1FFFFFFF) else accent.copy(alpha = .2f)).padding(horizontal = 10.dp, vertical = 4.dp)) {
                        MonoLabel(if (mode == "rate") "From the file" else "● Live", size = 10.5f, color = if (mode == "rate") InfoDim else accent)
                    }
                }
                Box(Modifier.pointerInput(mode) {
                    // a sideways swipe on the graph moves to the next or previous graph (and keeps the player's own pages from moving)
                    var dx = 0f
                    detectHorizontalDragGestures(onDragStart = { dx = 0f }, onDragEnd = {
                        val i = graphs.indexOfFirst { it.first == mode }
                        if (dx < -60f && i < graphs.lastIndex) mode = graphs[i + 1].first else if (dx > 60f && i > 0) mode = graphs[i - 1].first
                    }) { c, d -> c.consume(); dx += d }
                }) {
                    if (mode == "rate") RateBars(info.kbps, accent) else LiveGraph(mode, analysis, snap.playing, graphH, accent, toneC, groundC)
                }
                Txt(hint(mode, info.sampleRate), size = 12.5f, color = InfoDim, maxLines = 8)
            }
            // 2. the file at a glance, in one card: what it is and how good, then its numbers
            Column(Modifier.softCard(toneC).padding(14.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(14.dp)) {
                    val badgeBg = if (info.hiRes) Brush.linearGradient(listOf(Color(0xFFFFD36E), Color(0xFFF59E0B))) else Brush.linearGradient(listOf(InfoInk, InfoInk))
                    Box(Modifier.clip(RoundedCornerShape(12.dp)).background(badgeBg).padding(horizontal = 16.dp, vertical = 10.dp)) {
                        MonoLabel(if (info.hiRes) "Hi-Res" else info.codec.ifEmpty { "Audio" }, size = 15f, color = if (info.hiRes) Color(0xFF2A1A00) else InfoOnMain)
                    }
                    Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                        SerifTitle(info.tier.ifEmpty { "Quality unknown" }, size = 28f, color = InfoInk)
                        MonoLabel(listOf(info.sampling, if (info.channels == 1) "Mono" else if (info.channels == 2) "Stereo" else if (info.channels > 2) "${info.channels} channels" else "").filter { it.isNotEmpty() }.joinToString(" · "), size = 11f, color = InfoDim, maxLines = 2)
                    }
                }
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    val tiles = buildList {
                        if (info.kbps > 0) add(info.kbps.toString() to "kbps")
                        add((if (info.sampleRate > 0) "%.1f".format(info.sampleRate / 1000.0).removeSuffix(".0") else "?") to "kHz rate")
                        if (info.bits > 0) add(info.bits.toString() to "bit")
                        add((if (info.channels > 0) info.channels.toString() else "2") to if (info.channels == 1) "mono" else "channels")
                    }
                    for ((v, k) in tiles) Column(
                        Modifier.weight(1f).clip(RoundedCornerShape(14.dp)).border(1.dp, InfoInk.copy(alpha = .22f), RoundedCornerShape(14.dp)).padding(horizontal = 10.dp, vertical = 10.dp),
                        verticalArrangement = Arrangement.spacedBy(2.dp),
                    ) {
                        MonoLabel(v, size = 20f, color = InfoInk, upper = false)
                        MonoLabel(k, size = 9.5f, color = InfoDim)
                    }
                }
            }
            // 3. the details
            Detail(listOf(
                "Title" to t.title, "Artist" to t.artist, "Album" to t.album, "Year" to t.year.takeIf { it > 0 }?.toString(), "Genre" to t.genre,
                "Track" to t.trackNo.takeIf { it > 0 }?.let { (if (t.discNo > 1) "${t.discNo}-" else "") + it }, "Length" to t.durationMs.takeIf { it > 0 }?.let { fmtTime(it) },
            ), "Song", toneC)
            Detail(listOf(
                "Source" to com.ipodemu.library.SourceLabel.line(t.source), "Codec" to info.codec.takeIf { it.isNotEmpty() },
                "Size" to t.size.takeIf { it > 0 }?.let { if (it >= 1L shl 30) "%.2f GB".format(it / 1073741824.0) else "%.1f MB".format(it / 1048576.0) },
                "Location" to t.filePath.takeIf { it.isNotEmpty() },
                "Playback" to playbackLine(ctx, info),
            ), "File", toneC)
        }
    }
}

/** The path from file to speaker, as far as the phone says: the player hands the decoded sound over at the file's own rate and Android's mixer decides the rest. */
private fun playbackLine(ctx: android.content.Context, info: AudioInfo): String {
    if (info.sampleRate <= 0) return ""
    val am = ctx.getSystemService(android.content.Context.AUDIO_SERVICE) as android.media.AudioManager
    val mixer = am.getProperty(android.media.AudioManager.PROPERTY_OUTPUT_SAMPLE_RATE)?.toIntOrNull() ?: 0
    val usb = am.getDevices(android.media.AudioManager.GET_DEVICES_OUTPUTS).firstOrNull {
        it.type == android.media.AudioDeviceInfo.TYPE_USB_DEVICE || it.type == android.media.AudioDeviceInfo.TYPE_USB_HEADSET
    }
    val src = "${info.codec} ${info.sampling}".trim()
    val mixKhz = "%.1f kHz".format(mixer / 1000.0)
    return when {
        usb != null -> "$src → decoder (the app does not resample) → Android audio mixer → USB DAC ${usb.productName}. Android's mixer resamples to ${if (mixer > 0) mixKhz else "its own rate"} unless the phone and DAC run in Android 14's bit-perfect USB mode, which this app does not switch on yet."
        mixer > 0 && mixer != info.sampleRate -> "$src → decoder (the app does not resample) → Android audio mixer at $mixKhz: resampled by the device."
        mixer > 0 -> "$src → decoder (the app does not resample) → Android audio mixer at $mixKhz: native rate, but the mixer still processes it, so it is not bit-exact."
        else -> "$src → decoder (the app does not resample) → Android audio mixer. The phone does not say its mixer rate."
    }
}

/** A soft card of facts: a DM Mono heading, then each label in DM Mono over its value (two to a line when short, the whole width when long). */
@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun Detail(rows: List<Pair<String, String?>>, title: String, tone: Color) {
    val shown = rows.filter { !it.second.isNullOrBlank() }
    if (shown.isEmpty()) return
    Column(Modifier.softCard(tone).padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        MonoLabel(title, size = 11f, color = InfoDim)
        FlowRow(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            for ((k, v) in shown) Column(if (v!!.length > 22) Modifier.fillMaxWidth() else Modifier.fillMaxWidth(0.47f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                MonoLabel(k, size = 10f, color = InfoDim)
                Txt(v, size = 14.5f, color = InfoInk, maxLines = 4)
            }
        }
    }
}

@Composable
private fun RateBars(kbps: Int, accent: Color) {
    if (kbps <= 0) { Txt("This file doesn't say its bit rate.", size = 14f, color = InfoDim); return }
    val max = max(1411, kbps).toFloat()
    val rows = listOf("MP3, low" to 128, "MP3, high" to 320, "CD, uncompressed" to 1411, "This file" to kbps).sortedBy { it.second }
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        for ((name, v) in rows) {
            val me = name == "This file"
            Row(verticalAlignment = Alignment.CenterVertically) {
                Txt(name, Modifier.width(112.dp), size = 13f, weight = if (me) FontWeight.Bold else FontWeight.Normal, color = if (me) InfoInk else InfoDim)
                Box(Modifier.weight(1f).height(10.dp).clip(RoundedCornerShape(5.dp)).background(Color(0x14FFFFFF))) {
                    Box(Modifier.fillMaxWidth(v / max).height(10.dp).clip(RoundedCornerShape(5.dp)).background(if (me) accent else Color(0x55FFFFFF)))
                }
                MonoLabel("$v kbps", Modifier.width(80.dp).padding(start = 8.dp), size = 11f, color = if (me) InfoInk else InfoDim, upper = false)
            }
        }
    }
}


// ---- live graphs: the web's design I views (flacie.js drawCurve, drawLeds, drawWall, drawLoud, drawStereo) -----------------------------------------

private class GraphState {
    // spectrum / visualizer
    var peaks = FloatArray(0); var holds = FloatArray(0); var heights = FloatArray(0)
    // spectrogram
    var pix = IntArray(0); var pw = 0; var ph = 0; var bmp: Bitmap? = null; var acc = 0f; var lastNs = 0L
    var lut = IntArray(0); var lutKey = 0L
    // loudness
    var avg = Float.NaN; var peakHold = -90f; val hist = FloatArray(300); var histN = 0; var lastHist = 0L
    // stereo
    val trail = ArrayList<FloatArray>(); val smoothLR = floatArrayOf(Float.NaN, Float.NaN); val holdLR = floatArrayOf(-90f, -90f)
}

/** The spectrogram's colours: the ground (darkened), through the tone and the cover's colour, to near white, as the web's lutFor. */
internal fun spectrogramLut(ground: Int, tone: Int, accent: Int): IntArray {
    fun ch(c: Int, s: Int) = ((c shr s) and 255).toFloat()
    val stops = arrayOf(
        floatArrayOf(0f, ch(ground, 16) * .5f, ch(ground, 8) * .5f, ch(ground, 0) * .5f),
        floatArrayOf(.3f, ch(tone, 16), ch(tone, 8), ch(tone, 0)),
        floatArrayOf(.65f, ch(accent, 16), ch(accent, 8), ch(accent, 0)),
        floatArrayOf(1f, ch(accent, 16) + (255 - ch(accent, 16)) * .8f, ch(accent, 8) + (255 - ch(accent, 8)) * .8f, ch(accent, 0) + (255 - ch(accent, 0)) * .8f),
    )
    return IntArray(256) { i ->
        val t = i / 255f; var k = 1; while (k < stops.size - 1 && stops[k][0] < t) k++
        val a = stops[k - 1]; val b = stops[k]; val u = (t - a[0]) / (b[0] - a[0])
        fun c(q: Int) = (a[q] + (b[q] - a[q]) * u).toInt().coerceIn(0, 255)
        (0xFF shl 24) or (c(1) shl 16) or (c(2) shl 8) or c(3)
    }
}

/** A frequency label as the web writes it: "0", "5K", "20K". */
internal fun hzLabel(hz: Float): String = if (hz == 0f) "0" else "${(hz / 1000).toInt()}K"

@OptIn(UnstableApi::class)
@Composable
private fun LiveGraph(mode: String, analysis: LiveAnalysis, playing: Boolean, height: androidx.compose.ui.unit.Dp, accent: Color, tone: Color, ground: Color) {
    val state = remember(mode) { GraphState() }
    var frameNs by remember { mutableLongStateOf(0L) }
    // the tap on the sound is only on while a graph is shown
    androidx.compose.runtime.DisposableEffect(analysis) { analysis.watch(true); onDispose { analysis.watch(false) } }
    // the analysis (a 2048-point transform and 1,024 logarithms) runs on a background thread about 40 times a second: on the main thread, once per frame (120 a second on the Fold),
    // it took frames from the screen, and waiting for the audio thread's lock after each new song made it stutter
    LaunchedEffect(analysis) {
        kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.Default) {
            while (true) { analysis.update(); kotlinx.coroutines.delay(24) }
        }
    }
    LaunchedEffect(mode) {
        while (true) androidx.compose.runtime.withFrameNanos { ns -> frameNs = ns }
    }
    val ctx = androidx.compose.ui.platform.LocalContext.current
    val paint = remember {
        Paint(Paint.ANTI_ALIAS_FLAG).apply { typeface = try { androidx.core.content.res.ResourcesCompat.getFont(ctx, com.ipodemu.R.font.dm_mono_medium) } catch (_: Exception) { android.graphics.Typeface.MONOSPACE } }
    }
    Canvas(Modifier.fillMaxWidth().height(height)) {
        @Suppress("UNUSED_EXPRESSION") frameNs
        val g = Gfx(this, paint, accent, tone, ground, analysis, state, frameNs, playing && analysis.active)
        when (mode) {
            "curve" -> g.curve(); "leds" -> g.leds(); "wall" -> g.wall(); "loud" -> g.loud(); else -> g.stereo()
        }
    }
}

@OptIn(UnstableApi::class)
private class Gfx(
    val d: DrawScope, val paint: Paint, val accent: Color, val tone: Color, val ground: Color, val a: LiveAnalysis, val st: GraphState, val ns: Long, val playing: Boolean,
) {
    val ink = InfoInk; val dim = InfoDim
    val w = d.size.width; val h = d.size.height
    val dp = d.density
    val nyq get() = a.sampleRate / 2f

    fun text(s: String, x: Float, y: Float, color: Color = dim, size: Float = 10.5f, align: Paint.Align = Paint.Align.LEFT) {
        paint.color = color.toArgb(); paint.textSize = size * dp; paint.textAlign = align; paint.isFakeBoldText = false; paint.letterSpacing = .04f
        d.drawContext.canvas.nativeCanvas.drawText(s, x, y, paint)
    }

    private fun smoothPath(xs: FloatArray, ys: FloatArray, n: Int): Path = Path().apply {
        moveTo(xs[0], ys[0])
        for (i in 1 until n - 1) quadraticBezierTo(xs[i], ys[i], (xs[i] + xs[i + 1]) / 2, (ys[i] + ys[i + 1]) / 2)
    }

    /** A dotted line, the web's guides (2 px dots every 6 px). */
    private fun dottedV(x: Float, top: Float, bottom: Float, color: Color, dash: Float = 2f) {
        var y = top; while (y < bottom) { d.drawRect(color, Offset(x, y), Size(1f * dp, dash * dp)); y += 6 * dp }
    }
    private fun dottedH(y: Float, x0: Float, x1: Float, color: Color) {
        var x = x0; while (x < x1) { d.drawRect(color, Offset(x, y), Size(2 * dp, 1f * dp)); x += 6 * dp }
    }

    /** A line with a soft glow under it (the web's shadowBlur), drawn as wider faint strokes beneath the line itself. */
    private fun glow(path: Path, width: Float) {
        d.drawPath(path, accent.copy(alpha = .12f), style = Stroke(width * 4f, cap = StrokeCap.Round, join = androidx.compose.ui.graphics.StrokeJoin.Round))
        d.drawPath(path, accent.copy(alpha = .25f), style = Stroke(width * 2.2f, cap = StrokeCap.Round, join = androidx.compose.ui.graphics.StrokeJoin.Round))
        d.drawPath(path, accent, style = Stroke(width, cap = StrokeCap.Round, join = androidx.compose.ui.graphics.StrokeJoin.Round))
    }

    // spectrum: how loud each pitch is, 0 Hz on the left to the file's top on the right: a glowing line over a soft fill, the slowly falling peaks as dots
    fun curve() {
        val maxHz = nyq; val use = (a.bins * maxHz / nyq).toInt().coerceAtLeast(2)
        val stepHz = if (maxHz > 48000f) 20000f else if (maxHz > 26000f) 10000f else 5000f
        val bottom = h - 20 * dp; val top = 10 * dp; val span = bottom - top
        if (st.peaks.size != use) st.peaks = FloatArray(use)
        var k = 0f
        while (k <= maxHz) {
            val x = min(w - 1, k / maxHz * w)
            dottedV(x, top, bottom, ink.copy(alpha = .14f))
            text(hzLabel(k), if (k == 0f) 2f else min(x, w - 14 * dp), h - 4 * dp, align = if (k == 0f) Paint.Align.LEFT else Paint.Align.CENTER)
            k += stepHz
        }
        if (cutHz > 0 && maxHz > cutHz) {
            val x = cutHz.toFloat() / maxHz * w
            dottedV(x, top, bottom, ink.copy(alpha = .45f), 3f)
            val label = cutLabel.uppercase()
            paint.textSize = 10.5f * dp
            // the label sits left of the line; near the left edge (a Hi-Res file's wide axis) it would run off: then it goes right of the line
            if (x - 6 * dp - paint.measureText(label) < 0f) text(label, x + 6 * dp, top + 8 * dp, ink, align = Paint.Align.LEFT)
            else text(label, x - 6 * dp, top + 8 * dp, ink, align = Paint.Align.RIGHT)
        }
        val xs = FloatArray(use); val ys = FloatArray(use)
        for (i in 0 until use) {
            val v = a.freq[i] / 255f; st.peaks[i] = max(v, st.peaks[i] - 0.004f)
            xs[i] = i / (use - 1f) * w; ys[i] = bottom - v.pow(1.15f) * span
        }
        val fill = smoothPath(xs, ys, use).apply { lineTo(w, bottom); lineTo(0f, bottom); close() }
        d.drawPath(fill, Brush.verticalGradient(listOf(accent.copy(alpha = .55f), accent.copy(alpha = 0f)), startY = top, endY = bottom))
        glow(smoothPath(xs, ys, use), 2.5f * dp)
        // peaks: a dot every few pixels
        val every = max(1, (use / (w / (5 * dp))).toInt())
        var i = 0
        while (i < use) {
            val y = bottom - st.peaks[i].pow(1.15f) * span
            if (y < bottom - 2 * dp) d.drawCircle(ink.copy(alpha = .75f), 1.3f * dp, Offset(i / (use - 1f) * w, y))
            i += every
        }
        d.drawRect(ink.copy(alpha = .18f), Offset(0f, bottom), Size(w, 1f * dp))
    }

    // visualizer: rounded bars mirrored about a centre line, deep sounds left, high right, each with a floating peak dot
    fun leds() {
        val n = max(20, min(64, (w / (10 * dp)).toInt())); val lo = 35f; val hi = min(nyq, 18000f)
        if (st.heights.size != n) { st.heights = FloatArray(n); st.holds = FloatArray(n) }
        val slot = w / n; val bw = max(3 * dp, slot * 0.62f); val mid = (h - 16 * dp) / 2; val half = mid - 6 * dp
        val grad = Brush.verticalGradient(0f to ink.copy(alpha = .95f), .35f to accent, .65f to accent, 1f to ink.copy(alpha = .95f), startY = mid - half, endY = mid + half)
        for (i in 0 until n) {
            val f0 = lo * (hi / lo).pow(i / n.toFloat()); val f1 = lo * (hi / lo).pow((i + 1) / n.toFloat())
            val b0 = (f0 / nyq * a.bins).toInt(); val b1 = max(b0 + 1, kotlin.math.ceil(f1 / nyq * a.bins).toInt())
            var m = 0; for (k in b0 until min(b1, a.bins)) m = max(m, a.freq[k])
            // the highs carry less energy in real music, so they are lifted a little
            val v = min(1f, (m / 255f).pow(1.5f) * (1 + 0.7f * i / n))
            st.heights[i] = max(v, st.heights[i] - 0.035f); st.holds[i] = max(st.heights[i], st.holds[i] - 0.008f)
            val x = i * slot + (slot - bw) / 2; val bh = max(bw, st.heights[i] * half)
            d.drawRoundRect(ink.copy(alpha = .06f), Offset(x, mid - half), Size(bw, half * 2), CornerRadius(bw / 2))
            d.drawRoundRect(grad, Offset(x, mid - bh), Size(bw, bh * 2), CornerRadius(bw / 2))
            if (st.holds[i] > .03f) {
                val py = st.holds[i] * half + 4 * dp
                d.drawCircle(ink, bw / 2.4f, Offset(x + bw / 2, mid - py)); d.drawCircle(ink, bw / 2.4f, Offset(x + bw / 2, mid + py))
            }
        }
        text("BASS", 0f, h - 3 * dp); text("MID", w / 2, h - 3 * dp, align = Paint.Align.CENTER); text("TREBLE", w, h - 3 * dp, align = Paint.Align.RIGHT)
    }

    // spectrogram: a waterfall in the cover's colours. Pitch up the side, newest sound at the right edge, scrolling left as the song plays
    fun wall() {
        val key = (accent.toArgb().toLong() shl 32) xor (tone.toArgb().toLong() * 31) xor ground.toArgb().toLong()
        if (st.lutKey != key || st.lut.isEmpty()) { st.lut = spectrogramLut(ground.toArgb(), tone.toArgb(), accent.toArgb()); st.lutKey = key }
        val lut = st.lut
        val x0 = 40 * dp; val x1 = w; val y0 = 2 * dp; val y1 = h - 20 * dp
        val pw = (x1 - x0).toInt().coerceAtLeast(2); val ph = (y1 - y0).toInt().coerceAtLeast(2); val speed = 60 * dp
        if (st.pw != pw || st.ph != ph) { st.pw = pw; st.ph = ph; st.pix = IntArray(pw * ph) { lut[0] }; st.bmp = Bitmap.createBitmap(pw, ph, Bitmap.Config.ARGB_8888); st.lastNs = ns; st.acc = 0f }
        val maxHz = nyq; val use = (a.bins * maxHz / nyq).toInt().coerceAtLeast(2)
        val stepHz = if (maxHz > 48000f) 16000f else if (maxHz > 26000f) 8000f else 4000f
        val dt = min(100f, (ns - st.lastNs) / 1_000_000f); st.lastNs = ns
        if (playing) st.acc += dt / 1000f * speed
        val step = st.acc.toInt()
        if (step >= 1) {
            st.acc -= step
            val s = min(step, pw - 1)
            for (y in 0 until ph) {
                val row = y * pw
                System.arraycopy(st.pix, row + s, st.pix, row, pw - s)
                val k0 = ((ph - 1 - y) * use / ph); val k1 = max(k0 + 1, (ph - y) * use / ph)
                var m = 0; for (k in k0 until min(k1, use)) m = max(m, a.freq[k])
                val c = lut[((m / 255f).pow(1.25f) * 255).toInt().coerceIn(0, 255)]
                for (x in pw - s until pw) st.pix[row + x] = c
            }
        }
        val bmp = st.bmp ?: return
        bmp.setPixels(st.pix, 0, pw, 0, 0, pw, ph)
        val clip = Path().apply { addRoundRect(androidx.compose.ui.geometry.RoundRect(x0, y0, x0 + pw, y0 + ph, CornerRadius(12 * dp))) }
        d.clipPath(clip) {
            drawImage(bmp.asImageBitmap(), Offset(x0, y0))
            var k = stepHz
            while (k <= maxHz) { val y = y1 - k / maxHz * ph; dottedH(y, x0, x1, ink.copy(alpha = if (k == 16000f || k == 20000f) .4f else .14f)); k += stepHz }
        }
        var k = 0f
        while (k <= maxHz) {
            val y = y1 - k / maxHz * ph; val key2 = k == 16000f || k == 20000f
            text(hzLabel(k), x0 - 8 * dp, min(y1 - 1 * dp, max(y0 + 10 * dp, y + 4 * dp)), color = if (key2) ink else dim, align = Paint.Align.RIGHT)
            k += stepHz
        }
        text("${(pw / speed).toInt()} S AGO", x0, h - 4 * dp); text("NOW", x1, h - 4 * dp, align = Paint.Align.RIGHT)
    }

    private fun X(db: Float) = ((db + 60f) / 60f).coerceIn(0f, 1f)

    // loudness: the level right now as a big number and a row of lit capsules with a peak tick, then the last ten seconds as a soft hill
    fun loud() {
        val (rdb, pdb) = a.levels()
        st.avg = if (st.avg.isNaN()) rdb else st.avg + (rdb - st.avg) * 0.25f
        st.peakHold = max(pdb, st.peakHold - 0.4f)
        if (playing && ns / 1_000_000 - st.lastHist > 33) {
            st.lastHist = ns / 1_000_000
            if (st.histN < st.hist.size) st.hist[st.histN++] = rdb else { System.arraycopy(st.hist, 1, st.hist, 0, st.hist.size - 1); st.hist[st.hist.size - 1] = rdb }
        }
        val num = if (st.avg <= -59) "−∞" else "%.1f".format(st.avg)
        text(num, 0f, 34 * dp, ink, 34f)
        paint.textSize = 34 * dp; val tw = paint.measureText(num)
        text("DB AVERAGE NOW", tw + 10 * dp, 32 * dp, dim, 11f)
        text("PEAK ${if (st.peakHold <= -89) "−∞" else "%.1f".format(st.peakHold)} DB", w, 32 * dp, ink, 11f, Paint.Align.RIGHT)
        // the meter: capsules that light up to the level; the last few turn warm near 0 dB
        val my = 48 * dp; val mh = 14 * dp; val n = max(20, (w / (9 * dp)).toInt()); val cw = w / n
        val lit = X(st.avg) * n; val pki = min(n - 1, (X(st.peakHold) * n).toInt())
        for (i in 0 until n) {
            val hot = i >= n * .88f
            val col = if (i == pki) ink else if (i < lit) (if (hot) Color(0xFFFF7A5C) else accent) else ink.copy(alpha = .08f)
            d.drawRoundRect(col, Offset(i * cw + dp, my), Size(cw - 2.5f * dp, mh), CornerRadius(3 * dp))
        }
        val hy0 = my + mh + 18 * dp; val hy1 = h - 20 * dp; val hh = hy1 - hy0
        for (db in intArrayOf(-12, -24, -36, -48)) {
            val y = hy1 - X(db.toFloat()) * hh
            dottedH(y, 0f, w, ink.copy(alpha = .12f)); text("$db", 2f, y - 4 * dp)
        }
        if (st.histN > 1) {
            val n2 = st.histN; val xs = FloatArray(n2) { w - (n2 - 1 - it) / 299f * w }; val ys = FloatArray(n2) { hy1 - X(st.hist[it]) * hh }
            val fill = Path().apply { moveTo(xs[0], hy1); for (i in 0 until n2) lineTo(xs[i], ys[i]); lineTo(xs[n2 - 1], hy1); close() }
            d.drawPath(fill, Brush.verticalGradient(listOf(accent.copy(alpha = .6f), accent.copy(alpha = 0f)), startY = hy0, endY = hy1))
            glow(Path().apply { moveTo(xs[0], ys[0]); for (i in 1 until n2) lineTo(xs[i], ys[i]) }, 2.5f * dp)
        }
        text("10 S AGO", 0f, h - 4 * dp); text("NOW", w, h - 4 * dp, align = Paint.Align.RIGHT)
    }

    // stereo: a phase scope (what left and right do together) in a soft disc, beside a capsule meter for each side
    fun stereo() {
        val n = a.fftSize; var sl = 0f; var sr = 0f; var pl = 0f; var pr = 0f
        for (i in 0 until n) { val l = a.left[i]; val r = a.right[i]; sl += l * l; sr += r * r; pl = max(pl, kotlin.math.abs(l)); pr = max(pr, kotlin.math.abs(r)) }
        val rl = a.db(sqrt(sl / n)); val rr = a.db(sqrt(sr / n))
        for (i in 0..1) { val v = if (i == 0) rl else rr; st.smoothLR[i] = if (st.smoothLR[i].isNaN()) v else st.smoothLR[i] + (v - st.smoothLR[i]) * 0.25f; st.holdLR[i] = max(a.db(if (i == 0) pl else pr), st.holdLR[i] - 0.4f) }
        val s = min(h - 6 * dp, w * 0.52f); val cx = s / 2; val cy = h / 2; val rad = s / 2 - 6 * dp
        if (playing) {
            val pts = FloatArray((n / 2) * 2); var k = 0
            for (i in 0 until n step 2) { val l = a.left[i]; val r = a.right[i]; pts[k++] = cx + (r - l) * 0.7071f * rad * 1.2f; pts[k++] = cy - (l + r) * 0.7071f * rad * 1.2f }
            st.trail.add(0, pts); while (st.trail.size > 5) st.trail.removeAt(st.trail.size - 1)
        } else if (st.trail.isNotEmpty()) st.trail.removeAt(st.trail.size - 1)
        d.drawCircle(Brush.radialGradient(listOf(tone.copy(alpha = .9f), tone.copy(alpha = .25f)), Offset(cx, cy), rad), rad, Offset(cx, cy))
        val grid = ink.copy(alpha = .14f); val dash = PathEffect.dashPathEffect(floatArrayOf(3 * dp, 4 * dp))
        d.drawLine(grid, Offset(cx, cy - rad), Offset(cx, cy + rad), dp, pathEffect = dash); d.drawLine(grid, Offset(cx - rad, cy), Offset(cx + rad, cy), dp, pathEffect = dash)
        d.drawLine(grid, Offset(cx - rad * .7071f, cy - rad * .7071f), Offset(cx + rad * .7071f, cy + rad * .7071f), dp, pathEffect = dash)
        d.drawLine(grid, Offset(cx + rad * .7071f, cy - rad * .7071f), Offset(cx - rad * .7071f, cy + rad * .7071f), dp, pathEffect = dash)
        val alphas = floatArrayOf(.75f, .45f, .28f, .16f, .08f)
        st.trail.forEachIndexed { ti, pts -> var i = 0; while (i < pts.size) { d.drawCircle(accent.copy(alpha = alphas[ti]), dp, Offset(pts[i], pts[i + 1])); i += 2 } }
        text("L", cx - rad * .7071f - 8 * dp, cy - rad * .7071f - 2 * dp, ink, align = Paint.Align.CENTER); text("R", cx + rad * .7071f + 8 * dp, cy - rad * .7071f - 2 * dp, ink, align = Paint.Align.CENTER)
        // the two level meters: stacks of capsules
        val mx = s + 20 * dp; val mw = max(14 * dp, min(30 * dp, (w - mx - 70 * dp) / 2)); val mt = 6 * dp; val mb = h - 22 * dp; val mhh = mb - mt
        val rows = max(10, (mhh / (7 * dp)).toInt()); val rh = mhh / rows
        for (i in 0..1) {
            val x = mx + i * (mw + 12 * dp); val col = if (i == 0) accent else ink
            val lit = X(st.smoothLR[i]) * rows; val pk = min(rows - 1, (X(st.holdLR[i]) * rows).toInt())
            for (r in 0 until rows) {
                val y = mb - (r + 1) * rh
                d.drawRoundRect(if (r == pk) ink else if (r < lit) col else ink.copy(alpha = .07f), Offset(x, y + dp), Size(mw, rh - 2.5f * dp), CornerRadius(3 * dp))
            }
            text(if (i == 0) "L" else "R", x + mw / 2, h - 6 * dp, ink, align = Paint.Align.CENTER)
        }
        val tx = mx + 2 * (mw + 12 * dp) + 2 * dp
        for (db in intArrayOf(0, -12, -24, -36, -48)) text("$db", tx, mb - X(db.toFloat()) * mhh + 4 * dp)
    }
}
