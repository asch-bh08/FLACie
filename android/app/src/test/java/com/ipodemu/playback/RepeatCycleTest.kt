package com.ipodemu.playback

import androidx.media3.common.Player
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** The Repeat button (Now Playing, the car): off, all, one, off; what the player is told; and Autoplay keeping out while it repeats. */
class RepeatCycleTest {
    @Test fun aTapGoesOffAllOneOff() {
        assertEquals(RepeatCycle.ALL, RepeatCycle.next(RepeatCycle.OFF))
        assertEquals(RepeatCycle.ONE, RepeatCycle.next(RepeatCycle.ALL))
        assertEquals(RepeatCycle.OFF, RepeatCycle.next(RepeatCycle.ONE))
        // a stored value from an older version that makes no sense goes back to off
        assertEquals(RepeatCycle.ALL, RepeatCycle.next(7))
    }

    @Test fun theSettingMapsToThePlayersModesAndBack() {
        // the stored numbers (1 all, 2 one) are not the player's (1 one, 2 all): mixing them up made "Repeat all" repeat one song
        assertEquals(Player.REPEAT_MODE_ALL, RepeatCycle.toPlayer(RepeatCycle.ALL))
        assertEquals(Player.REPEAT_MODE_ONE, RepeatCycle.toPlayer(RepeatCycle.ONE))
        assertEquals(Player.REPEAT_MODE_OFF, RepeatCycle.toPlayer(RepeatCycle.OFF))
        for (s in listOf(RepeatCycle.OFF, RepeatCycle.ALL, RepeatCycle.ONE)) assertEquals(s, RepeatCycle.fromPlayer(RepeatCycle.toPlayer(s)))
    }

    @Test fun autoplayAddsSongsOnlyWithRepeatOff() {
        assertTrue(RepeatCycle.autoplayMayAdd(RepeatCycle.OFF))
        assertFalse("repeat all: the playlist starts again instead", RepeatCycle.autoplayMayAdd(RepeatCycle.ALL))
        assertFalse(RepeatCycle.autoplayMayAdd(RepeatCycle.ONE))
    }

    @Test fun theButtonSaysWhatItDoes() {
        assertEquals("Repeat off", RepeatCycle.label(RepeatCycle.OFF))
        assertEquals("Repeat all", RepeatCycle.label(RepeatCycle.ALL))
        assertEquals("Repeat one", RepeatCycle.label(RepeatCycle.ONE))
    }

    @Test fun theNoteAfterATapSaysWhatHappensAtTheEnd() {
        assertEquals("Repeat all: the queue starts again from the first song", RepeatCycle.note(RepeatCycle.ALL, autoplay = true))
        assertEquals("Repeat one: this song again", RepeatCycle.note(RepeatCycle.ONE, autoplay = true))
        assertEquals("Repeat off: Autoplay carries on after the last song", RepeatCycle.note(RepeatCycle.OFF, autoplay = true))
        assertEquals("Repeat off: playback stops after the last song", RepeatCycle.note(RepeatCycle.OFF, autoplay = false))
    }
}
