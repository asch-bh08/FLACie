package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test
import java.io.IOException
import java.net.ConnectException
import java.net.SocketTimeoutException

/**
 * The app's reads from FLACie Web (settings, the downloads list, the DJ's settings, charts ...): a read that fails on the way (the server restarting, a
 * dropped connection on a weak signal, a busy or rate-limited server) is tried again at most twice, after a short jittered wait or the server's own
 * Retry-After. Nothing that waiting cannot change is tried again (a refused sign-in, a 404), nor a slow answer (that would only double the wait).
 */
class WebRetryTest {
    private val waits = mutableListOf<Long>()
    private fun <T> run(call: () -> T): T = WebRetry.run(sleep = { waits.add(it) }, random = { 1.0 }, call = call)

    @Test fun a_dropped_connection_is_tried_again_after_a_growing_wait() {
        var calls = 0
        val out = run { calls++; if (calls < 3) throw ConnectException("refused") else "ok" }
        assertEquals("ok", out)
        assertEquals(listOf(500L, 1000L), waits)
    }

    @Test fun at_most_two_more_tries_then_the_last_failure() {
        var calls = 0
        try { run<String> { calls++; throw WebRetry.HttpStatus(503, null) }; fail() } catch (e: IOException) { assertEquals("HTTP 503", e.message) }
        assertEquals(3, calls)
    }

    @Test fun the_servers_retry_after_is_honoured_but_never_shorter_than_the_backoff() {
        var calls = 0
        run { calls++; if (calls == 1) throw WebRetry.HttpStatus(429, 3) else if (calls == 2) throw WebRetry.HttpStatus(429, 0) else "ok" }
        assertEquals(listOf(3000L, 1000L), waits)
    }

    @Test fun a_long_retry_after_is_not_waited_out_here() {
        // a rate limit with a minute to go: the person is told to wait (FriendlyError) instead of a frozen screen
        var calls = 0
        try { run<String> { calls++; throw WebRetry.HttpStatus(429, 60) }; fail() } catch (e: WebRetry.HttpStatus) { assertEquals(429, e.code) }
        assertEquals(1, calls)
    }

    @Test fun answers_waiting_cannot_change_and_slow_answers_are_not_tried_again() {
        for (e in listOf(WebRetry.HttpStatus(404, null), WebRetry.HttpStatus(400, null), WebRetry.HttpStatus(501, null), SocketTimeoutException("Read timed out"), WebRetry.Refused("FLACie Web doesn't know this account."))) {
            var calls = 0
            try { run<String> { calls++; throw e }; fail() } catch (_: IOException) { }
            assertEquals(e.toString(), 1, calls)
        }
        assertTrue(waits.isEmpty())
    }

    @Test fun jitter_shortens_the_wait_by_up_to_half() {
        assertEquals(250L, WebRetry.backoffMs(0, 0.0)); assertEquals(500L, WebRetry.backoffMs(0, 1.0))
        assertEquals(500L, WebRetry.backoffMs(1, 0.0)); assertEquals(1000L, WebRetry.backoffMs(1, 1.0))
        assertNull(WebRetry.waitAfter(WebRetry.HttpStatus(503, null), attempt = 2, random = 1.0))
    }
}
