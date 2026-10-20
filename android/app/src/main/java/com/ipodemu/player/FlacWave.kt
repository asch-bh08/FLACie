package com.ipodemu.player

import kotlin.math.min
import kotlin.math.roundToInt

/**
 * The seek bar's shape (200 bars, 0-255) from a FLAC file's frame sizes, without decoding: a port of the server's WaveShape.cs (FLACie.Core).
 * Every FLAC frame holds a fixed number of samples and a loud, busy passage packs into bigger frames than a quiet one, so the frames' bits per sample
 * over time are the song's shape. The phone streams straight from Jellyfin, so it cannot wait for the server's /api/wave; this works on the bytes the
 * player has already downloaded to play the song, and is cheap enough to run again each time more of them arrive.
 */
object FlacWave {
    const val BARS = 200
    /** A bar the bytes so far do not reach yet: drawn at the resting level. */
    const val UNKNOWN = -1
    /** The longest frame header: sync (2), codes (2), the UTF-8 number (up to 7), block size (2), sample rate (2), CRC-8 (1). */
    internal const val MAX_HEADER = 16

    /** The shape of a whole FLAC file, or null when it is not one (or holds no frames). The same numbers as WaveShape.FromFlac on the server. */
    fun fromFlac(f: ByteArray, length: Int = f.size): IntArray? {
        if (length < 42 || f[0] != 'f'.code.toByte() || f[1] != 'L'.code.toByte() || f[2] != 'a'.code.toByte() || f[3] != 'C'.code.toByte()) return null
        // the metadata blocks: each has a 4-byte header (last-block flag, type, 24-bit length); STREAMINFO gives the block size when it is fixed
        var at = 4; var fixedBlock = 0
        while (at + 4 <= length) {
            val last = (u(f, at) and 0x80) != 0; val type = u(f, at) and 0x7f; val len = (u(f, at + 1) shl 16) or (u(f, at + 2) shl 8) or u(f, at + 3)
            if (type == 0 && at + 4 + 4 <= length) { val mn = (u(f, at + 4) shl 8) or u(f, at + 5); val mx = (u(f, at + 6) shl 8) or u(f, at + 7); if (mn == mx) fixedBlock = mn }
            at += 4 + len
            if (last) break
        }
        // the frames: a sync code and a header whose CRC-8 checks out (so a sync pattern inside the audio is not taken for a frame)
        val starts = ArrayList<Long>(); val samples = ArrayList<Int>()
        var i = at
        while (i + 6 < length) {
            if (u(f, i) == 0xFF && (u(f, i + 1) and 0xFE) == 0xF8) header(f, i, length, fixedBlock)?.let { starts.add(i.toLong()); samples.add(it) }
            i++
        }
        return shape(starts, samples, length.toLong())
    }

    /** Lays the frames' bits per sample out over time and normalises them between the song's own 2nd and 98th percentiles. */
    internal fun shape(starts: List<Long>, samples: List<Int>, end: Long): IntArray? {
        if (starts.size < 2) return null
        var total = 0L; for (n in samples) total += n
        if (total <= 0) return null
        val sum = DoubleArray(BARS); val weight = DoubleArray(BARS)
        var t = 0L
        for (k in starts.indices) {
            val size = (if (k + 1 < starts.size) starts[k + 1] else end) - starts[k]
            val bar = min((BARS - 1).toLong(), t * BARS / total).toInt()
            sum[bar] += size * 8.0; weight[bar] += samples[k].toDouble()
            t += samples[k]
        }
        val rate = DoubleArray(BARS)
        for (b in 0 until BARS) rate[b] = if (weight[b] > 0) sum[b] / weight[b] else if (b > 0) rate[b - 1] else 0.0
        return normalise(rate)
    }

    /** 0-255 between the 2nd and 98th percentiles of [rate] (a quiet stretch still costs a few bits a sample, so the floor comes from the song itself). */
    private fun normalise(rate: DoubleArray): IntArray {
        val n = rate.size
        val sorted = rate.sorted()
        val lo = sorted[(n * .02).toInt()]; val hi = sorted[(n * .98).toInt()]
        if (hi - lo < 1e-9) return IntArray(n) { 128 }
        return IntArray(n) { (255 * ((rate[it] - lo) / (hi - lo)).coerceIn(0.0, 1.0)).roundToInt() }
    }

    /**
     * The number of samples in the frame whose header starts at [i], or null when the header is not a valid one. [length] is where the bytes end:
     * a header cut off by it is not a frame (on the server that only happens at the end of the file).
     */
    internal fun header(f: ByteArray, i: Int, length: Int, fixedBlock: Int): Int? {
        var p = i + 2
        val bsCode = u(f, p) shr 4; val srCode = u(f, p) and 0x0F; p++
        if (srCode == 0x0F || bsCode == 0) return null
        val chan = u(f, p) shr 4; if (chan > 10 || (u(f, p) and 1) != 0) return null; p++
        // the frame or sample number, UTF-8 coded (1 to 7 bytes)
        val lead = u(f, p)
        val extra = when { lead < 0x80 -> 0; lead >= 0xFE -> 6; lead >= 0xFC -> 5; lead >= 0xF8 -> 4; lead >= 0xF0 -> 3; lead >= 0xE0 -> 2; lead >= 0xC0 -> 1; else -> -1 }
        if (extra < 0 || p + 1 + extra >= length) return null
        for (k in 1..extra) if ((u(f, p + k) and 0xC0) != 0x80) return null
        p += 1 + extra
        val n: Int
        when {
            bsCode == 1 -> n = 192
            bsCode <= 5 -> n = 576 shl (bsCode - 2)
            bsCode == 6 -> { if (p >= length) return null; n = u(f, p) + 1; p++ }
            bsCode == 7 -> { if (p + 1 >= length) return null; n = ((u(f, p) shl 8) or u(f, p + 1)) + 1; p += 2 }
            else -> n = 256 shl (bsCode - 8)
        }
        if (srCode == 12) p++ else if (srCode == 13 || srCode == 14) p += 2
        if (p >= length) return null
        // CRC-8 (polynomial 0x07) over the header, which must match the byte after it
        var crc = 0
        for (k in i until p) { crc = crc xor u(f, k); repeat(8) { crc = if ((crc and 0x80) != 0) ((crc shl 1) xor 0x07) and 0xFF else (crc shl 1) and 0xFF } }
        if (crc != u(f, p)) return null
        // a fixed-block-size stream never has a frame larger than its block size (only the last one may be shorter)
        if (fixedBlock > 0 && n > fixedBlock) return null
        return n
    }

    private fun u(f: ByteArray, i: Int) = f[i].toInt() and 0xFF

    /**
     * The same method fed piece by piece, as the player's download arrives: it keeps only the frame list and a few bytes of overlap, never the file.
     * [partial] is the shape of what has arrived so far, placed by the length STREAMINFO gives (the bars past it are [UNKNOWN]); [finish] gives exactly
     * what [fromFlac] gives for the whole file.
     */
    class Builder {
        private var buf = ByteArray(0)
        private var len = 0
        /** The file offset of buf[0]. */
        private var base = 0L
        /** The next file offset to look at, in the current phase. */
        private var pos = 0L
        private var phase = MAGIC
        private var fixedBlock = 0
        /** Samples in the whole song from STREAMINFO (0 when it does not say). */
        var totalSamples = 0L; private set
        private val starts = ArrayList<Long>(); private val samples = ArrayList<Int>()
        /** Bytes taken in so far. */
        var received = 0L; private set
        val notFlac get() = phase == NOT_FLAC

        fun feed(b: ByteArray, off: Int = 0, n: Int = b.size) {
            if (phase == NOT_FLAC || n <= 0) return
            if (len + n > buf.size) buf = buf.copyOf(maxOf(len + n, buf.size * 2))
            System.arraycopy(b, off, buf, len, n); len += n; received += n
            run(final = false)
            // keep only the bytes from the next place to look at on
            val drop = (pos - base).coerceIn(0L, len.toLong()).toInt()
            if (drop > 0) { System.arraycopy(buf, drop, buf, 0, len - drop); len -= drop; base += drop }
        }

        private fun run(final: Boolean) {
            val end = base + len
            while (true) {
                when (phase) {
                    MAGIC -> {
                        if (end < 4) return
                        if (buf[0] != 'f'.code.toByte() || buf[1] != 'L'.code.toByte() || buf[2] != 'a'.code.toByte() || buf[3] != 'C'.code.toByte()) { phase = NOT_FLAC; return }
                        pos = 4; phase = META
                    }
                    META -> {
                        // a block header and, for STREAMINFO, its first 18 bytes (block sizes and the total length); a block past the bytes so far waits
                        if (pos + 4 > end) return
                        val i = (pos - base).toInt()
                        val last = (u(buf, i) and 0x80) != 0; val type = u(buf, i) and 0x7f
                        val blen = (u(buf, i + 1) shl 16) or (u(buf, i + 2) shl 8) or u(buf, i + 3)
                        if (type == 0) {
                            if (pos + 4 + 18 > end && !final) return
                            if (pos + 8 <= end) { val mn = (u(buf, i + 4) shl 8) or u(buf, i + 5); val mx = (u(buf, i + 6) shl 8) or u(buf, i + 7); if (mn == mx) fixedBlock = mn }
                            if (pos + 4 + 18 <= end) {
                                var ts = (u(buf, i + 4 + 13) and 0x0F).toLong()
                                for (k in 14..17) ts = (ts shl 8) or u(buf, i + 4 + k).toLong()
                                totalSamples = ts
                            }
                        }
                        pos += 4 + blen
                        if (last) phase = FRAMES
                    }
                    FRAMES -> {
                        // a header is only judged once all of it can be seen, so a piece boundary never hides or invents a frame
                        while (if (final) pos + 6 < end else pos + MAX_HEADER <= end) {
                            val i = (pos - base).toInt()
                            if (u(buf, i) == 0xFF && (u(buf, i + 1) and 0xFE) == 0xF8) header(buf, i, len, fixedBlock)?.let { starts.add(pos); samples.add(it) }
                            pos++
                        }
                        return
                    }
                    else -> return
                }
            }
        }

        /** The shape of everything fed so far, for a song whose download has not finished: [UNKNOWN] past the last whole frame. Null until a few bars are known. */
        fun partial(): IntArray? {
            if (phase != FRAMES || totalSamples <= 0 || starts.size < 3) return null
            val sum = DoubleArray(BARS); val weight = DoubleArray(BARS)
            var t = 0L; var lastBar = -1
            // the last frame found may not have arrived whole, so it is left out
            for (k in 0 until starts.size - 1) {
                val bar = min((BARS - 1).toLong(), t * BARS / totalSamples).toInt()
                sum[bar] += (starts[k + 1] - starts[k]) * 8.0; weight[bar] += samples[k].toDouble()
                t += samples[k]; lastBar = bar
            }
            // the bar the download stops in is only partly known: it waits for the rest
            val known = if (t >= totalSamples) BARS else lastBar
            if (known < 4) return null
            val rate = DoubleArray(known)
            for (b in 0 until known) rate[b] = if (weight[b] > 0) sum[b] / weight[b] else if (b > 0) rate[b - 1] else 0.0
            val norm = normalise(rate)
            return IntArray(BARS) { if (it < known) norm[it] else UNKNOWN }
        }

        /** The whole file has been fed ([fileLength] bytes): the exact shape, as [fromFlac] gives it. */
        fun finish(fileLength: Long = base + len): IntArray? {
            if (fileLength < 42 || phase == NOT_FLAC) return null
            run(final = true)
            if (phase != FRAMES) return null
            return shape(starts, samples, fileLength)
        }

        private companion object { const val MAGIC = 0; const val META = 1; const val FRAMES = 2; const val NOT_FLAC = -1 }
    }
}
