package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Test

/** Recently Played on the phone (and in the car) showed 0 for someone who listens on the web: the account's play history (kept by FLACie Web) never
 * reached the phone. Both lists now merge, newest first, one row per song. */
class HistorySyncTest {
    private fun h(p: String, w: Long, t: String = p, a: String = "Artist") = HistorySync.Entry(p, t, a, w)

    @Test fun both_lists_merge_newest_first() =
        assertEquals(listOf("web2", "phone", "web1"), HistorySync.merge(listOf(h("phone", 20)), listOf(h("web2", 30), h("web1", 10))).map { it.p })

    @Test fun a_song_played_in_both_places_is_listed_once_at_its_latest_play() {
        val m = HistorySync.merge(listOf(h("x", 50)), listOf(h("x", 10), h("y", 20)))
        assertEquals(listOf("x", "y"), m.map { it.p }); assertEquals(50L, m[0].w)
    }

    @Test fun the_same_song_under_another_address_is_listed_once() {
        val m = HistorySync.merge(listOf(h("https://jf/Audio/1/stream", 50, "Creep", "Radiohead")), listOf(h("jf:1", 40, "Creep", "Radiohead")))
        assertEquals(1, m.size)
    }

    @Test fun the_list_is_capped() = assertEquals(HistorySync.MAX, HistorySync.merge((1..300).map { h("p$it", it.toLong()) }, emptyList()).size)
}
