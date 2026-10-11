package com.ipodemu.player

import com.ipodemu.library.AudioFacts
import com.ipodemu.library.QualityRule
import com.ipodemu.library.Track
import com.ipodemu.library.TrackSource
import com.ipodemu.library.hiRes
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** The Hi-Res badge is a pure function of the file's real metadata: lossless AND (depth > 16 OR rate > 44.1 kHz). */
class HiResBadgeTest {
    private fun fact(codec: String, rate: Int, depth: Int, kbps: Int) = AudioFacts.Fact(codec, rate, depth, kbps)

    @Test fun rule_table() {
        // (lossless, rate, depth) -> hi-res
        assertFalse("FLAC 16/44.1", QualityRule.isHiRes(true, 44_100, 16))
        assertTrue("FLAC 24/44.1", QualityRule.isHiRes(true, 44_100, 24))
        assertTrue("FLAC 24/48", QualityRule.isHiRes(true, 48_000, 24))
        assertTrue("FLAC 16/48", QualityRule.isHiRes(true, 48_000, 16))
        assertTrue("FLAC 16/96", QualityRule.isHiRes(true, 96_000, 16))
        assertTrue("FLAC 24/192", QualityRule.isHiRes(true, 192_000, 24))
        assertFalse("lossy never", QualityRule.isHiRes(false, 96_000, 24))
        assertFalse("unknown values never", QualityRule.isHiRes(true, 0, 0))
        assertFalse("unknown rate, CD depth", QualityRule.isHiRes(true, 0, 16))
    }

    @Test fun high_bitrate_cd_flac_is_not_hi_res() {
        for (kbps in listOf(1100, 1411, 1700, 2000, 4000)) {
            val f = fact("flac", 44_100, 16, kbps)
            assertFalse("16/44.1 at $kbps kbps", f.hiRes)
            val i = buildAudioInfo(f, "flac", null, 0, 0, 0, 0)
            assertFalse("info 16/44.1 at $kbps kbps", i.hiRes)
            assertEquals("FLAC", i.codec)
        }
    }

    @Test fun genuine_hi_res_files_are_hi_res() {
        for ((r, d) in listOf(48_000 to 24, 96_000 to 16, 96_000 to 24, 192_000 to 24, 44_100 to 24)) {
            assertTrue("$d/$r", buildAudioInfo(fact("flac", r, d, 3000), "flac", null, 0, 0, 0, 0).hiRes)
        }
        assertTrue(buildAudioInfo(fact("alac", 48_000, 24, 2000), "m4a", null, 0, 0, 0, 0).hiRes)
        assertFalse(buildAudioInfo(fact("wav", 44_100, 16, 1411), "wav", null, 0, 0, 0, 0).hiRes)
        assertFalse(buildAudioInfo(fact("mp3", 44_100, 0, 320), "mp3", null, 0, 0, 0, 0).hiRes)
        assertFalse("lossy at 96 kHz", buildAudioInfo(fact("aac", 96_000, 0, 320), "m4a", null, 0, 0, 0, 0).hiRes)
    }

    @Test fun nothing_is_guessed_when_the_format_is_not_known() {
        // a FLAC whose size and length say 2000 kbps, no facts, nothing playing: neutral FLAC, never Hi-Res
        val i = buildAudioInfo(null, "flac", null, 0, 0, size = 2000L * 1000 / 8 * 240, durationMs = 240_000)
        assertEquals("FLAC", i.codec); assertFalse(i.hiRes); assertEquals(0, i.sampleRate); assertEquals(0, i.bits)
    }

    @Test fun a_hi_res_file_name_does_not_make_a_cd_file_hi_res() {
        val t = Track(path = "smb://h/s/Artist/Album/Song [Hi-Res].flac", title = "Song [Hi-Res]", artist = "A", album = "B", albumArtist = "A", genre = "", trackNo = 0, discNo = 0,
            durationMs = 240_000, year = 0, isMusic = true, artKey = null, mtime = 1, size = 40_000_000, source = TrackSource.NAS, filePath = "Artist/Album/Song [Hi-Res].flac")
        assertFalse("no facts yet: not Hi-Res by name", t.hiRes)
    }

    @Test fun the_files_facts_beat_the_renderer_so_a_skip_cannot_lend_a_badge() {
        // the previous song was 24/96; the new one is 16/44.1; the renderer still reports 96 kHz during the change
        val staleDecoder = DecoderFormat("audio/flac", 24, 96_000, 2, 3000)
        val next = buildAudioInfo(fact("flac", 44_100, 16, 900), "flac", staleDecoder, 96_000, 2, 0, 0)
        assertFalse("the facts of THIS file decide", next.hiRes)
        assertEquals(44_100, next.sampleRate); assertEquals(16, next.bits)
    }

    @Test fun without_facts_the_renderer_counts_only_when_the_caller_vouches_for_it() {
        // no decoder passed (a different song, or not playing yet): unknown, not Hi-Res
        assertFalse(buildAudioInfo(null, "flac", null, 96_000, 2, 0, 0).hiRes)
        // this very song is playing and the renderer says 96 kHz: that is real metadata of the stream
        assertTrue(buildAudioInfo(null, "flac", DecoderFormat("audio/flac", 0, 96_000, 2, 0), 96_000, 2, 0, 0).hiRes)
        // a float pipeline output (reported as 0 bits by audioInfo) is not a file depth
        assertFalse(buildAudioInfo(null, "flac", DecoderFormat("audio/flac", 0, 44_100, 2, 0), 44_100, 2, 0, 0).hiRes)
    }

    @Test fun same_inputs_always_give_the_same_answer() {
        val f = fact("flac", 96_000, 24, 4000)
        val results = (1..50).map { buildAudioInfo(f, "flac", null, 0, 0, 0, 0) }.toSet()
        assertEquals(1, results.size)
    }
}
