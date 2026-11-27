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
        assertEquals("Downloading 4 songs · 30%", DownloadActivity.line(emptyList(), 4, 30))
        assertEquals("Getting A · Downloading 1 song", DownloadActivity.line(listOf("A · x" to "Search"), 1, 30))
    }

    // owner, 7 Nov: the "downloading N now" line is for admins only; what the phone saves for offline is the person's own, so it still shows
    @Test fun theServersDownloadsAreShownToAdminsOnly() {
        assertNull(DownloadActivity.line(listOf("A · x" to "Downloading 45 %"), 0, -1, admin = false))
        assertEquals("Downloading 2 songs · 10%", DownloadActivity.line(listOf("A · x" to "Search"), 2, 10, admin = false))
        assertEquals("Getting A · 45%", DownloadActivity.line(listOf("A · x" to "Downloading 45 %"), 0, -1, admin = true))
    }
}
