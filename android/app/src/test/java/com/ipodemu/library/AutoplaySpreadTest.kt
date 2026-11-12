package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Test

/** Same rules as the web's AutoplaySpread (owner, 12 Nov): one version of a song, at most two more by the artist asked for, no artist twice in a row. */
class AutoplaySpreadTest {
    @Test fun otherVersionsAreTheSameSong() {
        val k = songKey("Dreamer", "Livin' Joy")
        assertEquals(k, songKey("Dreamer - Re-Original 7-inch Mix", "Livin' Joy"))
        assertEquals(k, songKey("Dreamer (Janice Robinson Vocal)", "Livin' Joy"))
        assertNotEquals(k, songKey("Dreamer", "Prince"))
        assertNotEquals(k, songKey("Dreamers", "Livin' Joy"))
    }

    @Test fun atMostTwoMoreByTheSeedArtist() {
        val next = AutoplaySpread.spread(listOf("Livin' Joy", "Livin' Joy", "Livin' Joy", "Haddaway", "Livin' Joy", "Corona"), { it }, "Livin' Joy")
        assertEquals(2, next.count { it == "Livin' Joy" })
        for (i in 1 until next.size) assertNotEquals(next[i - 1], next[i])
    }

    @Test fun lovedSongsDoNotCount() {
        val songs = listOf("X" to true, "Y" to false, "X" to true, "Z" to false, "X" to false, "W" to false, "X" to false, "X" to false)
        val next = AutoplaySpread.spread(songs, { it.first }, "X", { it.second })
        assertEquals(2, next.count { it.first == "X" && it.second })
        assertEquals(2, next.count { it.first == "X" && !it.second })
    }

    @Test fun autoplayKeepsToWhatWasChosen() {
        val q = (1..30).map { "p$it" } + listOf("auto1", "auto2", "auto3")
        val isAuto = { x: String -> x.startsWith("auto") }
        val seeds = AutoplaySpread.seeds(q, 32, isAuto)
        assertEquals(8, seeds.size); assertEquals("p30", seeds.last()); assertEquals(0, seeds.count(isAuto))
        assertEquals("p10", AutoplaySpread.lead(q, 9, isAuto))
        assertNotEquals(AutoplaySpread.lead(q, 31, isAuto), AutoplaySpread.lead(q, 32, isAuto))
        assertEquals("dreamer", AutoplaySpread.lead(listOf("dreamer", "auto1", "auto2"), 2, isAuto))
    }
}
