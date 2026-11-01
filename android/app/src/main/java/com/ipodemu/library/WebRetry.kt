package com.ipodemu.library

import java.io.IOException
import java.net.SocketTimeoutException

/**
 * Reads from FLACie Web ([FlacieWebClient]: settings, the downloads list, the DJ's settings, charts ...) that fail on the way are tried again, at most
 * twice: the server restarting, a connection dropped on a weak signal, a busy server (408, 429, 502, 503, 504, any 5xx but 501). The wait is
 * 0.5 s then 1 s, each shortened by up to half at random (so phones that lost the server together do not all come back in the same instant), or the
 * server's Retry-After when that is longer. A Retry-After over [MAX_RETRY_AFTER_MS] is not waited out here: the person is told to try again
 * ([FriendlyError] says "busy") rather than looking at a frozen screen. Never tried again: anything that waiting cannot change (a refused sign-in,
 * 404, 400) and a slow answer (a read timeout: asking again would only double the wait). Writes are never sent twice: only GETs come here.
 */
object WebRetry {
    const val MAX_RETRIES = 2
    const val MAX_RETRY_AFTER_MS = 10_000L

    /** A non-2xx answer: its code (as "HTTP 503", the form [FriendlyError] explains) and the server's Retry-After in seconds, if any. */
    class HttpStatus(val code: Int, val retryAfterSec: Long?) : IOException("HTTP $code")

    /** An answer that is final (FLACie Web does not know this account): its own sentence, never tried again. */
    class Refused(message: String) : IOException(message)

    fun backoffMs(attempt: Int, random: Double): Long = ((500L shl attempt.coerceIn(0, 10)) * (0.5 + 0.5 * random.coerceIn(0.0, 1.0))).toLong()

    /** How long to wait before trying again after [e] on try [attempt] (0 = the first), or null when it is not tried again. */
    fun waitAfter(e: IOException, attempt: Int, random: Double): Long? {
        if (attempt >= MAX_RETRIES) return null
        return when (e) {
            is Refused, is SocketTimeoutException -> null
            is HttpStatus -> {
                if (!(e.code == 408 || e.code == 429 || (e.code in 500..599 && e.code != 501))) return null
                val ra = e.retryAfterSec?.let { it * 1000L }
                if (ra != null && ra > MAX_RETRY_AFTER_MS) null else maxOf(backoffMs(attempt, random), ra ?: 0L)
            }
            else -> backoffMs(attempt, random)   // refused, reset or dropped connection: the server restarting, or the signal
        }
    }

    /** Runs [call] (one GET), trying again as above; rethrows the last failure. Blocking: call it off the main thread, as every FlacieWebClient call is. */
    fun <T> run(sleep: (Long) -> Unit = { Thread.sleep(it) }, random: () -> Double = { Math.random() }, call: () -> T): T {
        var attempt = 0
        while (true) {
            try { return call() }
            catch (e: IOException) {
                val wait = waitAfter(e, attempt, random()) ?: throw e
                sleep(wait)
                attempt++
            }
        }
    }
}
