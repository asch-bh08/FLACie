package com.ipodemu.playback

import kotlin.math.pow

/**
 * "Even out volume": Jellyfin measures each song's loudness when it scans the library and gives the gain (in dB) that brings it to a common level
 * (`NormalizationGain` on the item). Loud songs are turned down, quiet ones up as far as the volume setting leaves room. Pure, unit-tested.
 */
object Loudness {
    /** Never turn a song down by more than this. */
    const val MAX_CUT_DB = 15f
    /** Never turn a song up by more than this (and never past full volume). */
    const val MAX_BOOST_DB = 6f

    /** The multiplier for the player's volume: 1 when the setting is off or the song has no gain. */
    fun factor(gainDb: Float, enabled: Boolean): Float {
        if (!enabled || gainDb.isNaN() || gainDb.isInfinite()) return 1f
        val db = gainDb.coerceIn(-MAX_CUT_DB, MAX_BOOST_DB)
        return 10.0.pow(db / 20.0).toFloat()
    }

    /** The volume to give the player: what the person set, times the fade and the levelling, never above full. */
    fun output(volume: Float, fade: Float, factor: Float): Float = (volume * fade * factor).coerceIn(0f, 1f)
}
