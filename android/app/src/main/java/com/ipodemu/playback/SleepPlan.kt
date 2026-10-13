package com.ipodemu.playback

import com.ipodemu.library.Track

/** What the sleep timer waits for. */
enum class SleepMode { OFF, MINUTES, END_OF_TRACK, END_OF_ALBUM }

/** The sleep timer's arithmetic, apart from the player: how long is left, how loud the fade-out is, whether an album ends here, how a shake or a tap extends it. */
object SleepPlan {
    /** The volume falls to silence over this long before the timer fires. */
    const val FADE_MS = 30_000L
    /** A tap or a shake adds this many minutes. */
    const val EXTEND_MIN = 15
    /** The shake and the extend prompt are only offered in the last stretch, so a pocket jolt hours before sleep does nothing. */
    const val EXTEND_WINDOW_MS = 120_000L

    /** Gain 1..0 for the time left: full until the fade starts, then a smooth (cosine) fall, never below 0. */
    fun fadeGain(remainingMs: Long, fadeMs: Long = FADE_MS): Float {
        if (fadeMs <= 0L || remainingMs >= fadeMs) return 1f
        if (remainingMs <= 0L) return 0f
        val x = remainingMs.toDouble() / fadeMs
        return (0.5 - 0.5 * Math.cos(Math.PI * x)).toFloat().coerceIn(0f, 1f)
    }

    /** Time left in the song, or null when the length is unknown. */
    fun trackRemaining(positionMs: Long, durationMs: Long): Long? = if (durationMs <= 0L) null else (durationMs - positionMs).coerceAtLeast(0L)

    private fun norm(s: String) = s.trim().lowercase()
    private fun albumKey(t: Track) = norm(t.albumArtist.ifBlank { t.artist }) + "\u0000" + norm(t.album)

    /** True when the song at [index] is the last of its album in the queue: the next one is another album, or there is none. */
    fun albumEndsAfter(queue: List<Track>, index: Int): Boolean {
        val cur = queue.getOrNull(index) ?: return true
        val next = queue.getOrNull(index + 1) ?: return true
        if (norm(cur.album).isEmpty()) return true   // no album name: every song is its own "album"
        return albumKey(cur) != albumKey(next)
    }

    /** Time left until the end of the album: the rest of this song plus the following songs of the same album. */
    fun albumRemaining(queue: List<Track>, index: Int, positionMs: Long): Long? {
        val cur = queue.getOrNull(index) ?: return null
        var left = trackRemaining(positionMs, cur.durationMs) ?: return null
        var i = index
        while (!albumEndsAfter(queue, i)) { i++; val d = queue[i].durationMs; if (d <= 0L) return null; left += d }
        return left
    }

    /** The timer with [addMin] more minutes: counted from what is left (or from now when it had fired), never shorter than before. */
    fun extend(endsAtMs: Long, nowMs: Long, addMin: Int = EXTEND_MIN): Long = maxOf(endsAtMs, nowMs) + addMin * 60_000L

    /** Whether the extend offer applies: only a running timer, in its last [EXTEND_WINDOW_MS]. */
    fun canExtend(mode: SleepMode, remainingMs: Long?): Boolean = mode != SleepMode.OFF && remainingMs != null && remainingMs <= EXTEND_WINDOW_MS

    /** "End of this song" etc. for the chip and the sheet. */
    fun label(mode: SleepMode): String = when (mode) {
        SleepMode.OFF -> "Off"; SleepMode.MINUTES -> "Timer"; SleepMode.END_OF_TRACK -> "End of this song"; SleepMode.END_OF_ALBUM -> "End of this album"
    }
}

/**
 * Recognises a shake from accelerometer samples (m/s^2, gravity included): [REQUIRED] strong jolts within [WINDOW_MS], each at least [GAP_MS] apart,
 * then a quiet period of [COOLDOWN_MS] so one shake counts once. Pure: the caller feeds samples.
 */
class ShakeDetector(private val onShake: () -> Unit) {
    private var first = 0L; private var last = 0L; private var count = 0; private var quietUntil = 0L

    fun onSample(x: Float, y: Float, z: Float, nowMs: Long) {
        if (nowMs < quietUntil) return
        val g = Math.sqrt((x * x + y * y + z * z).toDouble()) / 9.80665
        if (g < THRESHOLD_G) return
        if (count > 0 && nowMs - first > WINDOW_MS) { count = 0 }
        if (count > 0 && nowMs - last < GAP_MS) return
        if (count == 0) first = nowMs
        last = nowMs; count++
        if (count >= REQUIRED) { count = 0; quietUntil = nowMs + COOLDOWN_MS; onShake() }
    }

    companion object { const val THRESHOLD_G = 2.2; const val REQUIRED = 3; const val WINDOW_MS = 1_200L; const val GAP_MS = 120L; const val COOLDOWN_MS = 2_000L }
}
