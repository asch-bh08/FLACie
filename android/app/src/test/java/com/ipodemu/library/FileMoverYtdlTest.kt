package com.ipodemu.library

import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assume.assumeTrue
import org.junit.Test

/** Live check of the yt-dlp route behind the file mover; only with FLACIE_FM_KEY set (it writes into the throwaway _flacie_fetchtest folder). */
class FileMoverYtdlTest {
    private val key get() = System.getenv("FLACIE_FM_KEY").orEmpty()
    private val url = "https://filemove.example.ts.net/filemove"

    @Test fun findsAPopularSongAndRefusesNonsense() = runBlocking {
        assumeTrue(key.isNotBlank())
        val ok = FileMoverClient().ytdl(url, key, "Doja Cat", "Say So", 237, "_flacie_fetchtest/Doja Cat - Say So")
        assertNotNull(ok); assertEquals("m4a", ok!!.first)
        assertNull(FileMoverClient().ytdl(url, key, "Nonexistent Artist Qzx", "Song That Does Not Exist 12345", 200, "_flacie_fetchtest/nothing"))
    }
}
