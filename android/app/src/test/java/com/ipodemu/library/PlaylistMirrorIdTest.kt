package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/** The Jellyfin song a playlist entry is mirrored as (6 Nov: songs swapped for a Hi-Res copy dropped out of the owner's playlist on Jellyfin). */
class PlaylistMirrorIdTest {
    private val old = "http://jf/Audio/" + "a".repeat(32) + "/stream?static=true"
    private val live = "b".repeat(32)

    @Test fun aLiveJellyfinSongIsItself() {
        assertEquals("a".repeat(32), PlaylistMerge.mirrorId(old, isLive = { true }, byTitle = { live }))
    }

    @Test fun aSongWhoseJellyfinCopyIsGoneIsFoundAgainByTitleAndArtist() {
        assertEquals(live, PlaylistMerge.mirrorId(old, isLive = { false }, byTitle = { live }))
        assertNull("nothing to send rather than a dead id", PlaylistMerge.mirrorId(old, isLive = { false }, byTitle = { null }))
    }

    @Test fun aLocalFileIsFoundByTitle() {
        assertEquals(live, PlaylistMerge.mirrorId("/sdcard/Music/x.flac", isLive = { true }, byTitle = { live }))
    }
}
