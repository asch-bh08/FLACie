package com.ipodemu

import com.ipodemu.library.IpodMirror
import com.ipodemu.library.IpodSnap
import com.ipodemu.library.UserPlaylist
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Test

class IpodMirrorTest {
    // the playlists as they are on the iPod (name, songs) -- the shape of the user's lac_playlists_names.txt
    private val ipod = listOf(
        IpodSnap("2026(LAC)", List(28) { "Song $it" to "Artist" }),
        IpodSnap("aura(LAC)", List(22) { "Aura $it" to "Eminem" }),
        IpodSnap("pop(LAC)", List(52) { "Pop $it" to "Various" }),
    )

    @Test fun planKeepsNamesCountsAndOrder() {
        val have = setOf("Song 0", "Aura 3")
        val plans = IpodMirror.plan("iPod", ipod) { t, _ -> if (t in have) "/music/$t.flac" else null }
        assertEquals(listOf("2026(LAC)", "aura(LAC)", "pop(LAC)"), plans.map { it.name })
        assertEquals(listOf(28, 22, 52), plans.map { it.paths.size })
        assertEquals("/music/Song 0.flac", plans[0].paths[0])
        // songs the library lacks still count, with title and artist remembered so another device can match them
        assertEquals(27, plans[0].meta.size)
        assertEquals("Song 5" to "Artist", plans[0].meta[plans[0].paths[5]])
    }

    @Test fun staleAccountPlaylistsAreTakenOverNotDuplicated() {
        val existing = listOf(
            UserPlaylist("a", "2026", ArrayList(List(23) { "x$it" })),
            UserPlaylist("b", "Aura", ArrayList(List(21) { "y$it" })),
            UserPlaylist("c", "House Music", ArrayList()),
        )
        val plans = IpodMirror.plan("iPod", ipod) { _, _ -> null }
        assertEquals("a", IpodMirror.target(existing, plans[0])?.id)
        assertEquals("b", IpodMirror.target(existing, plans[1])?.id)
        assertNull(IpodMirror.target(existing, plans[2]))   // "pop" matches nothing here: a new playlist
    }

    @Test fun alreadyLinkedPlaylistWinsEvenIfRenamedOnTheIpod() {
        val plans = IpodMirror.plan("iPod", listOf(IpodSnap("pop(LAC)", listOf("a" to "b")))) { _, _ -> null }
        val linked = UserPlaylist("z", "Old name", ArrayList(), ip = plans[0].key)
        assertNotNull(IpodMirror.target(listOf(linked, UserPlaylist("q", "pop", ArrayList())), plans[0]))
        assertEquals("z", IpodMirror.target(listOf(UserPlaylist("q", "pop", ArrayList()), linked), plans[0])?.id)
    }
}
