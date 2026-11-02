package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** Owner, 2 Nov: the DJ volume and music-under-the-DJ bars were glitchy. The finger and the knob agree, in steps of 5, and an old save's answer never moves it. */
class LevelBarTest {
    // a 300 px bar whose knob (radius 10) travels from 10 to 290
    @Test fun theFingerAndTheKnobAgreeEndToEnd() {
        assertEquals(0, LevelBar.at(10f, 300f, 10f)); assertEquals(0, LevelBar.at(0f, 300f, 10f))
        assertEquals(100, LevelBar.at(290f, 300f, 10f)); assertEquals(100, LevelBar.at(300f, 300f, 10f))
        assertEquals(50, LevelBar.at(150f, 300f, 10f))
        assertEquals(25, LevelBar.at(80f, 300f, 10f))   // 70 of 280 = 25 %
    }
    @Test fun theKnobSitsWhereTheFingerPutsIt() {
        for (v in 0..100 step 5) assertEquals(v, LevelBar.at(LevelBar.knobX(v, 300f, 10f), 300f, 10f))
    }
    @Test fun aZeroWidthBarDoesNotBreak() = assertEquals(0, LevelBar.at(5f, 0f, 10f))

    @Test fun onlyTheLatestSaveIsBelieved() {
        val s = LevelBar.Saves()
        val first = s.next(); val second = s.next()
        assertFalse("an older answer arriving late is ignored", s.isLatest(first))
        assertTrue(s.isLatest(second))
    }
}
