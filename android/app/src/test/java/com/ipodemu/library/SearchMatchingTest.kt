package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class SearchMatchingTest {
    @Test fun baseTitleDropsDescriptionsButKeepsVersions() {
        assertEquals("All The Stars", WebCatalog.baseTitle("All The Stars (From \"Black Panther: The Album\")"))
        assertEquals("(Don't Fear) The Reaper", WebCatalog.baseTitle("(Don't Fear) The Reaper"))
        assertEquals("Hello", WebCatalog.baseTitle("Hello - Remastered 2011"))
        assertEquals("Smooth Criminal", WebCatalog.baseTitle("Smooth Criminal (2012 Remaster)"))
        assertEquals("Song", WebCatalog.baseTitle("Song (feat. X) [Deluxe]"))
        assertEquals("Song (Live)", WebCatalog.baseTitle("Song (Live)"))
        assertEquals("Cool (Remix)", WebCatalog.baseTitle("Cool (Remix)"))
        assertEquals("Time (Part 2)", WebCatalog.baseTitle("Time (Part 2)"))
    }

    @Test fun openSourcesMatchTheSongWithoutItsMovieSuffix() {
        assertTrue(OpenSources.matches("Kendrick Lamar", "All The Stars (From \"Black Panther: The Album\")", 232, "Kendrick Lamar, SZA", "All The Stars", 233))
        assertFalse(OpenSources.matches("Kendrick Lamar", "All The Stars (From \"Black Panther: The Album\")", 232, "Kendrick Lamar", "All The Stars (Live)", 233))
    }

    private fun f(name: String, mb: Long, len: Int, depth: Int? = null, rate: Int? = null, user: String = name) =
        SlskdClient.FileResult(user, "music/Kendrick Lamar/$name", mb * 1_048_576, true, null, 1_000_000, 0, len, depth, rate)

    @Test fun hiResDetectionAndRanking() {
        val cd = f("Kendrick Lamar - All The Stars.flac", 25, 232, 16, 44100)
        val hi = f("Kendrick Lamar - All The Stars (24-96).flac", 90, 232, 24, 96000)
        val guessedHi = f("Kendrick Lamar - All The Stars hr.flac", 85, 232)           // the peer gave no bit depth: ~2.9 Mbps
        val mp3 = SlskdClient.FileResult("m", "music/Kendrick Lamar/Kendrick Lamar - All The Stars.mp3", 9L * 1_048_576, true, 320, 1_000_000, 0, 232)
        assertFalse(cd.hiRes); assertTrue(hi.hiRes); assertTrue(guessedHi.hiRes)
        val c = SlskdClient()
        // a normal download never takes a Hi-Res file, even when it is the only lossless one
        assertEquals(listOf(cd.username), c.rankSong(listOf(cd, hi, guessedHi), "Kendrick Lamar", "All The Stars", 232).map { it.username })
        assertEquals(listOf(mp3.username), c.rankSong(listOf(hi, mp3), "Kendrick Lamar", "All The Stars", 232).map { it.username })
        // the explicit Hi-Res download takes only Hi-Res files, the highest quality first
        assertEquals(listOf(hi.username, guessedHi.username), c.rankSong(listOf(cd, guessedHi, hi), "Kendrick Lamar", "All The Stars", 232, hiRes = true).map { it.username })
    }
}
