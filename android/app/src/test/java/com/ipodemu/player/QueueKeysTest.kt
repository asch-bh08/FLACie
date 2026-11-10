package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Test

/** Up next rows are keyed by the song, not its place, so a moved song keeps its key and glides to its new place (owner, 9 Nov: rows that reorder smoothly). */
class QueueKeysTest {
    @Test fun each_song_keeps_its_key_when_moved() {
        val before = QueueKeys.of(listOf("a", "b", "c"))
        val after = QueueKeys.of(listOf("a", "c", "b"))
        assertEquals(before[1], after[2]); assertEquals(before[2], after[1])
    }
    @Test fun the_same_song_twice_gets_two_keys() = assertEquals(listOf("a#0", "b#0", "a#1"), QueueKeys.of(listOf("a", "b", "a")))
}
