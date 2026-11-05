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

    @Test fun a_remix_is_another_song() = assertNotEquals(baseTitleKey("Freed From Desire"), baseTitleKey("Freed From Desire (Remix)"))
}
