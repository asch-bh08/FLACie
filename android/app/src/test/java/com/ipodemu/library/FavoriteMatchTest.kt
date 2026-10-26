package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/** A favourite made on the web or another phone is kept under that device's path; Favourites lists this phone's copy (matched by title and
 * artist), but its heart looked only for this phone's path, so 3 of 4 favourites showed no heart (and tapping it added a second favourite). */
class FavoriteMatchTest {
    private val webPath = "https://hms-jellyfin.tailb05910.ts.net/Audio/abc/stream?static=true"
    private val meta = mapOf(webPath to ("Chandelier" to "Sia"))

    @Test fun the_same_path_is_its_own_favourite() {
        assertEquals("/m/a.flac", FavoriteMatch.storedFor("/m/a.flac", matchKey("A", "B"), listOf("/m/a.flac"), emptyMap()))
    }

    @Test fun another_devices_copy_of_the_song_counts() {
        assertEquals(webPath, FavoriteMatch.storedFor("/m/Sia - Chandelier.flac", matchKey("Chandelier", "Sia"), listOf(webPath), meta))
    }

    @Test fun a_different_song_does_not() {
        assertNull(FavoriteMatch.storedFor("/m/x.flac", matchKey("Elastic Heart", "Sia"), listOf(webPath), meta))
    }
}
