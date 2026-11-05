package com.ipodemu.library

import org.junit.Assert.assertFalse
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** F6.3: "prodigy" means The Prodigy (played, many songs), not Prodigy of Mobb Deep; and the artist's page split into top songs, albums and singles. */
class SearchRankTopTest {
    private var n = 0
    private fun s(title: String, artist: String, album: String, year: Int) = Track(path = "/m/${++n}.flac", title = title, artist = artist, album = album, albumArtist = artist, genre = "",
        trackNo = 1, discNo = 1, durationMs = 200000, year = year, isMusic = true, artKey = null, mtime = 0, size = 0)
    private fun group(name: String, tracks: List<Track>) = Group(name, tracks, null)
    private fun albumsOf(all: List<Track>) = all.groupBy { it.album }.map { group(it.key, it.value) }

    @Test fun theArtistYouPlayAndHaveMostOfWins() {
        val prodigy = group("The Prodigy", List(5) { s("P$it", "The Prodigy", "A", 1997) })
        val mobb = group("Prodigy of Mobb Deep", List(9) { s("M$it", "Prodigy of Mobb Deep", "B", 2000) })
        val solo = group("Prodigy", List(2) { s("S$it", "Prodigy", "C", 2005) })
        val played = mapOf(prodigy.tracks[0].matchKey to 4)
        assertEquals("The Prodigy", SearchRankTop.artist(listOf(solo, mobb, prodigy), "prodigy", played)?.name)
        // nothing played: the one with most songs among the matching names (Mobb Deep is not one)
        assertEquals("The Prodigy", SearchRankTop.artist(listOf(solo, mobb, prodigy), "Prodigy", emptyMap())?.name)
        // played more beats bigger
        assertEquals("Prodigy", SearchRankTop.artist(listOf(solo, prodigy), "prodigy", mapOf(solo.tracks[0].matchKey to 9, prodigy.tracks[0].matchKey to 1))?.name)
    }

    @Test fun noArtistMeansNull() {
        assertNull(SearchRankTop.artist(listOf(group("Prodigy of Mobb Deep", listOf(s("x", "Prodigy of Mobb Deep", "B", 1)))), "prodigy", emptyMap()))
        assertNull(SearchRankTop.artist(listOf(group("The Prodigy", listOf(s("x", "The Prodigy", "B", 1)))), "the", emptyMap()))
        assertNull(SearchRankTop.artist(emptyList(), "", emptyMap()))
    }

    @Test fun thePageHasTopFivePopularAlbumsAndSinglesNewestFirst() {
        val fat = listOf("Smack My Bitch Up", "Breathe", "Diesel Power", "Funky Shit", "Serial Thrilla", "Mindfields", "Narayan").map { s(it, "The Prodigy", "The Fat of the Land", 1997) }
        val jilted = listOf("Voodoo People", "Poison", "No Good", "One Love", "Their Law", "Full Throttle", "Skylined").map { s(it, "The Prodigy", "Music for the Jilted Generation", 1994) }
        val single = listOf(s("Firestarter", "The Prodigy", "Firestarter", 1996), s("Firestarter (Instrumental)", "The Prodigy", "Firestarter", 1996))
        val ep = List(4) { s("Track $it", "The Prodigy", "Charly EP", 1991) }
        val namedEp = List(8) { s("Long $it", "The Prodigy", "Remixes - EP", 1992) }
        val all = fat + jilted + single + ep + namedEp
        val plays = mapOf(jilted[0].matchKey to 50, single[0].matchKey to 40, fat[0].matchKey to 30)
        val page = ArtistPage.of(group("The Prodigy", all), albumsOf(all), plays)
        assertEquals(listOf("Voodoo People", "Firestarter", "Smack My Bitch Up"), page.top.take(3).map { it.title })
        assertEquals(5, page.top.size)
        assertEquals(10, page.popular.size)
        assertTrue(page.popular.none { it in page.top })
        assertEquals(listOf("The Fat of the Land", "Music for the Jilted Generation"), page.albums.map { it.name })
        assertEquals(listOf("Firestarter", "Remixes - EP", "Charly EP"), page.singles.map { it.name })
    }

    @Test fun theAlbumSingleSplit() {
        fun g(name: String, count: Int) = group(name, List(count) { s("t$it", "A", name, 2000) })
        assertTrue(ArtistPage.isSingle(g("Hit", 1))); assertTrue(ArtistPage.isSingle(g("Three", 3))); assertFalse(ArtistPage.isSingle(g("Four", 4)))
        assertTrue("seven songs is an album", !ArtistPage.isSingle(g("Seven", 7)))
        assertTrue("the title says Single", ArtistPage.isSingle(g("Big Hit (Single)", 9)))
        assertTrue("the title says EP", ArtistPage.isSingle(g("Remixes EP", 8)))
        assertTrue("Deep is not EP", !ArtistPage.isSingle(g("Deep", 9)))
        assertEquals("Single", ArtistPage.kind(g("Hit", 1))); assertEquals("EP", ArtistPage.kind(g("Mini", 3))); assertEquals("Album", ArtistPage.kind(g("Big", 12)))
    }
}
