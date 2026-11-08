package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** Search shows the person's playlists that hold what was typed (owner, 7 Nov), the same as the web's (FLACie.Core PlaylistHits). */
class PlaylistHitsTest {
    private data class L(val id: String, val name: String, val songs: List<PlaylistHits.Song>)
    private fun l(id: String, name: String, vararg s: Pair<String, String>) = L(id, name, s.map { PlaylistHits.Song(it.first, it.second) })
    private val lists = listOf(
        l("1", "Gym", "Lose Yourself" to "Eminem", "Stronger" to "Kanye West", "Without Me" to "Eminem"),
        l("2", "Chill", "Dreamer" to "Ozzy Osbourne", "Stay With Me" to "Sam Smith"),
        l("3", "Dreamer vibes", "Yellow" to "Coldplay"),
        l("4", "Empty"),
    )
    private fun find(q: String) = PlaylistHits.find(lists, { it.name }, { it.songs }, q)

    @Test fun aPlaylistNamedLikeTheSearchComesFirstThenThoseHoldingTheSong() {
        val hits = find("dreamer")
        assertEquals(listOf("3", "2"), hits.map { it.list.id })
        assertNull(hits[0].holds)
        assertEquals("Has “Dreamer”", hits[1].holds)
    }

    @Test fun anArtistFindsThePlaylistsWithTheirSongsSayingHowMany() {
        val hits = find("eminem")
        assertEquals(listOf("1"), hits.map { it.list.id })
        assertEquals("2 songs by Eminem", hits[0].holds)
    }

    @Test fun aSongTitleOfSeveralWordsFindsItsPlaylist() {
        assertTrue(find("with me").any { it.list.id == "2" })
        assertEquals("Has “Stay With Me”", find("stay with me")[0].holds)
    }

    @Test fun nothingTypedOrNothingHeldFindsNothing() {
        assertTrue(find("").isEmpty())
        assertTrue(find("aura").isEmpty())
    }
}
