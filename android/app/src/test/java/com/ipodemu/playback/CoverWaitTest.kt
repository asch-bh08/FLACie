package com.ipodemu.playback

import kotlinx.coroutines.delay
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import java.io.File

/** The car showed blank squares for covers the phone had not saved yet: it asked once, was told "not there", and never asked again. A cover the phone
 * lacks is now fetched while the car waits, for a few seconds at most. */
class CoverWaitTest {
    private val f = File("cover.jpg")

    @Test fun a_saved_cover_is_handed_over_without_fetching() {
        var fetched = false
        assertEquals(f, CoverWait.file({ f }, { fetched = true }, 1000))
        assertEquals(false, fetched)
    }

    @Test fun a_missing_cover_is_fetched_then_handed_over() {
        var have: File? = null
        assertEquals(f, CoverWait.file({ have }, { have = f }, 1000))
    }

    @Test fun waiting_for_the_library_stops_when_it_is_in() {
        var n = 0
        val t0 = System.currentTimeMillis()
        assertEquals(true, CoverWait.until({ ++n >= 3 }, 2_000, 10))
        assertEquals(true, System.currentTimeMillis() - t0 < 1_000)
    }

    @Test fun waiting_for_the_library_gives_up() = assertEquals(false, CoverWait.until({ false }, 100, 10))

    @Test fun a_slow_fetch_gives_up_after_the_wait() {
        assertNull(CoverWait.file({ null }, { delay(5_000) }, 100))
    }
}
