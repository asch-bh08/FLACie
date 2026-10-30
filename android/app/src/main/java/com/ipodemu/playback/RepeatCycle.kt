package com.ipodemu.playback

import androidx.media3.common.Player

/**
 * The Repeat button (Now Playing, the car, the iPod menu): each tap goes off, all, one, off. The setting is stored as Prefs.repeat (0 off, 1 all,
 * 2 one), which is NOT the player's numbering (Player: 1 one, 2 all), so every place goes through here. With Repeat all the queue starts again at its
 * first song when it ends, and Autoplay adds nothing; with Repeat off Autoplay carries on after the end as before (the owner heard "random songs"
 * after a playlist in the car, 30 Oct, and wanted the playlist to start again instead).
 */
object RepeatCycle {
    const val OFF = 0
    const val ALL = 1
    const val ONE = 2

    fun next(setting: Int): Int = when (setting) { ALL -> ONE; ONE -> OFF; else -> ALL }

    fun toPlayer(setting: Int): Int = when (setting) { ALL -> Player.REPEAT_MODE_ALL; ONE -> Player.REPEAT_MODE_ONE; else -> Player.REPEAT_MODE_OFF }

    fun fromPlayer(mode: Int): Int = when (mode) { Player.REPEAT_MODE_ALL -> ALL; Player.REPEAT_MODE_ONE -> ONE; else -> OFF }

    /** Autoplay fills the queue only when nothing repeats. */
    fun autoplayMayAdd(setting: Int): Boolean = setting == OFF

    fun label(setting: Int): String = when (setting) { ALL -> "Repeat all"; ONE -> "Repeat one"; else -> "Repeat off" }

    /** The short note shown after a tap on the phone: what happens when the queue ends ([autoplay]: Settings' Autoplay switch). */
    fun note(setting: Int, autoplay: Boolean): String = when (setting) {
        ALL -> "Repeat all: the queue starts again from the first song"
        ONE -> "Repeat one: this song again"
        else -> if (autoplay) "Repeat off: Autoplay carries on after the last song" else "Repeat off: playback stops after the last song"
    }
}
