package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/** The same cases as FLACie.Tests `SongLinkTests`, so the phone and the web agree on what one song link is. */
class SongLinksTest {
    @Test fun songLinksAreRecognised() {
        assertEquals(SongLinks.Song("https://soundcloud.com/someproducer/dreamer-remix", "SoundCloud"), SongLinks.of("https://soundcloud.com/someproducer/dreamer-remix"))
        assertEquals(SongLinks.Song("https://soundcloud.com/someproducer/dreamer-remix", "SoundCloud"), SongLinks.of("  https://m.soundcloud.com/someproducer/dreamer-remix?si=abc&utm_source=x "))
        assertEquals(SongLinks.Song("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "YouTube"), SongLinks.of("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=42"))
        assertEquals(SongLinks.Song("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "YouTube"), SongLinks.of("https://youtu.be/dQw4w9WgXcQ?si=xyz"))
        assertEquals(SongLinks.Song("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "YouTube"), SongLinks.of("https://music.youtube.com/watch?v=dQw4w9WgXcQ"))
    }

    @Test fun otherTextIsNot() {
        for (t in listOf("https://soundcloud.com/someproducer/sets/summer", "https://soundcloud.com/someproducer", "https://soundcloud.com/someproducer/tracks",
            "https://soundcloud.com/discover", "https://www.youtube.com/playlist?list=PL1234567890ab", "https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=PL1234567890ab",
            "http://soundcloud.com/someproducer/dreamer-remix", "https://evil.example/soundcloud.com/a/b", "dreamer remix", "")) assertNull(t, SongLinks.of(t))
    }
}
