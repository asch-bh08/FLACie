package com.ipodemu.playback

import androidx.media3.common.C
import androidx.media3.common.Format
import com.ipodemu.library.Track

/** Technical details of the file that is playing, as label/value rows for the info card. */
class FileSpecs(val rows: List<Pair<String, String>>) {
    companion object {
        fun of(t: Track, f: Format?): FileSpecs {
            val ext = t.path.substringAfterLast('.', "").uppercase().ifEmpty { "?" }
            val rate = f?.sampleRate?.takeIf { it > 0 }?.let { String.format("%.1f kHz", it / 1000f) } ?: "-"
            val depth = when (f?.pcmEncoding) {
                C.ENCODING_PCM_8BIT -> "8-bit"
                C.ENCODING_PCM_16BIT -> "16-bit"
                C.ENCODING_PCM_24BIT -> "24-bit"
                C.ENCODING_PCM_32BIT -> "32-bit"
                C.ENCODING_PCM_FLOAT -> "32-bit float"
                else -> "-"
            }
            val kbps = if (t.durationMs > 0) (t.size * 8 / t.durationMs).toInt() else 0
            val ch = when (f?.channelCount) { null, Format.NO_VALUE -> "-"; 1 -> "Mono"; 2 -> "Stereo"; else -> "${f.channelCount} ch" }
            return FileSpecs(listOf(
                "Format" to ext,
                "Sample Rate" to rate,
                "Bit Depth" to depth,
                "Bitrate" to if (kbps > 0) "$kbps kbps" else "-",
                "Channels" to ch,
                "Size" to String.format("%.1f MB", t.size / 1_048_576.0),
            ))
        }
    }
}
