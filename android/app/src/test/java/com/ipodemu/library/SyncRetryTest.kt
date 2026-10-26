package com.ipodemu.library

import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test
import java.io.IOException

/** The sync at app start used to give up after one failure (the phone busy fetching covers turned it away), leaving Playlists and
 * Favourites at 0 until Sync now was pressed by hand. It now tries again after a pause. */
class SyncRetryTest {
    @Test fun a_failure_then_success_is_retried() = runBlocking {
        var tries = 0
        SyncRetry.attempt(listOf(0L, 0L)) { tries++; if (tries == 1) throw IOException("busy") }
        assertEquals(2, tries)
    }

    @Test fun success_first_time_runs_once() = runBlocking {
        var tries = 0
        SyncRetry.attempt(listOf(0L, 0L)) { tries++ }
        assertEquals(1, tries)
    }

    @Test fun gives_up_after_every_pause_with_the_last_error() {
        var tries = 0
        assertThrows(IOException::class.java) { runBlocking { SyncRetry.attempt(listOf(0L, 0L)) { tries++; throw IOException("down") } } }
        assertEquals(3, tries)
    }

    /** An outage longer than the pauses (Jellyfin's address gone for an hour) left Playlists empty until Sync now: with [thenEvery] it keeps going. */
    @Test fun keeps_trying_after_the_pauses_until_it_works() = runBlocking {
        var tries = 0
        SyncRetry.attempt(listOf(0L, 0L), thenEvery = 0L) { tries++; if (tries < 6) throw IOException("down") }
        assertEquals(6, tries)
    }
}
