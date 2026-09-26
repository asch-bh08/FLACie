package com.ipodemu.library

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
) {
    val albumKey: String get() = "$albumArtist|$album"
}

/** iPod-style sort key: case-insensitive, ignores leading "The ", "A ", "An ". */
fun sortKey(s: String): String {
    val l = s.trim().lowercase()
    for (p in arrayOf("the ", "an ", "a ")) if (l.startsWith(p) && l.length > p.length) return l.substring(p.length)
    return l
}
