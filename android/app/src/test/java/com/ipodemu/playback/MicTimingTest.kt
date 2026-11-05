package com.ipodemu.playback

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/** The mic's press -> recogniser ready -> listening timestamps, as they reach logcat (tag FLACieMic). */
class MicTimingTest {
    @Test fun marksAreLoggedWithMillisecondsSinceThePress() {
        var t = 1_000L; val lines = mutableListOf<String>()
        val m = MicTiming({ t }, { lines += it })
        m.press(); t += 40; m.recogniserReady(); t += 260
        assertEquals(300L, m.listening())
        assertEquals(listOf("press", "recogniser ready +40 ms", "listening +300 ms (recogniser ready to listening 260 ms)"), lines)
    }

    @Test fun aSlowStartSaysItMissedTheAim() {
        var t = 0L; val lines = mutableListOf<String>()
        val m = MicTiming({ t }, { lines += it })
        m.press(); t += 3_000
        assertEquals(3_000L, m.listening())
        assertTrue(lines.last().contains("over the 500 ms aim"))
    }

    @Test fun nothingIsLoggedWithoutAPressAndEachPressIsTimedOnce() {
        var t = 0L; val lines = mutableListOf<String>()
        val m = MicTiming({ t }, { lines += it })
        m.recogniserReady(); assertEquals(-1L, m.listening()); assertTrue(lines.isEmpty())
        m.press(); t += 10; m.listening()
        assertEquals(-1L, m.listening())
    }
}
