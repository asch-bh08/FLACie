package com.ipodemu.library.offline

import org.junit.Assert.assertEquals
import org.junit.Test

/** The Offline screen's "By album": what each downloaded album takes, biggest first; songs still downloading are not counted. */
class AlbumUseTest {
    private fun e(album: String, artist: String, bytes: Long, state: OfflineState = OfflineState.DONE) =
        OfflineEntry("k$album$bytes", "p$album$bytes", "t", artist, album, artist, "", 0, null, 0, 0, 0, state, OfflineReason.values().first(), bytes, bytes, 0)

    @Test fun albums_add_up_biggest_first() {
        val use = OfflineMath.byAlbum(listOf(e("A", "x", 10), e("B", "y", 50), e("A", "x", 30), e("B", "y", 5, OfflineState.DOWNLOADING)))
        assertEquals(listOf("B" to 50L, "A" to 40L), use.map { it.album to it.bytes })
        assertEquals(listOf(1, 2), use.map { it.songs })
    }
}
