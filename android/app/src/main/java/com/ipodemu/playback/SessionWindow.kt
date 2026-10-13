package com.ipodemu.playback

import androidx.media3.common.Player

/** What the media session (Bluetooth, Android Auto, lock screen, system media card) may ask the player: everything except the queue (timeline), see PlayerController.sessionPlayer. */
object SessionWindow {
    private val hidden = intArrayOf(Player.COMMAND_GET_TIMELINE)

    fun allowed(command: Int): Boolean = command !in hidden

    fun commands(all: Player.Commands): Player.Commands {
        val b = all.buildUpon()
        for (c in hidden) b.remove(c)
        return b.build()
    }
}
