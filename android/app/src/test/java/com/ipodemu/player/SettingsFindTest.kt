package com.ipodemu.player

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** Find a setting (owner, 25 Nov: the web has it, the phone did not): every word typed must be in the row's name, description, value or choices. */
class SettingsFindTest {
    @Test fun matchesAnyPartOfTheRow() {
        assertTrue(setMatches("", "Fade between songs"))
        assertTrue(setMatches("fade", "Fade between songs", "The end of a song fades into the next"))
        assertTrue(setMatches("data saver", "Streaming quality", null, null, "Automatic Always full Data saver"))
        assertTrue(setMatches("SLEEP", "Sleep timer"))
        assertFalse(setMatches("fade lyrics", "Fade between songs"))
        assertFalse(setMatches("equaliser", "Equalizer"))
    }
}
