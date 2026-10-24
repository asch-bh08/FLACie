package com.ipodemu.playback

import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeoutOrNull
import java.io.File

/** A cover for another app (the car): the one on the phone, or fetched while it waits, up to [waitMs]. Android Auto asks once and keeps a blank square
 * when told a cover is not there, so a "not yet" must become a short wait instead. Runs on the provider's own thread, never the main one. */
object CoverWait {
    /** Waits (on a background thread) until [ready] says yes, up to [waitMs]; true when it did. The car keeps the first answer it gets for a list, so an
     * answer given while the library is still loading must wait for it instead of being empty. */
    fun until(ready: () -> Boolean, waitMs: Long, stepMs: Long = 200): Boolean {
        val end = System.currentTimeMillis() + waitMs
        while (true) { if (ready()) return true; if (System.currentTimeMillis() >= end) return false; Thread.sleep(stepMs) }
    }

    fun file(have: () -> File?, fetch: suspend () -> Unit, waitMs: Long): File? {
        have()?.let { return it }
        runBlocking { withTimeoutOrNull(waitMs) { try { fetch() } catch (e: kotlinx.coroutines.CancellationException) { throw e } catch (_: Exception) { } } }
        return have()
    }
}
