package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Test

/** Same rules as the web's AutoplaySpread (owner, 12 Nov): one version of a song, at most two more by the artist asked for, no artist twice in a row. */
class AutoplaySpreadTest {
    @Test fun otherVersionsAreTheSameSong() {
        val k = songKey("Dreamer", "Livin' Joy")
        assertEquals(k, songKey("Dreamer - Re-Original 7-inch Mix", "Livin' Joy"))
        assertEquals(k, songKey("Dreamer (Janice Robinson Vocal)", "Livin' Joy"))
        assertNotEquals(k, songKey("Dreamer", "Prince"))
        assertNotEquals(k, songKey("Dreamers", "Livin' Joy"))
    }

    @Test fun atMostTwoMoreByTheSeedArtist() {
        val next = AutoplaySpread.spread(listOf("Livin' Joy", "Livin' Joy", "Livin' Joy", "Haddaway", "Livin' Joy", "Corona"), { it }, "Livin' Joy")
        assertEquals(2, next.count { it == "Livin' Joy" })
        for (i in 1 until next.size) assertNotEquals(next[i - 1], next[i])
    }

    @Test fun lovedSongsDoNotCount() {
        val songs = listOf("X" to true, "Y" to false, "X" to true, "Z" to false, "X" to false, "W" to false, "X" to false, "X" to false)
        val next = AutoplaySpread.spread(songs, { it.first }, "X", { it.second })
        assertEquals(2, next.count { it.first == "X" && it.second })
        assertEquals(2, next.count { it.first == "X" && !it.second })   // unknown ones stay at two
    }

    @Test fun theCapHoldsAcrossRefillsAndForLovedSongs() {
        val refill = listOf("Yeah Yeah Yeahs", "Yeah Yeah Yeahs", "MGMT", "Yeah Yeah Yeahs")
        assertEquals(0, AutoplaySpread.spread(refill, { it }, "Yeah Yeah Yeahs", already = 2).count { it == "Yeah Yeah Yeahs" })
        val loved = List(8) { "Yeah Yeah Yeahs" to true } + ("MGMT" to false)
        // owner, 26 Nov: loved ones may come more often, never more than 4 in any 10
        assertEquals(4, AutoplaySpread.spread(loved, { it.first }, "Yeah Yeah Yeahs", { it.second }).count { it.first == "Yeah Yeah Yeahs" })
    }

    @Test fun lovedSongsMayRunThreeInARowUnknownNeverTwice() {
        val loved = List(6) { "X" to true } + listOf("Y" to false, "Z" to false)
        val next = AutoplaySpread.spread(loved, { it.first }, "Q", { it.second })
        var run = 0; var worst = 0; var prev: String? = null
        for (s in next) { run = if (s.first == prev) run + 1 else 1; prev = s.first; worst = maxOf(worst, run) }
        assertEquals(3, worst)
        for (i in next.indices) assertEquals(true, next.drop(i).take(10).count { it.first == "X" } <= 4)
        val plain = AutoplaySpread.spread(listOf("A", "A", "A", "B", "C"), { it }, "Q")
        for (i in 1 until 3) assertNotEquals(plain[i - 1], plain[i])
    }

    @Test fun autoplayKeepsToWhatWasChosen() {
        val q = (1..30).map { "p$it" } + listOf("auto1", "auto2", "auto3")
        val isAuto = { x: String -> x.startsWith("auto") }
        val seeds = AutoplaySpread.seeds(q, 32, isAuto)
        assertEquals(8, seeds.size); assertEquals("p30", seeds.last()); assertEquals(0, seeds.count(isAuto))
        assertEquals("p10", AutoplaySpread.lead(q, 9, isAuto))
        assertNotEquals(AutoplaySpread.lead(q, 31, isAuto), AutoplaySpread.lead(q, 32, isAuto))
        assertEquals("dreamer", AutoplaySpread.lead(listOf("dreamer", "auto1", "auto2"), 2, isAuto))
    }

    @Test fun searchFoldsVersionsUnderThePlainOne() {
        fun t(title: String, artist: String, p: String) = Track(path = p, title = title, artist = artist, album = "", albumArtist = artist, genre = "", trackNo = 1, discNo = 1,
            durationMs = 200_000, year = 0, isMusic = true, artKey = null, mtime = 0, size = 0)
        val mix = t("Dreamer - Re-Original 7-inch Mix", "Livin' Joy", "/1"); val plain = t("Dreamer", "Livin' Joy", "/2")
        val vocal = t("Dreamer (Janice Robinson Vocal)", "Livin' Joy", "/3"); val prince = t("Dreamer", "Prince", "/4")
        assertEquals(listOf(plain, prince), AutoplaySpread.oneVersion(listOf(mix, prince, plain, vocal)))
        assertEquals(listOf(vocal), AutoplaySpread.oneVersion(listOf(vocal)))
    }
}
