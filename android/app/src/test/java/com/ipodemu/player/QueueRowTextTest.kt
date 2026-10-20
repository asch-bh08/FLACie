package com.ipodemu.player

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Test
import java.io.File

/** The line under a queue row's title, and a guard against the middle dot being garbled ("Â·") by a file saved in the wrong encoding. */
class QueueRowTextTest {
    @Test fun artistAndAlbumAreJoinedByOneMiddleDot() {
        assertEquals("Artist 101 · Artist 101 Album 4", queueRowSubtitle("Artist 101", "Artist 101 Album 4"))
        assertEquals("Artist 101", queueRowSubtitle("Artist 101", ""))
        assertEquals("Album", queueRowSubtitle("", "Album"))
        assertFalse(queueRowSubtitle("A", "B").contains('Â'))
    }

    @Test fun noSourceFileHoldsAGarbledMiddleDot() {
        val root = listOf(File("src/main/java"), File("app/src/main/java")).first { it.isDirectory }
        val bad = root.walk().filter { it.extension == "kt" }.filter { it.readText(Charsets.UTF_8).contains("Â·") }.map { it.name }.toList()
        assertEquals(emptyList<String>(), bad)
    }
}
