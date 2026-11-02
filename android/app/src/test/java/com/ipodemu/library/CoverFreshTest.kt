package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertSame
import org.junit.Test

/** Owner, 2 Nov: Careless Whisper (Wham!) played with Sia's 1000 Forms of Fear cover. Jellyfin lumps the loose files of a playlist folder into one fake album;
 *  the library corrects the cover key (CoverVetting), but a song saved in a queue before that kept the old key. The library's key wins. */
class CoverFreshTest {
    private fun t(key: String?) = Track(path = "https://jf/Audio/5b1f814986dac358a49fdda528d0b42d/stream?static=true", title = "Careless Whisper", artist = "George Michael",
        album = "The Final", albumArtist = "Wham!", genre = "", trackNo = 6, discNo = 1, durationMs = 300_000, year = 0, isMusic = true, artKey = key,
        mtime = 0, size = 0, source = TrackSource.JELLYFIN)

    @Test fun theLibrarysCoverWinsOverASavedOne() {
        val saved = t("jfffb82c6d51a93f6ece1f7f22a8cd9108")
        val lib = t(CoverVetting.catalogKey(saved))
        assertEquals(lib.artKey, CoverVetting.fresh(saved, lib).artKey)
    }
    @Test fun nothingChangesWhenTheyAgreeOrTheLibraryDoesNotKnowIt() {
        val saved = t("itabc")
        assertSame(saved, CoverVetting.fresh(saved, t("itabc")))
        assertSame(saved, CoverVetting.fresh(saved, null))
    }
    @Test fun theLibrarysArtistWinsToo() {
        val saved = t("itabc").copy(artist = "Wham!")
        assertEquals("George Michael", CoverVetting.fresh(saved, t("itabc")).artist)
    }
}
