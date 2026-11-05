package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** The DJ's playlist steps on the phone's playlists: by name, through UserData so they sync as any edit does. */
class DjPlaylistEditsTest {
    private fun ud() = UserData(kotlin.io.path.createTempDirectory("djedits").toFile())

    @Test fun addingMakesTheMissingPlaylistAndSkipsRepeats() {
        val u = ud(); var synced = 0; u.onChanged = { synced++ }
        val e = DjPlaylistEdits(u)
        e.add("Pop", listOf("a", "b"))
        e.add("pop", listOf("b", "c"))
        assertEquals(1, u.playlists.size)
        assertEquals(listOf("a", "b", "c"), u.playlists[0].paths)
        assertEquals("Pop", u.playlists[0].name)
        assertTrue(synced > 0)
    }

    @Test fun removingTakesOnlyThoseSongsAndIgnoresAMissingPlaylist() {
        val u = ud(); val e = DjPlaylistEdits(u)
        e.add("Pop", listOf("a", "b", "c"))
        e.remove("POP", listOf("b"))
        e.remove("Nope", listOf("a"))
        assertEquals(listOf("a", "c"), u.playlists[0].paths)
        assertEquals(1, u.playlists.size)
    }

    @Test fun creatingFillsAndRenamingKeepsTheSongs() {
        val u = ud(); val e = DjPlaylistEdits(u)
        e.create("Backroads", listOf("x", "y"))
        e.rename("backroads", "Country Roads")
        assertEquals("Country Roads", u.playlists[0].name)
        assertEquals(listOf("x", "y"), u.playlists[0].paths)
        e.rename("Missing", "Other"); e.rename("Country Roads", " ")
        assertEquals("Country Roads", u.playlists[0].name)
        e.create("Empty", emptyList())
        assertEquals(emptyList<String>(), u.playlists.first { it.name == "Empty" }.paths)
        assertNull(u.playlists.firstOrNull { it.name == "Other" })
    }
}
