package com.ipodemu.playback

import com.ipodemu.library.LyricLine

/**
 * The car side of the media session, pure and unit-tested ([PlaybackService] and [SessionPlayer] apply it). Android Auto draws the screen itself; an app chooses only
 * its custom buttons (which, in what order), the places it reserves for skip, the browse tree and the song's text.
 */
object CarActions {
    enum class Id { TALK, FAVORITE, SHUFFLE, DJ, REPEAT, SPEED }

    /** The car shows this many custom buttons at most. */
    const val MAX = 4

    /**
     * The custom buttons in order. With the DJ in the car: the mic first (so it sits next to the playback controls), then the heart, shuffle and DJ on/off.
     * Without it (no car, or no FLACie Web): repeat, heart, shuffle and, for long items, speed. Never more than [MAX].
     */
    fun order(carDj: Boolean, speedButton: Boolean): List<Id> =
        (if (carDj) listOf(Id.TALK, Id.FAVORITE, Id.SHUFFLE, Id.DJ) else listOfNotNull(Id.REPEAT, Id.FAVORITE, Id.SHUFFLE, if (speedButton) Id.SPEED else null)).take(MAX)

    /** Android Auto's "always reserve space" extras: with them the skip previous and next slots stay where they are even when a song can't skip. */
    val SLOT_RESERVATION_KEYS = listOf(
        "android.media.playback.ALWAYS_RESERVE_SPACE_FOR.ACTION_SKIP_TO_PREVIOUS",
        "android.media.playback.ALWAYS_RESERVE_SPACE_FOR.ACTION_SKIP_TO_NEXT",
    )

    /** The DJ's caption changes the artist line at most this often. */
    const val CAPTION_GAP_MS = 2_000L

    /**
     * Lets a changing line (the DJ's caption) through at most once per gap. [show] says what to display now; when it holds back a change, [waitMs] says how
     * long until it is allowed, so the caller asks again then.
     */
    class Gate(private val gapMs: Long = CAPTION_GAP_MS) {
        private var shown: String? = null
        private var changedAt = Long.MIN_VALUE / 2

        fun show(want: String?, now: Long): String? {
            if (want != shown && now - changedAt >= gapMs) { shown = want; changedAt = now }
            return shown
        }

        /** 0 when [want] is on show; else how long until the gate lets it through. */
        fun waitMs(want: String?, now: Long): Long = if (want == shown) 0L else (changedAt + gapMs - now).coerceAtLeast(1L)

        fun reset() { shown = null; changedAt = Long.MIN_VALUE / 2 }
    }

    /** The line that replaces the artist: the DJ's caption while it talks, else (when "Lyrics in the car" is on) the song's current lyric line, else null (the artist stays). */
    fun artistLine(caption: String?, lyric: String?, lyricsInCar: Boolean): String? =
        caption?.takeIf { it.isNotBlank() } ?: lyric?.takeIf { lyricsInCar && it.isNotBlank() }

    /** The synced lyric line at [posMs] (the same quarter-second lead the lyrics page uses), or null before the first line or for plain lyrics. */
    fun lyricAt(lines: List<LyricLine>, synced: Boolean, posMs: Long): String? {
        if (!synced) return null
        val i = lines.indexOfLast { it.timeMs <= posMs + 250 }
        return if (i < 0) null else lines[i].text.trim()
    }

    /** How long until the next lyric line is due ([speed]-adjusted), or null when there is none. */
    fun untilNextLine(lines: List<LyricLine>, synced: Boolean, posMs: Long, speed: Float = 1f): Long? {
        if (!synced) return null
        val next = lines.firstOrNull { it.timeMs > posMs + 250 } ?: return null
        return (((next.timeMs - 250 - posMs) / speed.coerceAtLeast(0.1f)).toLong()).coerceAtLeast(50L)
    }
}
