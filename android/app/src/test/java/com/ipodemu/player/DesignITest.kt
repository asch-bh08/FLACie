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

    @Test fun aNegativeCountMeansNoItalics() = assertEquals("1,204" to "", splitLastWords("1,204", -1))

    @Test fun homeHeadlineHasFourItalicWords() = assertEquals("Pick up " to "where you left off", splitLastWords("Pick up where you left off", 4))

    @Test fun albumFactsLineAsTheWebWritesIt() {
        assertEquals("2020 · 8 songs · 16 min · FLAC / MP3", DetailFacts.line(2020, 8, 16 * 60_000L + 5_000, listOf("/a/1.flac", "/a/2.FLAC", "/a/3.mp3", "/a/4")))
        assertEquals("1 song", DetailFacts.line(0, 1, 0, emptyList()))
        assertEquals("3 songs · 9 min · AAC", DetailFacts.line(0, 3, 9 * 60_000L, listOf("x.m4a?static=true")))
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
