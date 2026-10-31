package com.ipodemu.player

import com.ipodemu.library.Group
import com.ipodemu.library.Track
import com.ipodemu.library.sortKey
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.Random

class ExploreEngineTest {
    private fun song(i: Int, title: String, artist: String = "Artist", album: String = "Album", genre: String = "Pop", year: Int = 2010, mtime: Long = i.toLong(), dur: Long = 200_000) = Track(
        path = "/music/$i.flac", title = title, artist = artist, album = album, albumArtist = artist, genre = genre, trackNo = i % 12, discNo = 1,
        durationMs = dur, year = year, isMusic = true, artKey = null, mtime = mtime, size = 0, filePath = "$i.flac")

    private val titles = listOf("The Wall", "a day", "Alpha", "An Ending", "beta", "Zebra", "the zoo", "Gamma", "gamma", "Delta", "#1", "9 lives", "Echo", "Beta")
    private val songs: List<Track> = titles.mapIndexed { i, t -> song(i, t, artist = if (i % 3 == 0) "The Band" else "Solo $i", year = 1980 + i * 3, mtime = (i * 7919L) % 13, dur = 100_000L + (i * 37L) % 11) }
        .sortedBy { sortKey(it.title) }

    @Test fun aJellyfinStreamWithNoServerPathHasNoExtensionNotAPieceOfTheUrl() {
        val t = song(1, "Blow").copy(path = "http://100.114.148.48:8096/Audio/0123456789abcdef0123456789abcdef/stream?static=true", filePath = "", source = com.ipodemu.library.TrackSource.JELLYFIN)
        assertEquals("", trackExt(t))
    }

    @Test fun extensionsStillComeFromTheFileName() {
        assertEquals("flac", trackExt(song(1, "x")))
        assertEquals("mp3", trackExt(song(1, "x").copy(filePath = "/media/A.B/Song.MP3")))
        assertEquals("m4a", trackExt(song(1, "x").copy(filePath = "", path = "/sdcard/Music/a.m4a?x=1")))
    }

    @Test fun descendingTitleSortMatchesTheOldSelectorSortIncludingTies() {
        assertEquals(songs.sortedByDescending { sortKey(it.title) }, ExploreEngine.sortSongs(songs, "za"))
    }

    @Test fun artistSortUsesArtistThenAlbumThenDiscThenTrack() {
        val expected = songs.sortedWith(compareBy({ sortKey(it.artist) }, { it.album }, { it.discNo }, { it.trackNo }))
        assertEquals(expected, ExploreEngine.sortSongs(songs, "artist"))
    }

    @Test fun otherSortsKeepTheirMeaning() {
        assertEquals(songs.sortedByDescending { it.mtime }, ExploreEngine.sortSongs(songs, "new"))
        assertEquals(songs.sortedByDescending { it.year }, ExploreEngine.sortSongs(songs, "year"))
        assertEquals(songs.sortedBy { it.year }, ExploreEngine.sortSongs(songs, "oldest"))
        assertEquals(songs.sortedByDescending { it.durationMs }, ExploreEngine.sortSongs(songs, "long"))
        assertEquals(songs, ExploreEngine.sortSongs(songs, "az"))
    }

    @Test fun sortedByCachedCallsTheKeyOncePerItemAndIsStable() {
        var calls = 0
        val l = List(500) { it }.shuffled(Random(3))
        val out = with(ExploreEngine) { l.sortedByCached { calls++; it / 10 } }
        assertEquals(500, calls)
        assertEquals(l.sortedBy { it / 10 }, out)
    }

    private fun run(q: ExploreQuery, list: List<Track> = songs, albums: List<Group> = emptyList()) =
        ExploreEngine.run(q, list, { albums }, { emptyList() }, { emptyList() })

    @Test fun unfilteredAzIsTheLibraryItself() {
        val r = run(ExploreQuery("Songs"))
        assertTrue(r.songs === songs)
        assertEquals(songs.size, r.total)
    }

    @Test fun letterFilterUsesTheArticleFreeSortKey() {
        val r = run(ExploreQuery("Songs", letter = "W"))
        assertEquals(listOf("The Wall"), r.songs.map { it.title })
        val hash = run(ExploreQuery("Songs", letter = "#"))
        assertEquals(setOf("#1", "9 lives"), hash.songs.map { it.title }.toSet())
    }

    @Test fun textFilterIsExactFirstThenFuzzy() {
        val exact = run(ExploreQuery("Songs", query = "gamma"))
        assertFalse(exact.fuzzy)
        assertEquals(2, exact.songs.size)
        val typo = run(ExploreQuery("Songs", query = "gamna"))
        assertTrue(typo.fuzzy)
        assertEquals(2, typo.songs.size)
        assertTrue(run(ExploreQuery("Songs", query = "qqqqqq")).songs.isEmpty())
    }

    @Test fun decadeAndGenreFilters() {
        val withRock = songs + song(99, "Riff", genre = "Rock", year = 1971)
        val r = run(ExploreQuery("Songs", genre = "rock"), withRock)
        assertEquals(listOf("Riff"), r.songs.map { it.title })
        val d = run(ExploreQuery("Songs", decade = "1970s"), withRock)
        assertEquals(listOf("Riff"), d.songs.map { it.title })
        assertEquals(withRock.size, d.total)
    }

    @Test fun albumsAreFilteredAndSortedOffTheirTracks() {
        val a = Group("Old", listOf(song(1, "x", year = 1970, mtime = 5)), null)
        val b = Group("New", listOf(song(2, "y", year = 2020, mtime = 1)), null)
        val c = Group("Mid", listOf(song(3, "z", year = 1995, mtime = 9)), null)
        val all = listOf(a, b, c)
        fun names(sort: String) = run(ExploreQuery("Albums", sort = sort), albums = all).groups.map { it.name }
        assertEquals(listOf("Old", "New", "Mid"), names("az"))
        assertEquals(listOf("New", "Mid", "Old"), names("year"))
        assertEquals(listOf("Old", "Mid", "New"), names("oldest"))
        assertEquals(listOf("Mid", "Old", "New"), names("new"))
        assertEquals(listOf("Old", "New", "Mid"), names("za"))
        assertEquals(listOf("Old"), run(ExploreQuery("Albums", query = "old"), albums = all).groups.map { it.name })
    }

    @Test fun fmtTimeMatchesTheFormatterItReplaced() {
        for (ms in listOf(0L, 999L, 1_000L, 59_000L, 61_000L, 600_000L, 3_599_000L, 3_600_000L, 3_725_000L, 36_000_000L, -5L)) {
            val s = (ms / 1000).coerceAtLeast(0); val h = s / 3600
            val old = if (h > 0) "%d:%02d:%02d".format(h, s % 3600 / 60, s % 60) else "%d:%02d".format(s / 60, s % 60)
            assertEquals(old, fmtTime(ms))
        }
    }
}
