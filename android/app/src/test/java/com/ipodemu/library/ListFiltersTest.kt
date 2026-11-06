package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Test

/** The Playlists chip's genre and decade choices on the phone, from the library, as the web's Filters panel has them. */
class ListFiltersTest {
    private fun song(i: Int, genre: String, year: Int) = Track(
        path = "http://x/Audio/${"%032x".format(i)}/stream?static=true", title = "Song $i", artist = "A", album = "LP", albumArtist = "A", genre = genre, trackNo = 1, discNo = 1,
        durationMs = 200_000, year = year, isMusic = true, artKey = null, mtime = 0, size = 0, source = TrackSource.JELLYFIN, filePath = "s$i.flac")

    @Test fun genresMostSongsFirstAndDecadesNewestFirst() {
        val songs = List(5) { song(it, "Rock", 1994) } + List(3) { song(10 + it, "Electronic; Dance", 2003) } + song(20, "", 0) + song(21, "pop", 2021)
        val f = ListFilters.of(songs)
        assertEquals(listOf("Rock", "Electronic", "Dance", "Pop"), f.genres)
        assertEquals(listOf("2020s", "2000s", "1990s"), f.decades)
    }

    @Test fun atMostTwelveGenres() {
        assertEquals(12, ListFilters.of(List(20) { song(it, "Genre $it", 2000) }).genres.size)
    }
}
