package com.ipodemu.playback

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class QueueWindowTest {
    private fun seqNext(count: Int) = { i: Int -> if (i + 1 < count) i + 1 else QueueWindow.NONE }
    private fun seqPrev() = { i: Int -> if (i > 0) i - 1 else QueueWindow.NONE }

    @Test fun aSmallQueueIsShownWhole() {
        assertEquals(listOf(0, 1, 2, 3, 4), QueueWindow.indices(2, 5, seqNext(5), seqPrev()))
    }

    @Test fun aHugeQueueIsCutAroundTheCurrentSong() {
        val w = QueueWindow.indices(5000, 10_000, seqNext(10_000), seqPrev())
        assertEquals(QueueWindow.BEHIND + 1 + QueueWindow.AHEAD, w.size)
        assertEquals(5000 - QueueWindow.BEHIND, w.first()); assertEquals(5000 + QueueWindow.AHEAD, w.last())
        assertEquals(QueueWindow.BEHIND, QueueWindow.positionOf(w, 5000)); assertEquals(w, w.sorted())
    }

    @Test fun theEdgesAreShortNotEmpty() {
        assertEquals(1 + QueueWindow.AHEAD, QueueWindow.indices(0, 10_000, seqNext(10_000), seqPrev()).size)
        val end = QueueWindow.indices(9_999, 10_000, seqNext(10_000), seqPrev())
        assertEquals(QueueWindow.BEHIND + 1, end.size); assertEquals(9_999, end.last())
    }

    @Test fun aShuffledQueueFollowsPlayOrderNotListOrder() {
        val order = listOf(3, 0, 4, 1, 2)   // the play order of a 5-song queue
        val next = { i: Int -> order.indexOf(i).let { p -> if (p + 1 < order.size) order[p + 1] else QueueWindow.NONE } }
        val prev = { i: Int -> order.indexOf(i).let { p -> if (p > 0) order[p - 1] else QueueWindow.NONE } }
        assertEquals(order, QueueWindow.indices(4, 5, next, prev).let { it })
        assertEquals(listOf(0, 4, 1), QueueWindow.indices(4, 5, next, prev, behind = 1, ahead = 1))
    }

    @Test fun repeatAllDoesNotLoopForever() {
        val w = QueueWindow.indices(2, 5, { (it + 1) % 5 }, { (it + 4) % 5 })
        assertEquals(5, w.size); assertEquals(5, w.toSet().size); assertEquals(2, w[QueueWindow.positionOf(w, 2)])
    }

    @Test fun nothingOrOutOfRangeGivesAnEmptyWindow() {
        assertTrue(QueueWindow.indices(0, 0, seqNext(0), seqPrev()).isEmpty())
        assertTrue(QueueWindow.indices(-1, 5, seqNext(5), seqPrev()).isEmpty()); assertTrue(QueueWindow.indices(5, 5, seqNext(5), seqPrev()).isEmpty())
        assertEquals(QueueWindow.NONE, QueueWindow.positionOf(listOf(1, 2), 9))
    }
}
