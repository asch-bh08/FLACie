package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/** A Jellyfin server on the home network answers "who is JellyfinServer?" with a little JSON: its name and the address to use. */
class JellyfinDiscoveryTest {
    @Test fun a_reply_gives_the_name_and_address() =
        assertEquals(JellyfinDiscovery.Found("Living room", "http://192.168.1.20:8096"),
            JellyfinDiscovery.parse("""{"Address":"http://192.168.1.20:8096","Id":"abc","Name":"Living room","EndpointAddress":null}"""))

    @Test fun anything_else_is_ignored() {
        assertNull(JellyfinDiscovery.parse("hello"))
        assertNull(JellyfinDiscovery.parse("""{"Name":"x"}"""))
    }
}
