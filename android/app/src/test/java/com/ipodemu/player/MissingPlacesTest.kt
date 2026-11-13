package com.ipodemu.player

import com.ipodemu.library.Track
import com.ipodemu.library.WebCatalog
import org.junit.Assert.assertEquals
import org.junit.Test

/** An album's missing songs sit in their place on the phone (owner, 13 Nov: Random Access Memories, 3 of 13). */
class MissingPlacesTest {
    private fun t(no: Int, title: String) = Track(path = "/$no", title = title, artist = "Daft Punk", album = "RAM", albumArtist = "Daft Punk", genre = "", trackNo = no, discNo = 1,
        durationMs = 1, year = 2013, isMusic = true, artKey = null, mtime = 0, size = 0)
    private fun c(no: Int, title: String) = WebCatalog.Song("Daft Punk", title, "RAM", null, 1, no, 1)

    @Test fun eachMissingSongComesBeforeTheNextOneHere() {
        val have = listOf(t(3, "Giorgio by Moroder"), t(5, "Instant Crush"), t(6, "Lose Yourself to Dance"))
        val places = missingPlaces(have, listOf(c(1, "Give Life Back to Music"), c(2, "The Game of Love"), c(4, "Within"), c(7, "Touch"), c(13, "Contact")))
        assertEquals(mapOf(0 to listOf("Give Life Back to Music" to "Daft Punk", "The Game of Love" to "Daft Punk"), 1 to listOf("Within" to "Daft Punk"),
            3 to listOf("Touch" to "Daft Punk", "Contact" to "Daft Punk")), places)
    }
}
