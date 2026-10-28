package com.ipodemu.library

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.awaitCancellation
import kotlinx.coroutines.delay
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/** The owner's download said "Searching Soulseek..." forever with no error: whatever happens, a download now ends with a clear last word. */
class DownloadGuardTest {
    private fun run(quietMs: Long = 200, block: suspend ((DownloadStatus) -> Unit) -> Unit): List<DownloadStatus> {
        val seen = mutableListOf<DownloadStatus>()
        runBlocking { DownloadGuard.run(quietMs, { synchronized(seen) { seen += it } }, block) }
        return seen
    }

    @Test fun aDownloadThatFinishesIsLeftAlone() {
        val s = run { it(DownloadStatus(DownloadStage.SEARCHING, "Searching Soulseek...")); it(DownloadStatus(DownloadStage.DONE, "Done")) }
        assertEquals(listOf(DownloadStage.SEARCHING, DownloadStage.DONE), s.map { it.stage })
    }

    @Test fun anErrorEndsItWithAReason() {
        val s = run { it(DownloadStatus(DownloadStage.SEARCHING, "Searching Soulseek...")); throw IllegalStateException("boom") }
        assertEquals(DownloadStage.FAILED, s.last().stage)
        assertTrue(s.last().message, s.last().message.contains("went wrong"))
    }

    @Test fun nothingHappeningForTooLongEndsIt() {
        val s = run(quietMs = 150) { it(DownloadStatus(DownloadStage.SEARCHING, "Searching Soulseek...")); awaitCancellation() }
        assertEquals(DownloadStage.FAILED, s.last().stage)
        assertTrue(s.last().message, s.last().message.contains("Searching Soulseek"))
    }

    @Test fun newsKeepsItAlive() {
        val s = run(quietMs = 150) { report ->
            repeat(4) { report(DownloadStatus(DownloadStage.DOWNLOADING, "Downloading $it")); delay(80) }
            report(DownloadStatus(DownloadStage.DONE, "Done"))
        }
        assertEquals(DownloadStage.DONE, s.last().stage)
    }

    @Test fun endingWithoutAWordSaysSo() {
        val s = run { it(DownloadStatus(DownloadStage.SEARCHING, "Searching Soulseek...")) }
        assertEquals(DownloadStage.FAILED, s.last().stage)
    }

    @Test fun beingCancelledFromOutsideStillSaysSo() {
        val s = run { it(DownloadStatus(DownloadStage.SEARCHING, "Searching...")); throw CancellationException("scope went") }
        assertEquals(DownloadStage.FAILED, s.last().stage)
    }
}
