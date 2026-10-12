package com.ipodemu.library

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** The codec names Jellyfin really sends, and what is lossless among them (the Hi-Res badge needs lossless first). */
class QualityRuleTest {
    @Test fun losslessNamesAsJellyfinSpellsThem() {
        for (c in listOf("flac", "FLAC", "alac", "wavpack", "ape", "pcm_s16le", "pcm_s24le", "pcm_s24be", "pcm_f32le", "pcm_s32le", "dsd_lsbf_planar", "dsd_msbf", "wmalossless", "truehd", "tta"))
            assertTrue(c, QualityRule.isLossless(c))
    }

    @Test fun lossyNamesAreNeverLossless() {
        for (c in listOf("mp3", "aac", "opus", "vorbis", "wmav2", "ac3", "eac3", "mp2", "")) assertFalse(c, QualityRule.isLossless(c))
    }

    @Test fun theRuleIsLosslessAndBeyondCd() {
        assertTrue(QualityRule.isHiRes(QualityRule.isLossless("pcm_s24le"), 48_000, 24))
        assertFalse(QualityRule.isHiRes(QualityRule.isLossless("mp3"), 96_000, 24))
        assertFalse(QualityRule.isHiRes(QualityRule.isLossless("flac"), 44_100, 16))
    }
}
