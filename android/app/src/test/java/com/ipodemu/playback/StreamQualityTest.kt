package com.ipodemu.playback

import com.ipodemu.library.JellyfinPaths
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class StreamQualityTest {
    private val flac = StreamQuality.Pick.FLAC
    private val opus = StreamQuality.Pick.OPUS

    @Test fun under_1_5_mbps_picks_opus() {
        assertEquals(opus, StreamQuality.pick(1_499_999, flac))
        assertEquals(opus, StreamQuality.pick(300_000, opus))
    }

    @Test fun above_4_mbps_picks_flac() {
        assertEquals(flac, StreamQuality.pick(4_000_001, opus))
        assertEquals(flac, StreamQuality.pick(40_000_000, flac))
    }

    @Test fun in_between_keeps_the_current_choice() {
        for (bps in listOf(1_500_000L, 2_500_000L, 4_000_000L)) { assertEquals(opus, StreamQuality.pick(bps, opus)); assertEquals(flac, StreamQuality.pick(bps, flac)) }
    }

    @Test fun nothing_measured_keeps_the_current_choice() {
        assertEquals(opus, StreamQuality.pick(0, opus)); assertEquals(flac, StreamQuality.pick(-1, flac))
    }

    @Test fun only_automatic_converts() {
        assertTrue(StreamQuality.opusNow(0, opus))
        assertFalse(StreamQuality.opusNow(0, flac))
        assertFalse(StreamQuality.opusNow(1, opus))   // Always full
        assertFalse(StreamQuality.opusNow(2, opus))   // Data saver has its own copy
    }

    @Test fun a_downloaded_song_is_never_transcoded() {
        assertEquals(PlaySource.From.LOCAL, PlaySource.choose(ownAlac = false, local = true, needsTranscode = false, dataSaverPinned = false, hlsPossible = true, weakOpus = true, opusPossible = true))
        assertEquals(PlaySource.From.LOCAL, PlaySource.choose(false, true, false, true, true, weakOpus = true, opusPossible = true))
    }

    @Test fun a_streamed_song_on_a_weak_line_is_the_opus_copy_when_it_can_be_made() {
        assertEquals(PlaySource.From.OPUS, PlaySource.choose(false, false, false, false, true, weakOpus = true, opusPossible = true))
        assertEquals(PlaySource.From.STREAM, PlaySource.choose(false, false, false, false, true, weakOpus = true, opusPossible = false))
        assertEquals(PlaySource.From.STREAM, PlaySource.choose(false, false, false, false, true, weakOpus = false, opusPossible = true))
    }

    @Test fun the_opus_copy_is_jellyfins_128k_conversion() {
        val id = "0123456789abcdef0123456789abcdef"
        val url = JellyfinPaths.opusUrl("https://jf.example/jellyfin/Audio/$id/stream?static=true", "u1", "dev1")!!
        assertTrue(url.startsWith("https://jf.example/jellyfin/Audio/$id/universal?"))
        for (part in listOf("UserId=u1", "DeviceId=dev1", "Container=opus", "AudioCodec=opus", "MaxStreamingBitrate=128000", "AudioBitRate=128000")) assertTrue(part, url.contains(part))
    }

    @Test fun no_opus_copy_for_other_addresses_or_without_a_sign_in() {
        assertNull(JellyfinPaths.opusUrl("/storage/emulated/0/Music/a.flac", "u1", "d"))
        assertNull(JellyfinPaths.opusUrl("http://nas/music/a.flac", "u1", "d"))
        assertNull(JellyfinPaths.opusUrl("https://jf.example/Audio/0123456789abcdef0123456789abcdef/stream", "", "d"))
    }
}
