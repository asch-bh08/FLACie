package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/** The DJ bar above the mini player, as on the web: listening, what it heard, thinking; then its words come as captions. */
class DjStatusTest {
    @Test fun eachStageSaysWhatTheDjIsDoing() {
        val s = DjStatus()
        assertNull(s.text())
        s.listening(); assertEquals("Listening…", s.text())
        s.heard("play my favourite prodigy songs"); assertEquals("“play my favourite prodigy songs” · Thinking…", s.text())
        s.done(); assertNull(s.text())
    }

    @Test fun theLevelIsZeroToOneAndGoesBackToZero() {
        val s = DjStatus()
        s.listening(); s.level(10f); assertEquals(1f, s.level, 0f)
        s.level(-2f); assertEquals(0f, s.level, 0f)
        s.level(4f); assertEquals(0.5f, s.level, 0.01f)
        s.done(); assertEquals(0f, s.level, 0f)
    }
}
