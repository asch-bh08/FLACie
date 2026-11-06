package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** Lyrics as the web finds them (5 Nov: Lovegame's Jellyfin .lrc ran late and stuck on a line): LRCLIB's for the song's exact length first. */
class LyricsOrderTest {
    @Test fun theExactLrclibMatchComesBeforeJellyfin() {
        assertEquals(listOf("lrclib-exact", "jellyfin", "sidecar", "lrclib-search"), LyricsProvider.ORDER)
    }

    @Test fun aSavedCopyFromJellyfinUnderTheOldOrderIsFetchedAgainOnce() {
        assertTrue(LyricsProvider.stale("Jellyfin"))
        assertFalse(LyricsProvider.stale(LyricsProvider.header("Jellyfin")))
        assertFalse(LyricsProvider.stale("LRCLIB"))
        assertFalse(LyricsProvider.stale("#none"))
        assertEquals("Jellyfin", LyricsProvider.sourceOf(LyricsProvider.header("Jellyfin")))
        assertEquals("LRCLIB", LyricsProvider.sourceOf("LRCLIB"))
    }
}
