package com.ipodemu.library

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Test

/** What the phone sends to make a share (/api/share): song or playlist, its name, and each song's path, title and artist, in order. */
class ShareLinksTest {
    private fun t(title: String) = Track(path = "p/$title", title = title, artist = "Artist", album = "Al", albumArtist = "Artist", genre = "", trackNo = 0, discNo = 0,
        durationMs = 0, year = 0, isMusic = true, artKey = null, mtime = 0, size = 0, source = TrackSource.JELLYFIN)

    @Test fun the_request_carries_the_songs_in_order() {
        val o = JSONObject(ShareLinks.body("playlist", "Road trip", listOf(t("One"), t("Two"))))
        assertEquals("playlist", o.getString("kind")); assertEquals("Road trip", o.getString("name"))
        val e = o.getJSONArray("entries")
        assertEquals(2, e.length())
        assertEquals("Two", e.getJSONObject(1).getString("t")); assertEquals("Artist", e.getJSONObject(1).getString("a")); assertEquals("p/Two", e.getJSONObject(1).getString("p"))
    }
}
