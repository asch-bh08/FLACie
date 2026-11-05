package com.ipodemu.library

/**
 * A Jellyfin song's address is the server plus "/Audio/<id>/stream...": the id stays the same when the server's address changes, the rest doesn't. A playlist
 * saved under an older address (owner, 2 Nov: Wild Ones kept "http://100.114.148.48:8096/..." after the server moved to its ts.net name, and never played
 * in the car) is matched by the id, or moved onto the server in use now. Pure, unit-tested.
 */
object JellyfinPaths {
    private val ID = Regex("/Audio/([0-9a-fA-F]{32})/", RegexOption.IGNORE_CASE)

    /** The song's Jellyfin id (lower case), or null for an address that is not a Jellyfin stream. */
    fun idOf(path: String): String? = if (path.startsWith("http")) ID.find(path)?.groupValues?.get(1)?.lowercase() else null

    /** Jellyfin's Opus 128k conversion of the song at [path] (a plain stream, not HLS), or null for an address that is not a Jellyfin stream or when [userId] is blank. */
    fun opusUrl(path: String, userId: String, deviceId: String, bitrate: Int = 128_000): String? {
        val id = idOf(path) ?: return null
        if (userId.isBlank()) return null
        val base = path.substring(0, path.indexOf("/Audio/", ignoreCase = true))
        return "$base/Audio/$id/universal?UserId=$userId&DeviceId=$deviceId&MaxStreamingBitrate=$bitrate&AudioBitRate=$bitrate" +
            "&Container=opus&TranscodingContainer=opus&TranscodingProtocol=http&AudioCodec=opus&MaxAudioChannels=2&StartTimeTicks=0"
    }

    /** [path] on the server at [base] ("https://host[:port]"), the part from "/Audio/" on kept as it is; unchanged when it is not a Jellyfin stream or [base] is blank. */
    fun rebase(path: String, base: String): String {
        val b = base.trim().trimEnd('/')
        if (b.isEmpty() || idOf(path) == null) return path
        val at = path.indexOf("/Audio/", ignoreCase = true)
        return if (at < 0) path else b + path.substring(at)
    }
}
