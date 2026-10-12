package com.ipodemu.input

import android.view.KeyEvent
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class MediaKeysTest {
    @Test fun playResumesOnlyWhenNothingPlays() {
        assertTrue(MediaKeys.shouldToggle(KeyEvent.KEYCODE_MEDIA_PLAY, wantsToPlay = false))
        assertFalse(MediaKeys.shouldToggle(KeyEvent.KEYCODE_MEDIA_PLAY, wantsToPlay = true))   // must not pause a song that plays
    }

    @Test fun pauseStopsOnlyWhatPlays() {
        assertTrue(MediaKeys.shouldToggle(KeyEvent.KEYCODE_MEDIA_PAUSE, wantsToPlay = true))
        assertFalse(MediaKeys.shouldToggle(KeyEvent.KEYCODE_MEDIA_PAUSE, wantsToPlay = false))  // must not start a paused song
    }

    @Test fun playPauseAndHeadsetHookAlwaysToggle() {
        for (w in listOf(true, false)) {
            assertTrue(MediaKeys.shouldToggle(KeyEvent.KEYCODE_MEDIA_PLAY_PAUSE, w))
            assertTrue(MediaKeys.shouldToggle(KeyEvent.KEYCODE_HEADSETHOOK, w))
        }
    }

    @Test fun otherKeysAreNotDecidedHere() {
        assertFalse(MediaKeys.shouldToggle(KeyEvent.KEYCODE_MEDIA_NEXT, false))
        assertFalse(MediaKeys.shouldToggle(KeyEvent.KEYCODE_VOLUME_UP, true))
        assertTrue(MediaKeys.handles(KeyEvent.KEYCODE_MEDIA_PLAY))
        assertTrue(MediaKeys.handles(KeyEvent.KEYCODE_MEDIA_PAUSE))
        assertFalse(MediaKeys.handles(KeyEvent.KEYCODE_MEDIA_PLAY_PAUSE))
    }
}
