package com.ipodemu.playback

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class LiveAudioWatchTest {
    // owner's recording, 12 Nov: closing the full-screen graph stopped the Info card's graph behind it ("the graphs weren't loading")
    @Test fun aGraphClosingLeavesTheTapOnWhileAnotherIsShown() {
        val live = LiveAudio()
        live.watch(true)            // the Info card's graph
        live.watch(true)            // the full-screen one over it
        live.watch(false)           // full screen closed
        assertTrue(live.watching)
        live.watch(false)
        assertFalse(live.watching)
        live.watch(false)           // a stray extra close does not go below none
        live.watch(true)
        assertTrue(live.watching)
    }
}
