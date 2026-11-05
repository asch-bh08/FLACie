package com.ipodemu.playback

import com.ipodemu.library.Group
import com.ipodemu.library.LyricLine
import com.ipodemu.library.Track
import com.ipodemu.library.TrackSource
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class CarActionsTest {
    @Test fun the_mic_is_first_then_heart_shuffle_and_dj() {
        assertEquals(listOf(CarActions.Id.TALK, CarActions.Id.FAVORITE, CarActions.Id.SHUFFLE, CarActions.Id.DJ), CarActions.order(carDj = true, speedButton = true))
    }

    @Test fun never_more_than_four_buttons() {
        for (dj in listOf(true, false)) for (speed in listOf(true, false)) assertTrue(CarActions.order(dj, speed).size <= CarActions.MAX)
        assertEquals(4, CarActions.MAX)
    }

    @Test fun without_the_dj_the_old_buttons_stay_and_speed_only_when_asked() {
        assertEquals(listOf(CarActions.Id.REPEAT, CarActions.Id.FAVORITE, CarActions.Id.SHUFFLE), CarActions.order(false, false))
        assertEquals(CarActions.Id.SPEED, CarActions.order(false, true).last())
    }

    @Test fun skip_previous_and_next_slots_are_reserved() {
        assertEquals(2, CarActions.SLOT_RESERVATION_KEYS.size)
        assertTrue(CarActions.SLOT_RESERVATION_KEYS.any { it.endsWith("ACTION_SKIP_TO_PREVIOUS") })
        assertTrue(CarActions.SLOT_RESERVATION_KEYS.any { it.endsWith("ACTION_SKIP_TO_NEXT") })
    }

    @Test fun every_car_button_has_a_large_simple_icon() {
        for (n in listOf("ic_car_mic", "ic_car_mic_on", "ic_car_heart", "ic_car_heart_on", "ic_car_shuffle_on", "ic_car_shuffle_off", "ic_car_dj_on", "ic_car_dj_off")) {
            val f = java.io.File("src/main/res/drawable/$n.xml")
            assertTrue("$n missing", f.exists())
            assertTrue("$n should be 48dp", f.readText().contains("android:width=\"48dp\""))
        }
    }

    @Test fun a_caption_changes_the_line_at_most_every_two_seconds() {
        val g = CarActions.Gate()
        assertEquals("one", g.show("one", 10_000))
        assertEquals("one", g.show("two", 11_000))
        assertEquals(1_000L, g.waitMs("two", 11_000))
        assertEquals("two", g.show("two", 12_000))
        assertEquals(0L, g.waitMs("two", 12_000))
    }

    @Test fun when_the_dj_stops_the_artist_comes_back_after_the_gap() {
        val g = CarActions.Gate()
        g.show("hello", 0)
        assertEquals("hello", g.show(null, 500))
        assertNull(g.show(null, 2_000))
    }

    @Test fun the_first_line_is_never_held() {
        assertEquals("hi", CarActions.Gate().show("hi", 0))
    }

    @Test fun the_caption_beats_the_lyric_and_the_lyric_needs_its_setting() {
        assertEquals("DJ says", CarActions.artistLine("DJ says", "la la", true))
        assertEquals("DJ says", CarActions.artistLine("DJ says", "la la", false))
        assertEquals("la la", CarActions.artistLine(null, "la la", true))
        assertNull(CarActions.artistLine(null, "la la", false))
        assertNull(CarActions.artistLine("", "  ", true))
    }

    private val lines = listOf(LyricLine(1_000, "one"), LyricLine(5_000, " two "), LyricLine(9_000, "three"))

    @Test fun the_lyric_line_follows_the_position() {
        assertNull(CarActions.lyricAt(lines, true, 0))
        assertEquals("one", CarActions.lyricAt(lines, true, 1_000))
        assertEquals("two", CarActions.lyricAt(lines, true, 4_800))
        assertEquals("three", CarActions.lyricAt(lines, true, 60_000))
        assertNull(CarActions.lyricAt(lines, false, 6_000))
    }

    @Test fun the_next_lyric_is_due_in_time() {
        assertEquals(750L, CarActions.untilNextLine(lines, true, 0))
        assertEquals(3_750L, CarActions.untilNextLine(lines, true, 1_000))
        assertEquals(1_875L, CarActions.untilNextLine(lines, true, 1_000, speed = 2f))
        assertNull(CarActions.untilNextLine(lines, true, 9_000))
        assertNull(CarActions.untilNextLine(lines, false, 0))
    }

    private fun t(n: Int) = Track(
        path = "http://jf/$n", title = "Song $n", artist = "Ar", album = "Al$n", albumArtist = "Ar", genre = "", trackNo = n, discNo = 1,
        durationMs = 1000, year = 0, isMusic = true, artKey = "k$n", mtime = 0, size = 0, source = TrackSource.JELLYFIN)
    private val songs = (1..6).map { t(it) }
    private val src = object : BrowseSource {
        override fun recent() = songs.take(3); override fun favorites() = songs.take(2); override fun downloaded() = emptyList<Track>()
        override fun playlists() = listOf("a" to "Pop", "b" to "Rock"); override fun playlistTracks(id: String) = songs
        override fun albums() = songs.groupBy { it.albumKey }.map { (_, v) -> Group(v[0].album, v, v[0].artKey) }
        override fun artists() = listOf(Group("Ar", songs, "k1")); override fun search(query: String) = songs
        override fun allSongs() = songs
    }

    @Test fun playlists_albums_and_artists_browse_as_grids() {
        for (tab in listOf(BrowseTree.PLAYLISTS, BrowseTree.ALBUMS, BrowseTree.ARTISTS)) {
            assertTrue("$tab tab", BrowseTree.root().first { it.id == tab }.grid)
            val kids = BrowseTree.children(tab, 0, 100, src)
            assertTrue(kids.isNotEmpty()); assertTrue("$tab children", kids.all { it.grid && it.browsable })
        }
    }

    @Test fun every_song_plays_within_two_taps_of_a_tab() {
        val tabs = BrowseTree.root()
        assertTrue(tabs.size <= 4)
        // taps from a tab's own screen: open a list (1), then its Play all, Shuffle or a song (2); nothing opens a third level
        fun levels(id: String): Int = 1 + (BrowseTree.children(id, 0, 100, src).filter { it.browsable }.maxOfOrNull { levels(it.id) } ?: 0)
        for (tab in tabs) assertTrue("${tab.id} is deeper than two taps", levels(tab.id) <= 2)
        for (id in listOf("pl:a", "al:" + songs[0].albumKey, "ar:Ar")) assertEquals(listOf("all:$id", "shuf:$id"), BrowseTree.children(id, 0, 100, src).take(2).map { it.id })
    }
}
