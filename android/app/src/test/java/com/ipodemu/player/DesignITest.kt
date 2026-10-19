package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Test

/** The pure rules of design I outside the player: which words of a heading go in italics, and Home's greeting and headline. */
class DesignITest {
    @Test fun lastWordAloneMatchesTheSleeveSplit() {
        assertEquals("Lab " to "Album", splitLastWords("Lab Album", 1))
        assertEquals(SleeveText.split("Pick up where you left off"), splitLastWords("Pick up where you left off", 1))
    }

    @Test fun twoLastWordsForTheHomeHeadline() {
        assertEquals("Pick up where you " to "left off", splitLastWords("Pick up where you left off", 2))
    }

    @Test fun moreWordsThanTheTitleHasPutsItAllInItalics() {
        assertEquals("" to "Songs", splitLastWords("Songs", 2))
        assertEquals("" to "Your playlists", splitLastWords("Your playlists", 3))
    }

    @Test fun zeroOrTrailingSpacesStillGiveOneWord() {
        assertEquals("Daily " to "Mix", splitLastWords("Daily Mix  ", 0))
        assertEquals("" to "", splitLastWords("", 2))
    }

    @Test fun serifTitleKeepsTheText() {
        assertEquals("Pick up where you left off", serifTitle("Pick up where you left off", 2).text)
    }

    @Test fun greetingByHour() {
        assertEquals("Good morning", HomeWords.greeting(5))
        assertEquals("Good morning", HomeWords.greeting(11))
        assertEquals("Good afternoon", HomeWords.greeting(12))
        assertEquals("Good afternoon", HomeWords.greeting(17))
        assertEquals("Good evening", HomeWords.greeting(18))
        assertEquals("Good evening", HomeWords.greeting(2))
    }

    @Test fun headlineNeedsSomethingToPickUp() {
        assertEquals("Pick up where you left off", HomeWords.headline(true))
        assertEquals("Put something on", HomeWords.headline(false))
    }
}
