package com.ipodemu.playback

import com.ipodemu.library.Group
import com.ipodemu.library.Track
import com.ipodemu.library.TrackSource
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class BrowseTreeTest {
    private fun t(n: Int, album: String = "Al", artist: String = "Ar", disc: Int = 1, no: Int = n) = Track(
        path = "http://jf/Audio/%032x/stream?static=true".format(n), title = "Song $n", artist = artist, album = album, albumArtist = artist, genre = "", trackNo = no, discNo = disc,
        durationMs = 1000, year = 0, isMusic = true, artKey = "jf%032x".format(n), mtime = 0, size = 0, source = TrackSource.JELLYFIN)

    private inner class Src(
        val songs: List<Track> = (1..5).map { n -> t(n) }, val favs: List<Track> = emptyList(), val recents: List<Track> = emptyList(),
        val pls: Map<String, List<Track>> = emptyMap(),
    ) : BrowseSource {
        override fun recent() = recents; override fun favorites() = favs; override fun downloaded() = songs.take(2)
        override fun playlists() = pls.keys.map { it to "Playlist $it" }
        override fun playlistTracks(id: String) = pls[id] ?: emptyList()
        override fun albums() = songs.groupBy { it.albumKey }.map { (_, v) -> Group(v[0].album, v, v[0].artKey) }
        override fun artists() = songs.groupBy { it.artist }.map { (k, v) -> Group(k, v, v[0].artKey) }
        override fun search(query: String) = songs.filter { it.title.contains(query, true) }
        override fun allSongs() = songs
    }

    @Test fun rootIsFourTabsWithIcons() {
        val r = BrowseTree.root()
        assertEquals(listOf("Home", "Playlists", "Albums", "Artists"), r.map { it.title })
        assertTrue(r.all { it.browsable && !it.playable && it.icon != null })
        assertEquals(r.map { it.id }, BrowseTree.children(BrowseTree.ROOT, 0, 50, Src()).map { it.id })
    }

    @Test fun homeStartsWithShuffleThenYourMusicThenRecentSongs() {
        val src = Src(recents = (1..20).map { t(it) })
        val home = BrowseTree.children(BrowseTree.HOME, 0, 100, src)
        assertEquals("Shuffle all", home[0].title); assertTrue(home[0].playable && !home[0].browsable)
        assertEquals(listOf("Favourites", "Downloaded"), home.filter { it.group == "Your music" }.map { it.title })
        assertTrue(home.filter { it.group == "Your music" }.all { it.browsable && it.icon != null })
        val recent = home.filter { it.group == "Recently played" }
        assertEquals(BrowseTree.HOME_RECENT, recent.size); assertEquals("Song 1", recent[0].title); assertTrue(recent.all { it.playable })
        assertTrue(BrowseTree.children(BrowseTree.HOME, 0, 100, Src()).none { it.group == "Recently played" })
    }

    @Test fun shuffleAllPlaysTheWholeLibraryCapped() {
        val src = Src(songs = (1..500).map { t(it) })
        val r = BrowseTree.resolve(BrowseTree.SHUFFLE, src)!!
        assertEquals(BrowseTree.MAX_QUEUE, r.tracks.size); assertEquals(0, r.index)
        assertEquals(r.tracks.size, r.tracks.map { it.path }.toSet().size)
        assertNull(BrowseTree.resolve(BrowseTree.SHUFFLE, Src(songs = emptyList())))
    }

    @Test fun coverListsAreGridsAndSongsSayArtistAndAlbum() {
        val src = Src(pls = mapOf("p1" to listOf(t(3), t(4))))
        val pl = BrowseTree.children(BrowseTree.PLAYLISTS, 0, 50, src)
        assertTrue(pl[0].grid); assertEquals(t(3).artKey, pl[0].artKey); assertEquals("2 songs", pl[0].subtitle)
        assertTrue(BrowseTree.children(BrowseTree.ALBUMS, 0, 50, src).all { it.grid && it.artKey != null })
        assertTrue(BrowseTree.children(BrowseTree.ARTISTS, 0, 50, src).all { it.grid })
        val song = BrowseTree.children(pl[0].id, 0, 50, src)[2]
        assertFalse(song.grid); assertEquals("Ar · Al", song.subtitle)
    }

    @Test fun albumsSortByTheirWordsNotLeadingQuotesOrDots() {
        val src = Src(songs = listOf(t(1, "\"...And Crazy\""), t(2, "Bravo"), t(3, "\"Sugar Hill\"")))
        assertEquals(listOf("\"...And Crazy\"", "Bravo", "\"Sugar Hill\""), BrowseTree.children(BrowseTree.ALBUMS, 0, 50, src).map { it.title })
    }

    @Test fun insideAnAlbumPlayAllAndShuffleComeFirst() {
        val src = Src(songs = (1..4).map { t(it) })
        val album = BrowseTree.children(BrowseTree.ALBUMS, 0, 50, src)[0]
        val inside = BrowseTree.children(album.id, 0, 50, src)
        assertEquals(listOf("Play all", "Shuffle"), inside.take(2).map { it.title })
        assertTrue(inside.take(2).all { it.playable && !it.browsable && it.icon != null })
        assertEquals(4, inside.drop(2).size)
        val all = BrowseTree.resolve(inside[0].id, src)!!; assertEquals(0, all.index); assertEquals("Song 1", all.tracks[0].title); assertEquals(4, all.tracks.size)
        val shuf = BrowseTree.resolve(inside[1].id, src)!!; assertEquals(4, shuf.tracks.size); assertEquals(4, shuf.tracks.map { it.path }.toSet().size)
        // the second page of songs has no buttons in it
        assertTrue(BrowseTree.children(album.id, 1, 3, src).none { it.title == "Play all" })
    }

    @Test fun homeShowsRecentSongsAndPlaylistsAsCovers() {
        val src = Src(recents = (1..3).map { t(it) }, pls = mapOf("p1" to listOf(t(5)), "p2" to listOf(t(6))))
        val home = BrowseTree.children(BrowseTree.HOME, 0, 100, src)
        assertTrue(home.filter { it.group == "Recently played" }.all { it.cover })
        val pls = home.filter { it.group == "Your playlists" }
        assertEquals(listOf("Playlist p1", "Playlist p2"), pls.map { it.title }); assertTrue(pls.all { it.cover && it.browsable })
        assertFalse(home.first { it.title == "Favourites" }.cover)
    }

    @Test fun homeShowsEveryPlaylistBeforeYourMusic() {
        val src = Src(pls = (1..10).associate { "p$it" to listOf(t(it)) })
        val home = BrowseTree.children(BrowseTree.HOME, 0, 100, src)
        val pls = home.filter { it.group == "Your playlists" }
        assertEquals(10, pls.size)   // "pop" was the 7th and Home stopped at 6
        assertTrue(home.indexOf(pls.last()) < home.indexOfFirst { it.title == "Favourites" })
    }

    @Test fun oneArtistHowEverTheyAreSpelt() {
        val songs = listOf(t(1, artist = "2Pac"), t(2, artist = "2PAC"), t(3, artist = "2Pac; Dr. Dre"), t(4, artist = "2pac"), t(5, artist = "Blur"))
        val artists = BrowseTree.children(BrowseTree.ARTISTS, 0, 50, Src(songs = songs))
        assertEquals(listOf("2Pac", "Blur"), artists.map { it.title })
        assertEquals(4 + 2, BrowseTree.children(artists[0].id, 0, 50, Src(songs = songs)).size)   // Play all, Shuffle and the four songs
    }

    @Test fun everyListTheCarShowsIsRefreshed() =
        assertTrue(BrowseTree.LIVE.containsAll(BrowseTree.root().map { it.id } + listOf(BrowseTree.RECENT, BrowseTree.FAVORITES, BrowseTree.DOWNLOADED)))

    @Test fun songsCarryTheirListSoPlayingQueuesTheList() {
        val src = Src(favs = (1..4).map { t(it) })
        val kids = BrowseTree.children(BrowseTree.FAVORITES, 0, 50, src)
        assertEquals(4, kids.size); assertTrue(kids.all { it.playable && !it.browsable })
        val r = BrowseTree.resolve(kids[2].id, src)!!
        assertEquals(4, r.tracks.size); assertEquals(2, r.index); assertEquals("Song 3", r.tracks[r.index].title)
    }

    @Test fun pagingSlicesAndNeverExceedsTheCap() {
        val src = Src(favs = (1..250).map { t(it) })
        assertEquals(50, BrowseTree.children(BrowseTree.FAVORITES, 0, 50, src).size)
        assertEquals("Song 51", BrowseTree.children(BrowseTree.FAVORITES, 1, 50, src)[0].title)
        assertEquals(BrowseTree.MAX_PAGE, BrowseTree.children(BrowseTree.FAVORITES, 0, 5000, src).size)
        assertEquals(50, BrowseTree.children(BrowseTree.FAVORITES, 4, 50, src).size)
        assertTrue(BrowseTree.children(BrowseTree.FAVORITES, 5, 50, src).isEmpty()); assertTrue(BrowseTree.children(BrowseTree.FAVORITES, 999999, 50, src).isEmpty())
        assertEquals("Song 1", BrowseTree.children(BrowseTree.FAVORITES, -3, 50, src)[0].title)
    }

    @Test fun albumsAndArtistsAreSortedAndOpenToTheirSongs() {
        val songs = listOf(t(1, "Zed", "B"), t(2, "alpha", "A", no = 2), t(3, "alpha", "A", no = 1))
        val src = Src(songs = songs)
        val albums = BrowseTree.children(BrowseTree.ALBUMS, 0, 50, src)
        assertEquals(listOf("alpha", "Zed"), albums.map { it.title }); assertTrue(albums.all { it.browsable && it.playable })
        val inside = BrowseTree.children(albums[0].id, 0, 50, src)
        assertEquals(listOf("Song 3", "Song 2"), inside.drop(2).map { it.title })   // in track order
        val artists = BrowseTree.children(BrowseTree.ARTISTS, 0, 50, src)
        assertEquals(listOf("A", "B"), artists.map { it.title })
        assertEquals(2 + 2, BrowseTree.children(artists[0].id, 0, 50, src).size)
    }

    @Test fun playingAFolderQueuesItsSongsFromTheStart() {
        val src = Src(pls = mapOf("p1" to (1..3).map { t(it) }))
        val pl = BrowseTree.children(BrowseTree.PLAYLISTS, 0, 50, src)
        assertEquals(1, pl.size); assertEquals("Playlist p1", pl[0].title)
        val r = BrowseTree.resolve(pl[0].id, src)!!
        assertEquals(3, r.tracks.size); assertEquals(0, r.index)
        val album = BrowseTree.children(BrowseTree.ALBUMS, 0, 50, Src())[0]
        assertEquals(5, BrowseTree.resolve(album.id, Src())!!.tracks.size)
    }

    @Test fun unknownOrEmptyThingsResolveToNothingAndHaveNoChildren() {
        val src = Src()
        assertNull(BrowseTree.resolve("s:nonsense", src)); assertNull(BrowseTree.resolve("s:favorites\u001Fhttp://gone", src)); assertNull(BrowseTree.resolve("pl:none", src)); assertNull(BrowseTree.resolve("zzz", src))
        assertNull(BrowseTree.resolve(BrowseTree.FAVORITES, src))   // an empty favourites list is not something to play
        assertTrue(BrowseTree.children("s:x", 0, 50, src).isEmpty()); assertTrue(BrowseTree.children("zzz", 0, 50, src).isEmpty()); assertTrue(BrowseTree.children("pl:none", 0, 50, src).isEmpty())
    }

    @Test fun aHugeListIsWindowedAroundTheChosenSong() {
        val src = Src(favs = (1..5000).map { t(it) })
        val kids = BrowseTree.children(BrowseTree.FAVORITES, 0, 100, src)
        val r = BrowseTree.resolve(BrowseTree.songId(BrowseTree.FAVORITES, src.favs[2500].path), src)!!
        assertEquals(BrowseTree.MAX_QUEUE, r.tracks.size); assertEquals("Song 2501", r.tracks[r.index].title); assertTrue(r.index in 0 until BrowseTree.MAX_QUEUE)
        val first = BrowseTree.resolve(kids[0].id, src)!!
        assertEquals(0, first.index); assertEquals(BrowseTree.MAX_QUEUE, first.tracks.size)
        val last = BrowseTree.resolve(BrowseTree.songId(BrowseTree.FAVORITES, src.favs.last().path), src)!!
        assertEquals("Song 5000", last.tracks[last.index].title); assertEquals(last.tracks.size - 1, last.index)
    }

    @Test fun searchFindsSongsAndPlaysThem() {
        val src = Src()
        val nodes = BrowseTree.searchNodes("song 3", 0, 50, src)
        assertEquals(listOf("Song 3"), nodes.map { it.title })
        assertEquals("Song 3", BrowseTree.resolve(nodes[0].id, src)!!.tracks[0].title)
        assertNotNull(BrowseTree.searchPlay("song", src)); assertNull(BrowseTree.searchPlay("nothing like it", src))
        assertFalse(BrowseTree.searchNodes("zzz", 0, 50, src).isNotEmpty())
    }

    @Test fun songIdsDoNotCollideAcrossLists() {
        assertTrue(BrowseTree.songId("favorites", "p") != BrowseTree.songId("recent", "p"))
    }
}
