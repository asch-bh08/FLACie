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
    /** For Jellyfin tracks: the file's path on the server (e.g. "/media/Movies/music/A/B.flac"), used to recognise the
     * very same file when the NAS share it lives on is browsed directly too. Empty otherwise. */
    val filePath: String = "",
) {
    val albumKey: String get() = "$albumArtist|$album"
}

/** iPod-style sort key: case-insensitive, ignores leading "The ", "A ", "An ". */
fun sortKey(s: String): String {
    val l = s.trim().lowercase()
    for (p in arrayOf("the ", "an ", "a ")) if (l.startsWith(p) && l.length > p.length) return l.substring(p.length)
    return l
}

private val featParen = Regex("""\s*[(\[](feat\.?|ft\.?|featuring|with)\s[^)\]]*[)\]]""", RegexOption.IGNORE_CASE)
private val featTail = Regex("""\s+(feat\.?|ft\.?|featuring)\s.*$""", RegexOption.IGNORE_CASE)
private val artistSplit = Regex("""\s*(;|,|&|/|\s+x\s+|\s+(feat\.?|ft\.?|featuring|with|and)\s+)\s*""", RegexOption.IGNORE_CASE)
private val nonAlnum = Regex("""[^\p{L}\p{N}]+""")

/** Title with "(feat. X)" / "ft. X" dropped and punctuation folded, for matching one song across sources. */
fun normTitle(s: String): String = s.replace(featParen, "").replace(featTail, "").lowercase().replace(nonAlnum, " ").trim()
/** Just the lead artist: "Lady Gaga; Colby O'Donis", "Lady Gaga Feat. Colby O'Donis" and "Lady Gaga" all -> "lady gaga". */
fun primaryArtist(s: String): String = s.split(artistSplit).firstOrNull { it.isNotBlank() }.orEmpty().lowercase().replace(nonAlnum, " ").trim()
/** Same song regardless of which source spelled the credits how. */
fun matchKey(title: String, artist: String): String = "${normTitle(title)}|${primaryArtist(artist)}"
val Track.matchKey: String get() = matchKey(title, artist)

/** Identity of the underlying file for sources that can share one (Jellyfin serves the same NAS share the app also
 * browses over SMB): the last three path segments, decoded and lower-cased. Null when not applicable. */
val Track.fileKey: String?
    get() {
        val p = when (source) {
            TrackSource.JELLYFIN -> filePath
            TrackSource.NAS -> path
            else -> return null
        }
        if (p.isBlank()) return null
        val decoded = try { java.net.URLDecoder.decode(p.replace("+", "%2B"), "UTF-8") } catch (_: Exception) { p }
        val segs = decoded.replace('\\', '/').split('/').filter { it.isNotEmpty() }
        return if (segs.size < 2) null else segs.takeLast(3).joinToString("/").lowercase()
    }

/**
 * Repairs tag text that was decoded with the wrong charset: UTF-8 read as Latin-1 ("BeyoncÃ©") or Chinese GBK read as
 * Latin-1 ("±£ÂÞ Âó¿¨ÌØÄá" = 保罗 麦卡特尼). Only when the bytes decode cleanly (strict) into the other charset, and
 * for GBK only when the result is Chinese, so ordinary accented names are left alone.
 */
fun fixMojibake(s: String): String {
    if (s.isEmpty() || s.any { it.code > 0xFF }) return s
    val high = s.count { it.code >= 0x80 }
    if (high == 0) return s
    val bytes = ByteArray(s.length) { s[it].code.toByte() }
    fun strict(cs: String): String? = try {
        java.nio.charset.Charset.forName(cs).newDecoder()
            .onMalformedInput(java.nio.charset.CodingErrorAction.REPORT).onUnmappableCharacter(java.nio.charset.CodingErrorAction.REPORT)
            .decode(java.nio.ByteBuffer.wrap(bytes)).toString()
    } catch (_: Exception) { null }
    strict("UTF-8")?.let { if (it != s) return it }
    val letters = s.count { !it.isWhitespace() }
    if (high * 10 >= letters * 4) strict("GBK")?.let { g -> if (g.any { it in '一'..'鿿' }) return g }
    return s
}
