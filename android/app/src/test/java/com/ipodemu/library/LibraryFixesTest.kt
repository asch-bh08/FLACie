package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertSame
import org.junit.Assert.assertTrue
import org.junit.Test

/** The server's fixes on the phone (owner, 13 Nov: "make sure the phone has it all"): version names, songs' own albums, hidden second copies. */
class LibraryFixesTest {
    private fun t(id: String, title: String, album: String) = Track(path = "http://jf/Audio/$id/stream?static=true", title = title, artist = "A", album = album,
        albumArtist = "A", genre = "", trackNo = 1, discNo = 1, durationMs = 200_000, year = 2010, isMusic = true, artKey = "jf$id", mtime = 0, size = 0, source = TrackSource.JELLYFIN)

    @Test fun fixesAreAppliedByJellyfinIdAndCopiesHidden() {
        val a = "a".repeat(32); val b = "b".repeat(32); val c = "c".repeat(32); val d = "d".repeat(32)
        LibraryFixes.parse("""{"fixes":[{"id":"$b","title":"Voodoo People (05 Edit)","album":null,"albumArtist":null,"artKey":null},
            {"id":"$d","title":null,"album":"Doo-Wops & Hooligans","albumArtist":"Bruno Mars","artKey":"ext:abc"}],"hidden":["$c"]}""")
        val keep = t(a, "Voodoo People", "Music for the Jilted Generation")
        val out = LibraryFixes.apply(listOf(keep, t(b, "Voodoo People", "Their Law"), t(c, "Voodoo People", "Greatest Hits"), t(d, "Grenade", "Pop Megahits")))
        assertSame(keep, out[0])
        assertEquals("Voodoo People (05 Edit)", out[1].title)
        assertEquals(Triple("Doo-Wops & Hooligans", "Bruno Mars", "ext:abc"), Triple(out[3].album, out[3].albumArtist, out[3].artKey))
        assertTrue(LibraryFixes.isHidden(out[2])); assertFalse(LibraryFixes.isHidden(out[0]))
        LibraryFixes.parse("""{"fixes":[],"hidden":[]}""")
    }
}
