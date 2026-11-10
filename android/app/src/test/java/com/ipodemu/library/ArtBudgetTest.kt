package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** b126: memory at a warm start went from 95 to 142 MB once Home's covers were kept ready (b125); the cover cache is sized by bytes and holds only the first screen. */
class ArtBudgetTest {
    private val mb = 1024 * 1024

    @Test fun aTwelfthOfTheMemoryClass() = assertEquals(16 * mb, ArtBudget.lruBytes(192))

    @Test fun neverMoreThan24MbOnBigPhones() {
        assertEquals(24 * mb, ArtBudget.lruBytes(512))
        assertEquals(24 * mb, ArtBudget.lruBytes(4096))
        // a 256 MB class used to give a sixth of the largest heap (42 MB and more)
        assertTrue(ArtBudget.lruBytes(256) < 24 * mb)
    }

    @Test fun neverLessThan12MbOnSmallOnes() {
        assertEquals(12 * mb, ArtBudget.lruBytes(64))
        assertEquals(12 * mb, ArtBudget.lruBytes(0))
    }

    @Test fun holdsAScreenOfCoversOrMore() {
        // a 240 px row cover is 240*240*4 bytes: a screen of Home or Albums (about 30) fits even at the floor
        assertTrue(ArtBudget.lruBytes(64) / (240 * 240 * 4) >= 30)
    }

    @Test fun firstViewTakesTheFirstCoversOfTheFirstShelves() {
        val shelves = (0 until 6).map { s -> (0 until 20).map { i -> "s${s}c$i" to true } }
        val v = ArtBudget.firstView(shelves, shelvesShown = 4, perShelf = 8)
        assertEquals(32, v.size)
        assertEquals("s0c0", v.first().first)
        assertTrue(v.none { it.first.startsWith("s4") || it.first.startsWith("s5") })
        assertTrue(v.none { it.first.endsWith("c8") })
    }

    @Test fun firstViewKeepsEachCoverOnceAtItsFirstSize() {
        val v = ArtBudget.firstView(listOf(listOf("a" to true, "b" to true), listOf("a" to false, "c" to false)))
        assertEquals(listOf("a" to true, "b" to true, "c" to false), v)
    }

    @Test fun sameSampleIsOneCopy() {
        assertTrue(ArtBudget.sharesCopy(1, 1))
        assertFalse(ArtBudget.sharesCopy(2, 1))
    }
}
