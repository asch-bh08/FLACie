package com.ipodemu.library.tags

import com.ipodemu.library.fixMojibake

/**
 * Where tag bytes come from. Reads are ranges: over SMB each call is a network round trip, so the parsers below ask for
 * small headers and jump with offsets instead of reading whole files.
 */
interface RandomAccess {
    /** Total size of the file in bytes. */
    val size: Long
    /** Up to [length] bytes at [offset]; fewer (or none) when the range runs past the end. */
    fun read(offset: Long, length: Int): ByteArray
}

class ArrayAccess(private val data: ByteArray) : RandomAccess {
    override val size: Long get() = data.size.toLong()
    override fun read(offset: Long, length: Int): ByteArray {
        if (offset < 0 || offset >= data.size || length <= 0) return ByteArray(0)
        val end = minOf(data.size.toLong(), offset + length).toInt()
        return data.copyOfRange(offset.toInt(), end)
    }
}

/** Everything a tag reader can learn about one audio file. Numbers are 0 and text is "" when the file does not say. */
class AudioTags {
    var codec = ""
    var title = ""
    var artist = ""
    var albumArtist = ""
    var album = ""
    var genre = ""
    var trackNo = 0
    var discNo = 0
    var year = 0
    var durationMs = 0L
    var sampleRate = 0
    var bitDepth = 0
    var bitrateKbps = 0
    var channels = 0
    /** Embedded cover image (JPEG/PNG...), when the file has one and it was small enough to read. */
    var art: ByteArray? = null
    /** False when a cover is not needed (the album folder has one): embedded pictures are then not read. */
    internal var wantArt = true

    /** True when something useful was found (so an empty result is not mistaken for a read file). */
    val any: Boolean
        get() = title.isNotEmpty() || artist.isNotEmpty() || album.isNotEmpty() || durationMs > 0 || art != null || albumArtist.isNotEmpty() || trackNo > 0 || year > 0 || genre.isNotEmpty()

    override fun toString() = "AudioTags($codec '$title' / '$artist' / '$album' #$trackNo d$discNo $year ${durationMs}ms ${sampleRate}Hz ${bitDepth}bit ${bitrateKbps}kbps art=${art?.size})"
}

/** Caches 64 KB blocks of a slow [RandomAccess] (the first read fetches the head in one go), so tiny header reads are cheap. */
class BufferedAccess(private val inner: RandomAccess) : RandomAccess {
    override val size: Long = inner.size
    private val blocks = object : LinkedHashMap<Long, ByteArray>(16, 0.75f, true) {
        override fun removeEldestEntry(eldest: MutableMap.MutableEntry<Long, ByteArray>?) = this.size > 48
    }

    override fun read(offset: Long, length: Int): ByteArray {
        if (offset < 0 || length <= 0 || offset >= size) return ByteArray(0)
        val len = minOf(length.toLong(), size - offset).toInt()
        if (len >= BLOCK * 2) return inner.read(offset, len)
        val out = ByteArray(len)
        var pos = 0
        while (pos < len) {
            val abs = offset + pos
            val bi = abs / BLOCK
            val blk = block(bi)
            val inBlk = (abs - bi * BLOCK).toInt()
            if (inBlk >= blk.size) break
            val n = minOf(len - pos, blk.size - inBlk)
            System.arraycopy(blk, inBlk, out, pos, n)
            pos += n
        }
        return if (pos == len) out else out.copyOf(pos)
    }

    private fun block(bi: Long): ByteArray {
        blocks[bi]?.let { return it }
        // the very first block pulls 256 KB at once: the ID3/FLAC/Ogg headers and a normal cover live there
        val count = if (bi == 0L) 4 else 1
        val got = inner.read(bi * BLOCK, BLOCK * count)
        var i = 0
        do {
            val from = i * BLOCK
            val part = if (from >= got.size) ByteArray(0) else got.copyOfRange(from, minOf(got.size, from + BLOCK))
            blocks[bi + i] = part
            i++
        } while (i < count && i * BLOCK < got.size)
        return blocks[bi] ?: ByteArray(0)
    }

    companion object { const val BLOCK = 64 * 1024 }
}

/**
 * Reads tags and stream facts from MP3 (ID3v2.2/2.3/2.4, ID3v1), FLAC, MP4/M4A/ALAC, Ogg Vorbis/Opus, WAV and AIFF.
 * Pure Kotlin on a [RandomAccess]; it never throws: a damaged or unknown file gives whatever was readable, or null.
 */
object TagReader {
    /** Largest embedded cover that is read (a bigger one is skipped, the text tags still count). */
    const val ART_CAP = 8 * 1024 * 1024

    fun read(src: RandomAccess, fileName: String = "", wantArt: Boolean = true): AudioTags? {
        val t = AudioTags()
        t.wantArt = wantArt
        try {
            val ra = BufferedAccess(src)
            if (ra.size <= 0) return null
            val ext = fileName.substringAfterLast('.', "").lowercase()
            when (detect(ra, ext)) {
                "mp3" -> Mp3.parse(ra, t)
                "flac" -> Flac.parse(ra, t)
                "mp4" -> Mp4.parse(ra, t)
                "ogg" -> Ogg.parse(ra, t)
                "wav" -> Riff.parseWav(ra, t)
                "aiff" -> Riff.parseAiff(ra, t)
                else -> {}
            }
        } catch (_: Throwable) { /* keep what was read */ }
        t.art = t.art?.takeIf { it.size > 16 && looksLikeImage(it) }
        return if (t.any) t else null
    }

    private fun detect(ra: RandomAccess, ext: String): String {
        val h = ra.read(0, 16)
        fun at(i: Int, s: String) = h.size >= i + s.length && s.indices.all { h[i + it].toInt() == s[it].code }
        if (at(0, "ID3")) {
            val skip = Id3.tagLength(ra, 0)
            val n = ra.read(skip, 4)
            if (n.size >= 4 && n[0] == 'f'.code.toByte() && n[1] == 'L'.code.toByte()) return "flac"
            return "mp3"
        }
        if (at(0, "fLaC")) return "flac"
        if (at(0, "OggS")) return "ogg"
        if (at(4, "ftyp")) return "mp4"
        if (at(0, "RIFF") && at(8, "WAVE")) return "wav"
        if (at(0, "FORM") && (at(8, "AIFF") || at(8, "AIFC"))) return "aiff"
        if (h.size >= 2 && (h[0].toInt() and 0xFF) == 0xFF && (h[1].toInt() and 0xE0) == 0xE0) return "mp3"
        return when (ext) {
            "mp3" -> "mp3"
            "flac" -> "flac"
            "m4a", "mp4", "alac", "m4b" -> "mp4"
            "ogg", "oga", "opus" -> "ogg"
            "wav" -> "wav"
            "aif", "aiff", "aifc" -> "aiff"
            else -> ""
        }
    }

    fun looksLikeImage(b: ByteArray): Boolean =
        (b.size > 3 && b[0] == 0xFF.toByte() && b[1] == 0xD8.toByte()) ||
            (b.size > 4 && b[0] == 0x89.toByte() && b[1] == 'P'.code.toByte() && b[2] == 'N'.code.toByte() && b[3] == 'G'.code.toByte()) ||
            (b.size > 12 && b[0] == 'R'.code.toByte() && b[1] == 'I'.code.toByte() && b[8] == 'W'.code.toByte() && b[9] == 'E'.code.toByte()) ||
            (b.size > 4 && b[0] == 'G'.code.toByte() && b[1] == 'I'.code.toByte() && b[2] == 'F'.code.toByte())
}

// ---- byte helpers ---------------------------------------------------------------------------------------------------

internal fun ByteArray.u8(i: Int) = if (i in indices) this[i].toInt() and 0xFF else 0
internal fun ByteArray.be16(i: Int) = (u8(i) shl 8) or u8(i + 1)
internal fun ByteArray.be24(i: Int) = (u8(i) shl 16) or (u8(i + 1) shl 8) or u8(i + 2)
internal fun ByteArray.be32(i: Int): Long = (u8(i).toLong() shl 24) or (u8(i + 1).toLong() shl 16) or (u8(i + 2).toLong() shl 8) or u8(i + 3).toLong()
internal fun ByteArray.be64(i: Int): Long = (be32(i) shl 32) or be32(i + 4)
internal fun ByteArray.le16(i: Int) = u8(i) or (u8(i + 1) shl 8)
internal fun ByteArray.le32(i: Int): Long = u8(i).toLong() or (u8(i + 1).toLong() shl 8) or (u8(i + 2).toLong() shl 16) or (u8(i + 3).toLong() shl 24)
internal fun ByteArray.ascii(i: Int, n: Int): String {
    if (i < 0 || i + n > size) return ""
    return String(this, i, n, Charsets.ISO_8859_1)
}

/** First number in "3/12", "07", "2011-05-05": 0 when there is none. */
internal fun firstInt(s: String): Int {
    val m = Regex("""\d+""").find(s) ?: return 0
    return m.value.take(9).toIntOrNull() ?: 0
}

internal fun yearOf(s: String): Int {
    val m = Regex("""(?<!\d)(\d{4})(?!\d)""").find(s) ?: return 0
    return m.groupValues[1].toInt().takeIf { it in 1000..2999 } ?: 0
}

internal fun joinValues(a: String, b: String) = if (a.isEmpty()) b else if (b.isEmpty() || a == b) a else "$a; $b"

// ---- ID3 --------------------------------------------------------------------------------------------------------------

internal object Id3 {
    private val GENRES = listOf(
        "Blues", "Classic Rock", "Country", "Dance", "Disco", "Funk", "Grunge", "Hip-Hop", "Jazz", "Metal", "New Age", "Oldies",
        "Other", "Pop", "R&B", "Rap", "Reggae", "Rock", "Techno", "Industrial", "Alternative", "Ska", "Death Metal", "Pranks",
        "Soundtrack", "Euro-Techno", "Ambient", "Trip-Hop", "Vocal", "Jazz+Funk", "Fusion", "Trance", "Classical", "Instrumental",
        "Acid", "House", "Game", "Sound Clip", "Gospel", "Noise", "Alternative Rock", "Bass", "Soul", "Punk", "Space", "Meditative",
        "Instrumental Pop", "Instrumental Rock", "Ethnic", "Gothic", "Darkwave", "Techno-Industrial", "Electronic", "Pop-Folk",
        "Eurodance", "Dream", "Southern Rock", "Comedy", "Cult", "Gangsta", "Top 40", "Christian Rap", "Pop/Funk", "Jungle",
        "Native American", "Cabaret", "New Wave", "Psychedelic", "Rave", "Showtunes", "Trailer", "Lo-Fi", "Tribal", "Acid Punk",
        "Acid Jazz", "Polka", "Retro", "Musical", "Rock & Roll", "Hard Rock",
    )

    fun genreName(n: Int) = GENRES.getOrNull(n) ?: ""

    /** Header + body (+ footer) length of the ID3v2 tag at [base]; 0 when there is none. */
    fun tagLength(ra: RandomAccess, base: Long): Long {
        val h = ra.read(base, 10)
        if (h.size < 10 || h.ascii(0, 3) != "ID3") return 0
        val ver = h.u8(3)
        if (ver !in 2..4 || h.u8(4) == 0xFF) return 0
        if ((6..9).any { h.u8(it) >= 0x80 }) return 0
        val size = (h.u8(6) shl 21) or (h.u8(7) shl 14) or (h.u8(8) shl 7) or h.u8(9)
        val footer = ver == 4 && (h.u8(5) and 0x10) != 0
        return 10L + size + if (footer) 10 else 0
    }

    private fun synchsafe(b: ByteArray, i: Int) = (b.u8(i) shl 21) or (b.u8(i + 1) shl 14) or (b.u8(i + 2) shl 7) or b.u8(i + 3)

    private fun deunsync(b: ByteArray): ByteArray {
        val out = java.io.ByteArrayOutputStream(b.size)
        var i = 0
        while (i < b.size) {
            out.write(b[i].toInt())
            if (b[i] == 0xFF.toByte() && i + 1 < b.size && b[i + 1] == 0.toByte()) i++
            i++
        }
        return out.toByteArray()
    }

    private fun frameId(id: String, ver: Int): String = if (ver != 2) id else when (id) {
        "TT2" -> "TIT2"; "TP1" -> "TPE1"; "TP2" -> "TPE2"; "TAL" -> "TALB"; "TRK" -> "TRCK"; "TPA" -> "TPOS"
        "TYE" -> "TYER"; "TCO" -> "TCON"; "TLE" -> "TLEN"; "PIC" -> "APIC"; "TOR" -> "TORY"; "TDA" -> "TDAT"
        else -> id
    }

    /** One text value from a frame body: encoding byte, then text; several values (NUL separated) are joined. */
    private fun text(body: ByteArray, from: Int = 1, enc: Int = body.u8(0)): String {
        if (from >= body.size) return ""
        val parts = ArrayList<String>()
        var i = from
        val wide = enc == 1 || enc == 2
        while (i < body.size) {
            var j = i
            if (wide) { while (j + 1 < body.size && !(body[j] == 0.toByte() && body[j + 1] == 0.toByte())) j += 2; if (j + 1 >= body.size) j = body.size }
            else { while (j < body.size && body[j] != 0.toByte()) j++ }
            if (j > i) parts += decode(body, i, j, enc)
            i = j + if (wide) 2 else 1
        }
        return parts.map { it.trim().trimEnd('\u0000') }.filter { it.isNotEmpty() }.distinct().joinToString("; ")
    }

    private fun decode(b: ByteArray, from: Int, to: Int, enc: Int): String = when (enc) {
        1 -> {
            if (to - from >= 2 && b.u8(from) == 0xFE && b.u8(from + 1) == 0xFF) String(b, from + 2, to - from - 2, Charsets.UTF_16BE)
            else if (to - from >= 2 && b.u8(from) == 0xFF && b.u8(from + 1) == 0xFE) String(b, from + 2, to - from - 2, Charsets.UTF_16LE)
            else String(b, from, to - from, Charsets.UTF_16LE)
        }
        2 -> String(b, from, to - from, Charsets.UTF_16BE)
        3 -> String(b, from, to - from, Charsets.UTF_8)
        else -> fixMojibake(String(b, from, to - from, Charsets.ISO_8859_1))
    }

    private fun genre(raw: String): String {
        val s = raw.trim()
        Regex("""^\((\d+)\)(.*)$""").find(s)?.let { m ->
            val rest = m.groupValues[2].trim()
            return if (rest.isNotEmpty() && !rest.startsWith("(")) rest else genreName(m.groupValues[1].toInt())
        }
        if (s.matches(Regex("""\d{1,3}"""))) return genreName(s.toInt()).ifEmpty { s }
        return s
    }

    /** Reads the v2 tag at [base] into [t] (fields already set are kept). Returns its total length (0 = none). */
    fun readV2(ra: RandomAccess, base: Long, t: AudioTags): Long {
        val total = tagLength(ra, base)
        if (total == 0L) return 0
        val h = ra.read(base, 10)
        val ver = h.u8(3)
        val flags = h.u8(5)
        val bodyLen = total - 10 - if (ver == 4 && (flags and 0x10) != 0) 10 else 0
        var src: RandomAccess = ra
        var pos = base + 10
        var end = pos + bodyLen
        if ((flags and 0x80) != 0 && ver < 4) {
            // whole-tag unsynchronisation: undo it on the tag bytes, then parse from memory
            val raw = ra.read(pos, minOf(bodyLen, TagReader.ART_CAP.toLong() + 65536).toInt())
            val de = deunsync(raw)
            src = ArrayAccess(de); pos = 0; end = de.size.toLong()
        }
        if ((flags and 0x40) != 0) {
            val e = src.read(pos, 6)
            pos += if (ver == 4) synchsafe(e, 0).toLong() else e.be32(0) + 4
        }
        val hdr = if (ver == 2) 6 else 10
        val front = Cover(ver == 2)
        var year = 0; var tdrc = 0
        var guard = 0
        while (pos + hdr <= end && guard++ < 4000) {
            val fh = src.read(pos, hdr)
            if (fh.size < hdr || fh[0] == 0.toByte()) break
            val rawId = fh.ascii(0, if (ver == 2) 3 else 4)
            if (!rawId.all { it in 'A'..'Z' || it in '0'..'9' }) break
            val size = when (ver) { 2 -> fh.be24(3); 3 -> fh.be32(4).toInt(); else -> synchsafe(fh, 4) }
            if (size < 0 || pos + hdr + size > end) break
            val flag1 = if (ver >= 3) fh.u8(8) else 0
            val flag2 = if (ver >= 3) fh.u8(9) else 0
            val bodyPos = pos + hdr
            pos = bodyPos + size
            val compressed = if (ver == 3) (flag1 and 0x80) != 0 else (flag2 and 0x08) != 0
            val encrypted = if (ver == 3) (flag1 and 0x40) != 0 else (flag2 and 0x04) != 0
            if (compressed || encrypted || size == 0) continue
            val unsync = ver == 4 && (flag2 and 0x02) != 0
            val dli = ver == 4 && (flag2 and 0x01) != 0
            val id = frameId(rawId, ver)
            if (id == "APIC") {
                if (!t.wantArt) continue
                if (unsync || dli) {
                    val b = deunsync(src.read(bodyPos, minOf(size, TagReader.ART_CAP + 4096)))
                    front.offer(ArrayAccess(b), 0, b.size, dli)
                } else front.offer(src, bodyPos, size, false)
                continue
            }
            val want = when (id) { "TIT2", "TPE1", "TPE2", "TALB", "TRCK", "TPOS", "TYER", "TDRC", "TDOR", "TCON", "TLEN", "TORY" -> true; else -> false }
            if (!want || size > 65536) continue
            var body = src.read(bodyPos, size)
            if (unsync) body = deunsync(body)
            if (dli && body.size > 4) body = body.copyOfRange(4, body.size)
            val v = text(body)
            if (v.isEmpty()) continue
            when (id) {
                "TIT2" -> if (t.title.isEmpty()) t.title = v
                "TPE1" -> if (t.artist.isEmpty()) t.artist = v
                "TPE2" -> if (t.albumArtist.isEmpty()) t.albumArtist = v
                "TALB" -> if (t.album.isEmpty()) t.album = v
                "TRCK" -> if (t.trackNo == 0) t.trackNo = firstInt(v)
                "TPOS" -> if (t.discNo == 0) t.discNo = firstInt(v)
                "TYER", "TORY" -> if (year == 0) year = yearOf(v)
                "TDRC", "TDOR" -> if (tdrc == 0) tdrc = yearOf(v)
                "TCON" -> if (t.genre.isEmpty()) t.genre = genre(v)
                "TLEN" -> if (t.durationMs == 0L) t.durationMs = v.trim().toLongOrNull() ?: 0L // used only if the audio gives none
            }
        }
        if (t.year == 0) t.year = if (tdrc != 0) tdrc else year
        if (t.art == null) t.art = front.bytes()
        return total
    }

    /** The best picture of a tag: the front cover when there is one, else the first. Only the winner is read. */
    private class Cover(private val v22: Boolean) {
        private var src: RandomAccess? = null
        private var off = 0L
        private var len = 0
        private var type = -1

        fun offer(ra: RandomAccess, bodyPos: Long, size: Int, dli: Boolean) {
            val probe = ra.read(bodyPos, minOf(size, 2048))
            var i = if (dli) 4 else 0
            val enc = probe.u8(i); i++
            // v2.2 (PIC) carries a 3-letter format, v2.3/2.4 (APIC) a NUL-terminated MIME type
            if (v22) { i += 3 } else { while (i < probe.size && probe[i] != 0.toByte()) i++; i++ }
            val picType = probe.u8(i); i++
            // description, terminated per encoding
            if (enc == 1 || enc == 2) { while (i + 1 < probe.size && !(probe[i] == 0.toByte() && probe[i + 1] == 0.toByte())) i += 2; i += 2 }
            else { while (i < probe.size && probe[i] != 0.toByte()) i++; i++ }
            if (i >= probe.size && size > probe.size) return
            val dataLen = size - i
            if (dataLen <= 16 || dataLen > TagReader.ART_CAP) return
            if (type == -1 || (picType == 3 && type != 3)) { src = ra; off = bodyPos + i; len = dataLen; type = picType }
        }

        fun bytes(): ByteArray? = src?.read(off, len)?.takeIf { it.size == len }
    }
}

// ---- MP3 --------------------------------------------------------------------------------------------------------------

internal object Mp3 {
    private val BR_V1_L1 = intArrayOf(0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448)
    private val BR_V1_L2 = intArrayOf(0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384)
    private val BR_V1_L3 = intArrayOf(0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320)
    private val BR_V2_L1 = intArrayOf(0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256)
    private val BR_V2_L23 = intArrayOf(0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160)

    private class Header(val version: Int, val layer: Int, val bitrate: Int, val rate: Int, val channels: Int, val frameLen: Int, val samples: Int, val protection: Boolean)

    private fun header(b: ByteArray, i: Int): Header? {
        if (i < 0 || i + 4 > b.size) return null
        if (b.u8(i) != 0xFF || (b.u8(i + 1) and 0xE0) != 0xE0) return null
        val vb = (b.u8(i + 1) shr 3) and 3
        val lb = (b.u8(i + 1) shr 1) and 3
        if (vb == 1 || lb == 0) return null
        val brIdx = (b.u8(i + 2) shr 4) and 0xF
        val srIdx = (b.u8(i + 2) shr 2) and 3
        if (brIdx == 0 || brIdx == 15 || srIdx == 3) return null
        val layer = 4 - lb
        val v1 = vb == 3
        val br = when {
            v1 && layer == 1 -> BR_V1_L1[brIdx]
            v1 && layer == 2 -> BR_V1_L2[brIdx]
            v1 -> BR_V1_L3[brIdx]
            layer == 1 -> BR_V2_L1[brIdx]
            else -> BR_V2_L23[brIdx]
        }
        val rate = when (vb) { 3 -> intArrayOf(44100, 48000, 32000)[srIdx]; 2 -> intArrayOf(22050, 24000, 16000)[srIdx]; else -> intArrayOf(11025, 12000, 8000)[srIdx] }
        val pad = (b.u8(i + 2) shr 1) and 1
        val frameLen = if (layer == 1) (12 * br * 1000 / rate + pad) * 4 else (if (layer == 3 && !v1) 72 else 144) * br * 1000 / rate + pad
        val samples = if (layer == 1) 384 else if (layer == 3 && !v1) 576 else 1152
        val channels = if (((b.u8(i + 3) shr 6) and 3) == 3) 1 else 2
        return Header(if (vb == 3) 1 else 2, layer, br, rate, channels, frameLen, samples, (b.u8(i + 1) and 1) == 1)
    }

    fun parse(ra: RandomAccess, t: AudioTags) {
        t.codec = "mp3"
        var audioStart = 0L
        val id3 = Id3.tagLength(ra, 0)
        if (id3 > 0) { Id3.readV2(ra, 0, t); audioStart = id3 }
        // ID3v1 at the very end: used for whatever v2 did not give
        var tail = ra.size
        if (ra.size >= audioStart + 128) {
            val v1 = ra.read(ra.size - 128, 128)
            if (v1.ascii(0, 3) == "TAG") {
                tail -= 128
                fun f(o: Int, n: Int) = fixMojibake(String(v1, o, n, Charsets.ISO_8859_1).trimEnd('\u0000', ' ')).trim()
                if (t.title.isEmpty()) t.title = f(3, 30)
                if (t.artist.isEmpty()) t.artist = f(33, 30)
                if (t.album.isEmpty()) t.album = f(63, 30)
                if (t.year == 0) t.year = yearOf(f(93, 4))
                if (t.trackNo == 0 && v1.u8(125) == 0 && v1.u8(126) != 0) t.trackNo = v1.u8(126)
                if (t.genre.isEmpty()) t.genre = Id3.genreName(v1.u8(127))
            }
        }
        val tlen = t.durationMs
        t.durationMs = 0
        // first frame: scan forward for two consecutive valid headers
        val win = ra.read(audioStart, 32 * 1024)
        var p = -1
        var h: Header? = null
        var i = 0
        while (i + 4 <= win.size) {
            val c = header(win, i)
            if (c != null) {
                val next = i + c.frameLen
                val ok = if (next + 4 <= win.size) header(win, next) != null else true
                if (ok) { p = i; h = c; break }
            }
            i++
        }
        if (h == null || p < 0) { t.durationMs = tlen; return }
        val first = audioStart + p
        t.sampleRate = h.rate
        t.channels = h.channels
        t.bitDepth = 16
        // Xing / Info (also written by LAME for CBR) and VBRI hold the frame count
        val side = if (h.version == 1) (if (h.channels == 1) 17 else 32) else (if (h.channels == 1) 9 else 17)
        val crc = if (h.protection) 0 else 2
        var frames = 0L; var bytes = 0L
        for (xo in intArrayOf(4 + side, 4 + side + crc)) {
            val x = ra.read(first + xo, 16)
            val id = x.ascii(0, 4)
            if (id == "Xing" || id == "Info") {
                val fl = x.be32(4)
                var o = 8
                if ((fl and 1L) != 0L) { frames = x.be32(o); o += 4 }
                if ((fl and 2L) != 0L) bytes = x.be32(o)
                break
            }
        }
        if (frames == 0L) {
            val v = ra.read(first + 36, 18)
            if (v.ascii(0, 4) == "VBRI") { bytes = v.be32(10); frames = v.be32(14) }
        }
        val audioLen = (tail - first).coerceAtLeast(0)
        if (frames > 0) {
            t.durationMs = frames * h.samples * 1000L / h.rate
            val b = if (bytes > 0) bytes else audioLen
            t.bitrateKbps = if (t.durationMs > 0) (b * 8 / t.durationMs).toInt() else h.bitrate
        } else {
            t.durationMs = audioLen * 8L / h.bitrate
            t.bitrateKbps = h.bitrate
        }
        if (t.durationMs <= 0) t.durationMs = tlen
    }
}

// ---- FLAC -------------------------------------------------------------------------------------------------------------

internal object Flac {
    fun parse(ra: RandomAccess, t: AudioTags) {
        t.codec = "flac"
        var pos = Id3.tagLength(ra, 0)
        if (ra.read(pos, 4).ascii(0, 4) != "fLaC") return
        pos += 4
        var totalSamples = 0L
        var guard = 0
        var bestPicType = -1
        while (pos + 4 <= ra.size && guard++ < 256) {
            val h = ra.read(pos, 4)
            if (h.size < 4) break
            val last = (h.u8(0) and 0x80) != 0
            val type = h.u8(0) and 0x7F
            val len = h.be24(1)
            val body = pos + 4
            when (type) {
                0 -> if (len >= 18) {
                    val s = ra.read(body, 18)
                    val v = s.be64(10)
                    t.sampleRate = (v ushr 44).toInt()
                    t.channels = ((v ushr 41) and 7).toInt() + 1
                    t.bitDepth = ((v ushr 36) and 31).toInt() + 1
                    totalSamples = v and 0xFFFFFFFFFL
                }
                4 -> if (len in 8..(2 * 1024 * 1024)) VorbisComments.parse(ra.read(body, len), 0, t, flacPicture = false)
                6 -> if (t.wantArt && len in 40..(TagReader.ART_CAP + 4096)) {
                    // type(4) mime(4+n) desc(4+n) w h depth colors (16) data(4+n)
                    val head = ra.read(body, minOf(len, 4096))
                    val pt = head.be32(0).toInt()
                    var o = 4
                    val ml = head.be32(o).toInt(); o += 4 + ml
                    val dl = head.be32(o).toInt(); o += 4 + dl + 16
                    val n = head.be32(o).toInt(); o += 4
                    if (ml in 0..256 && dl in 0..4000 && n > 16 && o + n <= len && (bestPicType != 3 && (bestPicType == -1 || pt == 3))) {
                        val data = ra.read(body + o, n)
                        if (data.size == n) { t.art = data; bestPicType = pt }
                    }
                }
            }
            if (last) break
            pos = body + len
        }
        if (t.sampleRate > 0 && totalSamples > 0) {
            t.durationMs = totalSamples * 1000L / t.sampleRate
            if (t.durationMs > 0) t.bitrateKbps = (ra.size * 8 / t.durationMs).toInt()
        }
    }
}

/** Vorbis-style comments (FLAC, Ogg Vorbis/Opus): "KEY=value" with little-endian lengths. */
internal object VorbisComments {
    fun parse(b: ByteArray, start: Int, t: AudioTags, flacPicture: Boolean) {
        var o = start
        if (o + 4 > b.size) return
        val vendor = b.le32(o).toInt(); o += 4
        if (vendor < 0 || o + vendor > b.size) return
        o += vendor
        if (o + 4 > b.size) return
        val count = b.le32(o).toInt(); o += 4
        var date = 0
        var n = 0
        while (n++ < count && o + 4 <= b.size) {
            val l = b.le32(o).toInt(); o += 4
            if (l < 0 || o + l > b.size) break
            val e = String(b, o, l, Charsets.UTF_8)
            o += l
            val eq = e.indexOf('=')
            if (eq <= 0) continue
            val k = e.substring(0, eq).uppercase()
            val v = e.substring(eq + 1).trim()
            if (v.isEmpty()) continue
            when (k) {
                "TITLE" -> if (t.title.isEmpty()) t.title = v
                "ARTIST" -> t.artist = joinValues(t.artist, v)
                "ALBUMARTIST", "ALBUM ARTIST", "ALBUM_ARTIST" -> t.albumArtist = joinValues(t.albumArtist, v)
                "ALBUM" -> if (t.album.isEmpty()) t.album = v
                "TRACKNUMBER", "TRACK" -> if (t.trackNo == 0) t.trackNo = firstInt(v)
                "DISCNUMBER", "DISC" -> if (t.discNo == 0) t.discNo = firstInt(v)
                "DATE", "YEAR", "ORIGINALDATE" -> if (date == 0) date = yearOf(v)
                "GENRE" -> t.genre = joinValues(t.genre, v)
                "METADATA_BLOCK_PICTURE" -> if (t.wantArt && t.art == null && v.length < TagReader.ART_CAP * 2) {
                    try {
                        val raw = java.util.Base64.getMimeDecoder().decode(v)
                        pictureBlock(raw)?.let { t.art = it }
                    } catch (_: Exception) {}
                }
            }
        }
        if (t.year == 0) t.year = date
    }

    /** The image inside a FLAC PICTURE block (also used by Ogg's METADATA_BLOCK_PICTURE). */
    fun pictureBlock(b: ByteArray): ByteArray? {
        var o = 4
        val ml = b.be32(o).toInt(); if (ml < 0) return null; o += 4 + ml
        val dl = b.be32(o).toInt(); if (dl < 0) return null; o += 4 + dl + 16
        val n = b.be32(o).toInt(); o += 4
        if (n <= 16 || o + n > b.size) return null
        return b.copyOfRange(o, o + n)
    }
}

// ---- Ogg Vorbis / Opus ------------------------------------------------------------------------------------------------

internal object Ogg {
    fun parse(ra: RandomAccess, t: AudioTags) {
        var off = 0L
        var packetNo = 0
        val cur = java.io.ByteArrayOutputStream()
        var curOver = false
        var id: ByteArray? = null
        var comment: ByteArray? = null
        var pages = 0
        val cap = TagReader.ART_CAP * 4 / 3 + 65536
        while (pages++ < 6000 && packetNo < 2) {
            val h = ra.read(off, 27)
            if (h.size < 27 || h.ascii(0, 4) != "OggS") break
            val nseg = h.u8(26)
            val segs = ra.read(off + 27, nseg)
            if (segs.size < nseg) break
            val bodyLen = segs.sumOf { it.toInt() and 0xFF }
            val body = ra.read(off + 27 + nseg, bodyLen)
            var bo = 0
            for (s in segs) {
                val l = s.toInt() and 0xFF
                if (!curOver) { if (cur.size() + l > cap) curOver = true else cur.write(body, bo, minOf(l, body.size - bo)) }
                bo += l
                if (l < 255) {
                    val pk = if (curOver) null else cur.toByteArray()
                    if (packetNo == 0) id = pk else if (packetNo == 1) comment = pk
                    packetNo++
                    cur.reset(); curOver = false
                    if (packetNo >= 2) break
                }
            }
            off += 27 + nseg + bodyLen
        }
        val idp = id ?: return
        var opus = false
        var preSkip = 0
        if (idp.size >= 19 && idp.ascii(0, 8) == "OpusHead") {
            opus = true
            t.codec = "opus"
            t.channels = idp.u8(9)
            preSkip = idp.le16(10)
            t.sampleRate = 48000
        } else if (idp.size >= 16 && idp.u8(0) == 1 && idp.ascii(1, 6) == "vorbis") {
            t.codec = "vorbis"
            t.channels = idp.u8(11)
            t.sampleRate = idp.le32(12).toInt()
            val nominal = idp.le32(20).toInt()
            if (nominal > 0) t.bitrateKbps = nominal / 1000
        } else return
        val c = comment
        if (c != null) {
            if (opus && c.size > 8 && c.ascii(0, 8) == "OpusTags") VorbisComments.parse(c, 8, t, false)
            else if (!opus && c.size > 7 && c.u8(0) == 3 && c.ascii(1, 6) == "vorbis") VorbisComments.parse(c, 7, t, false)
        }
        // duration: the granule position of the last page
        val tailLen = minOf(ra.size, 70_000L).toInt()
        val tail = ra.read(ra.size - tailLen, tailLen)
        var i = tail.size - 27
        while (i >= 0) {
            if (tail[i] == 'O'.code.toByte() && tail.ascii(i, 4) == "OggS") {
                val g = tail.le32(i + 6) or (tail.le32(i + 10) shl 32)
                if (g > 0) {
                    val rate = if (opus) 48000L else t.sampleRate.toLong()
                    val samples = if (opus) g - preSkip else g
                    if (rate > 0 && samples > 0) t.durationMs = samples * 1000L / rate
                    break
                }
            }
            i--
        }
        if (t.durationMs > 0) t.bitrateKbps = (ra.size * 8 / t.durationMs).toInt()
    }
}

// ---- MP4 / M4A / ALAC ---------------------------------------------------------------------------------------------------

internal object Mp4 {
    private class Atom(val type: String, val start: Long, val hdr: Int, val end: Long) {
        val body: Long get() = start + hdr
    }

    private fun atoms(ra: RandomAccess, from: Long, to: Long, f: (Atom) -> Boolean) {
        var pos = from
        var guard = 0
        while (pos + 8 <= to && guard++ < 5000) {
            val h = ra.read(pos, 16)
            if (h.size < 8) return
            var size = h.be32(0)
            var hdr = 8
            if (size == 1L) { if (h.size < 16) return; size = h.be64(8); hdr = 16 } else if (size == 0L) size = to - pos
            if (size < hdr) return
            val end = minOf(to, pos + size)
            if (f(Atom(h.ascii(4, 4), pos, hdr, end))) return
            if (pos + size <= pos) return
            pos += size
        }
    }

    fun parse(ra: RandomAccess, t: AudioTags) {
        t.codec = "aac"
        var found = false
        atoms(ra, 0, ra.size) { a ->
            if (a.type == "moov") { moov(ra, a, t); found = true; true } else false
        }
        if (found && t.durationMs > 0 && t.bitrateKbps == 0) t.bitrateKbps = (ra.size * 8 / t.durationMs).toInt()
    }

    private fun moov(ra: RandomAccess, m: Atom, t: AudioTags) {
        var gotTrack = false
        atoms(ra, m.body, m.end) { a ->
            when (a.type) {
                "mvhd" -> {
                    val b = ra.read(a.body, 32)
                    val v = b.u8(0)
                    val scale = if (v == 1) b.be32(20) else b.be32(12)
                    val dur = if (v == 1) b.be64(24) else b.be32(16)
                    if (scale > 0 && dur > 0 && t.durationMs == 0L) t.durationMs = dur * 1000L / scale
                }
                "udta" -> atoms(ra, a.body, a.end) { c -> if (c.type == "meta") meta(ra, c, t); false }
                "meta" -> meta(ra, a, t)
                "trak" -> if (!gotTrack) gotTrack = track(ra, a, t)
            }
            false
        }
    }

    private fun track(ra: RandomAccess, trak: Atom, t: AudioTags): Boolean {
        var ok = false
        fun walk(from: Long, to: Long, path: List<String>) {
            atoms(ra, from, to) { a ->
                if (ok) return@atoms true
                when {
                    a.type == "stsd" && path.takeLast(1) == listOf("stbl") -> ok = stsd(ra, a, t)
                    a.type in setOf("mdia", "minf", "stbl") -> walk(a.body, a.end, path + a.type)
                }
                false
            }
        }
        walk(trak.body, trak.end, listOf("trak"))
        return ok
    }

    private fun stsd(ra: RandomAccess, a: Atom, t: AudioTags): Boolean {
        val b = ra.read(a.body, 8 + 36 + 8)
        if (b.be32(4) < 1) return false
        val fourcc = b.ascii(12, 4)
        if (fourcc !in setOf("mp4a", "alac", "fLaC", "ac-3", "ec-3", "Opus", "samr", "sowt", "twos", "in24", "in32", "lpcm")) return false
        val e = a.body + 8        // sample entry start (its size field)
        val entry = ra.read(e, 36)
        val ver = entry.be16(16)
        t.channels = entry.be16(24)
        val bits = entry.be16(26)
        t.sampleRate = (entry.be32(32) ushr 16).toInt()
        t.bitDepth = if (bits in 8..32) bits else 16
        when (fourcc) {
            "alac" -> {
                t.codec = "alac"
                val eSize = entry.be32(0)
                val childStart = e + 36 + if (ver == 1) 16 else 0
                atoms(ra, childStart, minOf(a.end, e + eSize)) { c ->
                    if (c.type == "alac") {
                        val cfg = ra.read(c.body + 4, 24)
                        if (cfg.size >= 24) {
                            if (cfg.u8(5) in 8..32) t.bitDepth = cfg.u8(5)
                            if (cfg.u8(9) > 0) t.channels = cfg.u8(9)
                            val sr = cfg.be32(20).toInt()
                            if (sr in 1000..768000) t.sampleRate = sr
                            val br = cfg.be32(16)
                            if (br > 0) t.bitrateKbps = (br / 1000).toInt()
                        }
                        true
                    } else false
                }
            }
            "fLaC" -> t.codec = "flac"
            "mp4a" -> t.codec = "aac"
            "Opus" -> t.codec = "opus"
            "ac-3", "ec-3" -> t.codec = "ac3"
            else -> t.codec = "pcm"
        }
        return true
    }

    private fun meta(ra: RandomAccess, m: Atom, t: AudioTags) {
        // a full box (4 version/flags bytes) in practice; some QuickTime files omit them
        val probe = ra.read(m.body, 12)
        val start = if (probe.ascii(4, 4) == "hdlr" || probe.ascii(4, 4) == "ilst") m.body else if (probe.ascii(8, 4).let { it == "hdlr" || it == "ilst" }) m.body + 4 else m.body + 4
        atoms(ra, start, m.end) { a -> if (a.type == "ilst") { ilst(ra, a, t); true } else false }
    }

    private fun ilst(ra: RandomAccess, il: Atom, t: AudioTags) {
        atoms(ra, il.body, il.end) { item ->
            var payload: ByteArray? = null
            var flagsType = 0
            var big = false
            atoms(ra, item.body, item.end) { d ->
                if (d.type == "data") {
                    val n = d.end - d.body - 8
                    val isCover = item.type == "covr"
                    if (n in 1..(if (isCover) (if (t.wantArt) TagReader.ART_CAP.toLong() else 0L) else 65536L)) {
                        val all = ra.read(d.body, 8 + n.toInt())
                        if (all.size == 8 + n.toInt()) { flagsType = all.be24(1); payload = all.copyOfRange(8, all.size) }
                    } else big = n > 0
                    true
                } else false
            }
            val p = payload
            if (p != null) {
                fun str() = String(p, Charsets.UTF_8).trim().trim('\u0000')
                when (item.type) {
                    "©nam" -> if (t.title.isEmpty()) t.title = str()
                    "©ART" -> if (t.artist.isEmpty()) t.artist = str()
                    "aART" -> if (t.albumArtist.isEmpty()) t.albumArtist = str()
                    "©alb" -> if (t.album.isEmpty()) t.album = str()
                    "©day" -> if (t.year == 0) t.year = yearOf(str())
                    "©gen" -> if (t.genre.isEmpty()) t.genre = str()
                    "gnre" -> if (t.genre.isEmpty() && p.size >= 2) t.genre = Id3.genreName(p.be16(0) - 1)
                    "trkn" -> if (t.trackNo == 0 && p.size >= 4) t.trackNo = p.be16(2)
                    "disk" -> if (t.discNo == 0 && p.size >= 4) t.discNo = p.be16(2)
                    "covr" -> if (t.art == null && flagsType in setOf(13, 14, 27, 0)) t.art = p
                }
            }
            if (big && item.type == "covr") { /* cover over the cap: skipped */ }
            false
        }
    }
}

// ---- WAV / AIFF -------------------------------------------------------------------------------------------------------

internal object Riff {
    fun parseWav(ra: RandomAccess, t: AudioTags) {
        t.codec = "wav"
        var pos = 12L
        var byteRate = 0L
        var dataLen = -1L
        var guard = 0
        while (pos + 8 <= ra.size && guard++ < 400) {
            val h = ra.read(pos, 8)
            if (h.size < 8) break
            val id = h.ascii(0, 4)
            var len = h.le32(4)
            val body = pos + 8
            when (id) {
                "fmt " -> {
                    val f = ra.read(body, 16)
                    t.channels = f.le16(2)
                    t.sampleRate = f.le32(4).toInt()
                    byteRate = f.le32(8)
                    t.bitDepth = f.le16(14)
                    if (byteRate > 0) t.bitrateKbps = (byteRate * 8 / 1000).toInt()
                }
                "data" -> {
                    // a streamed or damaged file can claim 0 or 4 GB: trust what is really there
                    val real = ra.size - body
                    dataLen = if (len == 0L || len > real) real else len
                }
                "LIST" -> if (len in 12..65536) {
                    val l = ra.read(body, len.toInt())
                    if (l.ascii(0, 4) == "INFO") {
                        var o = 4
                        while (o + 8 <= l.size) {
                            val k = l.ascii(o, 4); val n = l.le32(o + 4).toInt()
                            if (n < 0 || o + 8 + n > l.size) break
                            val v = fixMojibake(String(l, o + 8, n, Charsets.ISO_8859_1).trimEnd('\u0000').trim())
                            when (k) {
                                "INAM" -> if (t.title.isEmpty()) t.title = v
                                "IART" -> if (t.artist.isEmpty()) t.artist = v
                                "IPRD" -> if (t.album.isEmpty()) t.album = v
                                "ICRD" -> if (t.year == 0) t.year = yearOf(v)
                                "IGNR" -> if (t.genre.isEmpty()) t.genre = v
                                "ITRK", "IPRT" -> if (t.trackNo == 0) t.trackNo = firstInt(v)
                            }
                            o += 8 + n + (n and 1)
                        }
                    }
                }
                "id3 ", "ID3 " -> Id3.readV2(ra, body, t)
            }
            if (len > ra.size) break
            pos = body + len + (len and 1L)
        }
        if (byteRate > 0 && dataLen > 0) t.durationMs = dataLen * 1000L / byteRate
    }

    fun parseAiff(ra: RandomAccess, t: AudioTags) {
        t.codec = "aiff"
        var pos = 12L
        var frames = 0L
        var guard = 0
        while (pos + 8 <= ra.size && guard++ < 400) {
            val h = ra.read(pos, 8)
            if (h.size < 8) break
            val id = h.ascii(0, 4)
            val len = h.be32(4)
            val body = pos + 8
            when (id) {
                "COMM" -> {
                    val c = ra.read(body, 18)
                    t.channels = c.be16(0)
                    frames = c.be32(2)
                    t.bitDepth = c.be16(6)
                    val exp = c.be16(8) and 0x7FFF
                    val mant = c.be64(10)
                    if (mant != 0L) t.sampleRate = (mant.toULong().toDouble() * Math.pow(2.0, (exp - 16383 - 63).toDouble())).toInt()
                    if (t.sampleRate > 0 && t.channels > 0) t.bitrateKbps = t.sampleRate * t.bitDepth * t.channels / 1000
                }
                "NAME" -> if (len in 1..4096 && t.title.isEmpty()) t.title = fixMojibake(String(ra.read(body, len.toInt()), Charsets.ISO_8859_1).trimEnd('\u0000').trim())
                "AUTH" -> if (len in 1..4096 && t.artist.isEmpty()) t.artist = fixMojibake(String(ra.read(body, len.toInt()), Charsets.ISO_8859_1).trimEnd('\u0000').trim())
                "ID3 ", "id3 " -> Id3.readV2(ra, body, t)
            }
            if (len > ra.size) break
            pos = body + len + (len and 1L)
        }
        if (t.sampleRate > 0 && frames > 0) t.durationMs = frames * 1000L / t.sampleRate
    }
}
