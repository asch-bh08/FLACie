package com.ipodemu.playback

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/** "Hey Google, play ... on FLACie": what Google sends (MEDIA_PLAY_FROM_SEARCH) turned into the words the DJ is asked. */
class VoicePlayTest {
    @Test fun justPlayMusicIsNoWordsAtAll() {
        assertNull(VoicePlay.words("", null, null, null, null))
        assertNull(VoicePlay.words("  ", "vnd.android.cursor.item/*", null, null, null))
        assertNull("Google's own words for the bare request", VoicePlay.words("music", null, null, null, null))
    }

    @Test fun aSearchIsAskedOfTheDjAsAPlay() {
        assertEquals("play Bad Romance", VoicePlay.words("Bad Romance", null, null, null, null))
        assertEquals("play my favourites", VoicePlay.words("my favourites", null, null, null, null))
        assertEquals("already a play: left alone", "play something chill", VoicePlay.words("play something chill", null, null, null, null))
    }

    @Test fun googlesStructuredExtrasAreUsedWhenTheyAreThere() {
        assertEquals("play Bad Romance by Lady Gaga", VoicePlay.words("bad romance lady gaga", "vnd.android.cursor.item/audio", "Lady Gaga", null, "Bad Romance"))
        assertEquals("play the album The Fame by Lady Gaga", VoicePlay.words("the fame", "vnd.android.cursor.item/album", "Lady Gaga", "The Fame", null))
        assertEquals("play Lady Gaga", VoicePlay.words("lady gaga", "vnd.android.cursor.item/artist", "Lady Gaga", null, null))
        assertEquals("play my Gym playlist", VoicePlay.words("gym", "vnd.android.cursor.item/playlist", null, null, null))
    }
}
