package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Test

/** Recent searches, by the web's rules (UserSession.AddRecentSearch): newest first, each once, the half-typed steps dropped, ten kept. */
class RecentSearchesTest {
    @Test fun newest_first_each_once() = assertEquals(listOf("Radiohead", "bjork"), RecentSearches.add(RecentSearches.add(listOf("radiohead"), "bjork"), "Radiohead"))
    @Test fun replaces_the_steps_to_it() = assertEquals(listOf("beatles"), RecentSearches.add(listOf("beat"), "beatles"))
    @Test fun keeps_ten_and_ignores_one_letter() {
        var l = emptyList<String>()
        for (i in 0 until 14) l = RecentSearches.add(l, "song $i")
        assertEquals(10, l.size); assertEquals("song 13", l[0])
        assertEquals(l, RecentSearches.add(l, "a"))
    }
}
