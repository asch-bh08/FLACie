package com.ipodemu.library

import com.ipodemu.library.SearchLayout.Section.ALBUMS
import com.ipodemu.library.SearchLayout.Section.ARTISTS
import com.ipodemu.library.SearchLayout.Section.DOWNLOAD
import com.ipodemu.library.SearchLayout.Section.MADE
import com.ipodemu.library.SearchLayout.Section.PLAYLISTS
import com.ipodemu.library.SearchLayout.Section.SONGS
import com.ipodemu.library.SearchLayout.Section.TOP
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** The phone's Search order, the same as the web's (owner, 25 Nov: "heads will roll" showed playlists and downloads before the song the library has). */
class SearchLayoutTest {
    @Test fun theTopResultComesFirstAndTheCatalogLast() {
        val order = SearchLayout.order(top = true, songs = true, artists = true, albums = true, playlists = true, made = true, download = true)
        assertEquals(listOf(TOP, SONGS, ARTISTS, ALBUMS, PLAYLISTS, MADE, DOWNLOAD), order)
    }

    @Test fun headsWillRollIsAboveThePlaylistsAndTheDownloads() {
        // the owner's search: the song, the playlists holding it, Spotify's playlists named like it, and the catalog
        val order = SearchLayout.order(top = true, songs = false, artists = false, albums = false, playlists = true, made = true, download = true)
        assertEquals(TOP, order.first())
        assertTrue(order.indexOf(TOP) < order.indexOf(PLAYLISTS) && order.indexOf(TOP) < order.indexOf(MADE))
        assertEquals(DOWNLOAD, order.last())
    }

    @Test fun withNoTopResultTheReadyMadePlaylistsAreTheAnswer() {
        assertEquals(listOf(MADE, SONGS, DOWNLOAD), SearchLayout.order(top = false, songs = true, artists = false, albums = false, playlists = false, made = true, download = true))
    }

    @Test fun eachChipShowsOnlyItsKind() {
        fun o(only: String) = SearchLayout.order(top = true, songs = true, artists = true, albums = true, playlists = true, made = true, download = true, only = only)
        assertEquals(listOf(SONGS, DOWNLOAD), o("songs"))
        assertEquals(listOf(ARTISTS), o("artists"))
        assertEquals(listOf(ALBUMS, DOWNLOAD), o("albums"))
        assertEquals(listOf(PLAYLISTS, MADE), o("playlists"))
        assertEquals(listOf(DOWNLOAD), o("downloadable"))
        assertEquals(listOf("All", "Songs", "Artists", "Albums", "Playlists", "Downloadable"), SearchLayout.KINDS.map { it.second })
    }

    @Test fun theCatalogsCopiesOfTheSongWeHaveAreFolded() {
        val asked = SearchLayout.askedSong("heads will roll", "Heads Will Roll")
        assertEquals("Heads Will Roll", asked)
        val catalog = listOf("Heads Will Roll (A-Trak Remix)", "Heads Will Roll", "Heads Will Roll - Live", "Heads Will Roll Tonight", "Zero")
        val (shown, folded) = SearchLayout.foldCopies(catalog, { it }, asked)
        assertEquals(listOf("Heads Will Roll Tonight", "Zero"), shown)
        assertEquals(listOf("Heads Will Roll (A-Trak Remix)", "Heads Will Roll", "Heads Will Roll - Live"), folded)
    }

    @Test fun aPartOfANameFoldsNothing() {
        assertNull(SearchLayout.askedSong("heads", "Heads Will Roll"))
        assertEquals("Heads Will Roll", SearchLayout.askedSong("heads will roll yeah yeah yeahs", "Heads Will Roll"))
        val (shown, folded) = SearchLayout.foldCopies(listOf("Heads Will Roll (Remix)"), { it }, null)
        assertEquals(listOf("Heads Will Roll (Remix)"), shown)
        assertTrue(folded.isEmpty())
    }
}
