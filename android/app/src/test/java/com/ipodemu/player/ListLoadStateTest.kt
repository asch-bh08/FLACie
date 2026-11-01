package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Test

/** An empty list while the library is still loading shows grey placeholder rows, not "No songs yet" (which said the library was empty when it
 *  wasn't loaded yet); a filter that matches nothing, or a library that really is empty, still says so. */
class ListLoadStateTest {
    @Test fun songs_show_as_soon_as_there_are_any() = assertEquals(ListLoad.LIST, ListLoad.of(empty = false, filtering = false, loaded = false))
    @Test fun nothing_yet_while_loading_is_placeholders() = assertEquals(ListLoad.PLACEHOLDERS, ListLoad.of(empty = true, filtering = false, loaded = false))
    @Test fun nothing_after_loading_is_empty() = assertEquals(ListLoad.EMPTY, ListLoad.of(empty = true, filtering = false, loaded = true))
    @Test fun a_filter_that_matches_nothing_says_so_even_while_loading() = assertEquals(ListLoad.EMPTY, ListLoad.of(empty = true, filtering = true, loaded = false))
}
