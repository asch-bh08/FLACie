package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class DownloadActivityTest {
    @Test fun nothingGoingOnShowsNothing() = assertNull(DownloadActivity.line(emptyList(), 0, -1))

    @Test fun oneSongBeingFetchedNamesItWithItsProgress() =
        assertEquals("Getting Blinding Lights · 45%", DownloadActivity.line(listOf("Blinding Lights · The Weeknd" to "Downloading 45 %"), 0, -1))

    @Test fun severalSongsAreCounted() =
        assertEquals("Getting 3 songs", DownloadActivity.line(listOf("A · x" to "Search", "B · y" to "", "C · z" to ""), 0, -1))

    @Test fun songsSavedForOfflineAreSaidToo() {
        assertEquals("Saving 4 songs for offline · 30%", DownloadActivity.line(emptyList(), 4, 30))
        assertEquals("Getting A · Saving 1 song for offline", DownloadActivity.line(listOf("A · x" to "Search"), 1, 30))
    }
}
