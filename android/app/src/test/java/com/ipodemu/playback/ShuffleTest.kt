package com.ipodemu.playback

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import kotlin.random.Random

class ShuffleTest {
    @Test fun on_keeps_the_songs_up_to_the_current_one_in_place_and_shuffles_the_rest() {
        val o = QueueShuffle.on(20, 5, Random(1))
        assertEquals(listOf(0, 1, 2, 3, 4, 5), o.take(6))
        assertEquals((6 until 20).toList(), o.drop(6).sorted())
        assertNotEquals((6 until 20).toList(), o.drop(6))
        assertTrue(QueueEdit.isPermutation(o, 20))
    }

    @Test fun on_with_nothing_playing_shuffles_everything() {
        val o = QueueShuffle.on(30, -1, Random(2))
        assertEquals((0 until 30).toList(), o.sorted())
        assertNotEquals((0 until 30).toList(), o.toList())
    }

    @Test fun on_at_the_last_or_only_song_changes_nothing() {
        assertArrayEquals(intArrayOf(0, 1, 2), QueueShuffle.on(3, 2, Random(3)))
        assertArrayEquals(intArrayOf(0), QueueShuffle.on(1, 0, Random(3)))
        assertArrayEquals(intArrayOf(), QueueShuffle.on(0, -1, Random(3)))
    }

    @Test fun off_restores_the_order_and_finds_the_playing_song() {
        val o = QueueShuffle.on(10, 3, Random(4))
        val (order, pos) = QueueShuffle.off(o, 7)
        assertArrayEquals(IntArray(10) { it }, order)
        assertEquals(o[7], pos)
    }

    @Test fun off_keeps_the_current_song_when_it_is_in_the_unshuffled_part() {
        val o = QueueShuffle.on(10, 3, Random(5))
        assertEquals(3, QueueShuffle.off(o, 3).second)
        assertEquals(-1, QueueShuffle.off(o, 10).second)
        assertEquals(-1, QueueShuffle.off(o, -1).second)
    }
}
