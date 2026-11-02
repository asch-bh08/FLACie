package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/** Owner's phone, 2 Nov: Careless Whisper (Wham!, The Final) showed Sia's cover. It sits in a playlist folder (pop(LAC)) that Jellyfin made one album of,
 *  and it was the only song there without a picture of its own, so it alone pointed at the "album" picture and nothing disagreed. The folder decides. */
class CoverFolderTest {
    private var n = 0
    private fun t(album: String, key: String?, folder: String = "/media/Movies/music/pop(LAC)") = (++n).let { i ->
        Track(path = "https://jf/Audio/%032x/stream?static=true".format(i), title = "Song $i", artist = "A$i", album = album, albumArtist = "A$i", genre = "",
            trackNo = 1, discNo = 1, durationMs = 1000, year = 0, isMusic = true, artKey = key, mtime = 0, size = 0, source = TrackSource.JELLYFIN,
            filePath = "$folder/song $i.flac")
    }

    @Test fun theOnlySongWithoutItsOwnPictureInAMixedFolderGetsTheCatalog() {
        val careless = t("The Final", "jfffb82c6d51a93f6ece1f7f22a8cd9108")
        val others = listOf(t("21", "jf%032x".format(n + 1)), t("1000 Forms Of Fear", "jf%032x".format(n + 2)), t("Teenage Dream", "jf%032x".format(n + 3)))
        val out = CoverVetting.vet(listOf(careless) + others)
        assertEquals("by the song itself, not its album", CoverLookup.key(careless.artist, "", careless.title), out[0].artKey)
    }

    @Test fun anAlbumFolderKeepsItsPicture() {
        val a = t("Experience", "jfabc", "/music/The Prodigy/Experience"); val b = t("Experience", "jfabc", "/music/The Prodigy/Experience")
        val own = t("Experience", "jf%032x".format(n + 1), "/music/The Prodigy/Experience")
        assertEquals(listOf("jfabc", "jfabc"), CoverVetting.vet(listOf(a, b, own)).take(2).map { it.artKey })
    }
}
