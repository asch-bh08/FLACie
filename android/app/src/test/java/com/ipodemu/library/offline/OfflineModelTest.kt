package com.ipodemu.library.offline

import com.ipodemu.library.Track
import com.ipodemu.library.TrackSource
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class OfflineModelTest {
    private fun id(n: Int) = "%032x".format(n)
    private fun t(n: Int, src: TrackSource = TrackSource.JELLYFIN, dur: Long = 200_000, size: Long = 0) = Track(
        path = "http://jf/Audio/${id(n)}/stream?static=true", title = "Song $n", artist = "A", album = "Al", albumArtist = "A", genre = "Rock", trackNo = n, discNo = 1,
        durationMs = dur, year = 2000, isMusic = true, artKey = "jf${id(n)}", mtime = 0, size = size, source = src)

    // ---- the index ----
    @Test fun addKeepsOrderAndUpgradesFavoriteToManual() {
        val ix = OfflineIndex()
        ix.add(t(1), OfflineReason.FAVORITE, 10); ix.add(t(2), OfflineReason.MANUAL, 20)
        assertEquals(listOf("Song 1", "Song 2"), ix.entries.map { it.title })
        ix.add(t(1), OfflineReason.MANUAL, 30)
        assertEquals(OfflineReason.MANUAL, ix[OfflineMath.keyOf(t(1).path)]!!.reason)
        ix.add(t(2), OfflineReason.FAVORITE, 40)
        assertEquals("a manual pin is never downgraded", OfflineReason.MANUAL, ix[OfflineMath.keyOf(t(2).path)]!!.reason)
        assertEquals(2, ix.size)
    }

    @Test fun failedEntryIsQueuedAgainWhenAskedAgain() {
        val ix = OfflineIndex(); val e = ix.add(t(1), OfflineReason.MANUAL, 1)
        e.state = OfflineState.FAILED; e.error = "http 500"
        ix.add(t(1), OfflineReason.MANUAL, 2)
        assertEquals(OfflineState.QUEUED, e.state); assertNull(e.error)
    }

    @Test fun nextIsTheFirstQueuedOrInterrupted() {
        val ix = OfflineIndex()
        val a = ix.add(t(1), OfflineReason.MANUAL, 1); val b = ix.add(t(2), OfflineReason.MANUAL, 2); val c = ix.add(t(3), OfflineReason.MANUAL, 3)
        a.state = OfflineState.DONE; b.state = OfflineState.DOWNLOADING
        assertEquals(b, ix.next())
        b.state = OfflineState.FAILED
        assertEquals(c, ix.next())
        c.state = OfflineState.DONE
        assertNull(ix.next())
        assertEquals(2, ix.doneCount); assertEquals(1, ix.failedCount); assertEquals(0, ix.pendingCount)
    }

    @Test fun totalsCountOnlyFinishedSongs() {
        val ix = OfflineIndex()
        val a = ix.add(t(1), OfflineReason.MANUAL, 1); a.state = OfflineState.DONE; a.bytes = 5_000_000
        val b = ix.add(t(2), OfflineReason.MANUAL, 2); b.state = OfflineState.DOWNLOADING; b.bytes = 1_000_000
        assertEquals(5_000_000L, ix.doneBytes)
    }

    @Test fun saveAndLoadRoundTrip() {
        val ix = OfflineIndex()
        val a = ix.add(t(1), OfflineReason.FAVORITE, 111); a.state = OfflineState.DONE; a.bytes = 123; a.total = 123
        val b = ix.add(t(2), OfflineReason.MANUAL, 222); b.state = OfflineState.FAILED; b.error = "http 404"
        val back = OfflineIndex.decode(ix.encode())
        assertEquals(2, back.size)
        val a2 = back[OfflineMath.keyOf(t(1).path)]!!
        assertEquals(OfflineState.DONE, a2.state); assertEquals(OfflineReason.FAVORITE, a2.reason); assertEquals(123L, a2.bytes); assertEquals("Song 1", a2.title); assertEquals(111L, a2.addedMs)
        assertEquals("http 404", back[OfflineMath.keyOf(t(2).path)]!!.error)
        val tr = a2.toTrack()
        assertEquals(t(1).path, tr.path); assertEquals(TrackSource.JELLYFIN, tr.source); assertEquals("jf${id(1)}", tr.artKey)
    }

    @Test fun brokenOrForeignTextGivesAnEmptyIndex() {
        assertEquals(0, OfflineIndex.decode(null).size); assertEquals(0, OfflineIndex.decode("").size); assertEquals(0, OfflineIndex.decode("not json").size)
        assertEquals(0, OfflineIndex.decode("""{"v":99,"e":[{"k":"a","p":"b"}]}""").size)
        assertEquals("an entry with no path is skipped", 1, OfflineIndex.decode("""{"v":1,"e":[{"k":"a"},{"k":"b","p":"http://x/b"}]}""").size)
    }

    // ---- resume ----
    @Test fun rangeHeaderOnlyWhenThereIsAPart() {
        assertNull(OfflineMath.rangeHeader(0)); assertEquals("bytes=1000-", OfflineMath.rangeHeader(1000))
    }

    @Test fun resumeDecisions() {
        assertEquals(ResumeDecision.Append(1000), OfflineMath.decide(1000, 206, "bytes 1000-4999/5000", 4000))
        assertEquals("a range that starts elsewhere cannot be appended", ResumeDecision.Restart, OfflineMath.decide(1000, 206, "bytes 0-4999/5000", 5000))
        assertEquals("no Content-Range on a 206", ResumeDecision.Restart, OfflineMath.decide(1000, 206, null, 4000))
        assertEquals("the server ignored the range", ResumeDecision.Restart, OfflineMath.decide(1000, 200, null, 5000))
        assertEquals("a fresh download", ResumeDecision.Restart, OfflineMath.decide(0, 200, null, 5000))
        assertEquals(ResumeDecision.Complete, OfflineMath.decide(5000, 416, "bytes */5000", 0))
        assertEquals(ResumeDecision.Fail(416), OfflineMath.decide(0, 416, null, 0))
        assertEquals(ResumeDecision.Fail(404), OfflineMath.decide(0, 404, null, 0)); assertEquals(ResumeDecision.Fail(500), OfflineMath.decide(10, 500, null, 0))
    }

    @Test fun totalSizeFromTheAnswer() {
        assertEquals(5000L, OfflineMath.totalSize(206, 1000, "bytes 1000-4999/5000", 4000))
        assertEquals(5000L, OfflineMath.totalSize(206, 1000, "bytes 1000-4999/*", 4000))
        assertEquals(5000L, OfflineMath.totalSize(200, 0, null, 5000)); assertEquals(-1L, OfflineMath.totalSize(200, 0, null, -1))
    }

    // ---- names, types, orphans ----
    @Test fun fileNamesAreSafe() {
        assertEquals("abc123.flac", OfflineMath.fileName("abc123", "FLAC"))
        assertEquals("http___x_a_b.bin", OfflineMath.fileName("http://x/a b", "??"))
        assertTrue(OfflineMath.fileName("x".repeat(500), "mp3").length <= 84)
        assertEquals("item.bin", OfflineMath.fileName("", ""))
    }

    @Test fun extensionFromTheServer() {
        assertEquals("flac", OfflineMath.extensionFor("audio/flac", null)); assertEquals("mp3", OfflineMath.extensionFor("audio/mpeg; charset=x", null))
        assertEquals("m4a", OfflineMath.extensionFor("audio/mp4", null)); assertEquals("bin", OfflineMath.extensionFor("application/octet-stream", null)); assertEquals("bin", OfflineMath.extensionFor(null, null))
        assertEquals("flac", OfflineMath.extensionFor("application/octet-stream", """attachment; filename="Song 1.FLAC""""))
        assertEquals("opus", OfflineMath.extensionFor(null, "attachment; filename*=UTF-8''a%20b.opus"))
    }

    @Test fun orphansAreFilesNoEntryOwns() {
        val ix = OfflineIndex(); ix.add(t(1), OfflineReason.MANUAL, 1)
        val k = OfflineMath.keyOf(t(1).path)
        val files = listOf("$k.flac", "$k.flac.part", "${id(9)}.flac", "junk.part")
        assertEquals(listOf("${id(9)}.flac", "junk.part"), OfflineMath.orphans(files, ix))
    }

    // ---- network and formatting ----
    @Test fun wifiOnlyBlocksMeteredNetworks() {
        assertTrue(OfflineMath.networkAllowed(true, true, false)); assertFalse(OfflineMath.networkAllowed(true, true, true))
        assertTrue(OfflineMath.networkAllowed(false, true, true)); assertFalse(OfflineMath.networkAllowed(false, false, false)); assertFalse(OfflineMath.networkAllowed(true, false, false))
    }

    @Test fun byteFormatting() {
        assertEquals("0 B", OfflineMath.formatBytes(-5)); assertEquals("12 KB", OfflineMath.formatBytes(12_000)); assertEquals("230 MB", OfflineMath.formatBytes(230_000_000)); assertEquals("1.4 GB", OfflineMath.formatBytes(1_400_000_000))
    }

    @Test fun sizeEstimate() {
        assertEquals(1234L, OfflineMath.estimateBytes(t(1, size = 1234))); assertEquals(40_000_000L, OfflineMath.estimateBytes(t(1, dur = 0)))
        assertTrue(OfflineMath.estimateBytes(t(1, dur = 600_000)) > OfflineMath.estimateBytes(t(1, dur = 60_000)))
    }

    // ---- auto-download of favourites ----
    @Test fun favouritesAreAddedInOrderUntilTheLimit() {
        val ix = OfflineIndex()
        val favs = (1..5).map { t(it, size = 100_000_000) }
        val p = AutoDownloadPlan.plan(favs, ix, 250_000_000)
        assertEquals(listOf("Song 1", "Song 2"), p.add.map { it.title }); assertTrue(p.removeKeys.isEmpty())
        assertEquals(5, AutoDownloadPlan.plan(favs, ix, 0).add.size)
    }

    @Test fun whatIsAlreadyOfflineCountsTowardsTheLimitAndIsNotAddedAgain() {
        val ix = OfflineIndex()
        val e = ix.add(t(1, size = 100_000_000), OfflineReason.FAVORITE, 1); e.state = OfflineState.DONE; e.bytes = 100_000_000; e.total = 100_000_000
        val p = AutoDownloadPlan.plan((1..4).map { t(it, size = 100_000_000) }, ix, 250_000_000)
        assertEquals(listOf("Song 2"), p.add.map { it.title })
    }

    @Test fun unfavouritedFavoriteEntriesGoButManualOnesStay() {
        val ix = OfflineIndex()
        ix.add(t(1), OfflineReason.FAVORITE, 1); ix.add(t(2), OfflineReason.MANUAL, 2); ix.add(t(3), OfflineReason.FAVORITE, 3)
        val p = AutoDownloadPlan.plan(listOf(t(3)), ix, 0)
        assertEquals(listOf(OfflineMath.keyOf(t(1).path)), p.removeKeys)
    }

    @Test fun onlyJellyfinSongsAreDownloaded() {
        val p = AutoDownloadPlan.plan(listOf(t(1, src = TrackSource.NAS), t(2), t(3, src = TrackSource.LOCAL)), OfflineIndex(), 0)
        assertEquals(listOf("Song 2"), p.add.map { it.title })
        assertNotNull(p)
    }
}
