package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/** Owner, 2 Nov: Wild Ones never played; its playlist entry kept the server's old address. */
class JellyfinPathsTest {
    private val old = "http://192.0.2.10:8096/Audio/a8623f90db3a89c445e9d315731937e3/stream?static=true"

    @Test fun theIdIsReadFromAnyServersAddress() {
        assertEquals("a8623f90db3a89c445e9d315731937e3", JellyfinPaths.idOf(old))
        assertEquals("a8623f90db3a89c445e9d315731937e3", JellyfinPaths.idOf("https://jellyfin.example.ts.net/Audio/A8623F90DB3A89C445E9D315731937E3/stream?static=true"))
        assertNull(JellyfinPaths.idOf("/storage/emulated/0/Music/a.flac"))
        assertNull(JellyfinPaths.idOf("smb://nas/music/Audio/a8623f90db3a89c445e9d315731937e3/x.flac"))
    }

    @Test fun anOldAddressMovesToTheServerInUse() {
        assertEquals("https://jellyfin.example.ts.net/Audio/a8623f90db3a89c445e9d315731937e3/stream?static=true",
            JellyfinPaths.rebase(old, "https://jellyfin.example.ts.net/"))
        assertEquals("no server set: left alone", old, JellyfinPaths.rebase(old, ""))
        assertEquals("/storage/a.flac", JellyfinPaths.rebase("/storage/a.flac", "https://x"))
    }
}
