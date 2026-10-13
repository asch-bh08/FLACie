package com.ipodemu.playback

import androidx.media3.common.Player
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** The media session never gets the queue, but keeps every control a head unit or the lock screen needs. (Player.Commands needs Android's SparseBooleanArray: that side is checked on the emulator with dumpsys media_session.) */
class SessionWindowTest {
    @Test fun timelineIsHidden() {
        assertFalse(SessionWindow.allowed(Player.COMMAND_GET_TIMELINE))
    }

    @Test fun controlsStay() {
        for (cmd in listOf(Player.COMMAND_PLAY_PAUSE, Player.COMMAND_SEEK_TO_NEXT, Player.COMMAND_SEEK_TO_PREVIOUS, Player.COMMAND_SEEK_IN_CURRENT_MEDIA_ITEM,
            Player.COMMAND_GET_METADATA, Player.COMMAND_GET_CURRENT_MEDIA_ITEM, Player.COMMAND_SET_SHUFFLE_MODE, Player.COMMAND_SET_REPEAT_MODE, Player.COMMAND_SET_SPEED_AND_PITCH)) {
            assertTrue("command $cmd", SessionWindow.allowed(cmd))
        }
    }
}
