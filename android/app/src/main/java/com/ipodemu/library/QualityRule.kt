package com.ipodemu.library

/**
 * The one rule for the Hi-Res badge, used by every screen (song rows, Now Playing chip, Info panel, Explore filter): a file is Hi-Res when it is
 * lossless AND its REAL metadata says more than 16 bits or more than 44.1 kHz. Nothing else counts: not the bit rate, not the file extension, not a
 * "[Hi-Res]" in the file name, not a hint from a server. A value that is not known (0) never makes a file Hi-Res.
 */
object QualityRule {
    val LOSSLESS_CODECS = setOf("flac", "alac", "wav", "pcm", "aiff", "ape", "wavpack", "wv", "dsd", "tta", "pcm_s16le", "pcm_s24le", "pcm_s32le", "hi-res")

    fun isLossless(codec: String): Boolean = codec.lowercase() in LOSSLESS_CODECS

    fun isHiRes(lossless: Boolean, rate: Int, depth: Int): Boolean = lossless && (depth > 16 || rate > 44_100)
}
