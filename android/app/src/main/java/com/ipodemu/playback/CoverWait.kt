package com.ipodemu.playback

import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeoutOrNull
import java.io.File

/** A cover for another app (the car): the one on the phone, or fetched while it waits, up to [waitMs]. Android Auto asks once and keeps a blank square
 * when told a cover is not there, so a "not yet" must become a short wait instead. Runs on the provider's own thread, never the main one. */
object CoverWait {
    fun file(have: () -> File?, fetch: suspend () -> Unit, waitMs: Long): File? {
        have()?.let { return it }
        runBlocking { withTimeoutOrNull(waitMs) { try { fetch() } catch (e: kotlinx.coroutines.CancellationException) { throw e } catch (_: Exception) { } } }
        return have()
    }
}
