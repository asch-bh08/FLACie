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
import com.ipodemu.library.Track
import com.ipodemu.playback.LiveAnalysis
import kotlin.math.max
import kotlin.math.min
import kotlin.math.pow
import kotlin.math.sqrt

/** What the player knows about the file it is playing right now. */
private class AudioInfo(val codec: String, val lossless: Boolean, val sampleRate: Int, val channels: Int, val kbps: Int, val bits: Int) {
    /** Lossless and better than CD: more than 16 bits, or above 44.1 kHz. */
    val hiRes: Boolean get() = lossless && (sampleRate > 44_100 || bits > 16)
    val tier: String get() = when {
        hiRes -> "Hi-Res"
        lossless -> "Lossless"
        kbps >= 256 -> "High"
        kbps > 0 -> "Standard"
        else -> ""
    }
    val sampling: String get() = listOfNotNull(bits.takeIf { it > 0 }?.let { "$it-bit" }, sampleRate.takeIf { it > 0 }?.let { "%.1f kHz".format(it / 1000.0) }).joinToString(" / ")
}

@OptIn(UnstableApi::class)
@Suppress("DEPRECATION")
private fun audioInfo(app: com.ipodemu.App, t: Track): AudioInfo {
    val f = app.player.exo.audioFormat
    val mime = f?.sampleMimeType ?: ""
    val ext = t.filePath.substringAfterLast('.', "").lowercase().takeIf { it.length in 2..5 } ?: ""
    val codec = when {
        mime.contains("flac") || ext == "flac" -> "FLAC"
        mime.contains("alac") || ext == "alac" -> "ALAC"
        mime.contains("mpeg") || ext == "mp3" -> "MP3"
        mime.contains("mp4a") || mime.contains("aac") || ext in setOf("m4a", "aac") -> "AAC"
        mime.contains("opus") || ext == "opus" -> "Opus"
        mime.contains("vorbis") || ext == "ogg" -> "Vorbis"
        mime.contains("raw") || ext in setOf("wav", "aiff") -> "WAV"
        else -> ext.uppercase()
    }
    val bits = when (f?.pcmEncoding) { C.ENCODING_PCM_16BIT -> 16; C.ENCODING_PCM_24BIT -> 24; C.ENCODING_PCM_32BIT, C.ENCODING_PCM_FLOAT -> 32; else -> 0 }
    var kbps = ((f?.bitrate ?: -1).takeIf { it > 0 } ?: (f?.peakBitrate ?: -1).takeIf { it > 0 } ?: 0) / 1000
    if (kbps == 0 && t.size > 0 && t.durationMs > 0) kbps = (t.size * 8 / t.durationMs).toInt()
    val live = app.player.live
    return AudioInfo(codec, codec in setOf("FLAC", "ALAC", "WAV"), f?.sampleRate?.takeIf { it > 0 } ?: live.sampleRate, f?.channelCount?.takeIf { it > 0 } ?: live.channels, kbps, bits)
}

/** Where an MP3 at this file's bitrate usually stops (0 = not an MP3, no marker); set by the panel, read by the spectrum drawing. */
private var cutHz = 0
private var cutLabel = ""

private val MODES = listOf("rate" to "Bit rate", "curve" to "Spectrum", "leds" to "Visualizer", "wall" to "Spectrogram", "loud" to "Loudness", "stereo" to "Stereo")

private fun hint(mode: String, sampleRate: Int = 0) = when (mode) {
    "rate" -> "How much data each second of the song uses, next to common formats."
    "curve" -> "How loud each pitch is right now: deep sounds on the left, high on the right, up to ${if (sampleRate > 0) "%.1f".format(sampleRate / 2000.0) else "22"} kHz (half this file's sample rate). An MP3 is cut off by its encoder (about 16 kHz at 128 kbps, about 20 kHz at 320 kbps: the dashed line marks it)."
    "leds" -> "The song as it plays, deep sounds on the left and high ones on the right."
    "wall" -> "Pitch runs up the side (deep at the bottom, high at the top) and brighter means louder. The newest sound is on the right and scrolls left."
    "loud" -> "How loud the song is right now, in dB (0 is the loudest a file can go), and the last ten seconds."
    else -> "The dots show what left and right do together: a thin vertical line is the same sound in both, a wide cloud is a wide stereo picture. The bars are the level of each side."
}

/** The Info view of Now Playing: format at a glance, a graph for whatever you pick (all but the bit rate move with the song), then the file's details. */
@OptIn(ExperimentalLayoutApi::class, UnstableApi::class)
@Composable
fun TrackInfoPanel(t: Track, snap: PlayerSnap, modifier: Modifier) {
    val app = LocalApp.current
    val sc = LocalScheme.current
    var mode by rememberSaveable { mutableStateOf("curve") }
    val info = remember(t.path, snap.playing) { audioInfo(app, t) }
    val ctx = androidx.compose.ui.platform.LocalContext.current
    val analysis = remember {
        val am = ctx.getSystemService(android.content.Context.AUDIO_SERVICE) as android.media.AudioManager
        LiveAnalysis(app.player.live) { app.player.volume * (am.getStreamVolume(android.media.AudioManager.STREAM_MUSIC).toFloat() / am.getStreamMaxVolume(android.media.AudioManager.STREAM_MUSIC).coerceAtLeast(1)) }
    }
    cutHz = if (info.codec.equals("MP3", true) && info.kbps > 0) (if (info.kbps <= 96) 15000 else if (info.kbps <= 128) 16000 else if (info.kbps <= 160) 17500 else if (info.kbps <= 192) 19000 else if (info.kbps <= 256) 20000 else 20500) else 16000
    cutLabel = if (info.codec.equals("MP3", true) && info.kbps > 0) "${info.kbps} kbps MP3 usually ends here" else "MP3 usually ends here"
    androidx.compose.foundation.layout.BoxWithConstraints(modifier) {
    // the graph takes what room the panel has (header, chips and text above it need about 320dp), so its axis is never cut off at the bottom; Hi-Res files get more when there is more
    val graphH = (maxHeight - 380.dp).coerceIn(120.dp, if (info.hiRes) 300.dp else 220.dp)
    Column(Modifier.fillMaxSize().clip(RoundedCornerShape(16.dp)).background(Color(0x1AFFFFFF)).verticalScroll(rememberScrollState()).padding(16.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Box(Modifier.clip(RoundedCornerShape(10.dp)).background(if (info.hiRes) Brush.linearGradient(listOf(Color(0xFFFFD36E), Color(0xFFF59E0B))) else Brush.linearGradient(listOf(if (info.lossless) Color(0xFF1D2A1C) else Color(0x33FFFFFF), if (info.lossless) Color(0xFF1D2A1C) else Color(0x33FFFFFF)))).padding(horizontal = 12.dp, vertical = 8.dp)) {
                Txt(if (info.hiRes) "HI-RES" else info.codec.ifEmpty { "AUDIO" }, size = 16f, weight = FontWeight.ExtraBold, color = if (info.hiRes) Color(0xFF2A1A00) else if (info.lossless) Color(0xFFB8F0A8) else sc.onBg)
            }
            Column {
                Txt(info.tier.ifEmpty { "Quality unknown" }, size = 15f, weight = FontWeight.SemiBold)
                Txt(listOf(info.sampling, if (info.channels == 1) "Mono" else if (info.channels == 2) "Stereo" else "${info.channels} channels").filter { it.isNotEmpty() }.joinToString(" · "), size = 12f, color = sc.onBgDim)
            }
        }
        // the graphs as a tidy 3-by-2 grid of equal pills (nothing cut off), each with a proper name; the file's own figure is in the card below
        androidx.compose.foundation.layout.FlowRow(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp), maxItemsInEachRow = 3) {
            for ((id, label) in listOf("rate" to "Bit rate", "curve" to "Spectrum", "loud" to "Level", "stereo" to "Stereo", "leds" to "Visualizer", "wall" to "Spectrogram"))
                Box(Modifier.weight(1f).height(36.dp).clip(RoundedCornerShape(50)).background(if (mode == id) sc.accent else Palette.surface2).clickable { mode = id }, contentAlignment = Alignment.Center) {
                    Txt(label, size = 12f, weight = FontWeight.SemiBold, color = if (mode == id) sc.accent.readableInk() else Color.White, maxLines = 1)
                }
        }
        var hintOpen by remember(mode) { mutableStateOf(false) }
        val order = listOf("rate", "curve", "loud", "stereo", "leds", "wall")
        Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(Color(0x14FFFFFF)).padding(14.dp).pointerInput(mode) {
            // a sideways swipe on the graph moves to the next or previous graph (and keeps the player's own pages from moving)
            var dx = 0f
            detectHorizontalDragGestures(onDragStart = { dx = 0f }, onDragEnd = {
                val i = order.indexOf(mode)
                if (dx < -60f && i < order.lastIndex) mode = order[i + 1] else if (dx > 60f && i > 0) mode = order[i - 1]
            }) { c, d -> c.consume(); dx += d }
        }, verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Txt(MODES.first { it.first == mode }.second + (when (mode) { "rate" -> if (info.kbps > 0) " · ${info.kbps} kbps" else ""; "curve" -> if (info.sampleRate > 0) " · %.1f kHz".format(info.sampleRate / 1000.0) else ""; "loud" -> if (info.bits > 0) " · ${info.bits} bit" else ""; else -> "" }), Modifier.weight(1f), size = 17f, weight = FontWeight.ExtraBold)
                Box(Modifier.clip(RoundedCornerShape(50)).background(if (mode == "rate") Color(0x1FFFFFFF) else sc.accent.copy(alpha = .18f)).padding(horizontal = 9.dp, vertical = 3.dp)) {
                    Txt(if (mode == "rate") "FROM THE FILE" else "● LIVE", size = 10f, weight = FontWeight.ExtraBold, color = if (mode == "rate") sc.onBgDim else sc.accent)
                }
            }
            Txt(hint(mode, info.sampleRate), Modifier.clickable { hintOpen = !hintOpen }, size = 12.5f, color = sc.onBgDim, maxLines = if (hintOpen) 8 else 2)
            if (mode == "rate") RateBars(info.kbps) else LiveGraph(mode, analysis, snap.playing, graphH)
        }
        Detail(listOf(
            "Title" to t.title, "Artist" to t.artist, "Album" to t.album, "Year" to t.year.takeIf { it > 0 }?.toString(), "Genre" to t.genre,
            "Track" to t.trackNo.takeIf { it > 0 }?.let { (if (t.discNo > 1) "${t.discNo}-" else "") + it }, "Length" to t.durationMs.takeIf { it > 0 }?.let { fmtTime(it) },
        ), "SONG")
        Detail(listOf(
            "Source" to (if (t.source == com.ipodemu.library.TrackSource.CLOUD) "Streaming (a new download, played from your server)" else t.source.name.lowercase().replaceFirstChar { it.uppercase() }), "Codec" to info.codec.takeIf { it.isNotEmpty() },
            "Size" to t.size.takeIf { it > 0 }?.let { if (it >= 1L shl 30) "%.2f GB".format(it / 1073741824.0) else "%.1f MB".format(it / 1048576.0) },
            "Location" to t.filePath.takeIf { it.isNotEmpty() },
            "Playback" to playbackLine(ctx, info),
        ), "FILE")
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

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun Detail(rows: List<Pair<String, String?>>, title: String) {
    val sc = LocalScheme.current
    val shown = rows.filter { !it.second.isNullOrBlank() }
    if (shown.isEmpty()) return
    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Box(Modifier.fillMaxWidth().height(1.dp).background(Color(0x1AFFFFFF)))
        Txt(title, Modifier.padding(top = 6.dp), size = 11f, weight = FontWeight.ExtraBold, color = sc.onBgDim)
        androidx.compose.foundation.layout.FlowRow(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            // short facts sit two to a line, long ones (location, playback path) take the whole width
            for ((k, v) in shown) Column(if (v!!.length > 22) Modifier.fillMaxWidth() else Modifier.fillMaxWidth(0.47f)) {
                Txt(k, size = 11f, color = sc.onBgDim)
                Txt(v, size = 14f, maxLines = 3)
            }
        }
    }
}

@Composable
private fun RateBars(kbps: Int) {
    val sc = LocalScheme.current
    if (kbps <= 0) { Txt("This file doesn't say its bit rate.", size = 14f, color = sc.onBgDim); return }
    val max = max(1411, kbps).toFloat()
    val rows = listOf("MP3, low" to 128, "MP3, high" to 320, "CD, uncompressed" to 1411, "This file" to kbps).sortedBy { it.second }
    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
        for ((name, v) in rows) {
            val me = name == "This file"
            Row(verticalAlignment = Alignment.CenterVertically) {
                Txt(name, Modifier.width(112.dp), size = 13f, weight = if (me) FontWeight.Bold else FontWeight.Normal, color = if (me) sc.onBg else sc.onBgDim)
                Box(Modifier.weight(1f).height(10.dp).clip(RoundedCornerShape(5.dp)).background(Color(0x1FFFFFFF))) {
                    Box(Modifier.fillMaxWidth(v / max).height(10.dp).background(if (me) Brush.horizontalGradient(listOf(Color(0xFFFFB35C), sc.accent)) else Brush.horizontalGradient(listOf(Color(0x55FFFFFF), Color(0x55FFFFFF)))))
                }
                Txt("$v kbps", Modifier.width(74.dp).padding(start = 8.dp), size = 12.5f, weight = if (me) FontWeight.Bold else FontWeight.Normal, align = androidx.compose.ui.text.style.TextAlign.End)
            }
        }
    }
}


// ---- live graphs -------------------------------------------------------------------------------------------------------

private class GraphState {
    // spectrum / bars
    var peaks = FloatArray(0); var holds = FloatArray(0); var heights = FloatArray(0)
    // spectrogram
    var pix = IntArray(0); var pw = 0; var ph = 0; var bmp: Bitmap? = null; var acc = 0f; var lastNs = 0L
    // loudness
    var avg = Float.NaN; var peakHold = -90f; val hist = FloatArray(300); var histN = 0; var lastHist = 0L
    // stereo
    val trail = ArrayList<FloatArray>(); val smoothLR = floatArrayOf(Float.NaN, Float.NaN); val holdLR = floatArrayOf(-90f, -90f)
}

private val LUT = IntArray(256).also { lut ->
    val stops = arrayOf(floatArrayOf(0f, 0f, 0f, 4f), floatArrayOf(.18f, 38f, 12f, 84f), floatArrayOf(.4f, 104f, 22f, 112f), floatArrayOf(.62f, 190f, 52f, 82f), floatArrayOf(.82f, 250f, 140f, 10f), floatArrayOf(1f, 252f, 255f, 170f))
    for (i in 0 until 256) {
        val t = i / 255f; var k = 1; while (k < stops.size - 1 && stops[k][0] < t) k++
        val a = stops[k - 1]; val b = stops[k]; val u = (t - a[0]) / (b[0] - a[0])
        fun ch(c: Int) = (a[c] + (b[c] - a[c]) * u).toInt().coerceIn(0, 255)
        lut[i] = (0xFF shl 24) or (ch(1) shl 16) or (ch(2) shl 8) or ch(3)
    }
}

@OptIn(UnstableApi::class)
@Composable
private fun LiveGraph(mode: String, analysis: LiveAnalysis, playing: Boolean, height: androidx.compose.ui.unit.Dp) {
    val sc = LocalScheme.current
    val state = remember(mode) { GraphState() }
    var frameNs by remember { mutableLongStateOf(0L) }
    LaunchedEffect(mode) {
        while (true) androidx.compose.runtime.withFrameNanos { ns -> analysis.update(); frameNs = ns }
    }
    val paint = remember { Paint(Paint.ANTI_ALIAS_FLAG) }

    val h = height
    Canvas(Modifier.fillMaxWidth().height(h)) {
        @Suppress("UNUSED_EXPRESSION") frameNs
        val g = Gfx(this, paint, sc.accent, sc.onBgDim, sc.onBg, analysis, state, frameNs, playing && analysis.active)
        when (mode) {
            "curve" -> g.curve(); "leds" -> g.leds(); "wall" -> g.wall(); "loud" -> g.loud(); else -> g.stereo()
        }
    }
}

@OptIn(UnstableApi::class)
private class Gfx(
    val d: DrawScope, val paint: Paint, val accent: Color, val dim: Color, val ink: Color, val a: LiveAnalysis, val st: GraphState, val ns: Long, val playing: Boolean,
) {
    val w = d.size.width; val h = d.size.height
    val dp = d.density
    val nyq get() = a.sampleRate / 2f

    fun text(s: String, x: Float, y: Float, color: Color = dim, size: Float = 11f, align: Paint.Align = Paint.Align.LEFT, bold: Boolean = false) {
        paint.color = color.toArgb(); paint.textSize = size * dp; paint.textAlign = align; paint.isFakeBoldText = bold
        d.drawContext.canvas.nativeCanvas.drawText(s, x, y, paint)
    }

    private fun smoothPath(xs: FloatArray, ys: FloatArray, n: Int): Path = Path().apply {
        moveTo(xs[0], ys[0])
        for (i in 1 until n - 1) quadraticBezierTo(xs[i], ys[i], (xs[i] + xs[i + 1]) / 2, (ys[i] + ys[i + 1]) / 2)
    }

    // spectrum: 0 Hz up to the file's own Nyquist (half its sample rate), a filled curve and a slowly falling peak line
    fun curve() {
        val maxHz = nyq; val use = (a.bins * maxHz / nyq).toInt().coerceAtLeast(2)
        val stepHz = if (maxHz > 48000f) 20000f else if (maxHz > 26000f) 10000f else 5000f
        val bottom = h - 18 * dp; val top = 6 * dp; val span = bottom - top
        if (st.peaks.size != use) st.peaks = FloatArray(use)
        var k = 0f
        while (k <= maxHz) {
            val x = min(w - 1, k / maxHz * w)
            d.drawLine(Color(0x14FFFFFF), Offset(x, top), Offset(x, bottom), 1f)
            text(if (k == 0f) "0" else "${(k / 1000).toInt()} kHz", if (k == 0f) 2f else min(x, w - 22 * dp), h - 3 * dp, align = if (k == 0f) Paint.Align.LEFT else Paint.Align.CENTER)
            k += stepHz
        }
        if (cutHz > 0 && maxHz > cutHz) {
            val x = cutHz.toFloat() / maxHz * w
            d.drawLine(Color(0x4DFFFFFF), Offset(x, top), Offset(x, bottom), 1f, pathEffect = PathEffect.dashPathEffect(floatArrayOf(4 * dp, 4 * dp)))
            text(cutLabel, x - 5 * dp, top + 10 * dp, align = Paint.Align.RIGHT)
        }
        val xs = FloatArray(use); val ys = FloatArray(use); val pxs = FloatArray(use); val pys = FloatArray(use)
        for (i in 0 until use) {
            val v = a.freq[i] / 255f; st.peaks[i] = max(v, st.peaks[i] - 0.004f)
            xs[i] = i / (use - 1f) * w; ys[i] = bottom - v.pow(1.15f) * span; pxs[i] = xs[i]; pys[i] = bottom - st.peaks[i].pow(1.15f) * span
        }
        d.drawPath(smoothPath(pxs, pys, use), Color(0x47FFFFFF), style = Stroke(1.2f * dp))
        val fill = smoothPath(xs, ys, use).apply { lineTo(w, bottom); lineTo(0f, bottom); close() }
        d.drawPath(fill, Brush.verticalGradient(listOf(accent.copy(alpha = .85f), Color(0x05FFFFFF)), startY = top, endY = bottom))
        d.drawPath(smoothPath(xs, ys, use), accent, style = Stroke(2f * dp))
    }

    // bars: LED columns
    fun leds() {
        val n = max(18, min(48, (w / (13 * dp)).toInt())); val lo = 35f; val hi = min(nyq, 18000f)
        if (st.heights.size != n) { st.heights = FloatArray(n); st.holds = FloatArray(n) }
        val seg = 7 * dp; val gap = 2.5f * dp; val base = h - 16 * dp
        val rows = max(6, ((h - 16 * dp) / (seg + gap)).toInt()); val slot = w / n; val bw = max(3 * dp, slot * 0.7f)
        for (i in 0 until n) {
            val f0 = lo * (hi / lo).pow(i / n.toFloat()); val f1 = lo * (hi / lo).pow((i + 1) / n.toFloat())
            val b0 = (f0 / nyq * a.bins).toInt(); val b1 = max(b0 + 1, kotlin.math.ceil(f1 / nyq * a.bins).toInt())
            var m = 0; for (k in b0 until min(b1, a.bins)) m = max(m, a.freq[k])
            val v = min(1f, (m / 255f).pow(1.5f) * (1 + 0.7f * i / n))
            st.heights[i] = max(v, st.heights[i] - 0.035f); st.holds[i] = max(st.heights[i], st.holds[i] - 0.008f)
            val lit = (st.heights[i] * rows).toInt(); val peak = min(rows - 1, (st.holds[i] * rows).toInt()); val x = i * slot + (slot - bw) / 2
            for (r in 0 until rows) {
                val y = base - (r + 1) * (seg + gap) + gap
                val c = when { r < lit -> accent.copy(alpha = 0.45f + 0.55f * r / rows); r == peak && peak > 0 -> Color.White.copy(alpha = .9f); else -> Color.White.copy(alpha = .06f) }
                d.drawRoundRect(c, Offset(x, y), Size(bw, seg), CornerRadius(2 * dp))
            }
        }
        text("BASS", 0f, h - 3 * dp); text("MID", w / 2, h - 3 * dp, align = Paint.Align.CENTER); text("TREBLE", w, h - 3 * dp, align = Paint.Align.RIGHT)
    }

    // spectrogram waterfall: newest sound at the right edge, scrolling left
    fun wall() {
        val x0 = 44 * dp; val x1 = w - 2 * dp; val y0 = 4 * dp; val y1 = h - 18 * dp
        val pw = (x1 - x0).toInt().coerceAtLeast(2); val ph = (y1 - y0).toInt().coerceAtLeast(2); val speed = 60 * dp
        if (st.pw != pw || st.ph != ph) { st.pw = pw; st.ph = ph; st.pix = IntArray(pw * ph) { 0xFF04000A.toInt() }; st.bmp = Bitmap.createBitmap(pw, ph, Bitmap.Config.ARGB_8888); st.lastNs = ns; st.acc = 0f }
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
                val c = LUT[((m / 255f).pow(1.25f) * 255).toInt().coerceIn(0, 255)]
                for (x in pw - s until pw) st.pix[row + x] = c
            }
        }
        val bmp = st.bmp ?: return
        bmp.setPixels(st.pix, 0, pw, 0, 0, pw, ph)
        d.drawImage(bmp.asImageBitmap(), Offset(x0, y0))
        var k = 0f
        while (k <= maxHz) {
            val y = y1 - k / maxHz * ph; val key = k == 16000f || k == 20000f
            text(if (k == 0f) "0" else "${(k / 1000).toInt()} kHz", x0 - 6 * dp, min(y1 - 1 * dp, max(y0 + 10 * dp, y + 4 * dp)), color = if (key) ink else dim, align = Paint.Align.RIGHT)
            if (k > 0) d.drawLine(Color.White.copy(alpha = if (key) .5f else .12f), Offset(x0, y), Offset(x1, y), 1f, pathEffect = if (key) PathEffect.dashPathEffect(floatArrayOf(5 * dp, 4 * dp)) else null)
            k += stepHz
        }
        text("${(pw / speed).toInt()} s ago", x0, h - 3 * dp); text("now", x1, h - 3 * dp, align = Paint.Align.RIGHT)
    }

    private fun X(db: Float) = ((db + 60f) / 60f).coerceIn(0f, 1f)

    // loudness: the level now, a peak marker, and the last ten seconds
    fun loud() {
        val (rdb, pdb) = a.levels()
        st.avg = if (st.avg.isNaN()) rdb else st.avg + (rdb - st.avg) * 0.25f
        st.peakHold = max(pdb, st.peakHold - 0.4f)
        if (playing && ns / 1_000_000 - st.lastHist > 33) {
            st.lastHist = ns / 1_000_000
            if (st.histN < st.hist.size) st.hist[st.histN++] = rdb else { System.arraycopy(st.hist, 1, st.hist, 0, st.hist.size - 1); st.hist[st.hist.size - 1] = rdb }
        }
        val avgTxt = if (st.avg <= -59) "−∞" else "%.1f".format(st.avg)
        text(avgTxt, 0f, 30 * dp, ink, 30f, bold = true)
        paint.textSize = 30 * dp; val tw = paint.measureText(avgTxt)
        text("dB average now", tw + 8 * dp, 30 * dp, dim, 13f)
        text("peak ${if (st.peakHold <= -89) "−∞" else "%.1f".format(st.peakHold)} dB", w, 30 * dp, dim, 13f, Paint.Align.RIGHT)
        val my = 44 * dp; val mh = 12 * dp
        d.drawRoundRect(Color(0x14FFFFFF), Offset(0f, my), Size(w, mh), CornerRadius(mh / 2))
        d.drawRoundRect(Brush.horizontalGradient(listOf(accent, Color(0xFFFFB35C), Color(0xFFFF5C5C)), startX = 0f, endX = w), Offset(0f, my), Size(w * X(st.avg), mh), CornerRadius(mh / 2))
        d.drawRect(Color.White, Offset(min(w - 2 * dp, w * X(st.peakHold)), my - 2 * dp), Size(2 * dp, mh + 4 * dp))
        val hy0 = my + mh + 16 * dp; val hy1 = h - 18 * dp; val hh = hy1 - hy0
        for (db in intArrayOf(-12, -24, -36, -48)) {
            val y = hy1 - X(db.toFloat()) * hh
            d.drawLine(Color(0x17FFFFFF), Offset(0f, y), Offset(w, y), 1f); text("$db", 2f, y - 3 * dp)
        }
        if (st.histN > 1) {
            val n = st.histN; val xs = FloatArray(n) { w - (n - 1 - it) / 299f * w }; val ys = FloatArray(n) { hy1 - X(st.hist[it]) * hh }
            val fill = Path().apply { moveTo(xs[0], hy1); for (i in 0 until n) lineTo(xs[i], ys[i]); lineTo(xs[n - 1], hy1); close() }
            d.drawPath(fill, Brush.verticalGradient(listOf(accent.copy(alpha = .8f), Color(0x05FFFFFF)), startY = hy0, endY = hy1))
            val line = Path().apply { moveTo(xs[0], ys[0]); for (i in 1 until n) lineTo(xs[i], ys[i]) }
            d.drawPath(line, accent, style = Stroke(2 * dp))
        }
        text("10 s ago", 0f, h - 3 * dp); text("now", w, h - 3 * dp, align = Paint.Align.RIGHT)
    }

    // stereo: a phase scope beside a level meter for each side
    fun stereo() {
        val n = a.fftSize; var sl = 0f; var sr = 0f; var slr = 0f; var pl = 0f; var pr = 0f
        for (i in 0 until n) { val l = a.left[i]; val r = a.right[i]; sl += l * l; sr += r * r; slr += l * r; pl = max(pl, kotlin.math.abs(l)); pr = max(pr, kotlin.math.abs(r)) }
        val rl = a.db(sqrt(sl / n)); val rr = a.db(sqrt(sr / n))
        for (i in 0..1) { val v = if (i == 0) rl else rr; st.smoothLR[i] = if (st.smoothLR[i].isNaN()) v else st.smoothLR[i] + (v - st.smoothLR[i]) * 0.25f; st.holdLR[i] = max(a.db(if (i == 0) pl else pr), st.holdLR[i] - 0.4f) }
        val s = min(h - 6 * dp, w * 0.52f); val cx = s / 2; val cy = h / 2; val rad = s / 2 - 6 * dp
        // scope dots, the last few frames fading out
        if (playing) {
            val pts = FloatArray((n / 4) * 2); var k = 0
            for (i in 0 until n step 4) { val l = a.left[i]; val r = a.right[i]; pts[k++] = cx + (r - l) * 0.7071f * rad * 1.2f; pts[k++] = cy - (l + r) * 0.7071f * rad * 1.2f }
            st.trail.add(0, pts); while (st.trail.size > 4) st.trail.removeAt(st.trail.size - 1)
        } else if (st.trail.isNotEmpty()) st.trail.removeAt(st.trail.size - 1)
        val grid = Color(0x1FFFFFFF)
        d.drawCircle(grid, rad, Offset(cx, cy), style = Stroke(1f))
        d.drawLine(grid, Offset(cx, cy - rad), Offset(cx, cy + rad), 1f); d.drawLine(grid, Offset(cx - rad, cy), Offset(cx + rad, cy), 1f)
        d.drawLine(grid, Offset(cx - rad * .7071f, cy - rad * .7071f), Offset(cx + rad * .7071f, cy + rad * .7071f), 1f); d.drawLine(grid, Offset(cx + rad * .7071f, cy - rad * .7071f), Offset(cx - rad * .7071f, cy + rad * .7071f), 1f)
        val alphas = floatArrayOf(.75f, .4f, .22f, .1f)
        st.trail.forEachIndexed { ti, pts -> var i = 0; while (i < pts.size) { d.drawRect(accent.copy(alpha = alphas[ti]), Offset(pts[i] - .75f * dp, pts[i + 1] - .75f * dp), Size(1.5f * dp, 1.5f * dp)); i += 2 } }
        text("L", cx - rad * .7071f - 8 * dp, cy - rad * .7071f - 2 * dp, align = Paint.Align.CENTER); text("R", cx + rad * .7071f + 8 * dp, cy - rad * .7071f - 2 * dp, align = Paint.Align.CENTER)
        // the two meters
        val mx = s + 18 * dp; val mw = max(14 * dp, min(34 * dp, (w - mx - 70 * dp) / 2)); val mt = 6 * dp; val mb = h - 22 * dp; val mhh = mb - mt
        for (i in 0..1) {
            val x = mx + i * (mw + 12 * dp); val col = if (i == 0) accent else Color(0xFF7CC4FF)
            d.drawRoundRect(Color(0x14FFFFFF), Offset(x, mt), Size(mw, mhh), CornerRadius(5 * dp))
            val fh = X(st.smoothLR[i]) * mhh
            d.drawRoundRect(col.copy(alpha = .9f), Offset(x, mb - fh), Size(mw, fh), CornerRadius(5 * dp))
            d.drawRect(Color.White, Offset(x, mb - X(st.holdLR[i]) * mhh - 1.5f * dp), Size(mw, 2 * dp))
            text(if (i == 0) "L" else "R", x + mw / 2, h - 6 * dp, ink, align = Paint.Align.CENTER)
        }
        val tx = mx + 2 * (mw + 12 * dp) + 4 * dp
        for (db in intArrayOf(0, -12, -24, -36, -48)) text("$db dB", tx, mb - X(db.toFloat()) * mhh + 4 * dp)
    }
}
