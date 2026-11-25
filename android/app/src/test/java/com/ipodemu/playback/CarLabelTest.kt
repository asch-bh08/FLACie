package com.ipodemu.playback

import com.ipodemu.library.TrackSource
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/** Owner, 25 Nov: "Android Auto is terrible: no labels for FLAC or Jellyfin or anything". Each song in the car says what it is and where it plays from. */
class CarLabelTest {
    @Test fun hiResSaysItsDepthAndRate() {
        assertEquals("Hi-Res 24/96", CarLabel.quality("flac", 96_000, 24, 2900, lossless = true, hiRes = true))
        assertEquals("Hi-Res 24/44.1", CarLabel.quality("flac", 44_100, 24, 1400, lossless = true, hiRes = true))
        assertEquals("Hi-Res 24/192", CarLabel.quality("alac", 192_000, 24, 5000, lossless = true, hiRes = true))
        assertEquals("Hi-Res 24/88.2", CarLabel.quality("flac", 88_200, 24, 3000, lossless = true, hiRes = true))
    }

    @Test fun cdQualityLosslessSaysItsFormat() {
        assertEquals("FLAC", CarLabel.quality("flac", 44_100, 16, 900, lossless = true, hiRes = false))
        assertEquals("ALAC", CarLabel.quality("alac", 44_100, 16, 900, lossless = true, hiRes = false))
        assertEquals("WAV", CarLabel.quality("pcm_s16le", 44_100, 16, 1411, lossless = true, hiRes = false))
    }

    @Test fun lossySaysItsFormatAndBitRate() {
        assertEquals("MP3 320", CarLabel.quality("mp3", 44_100, 0, 320, lossless = false, hiRes = false))
        assertEquals("AAC 256", CarLabel.quality("aac", 44_100, 0, 256, lossless = false, hiRes = false))
        assertEquals("OGG", CarLabel.quality("vorbis", 44_100, 0, 0, lossless = false, hiRes = false))
    }

    @Test fun withNoFactsTheFileNameSays() {
        assertEquals("FLAC", CarLabel.fromFile("/media/music/A/Song.flac"))
        assertEquals("MP3", CarLabel.fromFile("/media/music/A/Song.MP3"))
        assertNull(CarLabel.fromFile("jf:1234abcd"))
    }

    @Test fun whereItPlaysFrom() {
        assertEquals("Offline", CarLabel.source(TrackSource.JELLYFIN, downloaded = true))
        assertEquals("Jellyfin", CarLabel.source(TrackSource.JELLYFIN, downloaded = false))
        assertEquals("Plex", CarLabel.source(TrackSource.PLEX, downloaded = false))
        assertEquals("NAS", CarLabel.source(TrackSource.NAS, downloaded = false))
        assertEquals("On phone", CarLabel.source(TrackSource.LOCAL, downloaded = false))
    }

    @Test fun theSongsLineIsArtistQualityAndSource() {
        assertEquals("Yeah Yeah Yeahs · FLAC · Jellyfin", CarLabel.line("Yeah Yeah Yeahs", "FLAC", "Jellyfin"))
        assertEquals("Robyn · Offline", CarLabel.line("Robyn", null, "Offline"))
        assertEquals("FLAC · Offline", CarLabel.line("", "FLAC", "Offline"))
    }
}
