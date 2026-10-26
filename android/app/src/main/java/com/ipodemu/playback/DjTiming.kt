package com.ipodemu.playback

import kotlin.math.min
import kotlin.math.roundToInt

/** When the DJ's clip starts, as a place in the song ([clipAtMs]), and how long the song is held back under it ([songDelayMs]). (0, 0): together, as before. */
data class DjTimingPlan(val clipAtMs: Int, val songDelayMs: Int) {
    companion object { val NOW = DjTimingPlan(0, 0) }
}

/**
 * Times the DJ like a radio presenter: talk over the song's intro and finish before the singing (`DjTiming` in FLACie.Core, with the same tests; keep the two
 * the same). Pure, unit-tested.
 * - Intro ([DjCue.introMs], from the server) or clip length not known: the clip starts with the song, as before.
 * - The clip fits the intro with [MARGIN_MS] to spare: it starts so it ends [MARGIN_MS] before the vocals (the music comes back up in that time).
 * - The clip is longer: it starts with the song, and the song is held back (silent, then from its start) just enough for the line to end [MARGIN_MS] before
 *   the vocals, never more than [MAX_SONG_DELAY_MS]. The clip is never cut off.
 */
object DjTiming {
    const val MARGIN_MS = 500
    const val MAX_SONG_DELAY_MS = 1_500
    /** The voice's speaking rate, for a clip whose length is not known yet (not measured: the server's cost estimate uses the same). */
    const val WORDS_PER_S = 2.6
    /** The quiet at the two ends of a clip. */
    const val CLIP_PAD_MS = 300

    fun plan(introMs: Int?, clipMs: Int?): DjTimingPlan {
        if (introMs == null || clipMs == null || introMs < 0 || clipMs <= 0) return DjTimingPlan.NOW
        if (clipMs + MARGIN_MS <= introMs) return DjTimingPlan(introMs - MARGIN_MS - clipMs, 0)
        return DjTimingPlan(0, min(MAX_SONG_DELAY_MS, clipMs + MARGIN_MS - introMs))
    }

    /** How long [text] takes to say, roughly (words split on spaces, as the server counts them). */
    fun estimateMs(text: String): Int = (text.split(' ').count { it.isNotEmpty() } / WORDS_PER_S * 1000).roundToInt() + CLIP_PAD_MS
}
