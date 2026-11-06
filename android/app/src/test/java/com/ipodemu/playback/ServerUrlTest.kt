package com.ipodemu.playback

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class ServerUrlTest {
    // owner, 6 Nov: the DJ's clips failed with an HTTP error. FLACie Web on :8443 "started with" the file mover's https://frank..., so a clip got the
    // file mover's key instead of the account's token and the server said 401
    @Test fun anotherPortOnTheSameHostIsAnotherServer() {
        assertFalse(ServerUrl.under("https://frank.ts.net:8443/api/dj/clip/a.mp3", "https://frank.ts.net"))
        assertTrue(ServerUrl.under("https://frank.ts.net/filemove/file?path=x", "https://frank.ts.net/filemove"))
        assertTrue(ServerUrl.under("https://frank.ts.net/filemove/file?path=x", "https://frank.ts.net/"))
        assertTrue(ServerUrl.under("https://frank.ts.net:8443/api/dj/clip/a.mp3", "https://frank.ts.net:8443"))
        assertFalse("a longer name is another host", ServerUrl.under("https://frankie.ts.net/x", "https://frank"))
        assertFalse(ServerUrl.under("https://frank.ts.net/x", ""))
    }
}
