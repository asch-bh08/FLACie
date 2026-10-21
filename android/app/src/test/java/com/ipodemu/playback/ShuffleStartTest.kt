package com.ipodemu.playback

import org.junit.Assert.assertEquals
import org.junit.Test

/** Turning shuffle on mid-song: the song playing goes first in the new order, so every other song is still to come (not just the few after it). */
class ShuffleStartTest {
    @Test fun the_current_song_leads_and_every_other_song_follows() {
        val order = ShuffleStart.order(current = 7, count = 10, random = kotlin.random.Random(1))
        assertEquals(7, order[0])
        assertEquals((0 until 10).toSet(), order.toSet())
        assertEquals(10, order.size)
    }
    @Test fun one_song_is_just_itself() = assertEquals(listOf(0), ShuffleStart.order(0, 1, kotlin.random.Random(1)).toList())
}
