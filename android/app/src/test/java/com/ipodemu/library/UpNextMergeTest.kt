package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** Up next = the queue's own rows + Autoplay suggestions still being fetched (display only). */
class UpNextMergeTest {
    private val now = 1_760_270_000_000L
    private fun song(title: String, artist: String = "Artist") = Track(
        path = "http://x/$title.flac", title = title, artist = artist, album = "LP", albumArtist = artist, genre = "", trackNo = 1, discNo = 1, durationMs = 200_000,
        year = 2020, isMusic = true, artKey = null, mtime = 0, size = 0, source = TrackSource.JELLYFIN)
    private fun pending(title: String, artist: String = "Artist") = UpNextMerge.Pending(matchKey(title, artist), title, artist, "LP", 200_000, null)

    private fun plan(upcoming: List<Track>, queue: List<Track>, auto: Set<String>, pend: List<UpNextMerge.Pending>, tags: Map<String, DownloadTag>) =
        UpNextMerge.plan(upcoming.mapIndexed { i, t -> (i + 1) to t }, queue.mapTo(HashSet()) { it.matchKey }, { it.path in auto }, pend) { tags[it] ?: DownloadTag.None }

    @Test fun pendingSuggestionsFollowTheQueueUnderTheDivider() {
        val a = song("A"); val b = song("B")
        val p1 = pending("New One"); val p2 = pending("New Two")
        val tags = mapOf(p1.key to DownloadTag.Downloading("Searching Soulseek..."), p2.key to DownloadTag.Downloading("Waiting for a peer..."))
        val r = plan(listOf(a, b), listOf(a, b), emptySet(), listOf(p1, p2), tags)
        assertNull("no queued Autoplay song to put it above", r.dividerBefore)
        assertTrue("so the divider goes after the queue rows", r.dividerAtEnd)
        assertEquals(listOf("New One", "New Two"), r.pending.map { it.pending.title })   // the order they will play
        assertEquals(DownloadTag.Downloading("Searching Soulseek..."), r.pending[0].tag)
        assertEquals("Autoplay · up next after this", UpNextMerge.DIVIDER)
    }

    @Test fun theDividerSitsAboveTheFirstQueuedAutoplaySong() {
        val a = song("A"); val auto1 = song("Auto 1"); val auto2 = song("Auto 2")
        val p = pending("Fetching")
        val r = plan(listOf(a, auto1, auto2), listOf(a, auto1, auto2), setOf(auto1.path, auto2.path), listOf(p), mapOf(p.key to DownloadTag.Downloading("Downloading 10%...")))
        assertEquals("queue index of Auto 1", 2, r.dividerBefore)
        assertFalse("not again at the end", r.dividerAtEnd)
        assertEquals(1, r.pending.size)
    }

    @Test fun noDividerWhenNothingIsAutoplayOrPending() {
        val a = song("A")
        val r = plan(listOf(a), listOf(a), emptySet(), emptyList(), emptyMap())
        assertNull(r.dividerBefore); assertFalse(r.dividerAtEnd); assertTrue(r.pending.isEmpty())
    }

    @Test fun aSuggestionThatReachedTheQueueIsNotShownTwice() {
        val a = song("A"); val landed = song("Landed")
        val p = pending("Landed")
        // finished (Recent) and already in the queue: only the queue row shows
        val r = plan(listOf(a, landed), listOf(a, landed), setOf(landed.path), listOf(p), mapOf(p.key to DownloadTag.Recent(now)))
        assertTrue(r.pending.isEmpty())
        assertEquals(2, r.dividerBefore)
        // finished but the queue does not have it yet: the suggestion row stays (no gap), showing the recent tag
        val r2 = plan(listOf(a), listOf(a), emptySet(), listOf(p), mapOf(p.key to DownloadTag.Recent(now)))
        assertEquals(1, r2.pending.size)
        assertEquals(DownloadTag.Recent(now), r2.pending[0].tag)
    }

    @Test fun aFailedFetchShowsWhileRecentThenGoes() {
        val a = song("A"); val p = pending("Broken")
        val within = plan(listOf(a), listOf(a), emptySet(), listOf(p), mapOf(p.key to DownloadTag.Failed(now)))
        assertEquals(DownloadTag.Failed(now), within.pending.single().tag)
        assertEquals("Couldn't download", DownloadTags.text(within.pending.single().tag, now + 5_000))
        // aged out: the store answers None, and the row is gone
        val gone = plan(listOf(a), listOf(a), emptySet(), listOf(p), emptyMap())
        assertTrue(gone.pending.isEmpty()); assertFalse(gone.dividerAtEnd)
    }

    @Test fun aSuggestionNobodyKnowsOrAPlainCloudMarkerIsNotARow() {
        val a = song("A"); val p1 = pending("Nothing"); val p2 = pending("Old cloud")
        val r = plan(listOf(a), listOf(a), emptySet(), listOf(p1, p2), mapOf(p2.key to DownloadTag.NewDownload))
        assertTrue(r.pending.isEmpty())
    }

    @Test fun theQueueRowsAreLeftAlone() {
        // the plan only reads the upcoming list: it never reorders, drops or re-indexes it
        val songs = (1..5).map { song("S$it") }
        val up = songs.mapIndexed { i, t -> (i + 10) to t }
        val before = up.toList()
        UpNextMerge.plan(up, songs.mapTo(HashSet()) { it.matchKey }, { false }, listOf(pending("X")), { DownloadTag.Downloading("a") })
        assertEquals(before, up)
    }
}
