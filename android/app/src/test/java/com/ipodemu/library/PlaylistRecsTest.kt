package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** "Recommended songs" under a playlist: the server's answer read, matched to this phone's songs, and the "+" adding one to the end. */
class PlaylistRecsTest {
    private fun t(i: Int, artist: String = "A$i") = Track(
        path = "jf:$i", title = "Song $i", artist = artist, album = "Al", albumArtist = artist, genre = "Rock", trackNo = 0, discNo = 0,
        durationMs = 200_000, year = 2000, isMusic = true, artKey = null, mtime = 0, size = 0, source = TrackSource.NAS,
    )

    @Test fun parsesTheServersPicks() {
        val json = """{"picks":[{"path":"http://x/Audio/1/stream","title":"One","artist":"Ann","album":"Al","durationMs":1000,"artKey":null},{"path":"p2","title":"Two","artist":"Bob"}]}"""
        val p = PlaylistRecs.parse(json)
        assertEquals(listOf("One", "Two"), p.map { it.title })
        assertEquals("http://x/Audio/1/stream", p[0].path)
        assertEquals("Bob", p[1].artist)
        assertTrue(PlaylistRecs.parse("""{"picks":[]}""").isEmpty())
        assertTrue(PlaylistRecs.parse("{}").isEmpty())
    }

    @Test fun picksBecomeThisPhonesSongsWithoutOnesInThePlaylistOrTwice() {
        val lib = (1..5).associate { "jf:$it" to t(it) }
        val picks = listOf(PlaylistRecs.Pick("jf:1", "Song 1", "A1"), PlaylistRecs.Pick("jf:2", "Song 2", "A2"), PlaylistRecs.Pick("other:2", "Song 2", "A2"),
            PlaylistRecs.Pick("gone", "Nope", "Nobody"), PlaylistRecs.Pick("jf:3", "Song 3", "A3"))
        // the server's path, else the same title and artist on this phone
        val resolve = { p: PlaylistRecs.Pick -> lib[p.path] ?: lib.values.firstOrNull { it.title == p.title && it.artist == p.artist } }
        val got = PlaylistRecs.onPhone(picks, resolve, setOf(t(1).matchKey))
        assertEquals(listOf("jf:2", "jf:3"), got.map { it.path })
    }

    @Test fun plusAddsTheSongToTheEndOfThePlaylistOnce() {
        val ud = UserData(kotlin.io.path.createTempDirectory("recs").toFile())
        var synced = 0
        ud.onChanged = { synced++ }
        val p = ud.createPlaylist("Mine", "jf:1")
        assertTrue(PlaylistRecs.add(ud, p.id, t(2)))
        assertEquals(listOf("jf:1", "jf:2"), ud.playlists.first { it.id == p.id }.paths)
        assertTrue(synced >= 2)   // made, then added: each goes to the account like any other edit
        assertFalse(PlaylistRecs.add(ud, p.id, t(2)))
        assertFalse(PlaylistRecs.add(ud, "no-such", t(3)))
        assertEquals(2, ud.playlists.first { it.id == p.id }.paths.size)
    }
}
