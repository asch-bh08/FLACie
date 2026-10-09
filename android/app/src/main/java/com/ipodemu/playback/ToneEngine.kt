package com.ipodemu.playback

import android.media.AudioAttributes
import android.media.AudioFormat
import android.media.AudioTrack
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import kotlin.math.PI
import kotlin.math.abs
import kotlin.math.cos
import kotlin.math.exp
import kotlin.math.ln
import kotlin.math.max
import kotlin.math.min
import kotlin.math.pow
import kotlin.math.sin

/**
 * Test tones for checking headphones: left / right, a frequency slider, sweeps, bass and treble steps, a kick drum, clicks, sibilance, close
 * tones, noise, a phase check and a 3D check. Everything is made here as raw audio (nothing is played from files) through one master level
 * that starts low and is capped, with a short fade on every start and stop: test tones are easy to make far too loud on an amplifier.
 * Same tests as the Headphone test page of FLACie Web.
 */
class ToneEngine {
    enum class Chan { L, R, LR }
    enum class Wave { SINE, TRIANGLE, SQUARE, SAW }

    sealed interface Mode {
        data class Tone(val hz: Double, val wave: Wave, val chan: Chan) : Mode
        data class Sweep(val from: Double, val to: Double, val secs: Double, val chan: Chan) : Mode
        data class Steps(val list: List<Double>, val chan: Chan) : Mode
        data class Noise(val pink: Boolean, val chan: Chan) : Mode
        data object Alternate : Mode
        data object Kick : Mode
        data object Clicks : Mode
        data object Sibilance : Mode
        data object HiHat : Mode
        data class Close(val diffHz: Double) : Mode
        data class Phase(val inPhase: Boolean) : Mode
        /** path: circle, lr (across the front), fb (front to back), ud (low to high) */
        data class Spin(val path: String, val secs: Double) : Mode
    }

    /** What is playing, shown on the screen. Updated from the audio thread about ten times a second. */
    var status by mutableStateOf("Ready"); private set
    /** The frequency being played right now (Hz), or 0. */
    var nowHz by mutableStateOf(0.0); private set
    /** Where a 3D test puts the sound, -1..1 across and -1..1 front (+) to back (-); null when not a 3D test. */
    var spot by mutableStateOf<Pair<Float, Float>?>(null); private set
    val playing: Boolean get() = thread != null

    @Volatile var level = 0.12f
    private fun gain() = (level * level * 0.35f)

    @Volatile private var thread: Thread? = null
    @Volatile private var wantStop = false

    fun start(mode: Mode) {
        stop(); wantStop = false
        val t = Thread { run(mode) }.also { it.isDaemon = true; it.name = "tone-engine" }
        thread = t; t.start()
    }

    fun stop() {
        val t = thread ?: return
        wantStop = true
        try { t.join(400) } catch (_: InterruptedException) { }
        thread = null; status = "Stopped"; nowHz = 0.0; spot = null
    }

    /** Update the tone of a running Tone mode without restarting it (the slider). */
    @Volatile private var liveHz = 0.0
    fun retune(hz: Double) { liveHz = hz }

    // ---- the audio thread ----

    private fun run(mode: Mode) {
        val sr = 48000
        val frames = 1024
        val buf = ShortArray(frames * 2)
        val track = AudioTrack.Builder()
            .setAudioAttributes(AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_MEDIA).setContentType(AudioAttributes.CONTENT_TYPE_MUSIC).build())
            .setAudioFormat(AudioFormat.Builder().setEncoding(AudioFormat.ENCODING_PCM_16BIT).setSampleRate(sr).setChannelMask(AudioFormat.CHANNEL_OUT_STEREO).build())
            .setBufferSizeInBytes(max(AudioTrack.getMinBufferSize(sr, AudioFormat.CHANNEL_OUT_STEREO, AudioFormat.ENCODING_PCM_16BIT), frames * 4 * 2))
            .setTransferMode(AudioTrack.MODE_STREAM).build()
        val g = Gen(sr, mode)
        liveHz = (mode as? Mode.Tone)?.hz ?: 0.0
        track.play()
        var fade = 0f; var out = 1f; var lastUi = 0L
        try {
            while (true) {
                if (wantStop) out = 0f
                val masterG = gain()
                for (i in 0 until frames) {
                    // 50 ms fade in on start, 60 ms fade out on stop
                    fade = if (out > 0f) min(1f, fade + 1f / (0.05f * sr)) else max(0f, fade - 1f / (0.06f * sr))
                    g.next(this)
                    val k = masterG * fade
                    buf[2 * i] = (g.l * k * 32767f).coerceIn(-32767f, 32767f).toInt().toShort()
                    buf[2 * i + 1] = (g.r * k * 32767f).coerceIn(-32767f, 32767f).toInt().toShort()
                }
                track.write(buf, 0, buf.size)
                val now = System.currentTimeMillis()
                if (now - lastUi > 100) { lastUi = now; status = g.status; nowHz = g.hz; spot = g.spot }
                if (wantStop && fade <= 0f) break
                if (g.finished) { wantStop = true }
            }
        } finally {
            try { track.stop() } catch (_: Exception) { }
            track.release()
            if (thread === Thread.currentThread()) { thread = null; status = if (g.finished) "Finished" else "Stopped"; nowHz = 0.0; spot = null }
        }
    }

    /** Makes the samples for one mode, one frame at a time. */
    private class Gen(val sr: Int, val mode: Mode) {
        var l = 0f; var r = 0f
        var status = "Playing"; var hz = 0.0; var spot: Pair<Float, Float>? = null; var finished = false
        private var n = 0L
        private val t get() = n.toDouble() / sr
        private var phase = 0.0; private var phase2 = 0.0
        private val rnd = java.util.Random(7)
        // pink noise (Paul Kellet)
        private var b0 = 0f; private var b1 = 0f; private var b2 = 0f; private var b3 = 0f; private var b4 = 0f; private var b5 = 0f; private var b6 = 0f
        private fun white() = rnd.nextFloat() * 2f - 1f
        private fun pink(): Float {
            val w = white()
            b0 = 0.99886f * b0 + w * 0.0555179f; b1 = 0.99332f * b1 + w * 0.0750759f; b2 = 0.969f * b2 + w * 0.153852f
            b3 = 0.8665f * b3 + w * 0.3104856f; b4 = 0.55f * b4 + w * 0.5329522f; b5 = -0.7616f * b5 - w * 0.016898f
            val p = (b0 + b1 + b2 + b3 + b4 + b5 + b6 + w * 0.5362f) * 0.11f; b6 = w * 0.115926f; return p
        }
        private fun wave(w: Wave, ph: Double): Float {
            val x = ph - kotlin.math.floor(ph)
            return when (w) {
                Wave.SINE -> sin(2 * PI * x).toFloat()
                Wave.TRIANGLE -> (4 * abs(x - 0.5) - 1).toFloat()
                Wave.SQUARE -> if (x < 0.5) 0.6f else -0.6f
                Wave.SAW -> ((2 * x - 1) * 0.6).toFloat()
            }
        }
        private fun place(v: Float, c: ToneEngine.Chan) { when (c) { Chan.L -> { l = v; r = 0f }; Chan.R -> { l = 0f; r = v }; Chan.LR -> { l = v; r = v } } }
        // a short burst envelope: fast up, exponential down
        private fun env(sinceStart: Double, dur: Double) = if (sinceStart < 0 || sinceStart > dur * 3) 0f else (min(1.0, sinceStart / 0.004) * exp(-sinceStart / (dur / 4.0))).toFloat()
        // simple filters
        private var lp1 = 0f; private var hp1 = 0f; private var hpPrev = 0f; private var bpLo = 0f; private var bpBand = 0f
        // far-ear delay line for the 3D test
        private val delay = FloatArray(128); private var dpos = 0; private var farLp = 0f; private var rearLp = 0f

        fun next(e: ToneEngine) {
            when (mode) {
                is Mode.Tone -> {
                    val f = if (e.liveHz > 0) e.liveHz else mode.hz
                    hz = f; status = "${fmtHz(f)} ${mode.wave.name.lowercase()}"
                    phase += f / sr; place(wave(mode.wave, phase), mode.chan)
                }
                is Mode.Sweep -> {
                    val p = min(1.0, t / mode.secs); val f = mode.from * (mode.to / mode.from).pow(p)
                    hz = f; status = "Sweep ${fmtHz(f)}"; phase += f / sr; place(sin(2 * PI * phase).toFloat(), mode.chan)
                    if (p >= 1.0) finished = true
                }
                is Mode.Steps -> {
                    val idx = (t / 2.2).toInt(); if (idx >= mode.list.size) { finished = true; l = 0f; r = 0f; return }
                    val f = mode.list[idx]; val tt = t - idx * 2.2
                    val a = (min(1.0, tt / 0.05) * min(1.0, max(0.0, (1.95 - tt) / 0.25))).toFloat().coerceAtLeast(0f)
                    hz = f; status = "${fmtHz(f)}  (${idx + 1} of ${mode.list.size}): do you hear it, and is it steady?"
                    phase += f / sr; place(sin(2 * PI * phase).toFloat() * a, mode.chan)
                }
                is Mode.Noise -> { status = if (mode.pink) "Pink noise" else "White noise"; place(if (mode.pink) pink() else white() * 0.6f, mode.chan) }
                Mode.Alternate -> {
                    val left = ((t / 1.2).toInt() % 2) == 0; val tt = t % 1.2; val a = min(1.0, min(tt / 0.04, (1.2 - tt) / 0.04)).toFloat()
                    hz = 1000.0; status = if (left) "Left ear" else "Right ear"; phase += 1000.0 / sr; val v = sin(2 * PI * phase).toFloat() * a
                    if (left) { l = v; r = 0f } else { l = 0f; r = v }
                }
                Mode.Kick -> {
                    val tt = t % 0.9; val f = 42.0 + (160.0 - 42.0) * exp(-tt / 0.06); phase2 += f / sr
                    val a = (min(1.0, tt / 0.004) * exp(-tt / 0.14)).toFloat(); hz = f; status = "Kick drum: tight and deep, then silent, not boomy"
                    val v = sin(2 * PI * phase2).toFloat() * a * 0.9f; l = v; r = v
                }
                Mode.Clicks -> {
                    val tt = t % 0.45; val k = (tt * sr).toInt(); val v = when (k) { 0 -> 1f; 1 -> -0.6f; else -> 0f } * 1.2f
                    status = "Clicks: each one crisp, with no ringing"; l = v; r = v
                }
                Mode.Sibilance -> {
                    // white noise through a band around 7 kHz
                    val w = white(); val f = 2 * sin(PI * 7000.0 / sr).toFloat(); val q = 1f / 1.2f
                    val high = w - bpLo - q * bpBand; bpBand += f * high; bpLo += f * bpBand
                    val a = env(t % 1.0, 0.35); hz = 7000.0; status = "“sss” sibilance: bright and clear, not harsh"; val v = bpBand * a * 1.2f; l = v; r = v
                }
                Mode.HiHat -> {
                    val w = white(); hp1 = 0.2f * (hp1 + w - hpPrev); hpPrev = w   // a rough high-pass above ~8 kHz
                    val step = (t / 0.25).toInt(); val tt = t % 0.25
                    val a = env(tt, if (step % 4 == 3) 0.22 else 0.07) * (if (step % 2 == 1) 0.45f else 0.8f); hz = 10000.0; status = "Hi-hat: a clean tick and a longer open hat"
                    val v = (w - lp1.also { lp1 += 0.45f * (w - lp1) }) * a * 1.2f; l = v; r = v
                }
                is Mode.Close -> {
                    hz = 1000.0; status = "1000 Hz + ${(1000 + mode.diffHz).toInt()} Hz: a slow wobble, ${mode.diffHz.toInt()} beats a second"
                    phase += 1000.0 / sr; phase2 += (1000.0 + mode.diffHz) / sr; val v = (sin(2 * PI * phase) + sin(2 * PI * phase2)).toFloat() * 0.5f; l = v; r = v
                }
                is Mode.Phase -> {
                    val v = pink(); l = v; r = if (mode.inPhase) v else -v
                    status = if (mode.inPhase) "In phase: the noise sits in the middle of your head" else "Out of phase: wide and hollow, outside the middle"
                }
                is Mode.Spin -> {
                    val q = (t % mode.secs) / mode.secs; var az = 0.0; var el = 0.0
                    when (mode.path) {
                        "circle" -> { az = q * 2 * PI; status = "Around your head, clockwise from the front" }
                        "lr" -> { val k = if (q < 0.5) q * 2 else 2 - q * 2; az = (-80 + 160 * k) * PI / 180; status = "Across the front, left to right and back" }
                        "fb" -> { val k = if (q < 0.5) q * 2 else 2 - q * 2; az = k * PI; status = "Front to back along the middle" }
                        else -> { val k = if (q < 0.5) q * 2 else 2 - q * 2; el = (-60 + 120 * k) * PI / 180; status = "Low to high in front of you" }
                    }
                    spot = Pair((sin(az) * cos(el)).toFloat(), (cos(az) * cos(el)).toFloat())
                    // noise pulses, then the two ears get a time and level difference (and the far ear and the back lose treble)
                    val tt = t % 0.35; val src = pink() * env(tt, 0.28) * 1.6f
                    val side = sin(az) * cos(el)                        // -1 left .. +1 right
                    val itd = (abs(side) * 0.00066 * sr).toFloat()      // up to ~0.66 ms later in the far ear
                    delay[dpos] = src; val di = itd.toInt().coerceIn(0, 120); val fr = itd - di
                    val a0 = delay[(dpos - di + 128) % 128]; val a1 = delay[(dpos - di - 1 + 128) % 128]; val late = a0 * (1 - fr) + a1 * fr; dpos = (dpos + 1) % 128
                    farLp += (0.15f + 0.55f * (1f - abs(side).toFloat())) * (late - farLp)
                    val rear = (-cos(az)).coerceAtLeast(0.0).toFloat(); rearLp += (0.9f - 0.7f * rear) * (src - rearLp)
                    val near = if (rear > 0f) rearLp else src
                    val farG = (1f - 0.55f * abs(side).toFloat()); val upDown = 1f
                    if (side >= 0) { r = near * (0.8f + 0.2f * abs(side).toFloat()) * upDown; l = farLp * farG * 0.9f } else { l = near * (0.8f + 0.2f * abs(side).toFloat()) * upDown; r = farLp * farG * 0.9f }
                }
            }
            n++
        }

        private fun fmtHz(f: Double) = if (f >= 1000) "%.2f kHz".format(f / 1000).replace(Regex("\\.?0+ kHz"), " kHz") else if (f < 100) "%.1f Hz".format(f).replace(".0 Hz", " Hz") else "${f.toInt()} Hz"
    }

    companion object {
        /** Slider position 0..1 to Hz (log scale, 20 Hz to 20 kHz) and back. */
        fun hzOf(p: Float): Double = 20.0 * 1000.0.pow(p.toDouble())
        fun posOf(hz: Double): Float = (ln(hz / 20.0) / ln(1000.0)).toFloat().coerceIn(0f, 1f)
    }
}
