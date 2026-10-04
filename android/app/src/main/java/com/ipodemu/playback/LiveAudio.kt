package com.ipodemu.playback

import androidx.media3.common.C
import androidx.media3.common.util.UnstableApi
import androidx.media3.exoplayer.audio.TeeAudioProcessor
import java.nio.ByteBuffer
import java.nio.ByteOrder
import kotlin.math.PI
import kotlin.math.cos
import kotlin.math.log10
import kotlin.math.max
import kotlin.math.min
import kotlin.math.sin
import kotlin.math.sqrt

/**
 * The sound as it goes to the speaker, for the Info tab's live graphs. ExoPlayer hands every buffer of decoded audio to this sink (it does not
 * change the sound); the last few thousand frames of left and right are kept so the screen can read them at its own pace.
 */
@UnstableApi
class LiveAudio : TeeAudioProcessor.AudioBufferSink {
    private val size = 8192
    private val left = FloatArray(size)
    private val right = FloatArray(size)
    private var head = 0          // next frame to write
    private var filled = 0
    private val lock = Any()
    @Volatile var sampleRate = 44100; private set
    @Volatile var channels = 2; private set
    private var encoding = C.ENCODING_PCM_16BIT

    override fun flush(sampleRateHz: Int, channelCount: Int, encoding: Int) {
        this.sampleRate = sampleRateHz; channels = channelCount; this.encoding = encoding
        synchronized(lock) { head = 0; filled = 0 }
    }

    override fun handleBuffer(buffer: ByteBuffer) {
        val b = buffer.asReadOnlyBuffer().order(ByteOrder.LITTLE_ENDIAN)
        val ch = max(1, channels)
        val bytes = when (encoding) { C.ENCODING_PCM_16BIT -> 2; C.ENCODING_PCM_24BIT -> 3; C.ENCODING_PCM_32BIT, C.ENCODING_PCM_FLOAT -> 4; C.ENCODING_PCM_8BIT -> 1; else -> return }
        val frames = b.remaining() / (bytes * ch)
        if (frames <= 0) return
        val start = b.position()
        synchronized(lock) {
            // only the newest frames can matter
            val skip = max(0, frames - size)
            for (f in skip until frames) {
                val base = start + f * bytes * ch
                val l = sample(b, base, bytes)
                val r = if (ch > 1) sample(b, base + bytes, bytes) else l
                left[head] = l; right[head] = r
                head = (head + 1) % size
            }
            filled = min(size, filled + frames - skip)
        }
    }

    private fun sample(b: ByteBuffer, at: Int, bytes: Int): Float = when (encoding) {
        C.ENCODING_PCM_16BIT -> b.getShort(at) / 32768f
        C.ENCODING_PCM_24BIT -> (((b.get(at + 2).toInt() shl 16) or ((b.get(at + 1).toInt() and 0xFF) shl 8) or (b.get(at).toInt() and 0xFF)) / 8388608f)
        C.ENCODING_PCM_32BIT -> b.getInt(at) / 2147483648f
        C.ENCODING_PCM_FLOAT -> b.getFloat(at)
        else -> ((b.get(at).toInt() and 0xFF) - 128) / 128f
    }

    /** Copies the newest [n] frames (oldest first) into [outL] and [outR]; false when nothing has played yet. */
    fun latest(outL: FloatArray, outR: FloatArray, n: Int): Boolean = synchronized(lock) {
        if (filled < n) return false
        var i = (head - n + size) % size
        for (k in 0 until n) { outL[k] = left[i]; outR[k] = right[i]; i = (i + 1) % size }
        true
    }
}

/**
 * The measurements the graphs draw, read from [LiveAudio] once per screen frame: pitch levels (like a browser's analyser: Blackman window, smoothed,
 * -100 to -30 dB spread over 0..255), the raw left and right waves, and levels in dB.
 */
@UnstableApi
class LiveAnalysis(private val live: LiveAudio, private val gain: () -> Float = { 1f }) {
    val fftSize = 2048
    val bins = fftSize / 2
    val freq = IntArray(bins)                  // 0..255 per pitch slice
    val left = FloatArray(fftSize)
    val right = FloatArray(fftSize)
    var active = false; private set            // true while real audio is arriving
    val sampleRate get() = live.sampleRate

    private val smooth = FloatArray(bins)
    private val window = FloatArray(fftSize) { i -> val a = 0.16f; (0.5f * (1 - a) - 0.5f * cos(2 * PI * i / fftSize).toFloat() + 0.5f * a * cos(4 * PI * i / fftSize).toFloat()) }
    private val re = FloatArray(fftSize)
    private val im = FloatArray(fftSize)
    private val rev = IntArray(fftSize).also { r -> val levels = Integer.numberOfTrailingZeros(fftSize); for (i in 0 until fftSize) { var x = 0; for (b in 0 until levels) x = x or (((i shr b) and 1) shl (levels - 1 - b)); r[i] = x } }
    private val cosT = FloatArray(fftSize / 2) { cos(2 * PI * it / fftSize).toFloat() }
    private val sinT = FloatArray(fftSize / 2) { sin(2 * PI * it / fftSize).toFloat() }

    fun update() {
        active = live.latest(left, right, fftSize)
        // the graphs show what comes out of the speaker: the player's volume and the phone's volume are applied to the tapped sound
        val g = gain().coerceIn(0f, 1f)
        if (g < 0.999f) for (i in 0 until fftSize) { left[i] *= g; right[i] *= g }
        if (!active) { for (i in 0 until bins) { smooth[i] *= 0.7f; freq[i] = toByte(smooth[i]) }; return }
        for (i in 0 until fftSize) { re[i] = (left[i] + right[i]) * 0.5f * window[i]; im[i] = 0f }
        fft()
        for (k in 0 until bins) {
            val mag = sqrt(re[k] * re[k] + im[k] * im[k]) / fftSize
            smooth[k] = 0.7f * smooth[k] + 0.3f * mag
            freq[k] = toByte(smooth[k])
        }
    }

    private fun toByte(mag: Float): Int {
        val db = 20f * log10(max(mag, 1e-9f))
        return ((db + 100f) / 70f * 255f).toInt().coerceIn(0, 255)
    }

    private fun fft() {
        val n = fftSize
        for (i in 0 until n) { val j = rev[i]; if (j > i) { var t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t } }
        var size = 2
        while (size <= n) {
            val half = size shr 1; val step = n / size
            var i = 0
            while (i < n) {
                var j = i; var k = 0
                while (j < i + half) {
                    val l = j + half
                    val tr = re[l] * cosT[k] + im[l] * sinT[k]; val ti = im[l] * cosT[k] - re[l] * sinT[k]
                    re[l] = re[j] - tr; im[l] = im[j] - ti; re[j] += tr; im[j] += ti
                    j++; k += step
                }
                i += size
            }
            size = size shl 1
        }
    }

    /** Average level (dB, 0 = full scale) and loudest peak of the mixed-down wave, from the latest frames. */
    fun levels(): Pair<Float, Float> {
        var sq = 0f; var pk = 0f
        for (i in 0 until fftSize) { val m = (left[i] + right[i]) * 0.5f; sq += m * m; val a = kotlin.math.abs(m); if (a > pk) pk = a }
        return db(sqrt(sq / fftSize)) to db(pk)
    }

    fun db(x: Float) = 20f * log10(max(x, 1e-6f))
}
