package com.ipodemu.playback

import com.ipodemu.playback.QueueEdit.Step
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class QueueEditTest {
    private fun ids(n: Int) = List(n) { it * 10 }   // songs are told apart by value, not by index

    // ---- move ------------------------------------------------------------------------------------------------------------------

    @Test fun movingTheCurrentSongCarriesTheCurrentIndexWithIt() {
        val e = QueueEdit.move(ids(5), current = 1, from = 1, to = 3)
        assertEquals(listOf(0, 20, 30, 10, 40), e.items)
        assertEquals(3, e.current)
        assertEquals(10, e.items[e.current])
    }

    @Test fun movingASongFromBeforeToAfterTheCurrentOneShiftsTheIndexDown() {
        val e = QueueEdit.move(ids(5), current = 2, from = 0, to = 4)
        assertEquals(listOf(10, 20, 30, 40, 0), e.items)
        assertEquals(1, e.current); assertEquals(20, e.items[e.current])
    }

    @Test fun movingASongFromAfterToBeforeTheCurrentOneShiftsTheIndexUp() {
        val e = QueueEdit.move(ids(5), current = 2, from = 4, to = 0)
        assertEquals(listOf(40, 0, 10, 20, 30), e.items)
        assertEquals(3, e.current); assertEquals(20, e.items[e.current])
    }

    @Test fun movingToTheCurrentIndexFromEitherSideKeepsTheSongCurrent() {
        val a = QueueEdit.move(ids(5), 2, 0, 2)   // lands on the current index: current shifts down
        assertEquals(20, a.items[a.current])
        val b = QueueEdit.move(ids(5), 2, 4, 2)   // lands on it from after: current shifts up
        assertEquals(20, b.items[b.current])
    }

    @Test fun movesThatDoNotCrossTheCurrentSongLeaveItsIndexAlone() {
        val e = QueueEdit.move(ids(6), 4, 0, 2)
        assertEquals(4, e.current)
        val f = QueueEdit.move(ids(6), 1, 3, 5)
        assertEquals(1, f.current)
    }

    @Test fun moveToTheSamePlaceOrOutOfRangeChangesNothing() {
        val l = ids(4)
        assertEquals(l, QueueEdit.move(l, 1, 2, 2).items)
        assertEquals(l, QueueEdit.move(l, 1, -1, 2).items)
        assertEquals(l, QueueEdit.move(l, 1, 0, 4).items)
        assertEquals(1, QueueEdit.move(l, 1, 2, 2).current)
    }

    @Test fun moveOnEmptyAndSingleLists() {
        assertEquals(emptyList<Int>(), QueueEdit.move(emptyList<Int>(), -1, 0, 0).items)
        assertEquals(listOf(7), QueueEdit.move(listOf(7), 0, 0, 0).items)
    }

    @Test fun everyMoveKeepsTheSameSongCurrent() {
        for (n in 1..6) for (c in 0 until n) for (f in 0 until n) for (t in 0 until n) {
            val l = ids(n); val e = QueueEdit.move(l, c, f, t)
            assertEquals("n=$n c=$c $f->$t", l[c], e.items[e.current])
            assertEquals(l.sorted(), e.items.sorted())
        }
    }

    // ---- remove ----------------------------------------------------------------------------------------------------------------

    @Test fun removingBeforeTheCurrentSongShiftsItsIndex() {
        val e = QueueEdit.remove(ids(5), 3, 1)
        assertEquals(listOf(0, 20, 30, 40), e.items)
        assertEquals(2, e.current); assertEquals(30, e.items[e.current]); assertFalse(e.currentRemoved)
    }

    @Test fun removingAfterTheCurrentSongChangesNothingElse() {
        val e = QueueEdit.remove(ids(5), 1, 3)
        assertEquals(1, e.current); assertEquals(listOf(0, 10, 20, 40), e.items)
    }

    @Test fun removingTheCurrentSongHandsOverToTheNextOne() {
        val e = QueueEdit.remove(ids(5), 2, 2)
        assertTrue(e.currentRemoved); assertFalse(e.stopped)
        assertEquals(2, e.current); assertEquals(30, e.items[e.current])
    }

    @Test fun removingTheLastCurrentSongStopsOnTheOneBeforeItOrWrapsWithRepeatAll() {
        val stop = QueueEdit.remove(ids(3), 2, 2)
        assertTrue(stop.stopped); assertTrue(stop.currentRemoved)
        assertEquals(1, stop.current)
        val wrap = QueueEdit.remove(ids(3), 2, 2, wrap = true)
        assertFalse(wrap.stopped); assertEquals(0, wrap.current)
    }

    @Test fun removingTheOnlySongEmptiesTheQueue() {
        val e = QueueEdit.remove(listOf(5), 0, 0)
        assertTrue(e.items.isEmpty()); assertEquals(-1, e.current); assertTrue(e.stopped)
    }

    @Test fun removingOutOfRangeChangesNothing() {
        val e = QueueEdit.remove(ids(3), 1, 7)
        assertEquals(ids(3), e.items); assertEquals(1, e.current)
    }

    @Test fun undoInsertsAtTheOldIndexAndKeepsTheCurrentSong() {
        for (n in 2..6) for (c in 0 until n) for (r in 0 until n) {
            val l = ids(n)
            val removed = QueueEdit.remove(l, c, r)
            if (r == c) continue   // the current song was removed: something else is current now
            val back = QueueEdit.insert(removed.items, removed.current, r, l[r])
            assertEquals(l, back.items)
            assertEquals(c, back.current)
        }
    }

    // ---- play order ------------------------------------------------------------------------------------------------------------

    @Test fun staleShuffleOrderFallsBackToTheQueueOrder() {
        assertArrayEquals(intArrayOf(0, 1, 2), QueueEdit.playOrder(3, intArrayOf(2, 2, 0)))
        assertArrayEquals(intArrayOf(0, 1, 2), QueueEdit.playOrder(3, intArrayOf(1, 0)))
        assertArrayEquals(intArrayOf(0, 1, 2), QueueEdit.playOrder(3, null))
        assertArrayEquals(intArrayOf(2, 0, 1), QueueEdit.playOrder(3, intArrayOf(2, 0, 1)))
    }

    @Test fun upcomingFollowsPlayOrderAndWrapsOnlyWithRepeatAll() {
        val order = intArrayOf(3, 1, 4, 0, 2)
        assertEquals(listOf(0, 2), QueueEdit.upcoming(order, 4, false))
        assertEquals(listOf(0, 2, 3, 1), QueueEdit.upcoming(order, 4, true))
        assertEquals(emptyList<Int>(), QueueEdit.upcoming(order, 2, false))
        assertEquals(listOf(3, 1, 4, 0), QueueEdit.upcoming(order, 2, true))
        assertEquals(emptyList<Int>(), QueueEdit.upcoming(order, 9, true))
    }

    // ---- removal plan ----------------------------------------------------------------------------------------------------------

    @Test fun removingAnotherSongJustRemovesIt() {
        val p = QueueEdit.removePlan(intArrayOf(0, 1, 2, 3), 1, 3, false)!!
        assertEquals(-1, p.jumpTo); assertFalse(p.pauseAfterJump); assertFalse(p.emptiesQueue)
    }

    @Test fun removingTheCurrentSongJumpsToTheNextInPlayOrderFirst() {
        val p = QueueEdit.removePlan(intArrayOf(2, 0, 3, 1), 3, 3, false)!!
        assertEquals(1, p.jumpTo); assertFalse(p.pauseAfterJump)   // shuffle: after 3 comes 1, not queue index 4
    }

    @Test fun removingTheLastSongInPlayOrderStopsOnTheOneBeforeIt() {
        val p = QueueEdit.removePlan(intArrayOf(0, 1, 2), 2, 2, false)!!
        assertEquals(1, p.jumpTo); assertTrue(p.pauseAfterJump)
    }

    @Test fun removingTheLastSongWithRepeatAllWrapsToTheFirst() {
        val p = QueueEdit.removePlan(intArrayOf(0, 1, 2), 2, 2, true)!!
        assertEquals(0, p.jumpTo); assertFalse(p.pauseAfterJump)
    }

    @Test fun removingTheOnlySongEmptiesAndUnknownIndexIsNull() {
        assertTrue(QueueEdit.removePlan(intArrayOf(0), 0, 0, false)!!.emptiesQueue)
        assertNull(QueueEdit.removePlan(intArrayOf(0, 1), 0, 5, false))
    }

    @Test fun theRemovalPlanKeepsTheRightSongCurrentInEveryCase() {
        for (n in 1..6) for (c in 0 until n) for (r in 0 until n) for (all in listOf(false, true)) {
            val l = ids(n)
            val plan = QueueEdit.removePlan(IntArray(n) { it }, c, r, all)!!
            if (plan.emptiesQueue) { assertEquals(1, n); continue }
            // what the controller does: jump (if asked), then remove the index; the current song must be the planned one
            val cur = if (plan.jumpTo >= 0) plan.jumpTo else c
            val e = QueueEdit.remove(l, cur, r)
            val want = if (r == c) l[plan.jumpTo] else l[c]
            assertEquals("n=$n c=$c r=$r all=$all", want, e.items[e.current])
            assertFalse(e.items.contains(l[r]))
        }
    }

    // ---- reorder in Up next ----------------------------------------------------------------------------------------------------

    @Test fun moveUpAndDownSwapTwoNeighboursOfUpNext() {
        val order = intArrayOf(0, 1, 2, 3, 4)
        assertArrayEquals(intArrayOf(0, 1, 3, 2, 4), QueueEdit.reorder(order, 1, false, 3, Step.UP))
        assertArrayEquals(intArrayOf(0, 1, 2, 4, 3), QueueEdit.reorder(order, 1, false, 3, Step.DOWN))
    }

    @Test fun playNextPutsTheSongFirstInUpNext() {
        assertArrayEquals(intArrayOf(0, 1, 4, 2, 3), QueueEdit.reorder(intArrayOf(0, 1, 2, 3, 4), 1, false, 4, Step.TOP))
    }

    @Test fun theEdgesAndNonUpcomingSongsChangeNothing() {
        val order = intArrayOf(0, 1, 2, 3)
        assertNull(QueueEdit.reorder(order, 1, false, 2, Step.UP))     // already next
        assertNull(QueueEdit.reorder(order, 1, false, 3, Step.DOWN))   // already last
        assertNull(QueueEdit.reorder(order, 1, false, 2, Step.TOP))
        assertNull(QueueEdit.reorder(order, 1, false, 1, Step.DOWN))   // the current song is not in Up next
        assertNull(QueueEdit.reorder(order, 1, false, 0, Step.UP))     // already played, repeat off
    }

    @Test fun reorderWorksInAShuffleOrderAndKeepsTheCurrentSongsSlot() {
        val order = intArrayOf(3, 0, 4, 1, 2)   // plays 3, 0, 4, 1, 2; current is 4
        val r = QueueEdit.reorder(order, 4, false, 2, Step.UP)!!
        assertArrayEquals(intArrayOf(3, 0, 4, 2, 1), r)
        assertEquals(4, r[2])
    }

    @Test fun withRepeatAllTheWrappedSongsAreReorderedToo() {
        val order = intArrayOf(0, 1, 2, 3)   // current 2: Up next is 3, then (wrap) 0, 1
        val r = QueueEdit.reorder(order, 2, true, 0, Step.UP)!!
        assertEquals(listOf(3, 0, 1), QueueEdit.upcoming(order, 2, true))
        assertEquals(listOf(0, 3, 1), QueueEdit.upcoming(r, 2, true))
        val top = QueueEdit.reorder(order, 2, true, 1, Step.TOP)!!
        assertEquals(listOf(1, 3, 0), QueueEdit.upcoming(top, 2, true))
        assertTrue(QueueEdit.isPermutation(top, 4))
    }

    @Test fun movedUpcomingKeepsTheSameSongs() {
        val up = listOf(5, 6, 7, 8)
        assertEquals(listOf(5, 7, 6, 8), QueueEdit.movedUpcoming(up, 7, Step.UP))
        assertEquals(listOf(5, 6, 8, 7), QueueEdit.movedUpcoming(up, 7, Step.DOWN))
        assertEquals(listOf(8, 5, 6, 7), QueueEdit.movedUpcoming(up, 8, Step.TOP))
        assertNull(QueueEdit.movedUpcoming(up, 5, Step.UP))
        assertNull(QueueEdit.movedUpcoming(up, 8, Step.DOWN))
        assertNull(QueueEdit.movedUpcoming(up, 99, Step.TOP))
        assertNull(QueueEdit.movedUpcoming(emptyList(), 1, Step.UP))
    }

    // ---- the player's own moves give the same result -----------------------------------------------------------------------------

    private fun applyMoves(items: List<Int>, current: Int, moves: List<Pair<Int, Int>>): Pair<List<Int>, Int> {
        var l = items; var c = current
        for ((f, t) in moves) { val e = QueueEdit.move(l, c, f, t); l = e.items; c = e.current }
        return l to c
    }

    @Test fun linearMovesGiveTheSameUpNextAsReorderForEveryCase() {
        var cases = 0
        for (n in 1..7) for (c in 0 until n) for (item in 0 until n) for (step in Step.values()) for (all in listOf(false, true)) {
            val l = ids(n); val order = IntArray(n) { it }
            val up = QueueEdit.upcoming(order, c, all)
            val expectedUp = QueueEdit.movedUpcoming(up, item, step)?.map { l[it] }
            val moves = QueueEdit.linearMoves(c, up, item, step)
            if (expectedUp == null) { assertTrue(moves.isEmpty()); assertNull(QueueEdit.reorder(order, c, all, item, step)); continue }
            cases++
            val (nl, nc) = applyMoves(l, c, moves)
            val tag = "n=$n c=$c item=$item $step all=$all moves=$moves"
            assertEquals(tag, l[c], nl[nc])                                                  // the playing song is still the current one
            assertEquals(tag, l.sorted(), nl.sorted())                                       // nothing lost or duplicated
            assertEquals(tag, expectedUp, QueueEdit.upcoming(IntArray(n) { it }, nc, all).map { nl[it] })
            // and the shuffle-order edit says the same
            val r = QueueEdit.reorder(order, c, all, item, step)!!
            assertTrue(tag, QueueEdit.isPermutation(r, n))
            assertEquals(tag, c, r[c])                                                       // the current song keeps its slot
            assertEquals(tag, expectedUp, QueueEdit.upcoming(r, c, all).map { l[it] })
        }
        assertTrue(cases > 100)
    }

    @Test fun swapMovesExchangeTwoSongs() {
        for (n in 2..7) for (a in 0 until n) for (b in 0 until n) {
            val l = ids(n)
            val (r, _) = applyMoves(l, -1, QueueEdit.swapMoves(a, b))
            val want = l.toMutableList().also { val t = it[a]; it[a] = it[b]; it[b] = t }
            assertEquals("n=$n $a<->$b", want, r)
        }
    }

    @Test fun playNextTargetIsJustAfterTheCurrentSong() {
        assertNull(QueueEdit.playNextTarget(2, 2))
        assertNull(QueueEdit.playNextTarget(2, 3))
        assertNull(QueueEdit.playNextTarget(-1, 3))
        assertEquals(3, QueueEdit.playNextTarget(2, 6))
        assertEquals(3, QueueEdit.playNextTarget(3, 0))
        for (n in 2..7) for (c in 0 until n) for (i in 0 until n) {
            val t = QueueEdit.playNextTarget(c, i) ?: continue
            val l = ids(n); val e = QueueEdit.move(l, c, i, t)
            assertEquals(l[c], e.items[e.current])
            assertEquals(l[i], e.items[e.current + 1])   // it plays right after the current song
        }
    }

    @Test fun rowActionsHideWhatCannotHappen() {
        val first = QueueEdit.rowActions(0, 3)
        assertFalse(first.up); assertTrue(first.down); assertFalse(first.top)
        val mid = QueueEdit.rowActions(1, 3)
        assertTrue(mid.up); assertTrue(mid.down); assertTrue(mid.top)
        val last = QueueEdit.rowActions(2, 3)
        assertTrue(last.up); assertFalse(last.down)
        assertTrue(QueueEdit.rowActions(2, 3, more = true).down)   // the list was cut short: more songs follow
        assertFalse(QueueEdit.rowActions(0, 1).down)
    }

    @Test fun placeAfterPutsTheUndoneSongBackBehindItsPredecessor() {
        assertArrayEquals(intArrayOf(2, 0, 3, 1), QueueEdit.placeAfter(intArrayOf(2, 0, 1, 3), 3, 0))
        assertArrayEquals(intArrayOf(3, 2, 0, 1), QueueEdit.placeAfter(intArrayOf(2, 0, 1, 3), 3, -1))
        assertTrue(QueueEdit.isPermutation(QueueEdit.placeAfter(intArrayOf(2, 0, 1, 3), 1, 9), 4))
        assertNotNull(QueueEdit.placeAfter(intArrayOf(0), 0, -1))
    }
}
