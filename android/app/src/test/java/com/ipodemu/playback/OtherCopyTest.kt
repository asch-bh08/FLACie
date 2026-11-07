package com.ipodemu.playback

import com.ipodemu.library.Track
import com.ipodemu.library.TrackSource
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/** A song whose file was swapped for a better copy plays from the copy the library has now (owner, 6 Nov: Rhythm of the Night said streaming and never played). */
class OtherCopyTest {
    private fun t(path: String, title: String, artist: String, ms: Long, src: TrackSource = TrackSource.JELLYFIN) = Track(path = path, title = title, artist = artist, album = "", albumArtist = artist,
        genre = "", trackNo = 0, discNo = 0, durationMs = ms, year = 0, isMusic = true, artKey = null, mtime = 0, size = 0, source = src, filePath = "")

    @Test fun theLibrarysOtherCopyOfTheSameSongIsTaken() {
        val gone = t("http://mover/file?path=music%2FCorona%2Fx.m4a", "The Rhythm Of The Night", "Corona", 264_000)
        val now = t("http://jf/Audio/" + "b".repeat(32) + "/stream?static=true", "The Rhythm of the Night", "Corona", 264_306)
        val clip = t("http://jf/Audio/" + "c".repeat(32) + "/stream?static=true", "The Rhythm of the Night", "Corona", 12_000)
        val other = t("http://jf/Audio/" + "d".repeat(32) + "/stream?static=true", "Rhythm of the Night", "DeBarge", 234_000)
        assertEquals(now.path, OtherCopy.of(gone, listOf(gone, clip, other, now))?.path)
        assertNull(OtherCopy.of(gone, listOf(gone, other)))
    }

    // owner's log, 6 Nov: "Freed From Desire - Radio Edit" 404'd after the Hi-Res swap and nothing played: the new file is tagged "Freed From Desire"
    @Test fun noExactCopyTheSameSongWithoutItsVersionNoteIsTaken() {
        val gone = t("http://mover/file?path=music%2FGala%2FGala+-+Freed+From+Desire.flac", "Freed From Desire - Radio Edit", "Gala", 211_000)
        val hiRes = t("http://jf/Audio/" + "e".repeat(32) + "/stream?static=true", "Freed From Desire", "Gala", 212_000)
        val remix = t("http://jf/Audio/" + "f".repeat(32) + "/stream?static=true", "Freed From Desire (Remix)", "Gala", 300_000)
        val cover = t("http://jf/Audio/" + "a".repeat(32) + "/stream?static=true", "Freed From Desire", "Somebody Else", 200_000)
        assertEquals(hiRes.path, OtherCopy.of(gone, listOf(gone, remix, cover, hiRes))?.path)
        assertNull("a remix or another artist's song is not the same song", OtherCopy.of(gone, listOf(gone, remix, cover)))
    }
}
