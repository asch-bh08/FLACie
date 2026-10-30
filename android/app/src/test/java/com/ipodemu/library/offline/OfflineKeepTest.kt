package com.ipodemu.library.offline

import com.ipodemu.library.Track
import com.ipodemu.library.TrackSource
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/** Keeping a whole album or playlist offline: the button's words, which songs to queue, and the playlists that keep themselves up to date. */
class OfflineKeepTest {
    private fun id(n: Int) = "%032x".format(n)
    private fun t(n: Int, src: TrackSource = TrackSource.JELLYFIN) = Track(
        path = "http://jf/Audio/${id(n)}/stream?static=true", title = "Song $n", artist = "A", album = "Al", albumArtist = "A", genre = "", trackNo = n, discNo = 1,
        durationMs = 200_000, year = 2000, isMusic = true, artKey = null, mtime = 0, size = 0, source = src)
    private fun ix(vararg e: Pair<Int, OfflineState>, reason: OfflineReason = OfflineReason.MANUAL): OfflineIndex {
        val x = OfflineIndex(); for ((n, s) in e) x.add(t(n), reason, 1).state = s; return x
    }
    private fun lookup(x: OfflineIndex): (String) -> OfflineEntry? = { p -> x[OfflineMath.keyOf(p)] }

    // ---- the button ----
    @Test fun nothingKeptYetOffersDownload() {
        val s = KeepStatus.of((1..3).map { t(it) }, lookup(OfflineIndex()))
        assertEquals("Download", s.label); assertEquals(KeepStatus.Kind.NONE, s.kind)
    }
    @Test fun somePendingShowsHowFarItIs() {
        val s = KeepStatus.of((1..12).map { t(it) }, lookup(ix(1 to OfflineState.DONE, 2 to OfflineState.DONE, 3 to OfflineState.DONE, 4 to OfflineState.DOWNLOADING, 5 to OfflineState.QUEUED)))
        assertEquals("Downloading 3 of 12", s.label); assertEquals(KeepStatus.Kind.BUSY, s.kind)
    }
    @Test fun allDoneSaysDownloadedAndOffersRemove() {
        val s = KeepStatus.of((1..2).map { t(it) }, lookup(ix(1 to OfflineState.DONE, 2 to OfflineState.DONE)))
        assertEquals("Downloaded", s.label); assertEquals(KeepStatus.Kind.DONE, s.kind)
    }
    @Test fun failedOnesOfferAnotherTry() {
        val s = KeepStatus.of((1..2).map { t(it) }, lookup(ix(1 to OfflineState.DONE, 2 to OfflineState.FAILED)))
        assertEquals("1 failed: try again", s.label); assertEquals(KeepStatus.Kind.FAILED, s.kind)
    }
    @Test fun halfKeptOffersTheRest() {
        val s = KeepStatus.of((1..4).map { t(it) }, lookup(ix(1 to OfflineState.DONE)))
        assertEquals("Download the other 3", s.label); assertEquals(KeepStatus.Kind.NONE, s.kind)
    }
    @Test fun songsNotOnTheServerAreLeftOut() {
        val s = KeepStatus.of(listOf(t(1), t(2, TrackSource.LOCAL)), lookup(ix(1 to OfflineState.DONE)))
        assertEquals("Downloaded", s.label)
        assertEquals("Not available", KeepStatus.of(listOf(t(2, TrackSource.LOCAL)), lookup(OfflineIndex())).label)
    }

    // ---- which songs to queue ----
    @Test fun queuesOnlyServerSongsNotAlreadyThereAndRetriesFailed() {
        val x = ix(1 to OfflineState.DONE, 2 to OfflineState.FAILED, 3 to OfflineState.QUEUED)
        val add = KeepPlan.toEnqueue(listOf(t(1), t(2), t(3), t(4), t(4), t(5, TrackSource.LOCAL)), lookup(x))
        assertEquals(listOf("Song 2", "Song 4"), add.map { it.title })
    }

    // ---- playlists kept offline ----
    @Test fun aSongAddedToAKeptPlaylistIsQueued() {
        val p = PlaylistKeepPlan.plan(listOf(listOf(t(1), t(2))), emptySet(), ix(1 to OfflineState.DONE, reason = OfflineReason.PLAYLIST))
        assertEquals(listOf("Song 2"), p.add.map { it.title }); assertTrue(p.removeKeys.isEmpty())
    }
    @Test fun aSongTakenOutOfEveryKeptPlaylistGoes() {
        val x = ix(1 to OfflineState.DONE, 2 to OfflineState.DONE, reason = OfflineReason.PLAYLIST)
        val p = PlaylistKeepPlan.plan(listOf(listOf(t(1))), emptySet(), x)
        assertEquals(listOf(OfflineMath.keyOf(t(2).path)), p.removeKeys)
    }
    @Test fun aSongInAnotherKeptPlaylistStays() {
        val x = ix(1 to OfflineState.DONE, 2 to OfflineState.DONE, reason = OfflineReason.PLAYLIST)
        val p = PlaylistKeepPlan.plan(listOf(listOf(t(1)), listOf(t(2))), emptySet(), x)
        assertTrue(p.removeKeys.isEmpty() && p.add.isEmpty())
    }
    @Test fun aManualDownloadIsNeverRemovedByAPlaylist() {
        val p = PlaylistKeepPlan.plan(emptyList(), emptySet(), ix(1 to OfflineState.DONE, reason = OfflineReason.MANUAL))
        assertTrue(p.removeKeys.isEmpty())
    }
    @Test fun aFavouriteSongStaysAndBecomesAFavouriteDownload() {
        val x = ix(1 to OfflineState.DONE, reason = OfflineReason.PLAYLIST)
        val p = PlaylistKeepPlan.plan(emptyList(), setOf(OfflineMath.keyOf(t(1).path)), x)
        assertTrue(p.removeKeys.isEmpty()); assertEquals(listOf(OfflineMath.keyOf(t(1).path)), p.demoteKeys)
    }

    // ---- the index keeps the strongest reason ----
    @Test fun playlistReasonUpgradesFavouriteButNeverManual() {
        val x = OfflineIndex(); x.add(t(1), OfflineReason.FAVORITE, 1); x.add(t(2), OfflineReason.MANUAL, 1)
        x.add(t(1), OfflineReason.PLAYLIST, 2); x.add(t(2), OfflineReason.PLAYLIST, 2)
        assertEquals(OfflineReason.PLAYLIST, x[OfflineMath.keyOf(t(1).path)]!!.reason)
        assertEquals(OfflineReason.MANUAL, x[OfflineMath.keyOf(t(2).path)]!!.reason)
    }
    @Test fun playlistReasonSurvivesSaving() {
        val x = OfflineIndex(); x.add(t(1), OfflineReason.PLAYLIST, 1)
        assertEquals(OfflineReason.PLAYLIST, OfflineIndex.decode(x.encode())[OfflineMath.keyOf(t(1).path)]!!.reason)
    }
    @Test fun theKeptPlaylistIdsAreAListThatSurvivesOddText() {
        assertEquals(setOf("u1", "u2"), KeptLists.decode(KeptLists.encode(setOf("u1", "u2"))))
        assertTrue(KeptLists.decode("").isEmpty())
    }
}
