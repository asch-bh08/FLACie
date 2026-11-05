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

    /** Fewer bytes than this downloaded and the meter is still guessing (its start value is an average for the country, which can read as a weak line). */
    const val MIN_MEASURED_BYTES = 2L * 1024 * 1024

    /** The version to use from the measurement [bps] (bits per second; 0 or less: nothing measured), given the one in use. Not before [MIN_MEASURED_BYTES] were [measuredBytes]. */
    fun pick(bps: Long, current: Pick, measuredBytes: Long = Long.MAX_VALUE, heldAtBytes: Long = 0): Pick = when {
        bps <= 0 || measuredBytes - heldAtBytes < MIN_MEASURED_BYTES -> current
        bps < LOW_BPS -> Pick.OPUS
        bps > HIGH_BPS -> Pick.FLAC
        else -> current
    }

    /** Whether [uri] is the Opus conversion ([com.ipodemu.library.JellyfinPaths.opusUrl]). */
    fun isOpusUri(uri: String) = uri.contains("/universal") && uri.contains("AudioCodec=opus")

    /** A song on the Opus copy that failed to load or play is tried once more on the original stream; the copy is only a saving, never a reason to go silent. */
    fun retryOnFlac(uri: String, alreadyRetried: Boolean) = isOpusUri(uri) && !alreadyRetried

    /** Whether streamed songs are converted to Opus now: only when Settings says Automatic (0) and the line is weak. */
    fun opusNow(setting: Int, auto: Pick): Boolean = setting == 0 && auto == Pick.OPUS
}
