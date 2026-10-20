package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Test

/** Saving the queue names the new playlist after where it plays from, never the same name as one already there. */
class QueuePlaylistNameTest {
    @Test fun named_after_where_it_plays_from() = assertEquals("Lab Album (queue)", queuePlaylistName("Lab Album", emptyList()))
    @Test fun no_context_is_my_queue() = assertEquals("My queue (queue)", queuePlaylistName(null, emptyList()))
    @Test fun a_taken_name_gets_a_number() = assertEquals("Lab Album (queue) 3", queuePlaylistName("Lab Album", listOf("lab album (queue)", "Lab Album (queue) 2")))
}
