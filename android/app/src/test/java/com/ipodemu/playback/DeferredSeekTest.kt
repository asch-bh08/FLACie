package com.ipodemu.playback

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class DeferredSeekTest {
    @Test fun the_same_song_is_still_meant() = assertTrue(DeferredSeek.stillMeant("/a/skyfall", "/a/skyfall"))
    @Test fun another_song_is_not_it() = assertFalse(DeferredSeek.stillMeant("/a/skyfall", "/a/chandelier"))
    @Test fun an_unknown_song_on_either_side_does_nothing() {
        assertFalse(DeferredSeek.stillMeant(null, "/a/x"))
        assertFalse(DeferredSeek.stillMeant("/a/x", null))
        assertFalse(DeferredSeek.stillMeant("", ""))
    }
}
