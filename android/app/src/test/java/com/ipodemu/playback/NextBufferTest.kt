package com.ipodemu.playback

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class NextBufferTest {
    @Test fun the_start_is_thirty_seconds_of_the_song() {
        // a 5-minute, 30 MB song: 30 s is a tenth, plus a little
        val b = NextBuffer.headBytes(30_000_000, 300_000)
        assertTrue("$b", b in 3_000_000L..3_500_000L)
    }

    @Test fun a_short_song_is_buffered_whole() = assertEquals(900_000L, NextBuffer.headBytes(900_000, 20_000))

    @Test fun the_start_is_never_tiny_or_huge() {
        assertEquals(768L * 1024, NextBuffer.headBytes(1_000_000, 6_000_000))
        assertEquals(12L * 1024 * 1024, NextBuffer.headBytes(2_000_000_000, 600_000))
    }

    @Test fun an_unknown_size_gets_a_fixed_start() = assertEquals(3L * 1024 * 1024, NextBuffer.headBytes(0, 200_000))

    @Test fun what_has_played_or_was_skipped_past_is_freed_but_the_playing_and_next_songs_stay() {
        val buffered = listOf("a", "b", "c", "d", "e")
        // a played, b and c skipped past; d is playing, e is next
        assertEquals(listOf("a", "b", "c"), NextBuffer.stale(buffered, listOf("d", "e")))
    }

    @Test fun nothing_is_freed_when_everything_is_still_wanted() = assertTrue(NextBuffer.stale(listOf("a", "b"), listOf("b", "a", "c")).isEmpty())
}
