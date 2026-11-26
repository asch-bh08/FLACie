package com.ipodemu.playback

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class QueueSplitTest {
    @Test fun `a small queue is not split`() {
        assertNull(QueueSplit.plan(size = 300, index = 10, limit = 400))
    }

    @Test fun `a big queue gets a window around the chosen song`() {
        val p = QueueSplit.plan(size = 10_000, index = 5_000, limit = 400, behind = 50, ahead = 150)!!
        assertEquals(4_950, p.from); assertEquals(5_151, p.to); assertEquals(50, p.chosenInWindow)
    }

    @Test fun `the window stops at the ends of the list`() {
        val a = QueueSplit.plan(10_000, 3, 400, 50, 150)!!
        assertEquals(0, a.from); assertEquals(154, a.to); assertEquals(3, a.chosenInWindow)
        val b = QueueSplit.plan(10_000, 9_999, 400, 50, 150)!!
        assertEquals(9_949, b.from); assertEquals(10_000, b.to); assertEquals(50, b.chosenInWindow)
    }

    @Test fun `the rest goes round the window in order`() {
        val all = (0 until 20).toList()
        val p = QueueSplit.plan(20, 10, limit = 5, behind = 2, ahead = 3)!!
        val window = all.subList(p.from, p.to)
        assertEquals(listOf(8, 9, 10, 11, 12, 13), window)
        val m = QueueSplit.whole(window, before = all.subList(0, p.from), after = all.subList(p.to, all.size), windowSize = window.size)
        assertEquals(all, m)
    }

    @Test fun `songs added to the end while the rest was made stay after the rest`() {
        val all = (0 until 20).toList()
        val p = QueueSplit.plan(20, 10, 5, 2, 3)!!
        val now = all.subList(p.from, p.to) + listOf(100, 101)   // two added by the user in between
        val m = QueueSplit.whole(now, all.subList(0, p.from), all.subList(p.to, all.size), windowSize = p.to - p.from)
        assertEquals(all + listOf(100, 101), m)
    }
}
