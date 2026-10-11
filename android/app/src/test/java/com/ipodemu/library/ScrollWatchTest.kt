package com.ipodemu.library

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** A cover request for a row that scrolled away is dropped; one for a row still drawn, or on a screen that is simply not redrawn, is kept. */
class ScrollWatchTest {
    @Test fun aRowStillBeingDrawnIsNotScrolledOff() {
        val w = ScrollWatch(after = 10)
        w.want("a", w.draw())
        repeat(100) { w.want("a", w.draw()) }   // drawn again every frame
        assertFalse(w.scrolledOff("a"))
    }

    @Test fun aRowThatIsNoLongerAskedForIsScrolledOffOnceNewerDrawsPileUp() {
        val w = ScrollWatch(after = 10)
        w.want("old", w.draw())
        repeat(10) { w.want("new$it", w.draw()) }
        assertFalse("not yet: ten draws only", w.scrolledOff("old"))
        w.want("new11", w.draw())
        assertTrue(w.scrolledOff("old"))
    }

    @Test fun aScreenThatIsNotRedrawnKeepsItsRequests() {
        val w = ScrollWatch(after = 10)
        repeat(8) { w.want("row$it", w.draw()) }
        // nothing is drawn for a long time (the user is reading): nothing counts as scrolled away
        repeat(5) { assertFalse(w.scrolledOff("row$it")) }
    }

    @Test fun aFinishedOrUnknownRequestCountsAsGone() {
        val w = ScrollWatch()
        assertTrue(w.scrolledOff("never asked"))
        w.want("a", w.draw()); w.done("a")
        assertTrue(w.scrolledOff("a"))
    }
}
