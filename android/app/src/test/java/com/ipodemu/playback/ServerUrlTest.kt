package com.ipodemu.playback

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class ServerUrlTest {
    // owner, 6 Nov: the DJ's clips failed with an HTTP error. FLACie Web on :8443 "started with" the file mover's address on the same host, so a clip got the
    // file mover's key instead of the account's token and the server said 401
    @Test fun anotherPortOnTheSameHostIsAnotherServer() {
        assertFalse(ServerUrl.under("https://nas.example.ts.net:8443/api/dj/clip/a.mp3", "https://nas.example.ts.net"))
        assertTrue(ServerUrl.under("https://nas.example.ts.net/filemove/file?path=x", "https://nas.example.ts.net/filemove"))
        assertTrue(ServerUrl.under("https://nas.example.ts.net/filemove/file?path=x", "https://nas.example.ts.net/"))
        assertTrue(ServerUrl.under("https://nas.example.ts.net:8443/api/dj/clip/a.mp3", "https://nas.example.ts.net:8443"))
        assertFalse("a longer name is another host", ServerUrl.under("https://nasbox.example.ts.net/x", "https://nas"))
        assertFalse(ServerUrl.under("https://nas.example.ts.net/x", ""))
    }
}
