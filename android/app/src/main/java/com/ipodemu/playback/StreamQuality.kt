package com.ipodemu.playback

/**
 * Lower quality on a weak signal (F5), pure and unit-tested. The measured throughput of the streams picks the version of the NEXT songs: under [LOW_BPS] Jellyfin's
 * Opus 128k conversion, above [HIGH_BPS] the original FLAC, in between whatever is in use. It is only looked at between songs (a song never changes version while
 * it plays), and a song kept on the phone is never converted ([PlaySource]). Only for "Streaming quality: Automatic"; Always full and Data saver stay as chosen.
 */
object StreamQuality {
    enum class Pick { FLAC, OPUS }

    const val LOW_BPS = 1_500_000L
    const val HIGH_BPS = 4_000_000L
    /** The bitrate of the small version. */
    const val OPUS_BPS = 128_000

    /** The version to use from the measurement [bps] (bits per second; 0 or less: nothing measured), given the one in use. */
    fun pick(bps: Long, current: Pick): Pick = when {
        bps <= 0 -> current
        bps < LOW_BPS -> Pick.OPUS
        bps > HIGH_BPS -> Pick.FLAC
        else -> current
    }

    /** Whether streamed songs are converted to Opus now: only when Settings says Automatic (0) and the line is weak. */
    fun opusNow(setting: Int, auto: Pick): Boolean = setting == 0 && auto == Pick.OPUS
}
