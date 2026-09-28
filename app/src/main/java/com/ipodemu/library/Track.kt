package com.ipodemu.library

/** Where a track in the merged library actually lives -- Local (this device's own Scanner),
 * Ipod (a Sync-mode device's library), Jellyfin/Plex (direct to those servers' own REST APIs),
 * Nas (an SMB/CIFS network share, home network only), or Cloud (a track this app downloaded
 * itself and injected straight into the library the instant it landed, served back over HTTP by
 * the file-mover service -- WAN-capable like Jellyfin/Plex, but visible in seconds rather than
 * waiting on a library scan). */
enum class TrackSource { LOCAL, IPOD, JELLYFIN, PLEX, NAS, CLOUD }

data class Track(
    val path: String,
    val title: String,
    val artist: String,
    val album: String,
    val albumArtist: String,
    val genre: String,
    val trackNo: Int,
    val discNo: Int,
    val durationMs: Long,
    val year: Int,
    val isMusic: Boolean,
    val artKey: String?,
    val mtime: Long,
    val size: Long,
    val source: TrackSource = TrackSource.LOCAL,
) {
    val albumKey: String get() = "$albumArtist|$album"
}

/** iPod-style sort key: case-insensitive, ignores leading "The ", "A ", "An ". */
fun sortKey(s: String): String {
    val l = s.trim().lowercase()
    for (p in arrayOf("the ", "an ", "a ")) if (l.startsWith(p) && l.length > p.length) return l.substring(p.length)
    return l
}
