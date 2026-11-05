package com.ipodemu.library

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** Owner, 5 Nov: a playlist change on the web is noticed by its stamp, once, and fetched again if the fetch did not finish. */
class PlaylistWatchTest {
    @Test fun theFirstStampIsOnlyRemembered() { assertFalse(PlaylistWatch().changed("a1")) }

    @Test fun aNewStampIsAChangeUntilItIsTaken() {
        val w = PlaylistWatch(); w.changed("a1")
        assertTrue(w.changed("b2"))
        assertTrue(w.changed("b2"))   // the fetch had not finished: still a change
        w.taken("b2")
        assertFalse(w.changed("b2"))
    }

    @Test fun aBlankStampFromAnOldServerIsNeverAChange() {
        val w = PlaylistWatch(); w.changed("a1")
        assertFalse(w.changed(""))
    }
}
