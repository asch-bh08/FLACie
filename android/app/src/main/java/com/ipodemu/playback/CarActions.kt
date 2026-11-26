package com.ipodemu.playback

import com.ipodemu.library.LyricLine

/**
 * The car side of the media session, pure and unit-tested ([PlaybackService] and [SessionPlayer] apply it). Android Auto draws the screen itself; an app chooses only
 * its custom buttons (which, in what order), the places it reserves for skip, the browse tree and the song's text.
 */
object CarActions {
    enum class Id { TALK, FAVORITE, SHUFFLE, DJ, REPEAT, SPEED }

    /** At most this many custom buttons: the car shows the first few on its bar and the rest under its "more" button. */
    const val MAX = 5

    /**
     * The custom buttons in order, the most used first (owner, 26 Nov: "Android Auto maximum improvement"). In the car with the DJ: the mic (next to the playback
     * controls), the heart, shuffle, DJ on/off and repeat. In the car without the DJ (no FLACie Web): the heart, shuffle, repeat and, for long items, speed.
     * Without a car (the notification): repeat, heart, shuffle and speed for long items.
     */
    fun order(car: Boolean, carDj: Boolean, speedButton: Boolean): List<Id> = when {
        car && carDj -> listOf(Id.TALK, Id.FAVORITE, Id.SHUFFLE, Id.DJ, Id.REPEAT)
        car -> listOfNotNull(Id.FAVORITE, Id.SHUFFLE, Id.REPEAT, if (speedButton) Id.SPEED else null)
        else -> listOfNotNull(Id.REPEAT, Id.FAVORITE, Id.SHUFFLE, if (speedButton) Id.SPEED else null)
    }.take(MAX)

    // the names say the state (the car reads them out and shows them on a long press), the icons change with it
    fun shuffleName(on: Boolean) = if (on) "Shuffle: on" else "Shuffle: off"
    fun repeatName(setting: Int) = "Repeat: " + when (setting) { RepeatCycle.ALL -> "all"; RepeatCycle.ONE -> "one"; else -> "off" }
    fun favouriteName(on: Boolean) = if (on) "Favourite: on" else "Favourite: off"
    fun djName(on: Boolean) = if (on) "DJ: on" else "DJ: off"
    fun shuffleIcon(on: Boolean) = if (on) "ic_car_shuffle_on" else "ic_car_shuffle_off"
    fun repeatIcon(setting: Int) = when (setting) { RepeatCycle.ALL -> "ic_car_repeat"; RepeatCycle.ONE -> "ic_car_repeat_one"; else -> "ic_car_repeat_off" }
    fun favouriteIcon(on: Boolean) = if (on) "ic_car_heart_on" else "ic_car_heart"
    fun djIcon(on: Boolean) = if (on) "ic_car_dj_on" else "ic_car_dj_off"

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
