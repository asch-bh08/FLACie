package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Test

class SleepTimerTest {
    @Test fun minutesRoundUp() {
        assertEquals("15 min left", formatSleepLeft(15 * 60_000L))
        assertEquals("15 min left", formatSleepLeft(14 * 60_000L + 1L))
        assertEquals("2 min left", formatSleepLeft(61_000L))
    }

    @Test fun lastMinuteShowsSeconds() {
        assertEquals("59 s left", formatSleepLeft(59_000L))
        assertEquals("1 s left", formatSleepLeft(1L))
        assertEquals("60 s left".replace("60 s", "1 min"), formatSleepLeft(60_000L))
    }

    @Test fun neverNegative() {
        assertEquals("0 s left", formatSleepLeft(-5L))
    }
}
