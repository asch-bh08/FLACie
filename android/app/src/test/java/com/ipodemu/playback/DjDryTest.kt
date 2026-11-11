package com.ipodemu.playback

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** Owner, 11 Nov: "the DJ on a skip talks for about a second, stops, then the music carries on". A clip that stalls for a moment while the new song loads
 *  over the same connection was ended at once; now it is given [DjLine.DRY_GRACE_MS] to carry on before the line ends. */
class DjDryTest {
    @Test fun aShortStallDoesNotEndTheLine() {
        assertFalse(DjLine.dryTooLong(drySince = 10_000, now = 10_400))
        assertFalse(DjLine.dryTooLong(drySince = 10_000, now = 10_000 + DjLine.DRY_GRACE_MS - 1))
    }

    @Test fun aClipStillStuckAfterTheGraceEndsTheLine() = assertTrue(DjLine.dryTooLong(drySince = 10_000, now = 10_000 + DjLine.DRY_GRACE_MS))
}
