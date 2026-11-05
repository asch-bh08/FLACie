package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class PlayCountsTest {
    private val now = 1_800_000_000_000L
    private val day = 86_400_000L
    private fun play(key: String, heard: Double, ago: Int = 1, length: Double = 200.0) = PlayRecord(key, heard, length, now - ago * day)
    private fun full(key: String, n: Int, ago: Int = 1) = (1..n).map { play(key, 200.0, ago) }

    @Test fun threeRealPlaysShow() = assertEquals(listOf("a"), PlayCounts.rank(full("a", 3), now))

    @Test fun twoRealPlaysDoNotShow() = assertTrue(PlayCounts.rank(full("a", 2), now).isEmpty())

    @Test fun sixtyPercentIsReal() {
        assertTrue(PlayCounts.isReal(120.0, 200.0)); assertTrue(!PlayCounts.isReal(119.0, 200.0))
    }

    @Test fun fourMinutesIsRealForALongSong() = assertTrue(PlayCounts.isReal(240.0, 900.0))

    @Test fun aShortSongHeardInFullIsNotASkip() {
        assertTrue(PlayCounts.isReal(20.0, 25.0)); assertTrue(!PlayCounts.isSkip(20.0, 25.0))
    }

    @Test fun oneFullPlayThenAFortyPercentSkipDoesNotShow() {
        assertTrue(PlayCounts.rank(full("a", 1) + play("a", 80.0), now).isEmpty())
    }

    @Test fun aSkipUnderThirtySecondsCountsAgainst() {
        // three real plays and three early skips cancel out: not shown
        assertTrue(PlayCounts.rank(full("a", 3) + (1..3).map { play("a", 5.0) }, now).isEmpty())
        // one skip only takes a little off, and the song still shows
        assertEquals(listOf("a"), PlayCounts.rank(full("a", 4) + play("a", 5.0), now))
    }

    @Test fun aPlayBetweenSkipAndRealCountsForNothing() =
        assertEquals(listOf("a"), PlayCounts.rank(full("a", 3) + (1..5).map { play("a", 60.0) }, now))

    @Test fun recentPlaysOutweighOldOnes() =
        assertEquals(listOf("fresh", "old"), PlayCounts.rank(full("old", 5, ago = 90) + full("fresh", 3, ago = 1), now))

    @Test fun halfLifeIsThirtyDays() =
        // 4 plays 30 days old weigh 2.0; 3 plays today weigh 3.0
        assertEquals(listOf("b", "a"), PlayCounts.rank(full("a", 4, ago = 30) + full("b", 3, ago = 0), now))

    @Test fun rankedHighestFirstOneCopyEach() =
        assertEquals(listOf("b", "a"), PlayCounts.rank(full("a", 3) + full("b", 6) + full("a", 1), now))
}
