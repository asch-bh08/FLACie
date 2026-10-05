package com.ipodemu.library

import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Assume.assumeTrue
import org.junit.Test

class OpenSourcesTest {
    @Test fun sameSongRules() {
        assertTrue(OpenSources.matches("Kevin MacLeod", "Sneaky Snitch", 137, "Kevin MacLeod", "Sneaky Snitch (feat. X)", 139))
        assertFalse(OpenSources.matches("Kevin MacLeod", "Sneaky Snitch", 137, "Kevin MacLeod", "Sneaky Snitch", 1583)) // a 26 minute live take
        assertFalse(OpenSources.matches("Kevin MacLeod", "Sneaky Snitch", 0, "Someone Else", "Sneaky Snitch", 0))
    }

    @Test fun onlyKnownHostsAreFetched() {
        assertTrue(OpenSources.allowed(OpenHit("archive", "x", "https://archive.org/download/a/b.mp3", "mp3", 0)))
        assertTrue(OpenSources.allowed(OpenHit("audius", "x", "https://discovery-1.example.org/v1/tracks/1/stream", "mp3", 0)))
        assertFalse(OpenSources.allowed(OpenHit("archive", "x", "http://archive.org/a.mp3", "mp3", 0)))
        assertFalse(OpenSources.allowed(OpenHit("archive", "x", "https://evil.example/a.mp3", "mp3", 0)))
        assertFalse(OpenSources.allowed(OpenHit("jamendo", "x", "https://127.0.0.1/a.mp3", "mp3", 0)))
    }

    @Test fun settingsOrderAndSwitches() {
        val on = OpenSourceSettings(true, true, true, "id", listOf("jamendo", "archive"))
        assertEquals(listOf("jamendo", "archive", "audius", "ytdl"), on.fullOrder())    // missing sources are appended
        assertEquals(listOf("jamendo", "archive", "audius"), on.active())
        assertEquals(listOf("jamendo", "audius"), on.copy(archive = false).active())  // off sources are skipped
        assertEquals(listOf("archive", "audius"), on.copy(jamendoId = "").copy(order = emptyList()).active()) // no client id: Jamendo stays off
        assertFalse(on.copy(archive = false, audius = false, jamendo = false).any())
        assertEquals(listOf("archive", "audius", "jamendo", "ytdl"), OpenSourceSettings(true, true, true, "x", listOf("bogus", "archive", "archive")).fullOrder())
        // yt-dlp is off by default and only searched when switched on, in its place in the order
        assertFalse("ytdl" in on.active())
        assertEquals(listOf("jamendo", "archive", "audius", "ytdl"), on.copy(ytdl = true).active())
        assertEquals(listOf("ytdl", "archive"), OpenSourceSettings(true, false, false, "", listOf("ytdl"), true).active())
    }

    /** Live APIs: only with FLACIE_NET_TESTS=1 (same songs as the FLACie Web probe). */
    @Test fun liveSources() = runBlocking {
        assumeTrue(System.getenv("FLACIE_NET_TESTS") == "1")
        val audius = OpenSources.find("Kevin MacLeod", "Sneaky Snitch", 137, OpenSourceSettings(true, true, false, "", emptyList()))
        println("audius: $audius"); assertNotNull(audius); assertEquals("audius", audius!!.source)
        val archive = OpenSources.find("Grateful Dead", "Dark Star", 0, OpenSourceSettings(true, true, false, "", emptyList()))
        println("archive: $archive"); assertNotNull(archive); assertEquals("archive", archive!!.source)
        val none = OpenSources.find("Taylor Swift", "Anti-Hero", 200, OpenSourceSettings(true, true, false, "", emptyList()))
        println("none: $none"); assertEquals(null, none)
    }
}
