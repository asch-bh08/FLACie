package com.ipodemu.library

import com.ipodemu.library.tags.ArrayAccess
import com.ipodemu.library.tags.AudioTags
import com.ipodemu.library.tags.RandomAccess
import com.ipodemu.library.tags.TagReader
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayOutputStream
import java.io.IOException
import java.util.Random

class TagReaderTest {
    private fun bytes(name: String): ByteArray =
        (javaClass.getResourceAsStream("/tags/$name") ?: error("missing fixture $name")).use { it.readBytes() }

    private fun read(name: String, wantArt: Boolean = true): AudioTags =
        TagReader.read(ArrayAccess(bytes(name)), name, wantArt) ?: error("no tags read from $name")

    private fun assertJpeg(b: ByteArray?) {
        assertNotNull("no art", b)
        assertEquals(0xFF, b!![0].toInt() and 0xFF); assertEquals(0xD8, b[1].toInt() and 0xFF)
    }

    private fun assertPng(b: ByteArray?) {
        assertNotNull("no art", b)
        assertEquals(0x89, b!![0].toInt() and 0xFF); assertEquals('P'.code, b[1].toInt()); assertEquals('N'.code, b[2].toInt()); assertEquals('G'.code, b[3].toInt())
    }

    private fun assertStandard(t: AudioTags, hasAlbumArtist: Boolean = true) {
        assertEquals("Test_Title", t.title)
        assertEquals("Test_Artist", t.artist)
        if (hasAlbumArtist) assertEquals("Album_Artist", t.albumArtist)
        assertEquals("Test_Album", t.album)
        assertEquals(3, t.trackNo)
        assertEquals(2011, t.year)
        assertEquals("Rock", t.genre)
    }

    private fun assertDuration(expectedMs: Long, t: AudioTags, tolMs: Long = 50) =
        assertTrue("duration ${t.durationMs} not within $tolMs of $expectedMs", Math.abs(t.durationMs - expectedMs) <= tolMs)

    // ---- MP3 -----------------------------------------------------------------------------------------------------

    @Test fun mp3_id3v23_with_cover() {
        val t = read("mp3_v23_art.mp3")
        assertStandard(t)
        assertEquals(1, t.discNo)
        assertDuration(2000, t)
        assertEquals(44100, t.sampleRate)
        assertEquals("mp3", t.codec)
        assertTrue(t.bitrateKbps in 56..72)
        assertJpeg(t.art)
    }

    @Test fun mp3_id3v24_unicode_png_cover() {
        val t = read("mp3_v24_unicode.mp3")
        assertEquals("Ünïcödé 日本語 Тест", t.title)
        assertEquals("Björk & 坂本龍一", t.artist)
        assertEquals("Café Ωmega", t.album)
        assertEquals(7, t.trackNo)
        assertEquals(1999, t.year)
        assertDuration(2000, t)
        assertPng(t.art)
    }

    @Test fun mp3_without_tags_estimates_duration_from_cbr() {
        val t = read("mp3_notags.mp3")
        assertEquals("", t.title)
        assertEquals(44100, t.sampleRate)
        // no Xing header: bytes / bitrate (the encoder pads a little, ffprobe says 2.0375 s)
        assertDuration(2037, t, 60)
        assertNull(t.art)
    }

    @Test fun mp3_id3v1_only() {
        val t = read("mp3_v1.mp3")
        assertEquals("V1_Title", t.title)
        assertEquals("V1_Artist", t.artist)
        assertEquals("V1_Album", t.album)
        assertEquals(2004, t.year)
        assertEquals(5, t.trackNo)
        assertEquals("Pop", t.genre)
        assertDuration(2000, t)
    }

    @Test fun mp3_id3v22_frames_and_pic() {
        fun frame(id: String, body: ByteArray) = id.toByteArray(Charsets.ISO_8859_1) + byteArrayOf((body.size shr 16).toByte(), (body.size shr 8).toByte(), body.size.toByte()) + body
        fun text(id: String, s: String) = frame(id, byteArrayOf(0) + s.toByteArray(Charsets.ISO_8859_1))
        val jpeg = bytes("cover.jpg")
        val pic = frame("PIC", byteArrayOf(0) + "JPG".toByteArray() + byteArrayOf(3) + byteArrayOf(0) + jpeg)
        val body = text("TT2", "Old Title") + text("TP1", "Old Artist") + text("TAL", "Old Album") + text("TRK", "4/9") + text("TYE", "1987") + text("TCO", "(17)") + pic
        val n = body.size
        val head = byteArrayOf('I'.code.toByte(), 'D'.code.toByte(), '3'.code.toByte(), 2, 0, 0,
            ((n shr 21) and 0x7F).toByte(), ((n shr 14) and 0x7F).toByte(), ((n shr 7) and 0x7F).toByte(), (n and 0x7F).toByte())
        val file = head + body + bytes("mp3_notags.mp3")
        val t = TagReader.read(ArrayAccess(file), "x.mp3")!!
        assertEquals("Old Title", t.title)
        assertEquals("Old Artist", t.artist)
        assertEquals("Old Album", t.album)
        assertEquals(4, t.trackNo)
        assertEquals(1987, t.year)
        assertEquals("Rock", t.genre)
        assertJpeg(t.art)
        assertDuration(2037, t, 60)
    }

    @Test fun mp3_art_is_skipped_when_not_wanted() {
        val t = read("mp3_v23_art.mp3", wantArt = false)
        assertNull(t.art)
        assertEquals("Test_Title", t.title)
    }

    // ---- FLAC ----------------------------------------------------------------------------------------------------

    @Test fun flac_streaminfo_comments_picture() {
        val t = read("flac_art.flac")
        assertStandard(t)
        assertEquals(1, t.discNo)
        assertEquals(44100, t.sampleRate)
        assertEquals(16, t.bitDepth)
        assertEquals("flac", t.codec)
        assertDuration(2000, t)
        assertJpeg(t.art)
    }

    @Test fun flac_hires_stream_facts() {
        val t = read("flac_hires.flac")
        assertEquals("HiRes", t.title)
        assertEquals(96000, t.sampleRate)
        assertEquals(24, t.bitDepth)
        assertDuration(1000, t)
    }

    // ---- MP4 -----------------------------------------------------------------------------------------------------

    @Test fun m4a_aac_moov_at_end() {
        val data = bytes("m4a_aac_moovend.m4a")
        // the fixture really has its moov after mdat
        val s = String(data, Charsets.ISO_8859_1)
        assertTrue(s.indexOf("moov") > s.indexOf("mdat"))
        val t = TagReader.read(ArrayAccess(data), "a.m4a")!!
        assertStandard(t)
        assertEquals(3, t.trackNo)
        assertEquals(1, t.discNo)
        assertEquals("aac", t.codec)
        assertEquals(44100, t.sampleRate)
        assertDuration(2000, t)
        assertJpeg(t.art)
    }

    @Test fun m4a_aac_moov_in_front() {
        val data = bytes("m4a_aac_front.m4a")
        val s = String(data, Charsets.ISO_8859_1)
        assertTrue(s.indexOf("moov") < s.indexOf("mdat"))
        val t = TagReader.read(ArrayAccess(data), "a.m4a")!!
        assertStandard(t)
        assertDuration(2000, t)
        assertJpeg(t.art)
    }

    @Test fun m4a_alac_facts() {
        val t = read("alac.m4a")
        assertStandard(t)
        assertEquals("alac", t.codec)
        assertEquals(44100, t.sampleRate)
        assertEquals(16, t.bitDepth)
        assertDuration(2000, t)
        assertJpeg(t.art)
    }

    // ---- Ogg -----------------------------------------------------------------------------------------------------

    @Test fun ogg_vorbis() {
        val t = read("ogg_vorbis.ogg")
        assertStandard(t)
        assertEquals("vorbis", t.codec)
        assertEquals(44100, t.sampleRate)
        assertDuration(2000, t)
    }

    @Test fun ogg_opus() {
        val t = read("opus.opus")
        assertStandard(t)
        assertEquals("opus", t.codec)
        assertEquals(48000, t.sampleRate)
        assertDuration(2000, t, 50)
    }

    // ---- WAV / AIFF ----------------------------------------------------------------------------------------------

    @Test fun wav_info_and_duration() {
        val t = read("wav_tags.wav")
        assertEquals("Test_Title", t.title)
        assertEquals("Test_Artist", t.artist)
        assertEquals("Test_Album", t.album)
        assertEquals(8000, t.sampleRate)
        assertEquals(16, t.bitDepth)
        assertDuration(1000, t)
        assertEquals("wav", t.codec)
    }

    @Test fun aiff_comm_and_id3() {
        val t = read("aiff_tags.aiff")
        assertEquals("aiff", t.codec)
        assertEquals(8000, t.sampleRate)
        assertEquals(16, t.bitDepth)
        assertDuration(1000, t)
        assertEquals("Test_Title", t.title)
        assertEquals("Test_Artist", t.artist)
    }

    // ---- damaged input never throws -------------------------------------------------------------------------------

    private val all = listOf("mp3_v23_art.mp3", "mp3_v24_unicode.mp3", "mp3_notags.mp3", "mp3_v1.mp3", "flac_art.flac", "flac_hires.flac",
        "m4a_aac_moovend.m4a", "m4a_aac_front.m4a", "alac.m4a", "ogg_vorbis.ogg", "opus.opus", "wav_tags.wav", "aiff_tags.aiff")

    @Test fun truncated_files_never_throw() {
        for (name in all) {
            val full = bytes(name)
            val cuts = listOf(0, 1, 3, 4, 8, 10, 11, 12, 20, 27, 28, 100, 500, 1000, 4000, full.size / 3, full.size / 2, full.size - 200, full.size - 1)
            for (c in cuts.filter { it in 0..full.size }) {
                // the result may be partial tags or null: it just must not throw
                TagReader.read(ArrayAccess(full.copyOf(c)), name)
            }
        }
    }

    @Test fun truncated_mp4_with_moov_at_end_gives_no_crash_and_no_wrong_art() {
        val full = bytes("m4a_aac_moovend.m4a")
        val t = TagReader.read(ArrayAccess(full.copyOf(full.size - 400)), "a.m4a")
        // moov was cut: either nothing or a tag set without a corrupt picture
        if (t?.art != null) assertJpeg(t.art)
    }

    @Test fun garbage_never_throws() {
        val rnd = Random(42)
        for (round in 0 until 60) {
            val b = ByteArray(rnd.nextInt(5000) + 1).also { rnd.nextBytes(it) }
            // make some of them look like real containers with hostile sizes
            when (round % 6) {
                0 -> { "ID3".toByteArray().copyInto(b); b[3] = 4; b[4] = 0 }
                1 -> "fLaC".toByteArray().copyInto(b)
                2 -> "OggS".toByteArray().copyInto(b)
                3 -> if (b.size > 12) { "ftyp".toByteArray().copyInto(b, 4) }
                4 -> if (b.size > 12) { "RIFF".toByteArray().copyInto(b); "WAVE".toByteArray().copyInto(b, 8) }
                5 -> if (b.size > 12) { "FORM".toByteArray().copyInto(b); "AIFF".toByteArray().copyInto(b, 8) }
            }
            for (ext in listOf("mp3", "flac", "m4a", "ogg", "wav", "aiff", "xyz")) TagReader.read(ArrayAccess(b), "f.$ext")
        }
        assertNull(TagReader.read(ArrayAccess(ByteArray(0)), "a.mp3"))
    }

    @Test fun io_errors_do_not_escape() {
        val failing = object : RandomAccess {
            override val size = 1_000_000L
            override fun read(offset: Long, length: Int): ByteArray = throw IOException("network down")
        }
        assertNull(TagReader.read(failing, "a.mp3"))
    }

    @Test fun reads_are_ranges_not_the_whole_file() {
        // a 40 MB file whose only real content is a header: the reader must not ask for it all
        val head = bytes("flac_art.flac")
        var total = 0L
        val big = object : RandomAccess {
            override val size = 40L * 1024 * 1024
            override fun read(offset: Long, length: Int): ByteArray {
                total += length
                if (offset >= head.size) return ByteArray(minOf(length.toLong(), size - offset).toInt())
                return head.copyOfRange(offset.toInt(), minOf(head.size, offset.toInt() + length))
            }
        }
        val t = TagReader.read(big, "a.flac")!!
        assertEquals("Test_Title", t.title)
        assertTrue("read $total bytes", total < 2 * 1024 * 1024)
    }

    @Test fun output_stream_helper_sanity() {
        // keeps the fixtures honest: each stays under 60 KB
        for (name in all + listOf("cover.jpg", "cover.png")) assertTrue("$name too big", bytes(name).size < 60_000)
        assertTrue(ByteArrayOutputStream().size() == 0)
    }
}
