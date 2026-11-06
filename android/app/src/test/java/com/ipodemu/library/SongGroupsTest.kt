package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Test

/** Search's versions as one row a song (owner, 6 Nov: "you got the love" by The Source showed 10 versions; "filter them and list like Spotify would"). */
class SongGroupsTest {
    private fun s(artist: String, title: String) = WebCatalog.Song(artist, title, "", null, 200_000, 0, 0)

    @Test fun theVersionsOfOneSongByOneArtistAreOneRowLedByTheFirst() {
        val found = listOf(
            s("The Source", "You Got the Love (feat. Candi Staton)"), s("Florence + the Machine", "You Got the Love"),
            s("The Source", "You Got the Love (Now Voyager Mix)"), s("The Source", "You Got the Love - 2006 Remix"), s("Candi Staton", "You Got the Love"),
            s("The Source", "You Got the Love (New Voyager Radio Edit)"),
        )
        val g = WebCatalog.groups(found)
        assertEquals(listOf("The Source", "Florence + the Machine", "Candi Staton"), g.map { it.first.artist })
        assertEquals("You Got the Love (feat. Candi Staton)", g[0].first.title)
        assertEquals(3, g[0].second.size)   // the other versions, in their order
        assertEquals(0, g[1].second.size)
    }
}
