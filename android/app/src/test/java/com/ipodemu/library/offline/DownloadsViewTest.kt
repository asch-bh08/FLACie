package com.ipodemu.library.offline

import com.ipodemu.library.Track
import com.ipodemu.library.TrackSource
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/** The Downloads section: the little mark on songs and lists, which playlists and albums count as downloaded, and the words at the top while songs are on their way. */
class DownloadsViewTest {
    private fun id(n: Int) = "%032x".format(n)
    private fun path(n: Int) = "http://jf/Audio/${id(n)}/stream?static=true"
    private fun t(n: Int, album: String = "Al") = Track(
        path = path(n), title = "Song $n", artist = "A", album = album, albumArtist = "A", genre = "", trackNo = n, discNo = 1,
        durationMs = 200_000, year = 2000, isMusic = true, artKey = null, mtime = 0, size = 0, source = TrackSource.JELLYFIN)
    private fun ix(vararg e: Pair<Int, OfflineState>): OfflineIndex {
        val x = OfflineIndex(); for ((n, s) in e) { val en = x.add(t(n), OfflineReason.MANUAL, 1); en.state = s; if (s == OfflineState.DONE) en.bytes = 5_000_000 }; return x
    }
    private fun st(x: OfflineIndex): (String) -> OfflineState? = { p -> x[OfflineMath.keyOf(p)]?.state }

    // ---- the mark on a song ----
    @Test fun aFinishedSongIsMarkedDownloaded() = assertEquals(DownloadMark.DOWNLOADED, DownloadMarks.ofSong(OfflineState.DONE))
    @Test fun aSongOnItsWayShowsTheSmallBusyMark() {
        assertEquals(DownloadMark.DOWNLOADING, DownloadMarks.ofSong(OfflineState.QUEUED)); assertEquals(DownloadMark.DOWNLOADING, DownloadMarks.ofSong(OfflineState.DOWNLOADING))
    }
    @Test fun failedAndUnknownSongsCarryNoMark() {
        assertEquals(DownloadMark.NONE, DownloadMarks.ofSong(OfflineState.FAILED)); assertEquals(DownloadMark.NONE, DownloadMarks.ofSong(null))
    }

    // ---- the mark on a playlist or album ----
    @Test fun aListWithEverySongOnThePhoneIsDownloaded() {
        val x = ix(1 to OfflineState.DONE, 2 to OfflineState.DONE)
        assertEquals(DownloadMark.DOWNLOADED, DownloadMarks.ofList(listOf(path(1), path(2)), st(x)))
    }
    @Test fun aHalfDownloadedListShowsNothing() {
        val x = ix(1 to OfflineState.DONE)
        assertEquals(DownloadMark.NONE, DownloadMarks.ofList(listOf(path(1), path(2)), st(x)))
    }
    @Test fun aListWhoseSongsAreAllOnTheirWayShowsTheBusyMark() {
        val x = ix(1 to OfflineState.DONE, 2 to OfflineState.QUEUED)
        assertEquals(DownloadMark.DOWNLOADING, DownloadMarks.ofList(listOf(path(1), path(2)), st(x)))
    }
    @Test fun songsThatCanNotBeDownloadedDoNotHoldTheMarkBack() {
        val x = ix(1 to OfflineState.DONE)
        assertEquals(DownloadMark.DOWNLOADED, DownloadMarks.ofList(listOf(path(1), "/storage/music/local.flac"), st(x)))
        assertEquals(DownloadMark.NONE, DownloadMarks.ofList(listOf("/storage/music/local.flac"), st(x)))
    }
    @Test fun anEmptyListHasNoMark() = assertEquals(DownloadMark.NONE, DownloadMarks.ofList(emptyList(), st(OfflineIndex())))
    @Test fun aSongTwiceInAListCountsOnce() {
        assertEquals(DownloadMark.DOWNLOADED, DownloadMarks.ofList(listOf(path(1), path(1)), st(ix(1 to OfflineState.DONE))))
    }

    // ---- which lists the Downloads section shows ----
    @Test fun onlyFullyDownloadedListsAreShownWithTheirSizeAndSongs() {
        val x = ix(1 to OfflineState.DONE, 2 to OfflineState.DONE, 3 to OfflineState.DONE, 4 to OfflineState.QUEUED)
        val specs = listOf(
            ListSpec("p1", "Road trip", "Playlist", "ak1", listOf(path(1), path(2))),
            ListSpec("p2", "Mixed", "Playlist", null, listOf(path(3), path(4))),
            ListSpec("p3", "Never", "Playlist", null, listOf(path(9))),
        )
        val out = DownloadsOverview.lists(specs) { x[OfflineMath.keyOf(it)] }
        assertEquals(listOf("Road trip"), out.map { it.name })
        assertEquals(2, out[0].songs); assertEquals(10_000_000L, out[0].bytes)
        assertEquals("2 songs · 10 MB", out[0].detail)
        assertEquals("ak1", out[0].artKey)
    }
    @Test fun listsAreSortedByNameIgnoringCase() {
        val x = ix(1 to OfflineState.DONE)
        val specs = listOf(ListSpec("b", "beta", "", null, listOf(path(1))), ListSpec("a", "Alpha", "", null, listOf(path(1))), ListSpec("c", "Zed", "", null, listOf(path(1))))
        assertEquals(listOf("Alpha", "beta", "Zed"), DownloadsOverview.lists(specs) { x[OfflineMath.keyOf(it)] }.map { it.name })
    }
    @Test fun oneSongIsSaidInTheSingular() {
        val x = ix(1 to OfflineState.DONE)
        assertEquals("1 song · 5 MB", DownloadsOverview.lists(listOf(ListSpec("a", "A", "", null, listOf(path(1))))) { x[OfflineMath.keyOf(it)] }[0].detail)
    }

    // ---- the songs and the top line ----
    @Test fun allSongsAreTheFinishedOnesByTitle() {
        val x = ix(3 to OfflineState.DONE, 1 to OfflineState.DONE, 2 to OfflineState.QUEUED)
        assertEquals(listOf("Song 1", "Song 3"), DownloadsOverview.songs(x.entries).map { it.title })
    }
    @Test fun theTopLineSaysHowFarItIs() {
        assertEquals("Downloading 12 of 54", DownloadsOverview.progressLine(12, 42, null))
    }
    @Test fun theTopLineSaysWhyItWaits() {
        assertEquals("Waiting for Wi-Fi · 12 of 54", DownloadsOverview.progressLine(12, 42, "Waiting for Wi-Fi"))
    }
    @Test fun noTopLineWhenNothingIsOnItsWay() = assertNull(DownloadsOverview.progressLine(12, 0, null))
    @Test fun failedSongsGetTheirOwnLine() {
        assertEquals("1 song could not download", DownloadsOverview.failedLine(1)); assertEquals("3 songs could not download", DownloadsOverview.failedLine(3)); assertNull(DownloadsOverview.failedLine(0))
    }
    @Test fun theStorageLineIsSongsAndSize() = assertEquals("14 songs · 1.2 GB", DownloadsOverview.storageLine(14, 1_200_000_000L))
    @Test fun theStorageLineForNothingSaysSo() = assertEquals("Nothing downloaded yet", DownloadsOverview.storageLine(0, 0))
}
