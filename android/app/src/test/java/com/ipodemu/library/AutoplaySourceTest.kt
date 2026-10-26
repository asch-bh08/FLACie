package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import java.io.IOException

/** Autoplay asks the server's engine first; with no signal or a server error it picks the way it always has. */
class AutoplaySourceTest {
    private fun song(title: String, artist: String = "Artist", path: String = "http://phone/$title.flac") = Track(
        path = path, title = title, artist = artist, album = "LP", albumArtist = artist, genre = "", trackNo = 1, discNo = 1, durationMs = 200_000,
        year = 2020, isMusic = true, artKey = null, mtime = 0, size = 0, source = TrackSource.JELLYFIN)

    private val serverJson = """{"picks":[
        {"path":"http://server/Audio/abc/stream?static=true","title":"One","artist":"Artist","album":"LP","durationMs":200000,"isNew":false,"why":"Because you played Two"},
        {"path":null,"title":"Fresh","artist":"Other","album":"New LP","durationMs":180000,"isNew":true,"why":"New from Other"},
        {"path":"/gone.flac","title":"Missing","artist":"Nobody","album":"","durationMs":1,"isNew":false,"why":null}]}"""

    @Test fun Autoplay_uses_the_server_and_falls_back_offline() {
        val one = song("One")
        // the server's path is not the phone's: the song is found by title and artist
        val resolve = { p: RecPick -> if (p.title == "One") one else null }
        val plan = AutoplaySource.choose({ RecPick.parseNext(serverJson) }, resolve, seen = emptySet())!!
        assertEquals(listOf(one), plan.tracks)
        assertEquals(listOf("Fresh"), plan.fresh.map { it.title })
        assertEquals("Because you played Two", plan.why[one.path])

        // server down: null, and Autoplay uses today's picks
        assertNull(AutoplaySource.choose({ throw IOException("no signal") }, resolve, seen = emptySet()))
        // nothing usable from the server: today's picks too
        assertNull(AutoplaySource.choose({ RecPick.parseNext(serverJson) }, { null }, seen = setOf(matchKey("Fresh", "Other"))))
    }

    @Test fun Songs_already_queued_or_heard_lately_are_left_out() {
        val one = song("One")
        val plan = AutoplaySource.choose({ RecPick.parseNext(serverJson) }, { p -> if (p.title == "One") one else null }, seen = setOf(one.matchKey))!!
        assertEquals(emptyList<Track>(), plan.tracks)
        assertEquals(listOf("Fresh"), plan.fresh.map { it.title })
    }

    @Test fun Home_shelves_are_read_from_the_server() {
        val json = """{"shelves":[{"id":"quick-picks","title":"Quick picks","subtitle":"For you","rotates":false,
            "tracks":[{"path":"a","title":"One","artist":"Artist","album":"LP","durationMs":1,"isNew":false,"why":null},
                      {"path":null,"title":"Fresh","artist":"Other","album":"","durationMs":1,"isNew":true,"why":null}],"albums":[]}]}"""
        val s = RecPick.parseHome(json)
        assertEquals(1, s.size); assertEquals("quick-picks", s[0].id); assertEquals(2, s[0].tracks.size); assertEquals(true, s[0].tracks[1].isNew)

        val one = song("One")
        val albums = HomeShelf("recently-added", "Recently added", "", albums = listOf(Group("LP", listOf(one), null)))
        val home = HomeShelves.fromServer(s, { p -> if (p.title == "One") one else null }, listOf(HomeShelf("quick-picks", "Old", "", listOf(one)), albums))!!
        assertEquals(listOf("quick-picks", "recently-added"), home.map { it.id })
        assertEquals(listOf(one), home[0].tracks)
        // nothing on the phone matches: the phone's own shelves stay
        assertNull(HomeShelves.fromServer(s, { null }, listOf(albums)))
    }
}
