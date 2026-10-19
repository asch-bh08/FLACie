package com.ipodemu.player

import java.io.File
import java.security.MessageDigest
import kotlin.math.abs
import kotlin.math.max
import kotlin.math.min
import kotlin.math.roundToInt
import kotlin.math.sqrt

/*
 * The pure parts of the Living Sleeve player (LivingSleeve.kt): nothing here touches Android, so each rule has a plain JVM test.
 * They are the same rules as the web player's (flacie.js npAccent, npWave, npWaveMake), so a song looks the same in both.
 */

/**
 * The two colours the whole screen takes from the cover: [main] (lifted to about 66% lightness, so dark text on it is easy to read: the play
 * button, the played part of the seek bar, the glow) and [second] (the next most common hue at about 32%, the darker field in the corner).
 * Both are opaque ARGB ints. [fromCover] is false for the warm default shown when a cover has nothing usable in it.
 */
data class SleeveColours(
    val main: Int, val second: Int, val fromCover: Boolean,
    // design I: the ground (the dark colour the cover fades into), the tone (tinted buttons and chips) and the text on the tone, all from the second colour
    val ground: Int = argb(22, 58, 63), val tone: Int = argb(36, 82, 90), val toneFg: Int = argb(231, 243, 240),
) {
    companion object {
        val DEFAULT = SleeveColours(argb(232, 119, 79), argb(47, 93, 98), false)

        private fun argb(r: Int, g: Int, b: Int) = (0xFF shl 24) or (r shl 16) or (g shl 8) or b

        /**
         * Reads the colours from the cover's pixels (ARGB ints; the caller shrinks the picture to about 24 by 24 first, so this costs nothing).
         * Pixels are sorted into twelve hue bins of 30 degrees, each pixel counting by its saturation cubed (a vivid patch beats a big dull area);
         * near-black and near-white pixels are ignored. The strongest bin gives [main], the next one [second] (the same bin when the cover has one hue).
         */
        fun extract(pixels: IntArray): SleeveColours {
            val w = DoubleArray(12); val hue = DoubleArray(12); val sat = DoubleArray(12); val n = IntArray(12)
            for (p in pixels) {
                val r = ((p shr 16) and 255) / 255.0; val g = ((p shr 8) and 255) / 255.0; val b = (p and 255) / 255.0
                val mx = max(r, max(g, b)); val mn = min(r, min(g, b)); val l = (mx + mn) / 2; val d = mx - mn
                val s = if (d == 0.0) 0.0 else d / (1 - abs(2 * l - 1))
                val h = if (d == 0.0) 0.0 else 60 * when (mx) { r -> ((g - b) / d + 6) % 6; g -> (b - r) / d + 2; else -> (r - g) / d + 4 }
                if (l < .12 || l > .92) continue
                val k = (Math.round(h / 30).toInt()) % 12
                w[k] += .04 + s * s * s * 4
                // the hue is kept as its distance from the bin's centre, so reds either side of 0 degrees average to red (not to cyan)
                var off = h - k * 30.0; if (off > 180) off -= 360 else if (off < -180) off += 360
                hue[k] += off; sat[k] += s; n[k]++
            }
            val top = (0 until 12).filter { n[it] > 0 }.sortedByDescending { w[it] }   // a stable sort: equal bins keep hue order
            if (top.isEmpty()) return DEFAULT
            fun col(k: Int, l: Double, smax: Double = .85): Int {
                val h = ((k * 30.0 + hue[k] / n[k]) % 360 + 360) % 360
                val s = min(smax, max(.35, sat[k] / n[k]))
                val a = s * min(l, 1 - l)
                fun f(m: Int): Int { val kk = (m + h / 30) % 12; return (255 * (l - a * max(-1.0, min(min(kk - 3, 9 - kk), 1.0)))).roundToInt() }
                return argb(f(0), f(8), f(4))
            }
            val two = top.getOrElse(1) { top[0] }
            return SleeveColours(col(top[0], .66), col(two, .32), true, col(two, .16, .5), col(two, .26, .45), col(two, .92, .4))
        }
    }
}

/** The words around the title, the same rules as the web player's (NowPlaying.razor Lead, Last and WhereLine). */
object SleeveText {
    /** The title split for the serif heading: everything up to and including the last space, and the last word, which is set in italics. */
    fun split(title: String): Pair<String, String> {
        val s = title.trimEnd()
        val i = s.lastIndexOf(' ')
        return if (i < 0) "" to s else s.substring(0, i + 1) to s.substring(i + 1)
    }

    /** The pill at the top: "ALBUM · TRACK n", or [fallback] (where it plays from) when the song has no album or no number. */
    fun where(album: String, trackNo: Int, fallback: String): String =
        if (album.isNotBlank() && trackNo > 0) "${album.uppercase()} · TRACK $trackNo" else fallback.uppercase()

    /** A title longer than this gets the smaller heading (40 sp instead of 52), as on the web. */
    const val LONG_TITLE = 26
}

/**
 * The ring seek bar: the song's loudness as [BARS] bars round the play button, starting at 12 o'clock and going clockwise.
 * The same geometry as the web's (flacie.js drawRing and ringFrac).
 */
object RingSeek {
    const val BARS = 80

    /**
     * Where a touch at ([x], [y]) inside a [w] by [h] box falls on the ring, as a fraction of the song (0 at 12 o'clock, 0.25 at 3 o'clock),
     * or null when it is on the play button in the middle (inside 55% of the radius) or outside the ring, so a tap on play never seeks.
     */
    fun fraction(x: Float, y: Float, w: Float, h: Float): Float? {
        val dx = x - w / 2f; val dy = y - h / 2f
        val d = sqrt(dx * dx + dy * dy) / (min(w, h) / 2f)
        if (d < .55f || d > 1.05f) return null
        return angleFraction(dx, dy)
    }

    /** The fraction for a point relative to the centre, whatever its distance: used while a drag that started on the ring goes on. */
    fun angleFraction(dx: Float, dy: Float): Float {
        val a = (Math.atan2(dx.toDouble(), (-dy).toDouble()) / (2 * Math.PI)).toFloat()
        return ((a + 1f) % 1f).coerceIn(0f, 1f)
    }

    /** Whether bar [i] of [n] is in the played part (its middle is at or before [played]). */
    fun isPlayed(i: Int, n: Int, played: Float): Boolean = (i + .5f) / n <= played
}

/**
 * Turns decoded audio into the song's loudness shape: [slices] numbers from 0 to 255 along its length (the RMS of each slice, which follows the ear
 * better than peaks, scaled so the loudest slice is 255). Feed it the decoder's blocks in any order with their start times; only the first channel counts.
 */
class WaveSlicer(private val slices: Int, private val durationUs: Long) {
    private val sum = DoubleArray(slices)
    private val count = IntArray(slices)

    /** [samples] holds [count] interleaved 16-bit samples of [channels] channels, the first of which starts at [ptsUs]. */
    fun add(ptsUs: Long, samples: ShortArray, count: Int, channels: Int) {
        val slice = (ptsUs.coerceAtLeast(0L) * slices / durationUs).coerceIn(0L, (slices - 1).toLong()).toInt()
        val step = max(1, channels)
        var s = 0.0; var c = 0
        var i = 0
        while (i < count) { val v = samples[i].toDouble(); s += v * v; c++; i += step }
        sum[slice] += s; this.count[slice] += c
    }

    /** The shape so far: silence everywhere (and no division by zero) when nothing, or only silence, was decoded. */
    fun result(): IntArray {
        val rms = DoubleArray(slices) { if (count[it] == 0) 0.0 else sqrt(sum[it] / count[it]) }
        val peak = rms.max()
        return IntArray(slices) { if (peak <= 0.0) 0 else (255 * rms[it] / peak).roundToInt() }
    }

    companion object {
        /** Slices in a song's shape: the same 200 as the web. */
        const val SLICES = 200
        /** A slicer, or null when the song's length is not known (there is nothing to divide it by). */
        fun orNull(slices: Int, durationUs: Long): WaveSlicer? = if (durationUs > 0) WaveSlicer(slices, durationUs) else null
    }
}

/** Shapes kept on disk, one small file per song (200 bytes), so a song is only ever decoded once. */
object WaveCache {
    /** The file name for a song: the SHA-1 of its path in hex (a path can hold characters a file name cannot, and can be very long). */
    fun key(path: String): String = MessageDigest.getInstance("SHA-1").digest(path.toByteArray()).joinToString("") { "%02x".format(it) }

    private fun file(dir: File, path: String) = File(dir, key(path) + ".wave")

    /** The saved shape, or null when there is none or the file is not exactly one shape long (a half-written file is never trusted). */
    fun read(dir: File, path: String): IntArray? = try {
        val f = file(dir, path)
        if (!f.isFile || f.length() != WaveSlicer.SLICES.toLong()) null else f.readBytes().let { b -> IntArray(b.size) { b[it].toInt() and 255 } }
    } catch (_: Exception) { null }

    fun write(dir: File, path: String, shape: IntArray) {
        try {
            dir.mkdirs()
            val tmp = File(dir, key(path) + ".tmp")
            tmp.writeBytes(ByteArray(shape.size) { shape[it].coerceIn(0, 255).toByte() })
            if (!tmp.renameTo(file(dir, path))) tmp.delete()
        } catch (_: Exception) { }
    }
}

/** The seek bar's bars: a count that fits the width, and a height for each from the shape. */
object WaveBars {
    /** How many 3 dp bars with 2 dp gaps fit [widthPx] (never fewer than 8). */
    fun count(widthPx: Float, barPx: Float, gapPx: Float): Int = max(8, ((widthPx + gapPx) / (barPx + gapPx)).toInt())

    /**
     * Each bar's height as a fraction of the bar area, from 0.1 (quiet) to 1 (loudest): the loudest slice under it, so a short loud hit is never lost.
     * Until the shape is known ([shape] null) every bar is level at 0.4.
     */
    fun heights(shape: IntArray?, n: Int): FloatArray {
        if (shape == null || shape.isEmpty()) return FloatArray(n) { 0.4f }
        return FloatArray(n) { i ->
            val a = (i.toLong() * shape.size / n).toInt().coerceAtMost(shape.size - 1)
            val b = max(a + 1, ((i + 1).toLong() * shape.size / n).toInt()).coerceAtMost(shape.size)
            var m = 0; for (k in a until b) m = max(m, shape[k])
            0.1f + 0.9f * m / 255f
        }
    }
}

object SleeveMotion {
    /** The background fields drift only while a song plays, with the app's animations on and the phone's animation scale above zero. */
    fun drifts(playing: Boolean, motionPref: Boolean, animatorScale: Float): Boolean = playing && motionPref && animatorScale > 0f
}
