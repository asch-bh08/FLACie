package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Test

/** Spotify's "More And More - Single Version" is the "More And More" that was downloaded for it (owner, 5 Nov: shown as missing in the playlist). */
class BaseTitleKeyTest {
    @Test fun spotifys_extras_name_the_same_song() {
        assertEquals(baseTitleKey("More And More"), baseTitleKey("More And More - Single Version"))
        assertEquals(baseTitleKey("Freed From Desire"), baseTitleKey("Freed From Desire - Radio Edit"))
        assertEquals(baseTitleKey("Holding Out for a Hero"), baseTitleKey("Holding Out for a Hero - From \"Footloose\" Soundtrack"))
        assertEquals(baseTitleKey("Scatman"), baseTitleKey("Scatman (ski-ba-bop-ba-dop-bop)"))
    }

    /** "80s - 90s rally" still listed these as not in the library after they were downloaded (owner, 7 Nov). */
    @Test fun a_dash_note_after_a_bracket_and_video_edits_name_the_same_song() {
        assertEquals(baseTitleKey("My Heart Beats Like A Drum (Dam Dam Dam)"), baseTitleKey("My Heart Beats Like A Drum (Dam Dam Dam) - Radio Edit"))
        assertEquals(baseTitleKey("Bailando"), baseTitleKey("Bailando - Video Edit"))
        assertEquals(baseTitleKey("Some Song"), baseTitleKey("Some Song - Single Edit"))
    }

    @Test fun a_remix_is_another_song() = assertNotEquals(baseTitleKey("Freed From Desire"), baseTitleKey("Freed From Desire (Remix)"))

    /** A download tagged with a compilation's name for its artist ("90's Eurodance") but filed in the artist's folder is that artist's song. */
    @Test fun the_artist_folder_a_song_is_filed_in() {
        fun t(filePath: String, path: String = "http://jf/Audio/1/stream", source: TrackSource = TrackSource.JELLYFIN) = Track(path = path, title = "Lover On The Line",
            artist = "90's Eurodance", album = "", albumArtist = "", genre = "", trackNo = 0, discNo = 0, durationMs = 1, year = 0, isMusic = true, artKey = null,
            mtime = 0, size = 1, source = source, filePath = filePath)
        assertEquals(primaryArtist("The Free"), folderArtist(t("/media/Movies/music/The Free/The Free - Lover On The Line.flac")))
        assertEquals(primaryArtist("The Free"), folderArtist(t("", "https://fm.example.ts.net/file?path=music%2FThe+Free%2FThe+Free+-+Lover+On+The+Line.flac", TrackSource.CLOUD)))
        assertEquals("", folderArtist(t("")))
    }
}
