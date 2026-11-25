package com.ipodemu.playback

import com.ipodemu.library.AudioFacts
import com.ipodemu.library.Track
import com.ipodemu.library.TrackSource

/**
 * The line under a song in the car (owner, 25 Nov: "Android Auto is terrible: no labels for FLAC or Jellyfin or anything other than play, pause,
 * shuffle, DJ"): its artist, what the file is (Hi-Res 24/96, FLAC, MP3 320) and where it plays from (Offline when it is on the phone, else Jellyfin, Plex,
 * NAS...). The format is what the server or Jellyfin read from the file ([AudioFacts]); without that, the file name's ending. Pure, unit-tested.
 */
object CarLabel {
    /** "Hi-Res 24/96", "FLAC", "MP3 320". */
    fun quality(codec: String, rate: Int, depth: Int, kbps: Int, lossless: Boolean, hiRes: Boolean): String {
        val name = codecName(codec)
        return when {
            hiRes -> "Hi-Res ${if (depth > 0) depth else 24}/${khz(rate)}"
            lossless -> name
            kbps > 0 -> "$name $kbps"
            else -> name
        }
    }

    private fun khz(rate: Int): String = if (rate % 1000 == 0) (rate / 1000).toString() else String.format(java.util.Locale.ROOT, "%.1f", rate / 1000.0)

    private fun codecName(codec: String): String {
        val c = codec.lowercase()
        return when {
            c.startsWith("pcm") -> "WAV"
            c == "vorbis" -> "OGG"
            c == "wmalossless" || c.startsWith("wma") -> "WMA"
            c.startsWith("dsd") -> "DSD"
            else -> c.uppercase()
        }
    }

    private val known = setOf("flac", "mp3", "ogg", "opus", "wav", "aiff", "aif", "m4a", "alac", "aac", "wma", "ape", "wv", "dsf")

    /** The file name's ending, when the path is a file ("Song.flac" -> "FLAC"); null for a server id. */
    fun fromFile(path: String): String? = path.substringAfterLast('/').substringAfterLast('.', "").lowercase().takeIf { it in known }?.uppercase()

    fun source(s: TrackSource, downloaded: Boolean): String = if (downloaded) "Offline" else when (s) {
        TrackSource.JELLYFIN -> "Jellyfin"
        TrackSource.PLEX -> "Plex"
        TrackSource.NAS -> "NAS"
        TrackSource.LOCAL -> "On phone"
        TrackSource.CLOUD -> "FLACie"
        TrackSource.IPOD -> "iPod"
    }

    fun line(artist: String, quality: String?, source: String?): String = listOf(artist, quality, source).filter { !it.isNullOrBlank() }.joinToString(" · ")

    /** [t]'s format from what the server or Jellyfin read, else its file name. */
    fun qualityOf(t: Track): String? = AudioFacts.of(t)?.let { f -> quality(f.codec, f.rate, f.depth, f.kbps, f.lossless, f.hiRes) }?.takeIf { it.isNotBlank() }
        ?: fromFile(t.filePath.ifEmpty { t.path })
}
