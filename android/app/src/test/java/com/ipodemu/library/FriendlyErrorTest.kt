package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Test
import java.io.IOException
import java.net.SocketTimeoutException
import java.net.UnknownHostException

/** What a person is told when something fails, as on the web (FriendlyError.cs): plain words and what to try, never an exception's own text. */
class FriendlyErrorTest {
    @Test fun an_address_that_cannot_be_found() =
        assertEquals("Can't reach the server. Check the address and that it's switched on.", FriendlyError.of(UnknownHostException("Unable to resolve host \"jf.example\": No address associated with hostname")))

    @Test fun too_slow() = assertEquals("The server took too long to answer. Try again in a moment.", FriendlyError.of(SocketTimeoutException("timeout")))

    @Test fun a_bare_http_code_is_explained() {
        assertEquals("The server doesn't have that. Check the address.", FriendlyError.of(IOException("HTTP 404")))
        assertEquals("The server had a problem answering. Try again in a moment.", FriendlyError.of(IOException("HTTP 502")))
        assertEquals("The server refused the sign-in. Check the user name and password.", FriendlyError.of(IOException("HTTP 401")))
    }

    @Test fun our_own_words_are_kept() = assertEquals("The code expired. Try again.", FriendlyError.of(IOException("The code expired. Try again.")))

    @Test fun anything_else_is_a_plain_line() = assertEquals("Something went wrong. Try again.", FriendlyError.of(NullPointerException("Attempt to invoke virtual method on a null object reference")))

    @Test fun with_what_failed() = assertEquals("Sync failed: the server took too long to answer. Try again in a moment.", FriendlyError.of(SocketTimeoutException(), "Sync failed"))
}
