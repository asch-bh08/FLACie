package com.ipodemu.library

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import java.util.concurrent.atomic.AtomicLong
import java.util.concurrent.atomic.AtomicReference

/**
 * Makes sure a download always ends with a clear last word. The owner's download said "Searching Soulseek..." for ever, with no error and no try on
 * YouTube: the job had died (the scope it ran in was cancelled by another job's failure) and nothing said so. Now, whatever happens, it ends as DONE or
 * FAILED: an error, the job being stopped, the job ending without a final status, or nothing new for [QUIET_MS] (a download moving bytes reports as
 * it goes, so only a stuck one goes quiet that long), which also stops it.
 */
object DownloadGuard {
    const val QUIET_MS = 180_000L

    suspend fun run(quietMs: Long = QUIET_MS, report: (DownloadStatus) -> Unit, block: suspend ((DownloadStatus) -> Unit) -> Unit) {
        val lastAt = AtomicLong(System.currentTimeMillis())
        val last = AtomicReference<DownloadStatus?>(null)
        val ended = java.util.concurrent.atomic.AtomicBoolean(false)
        var timedOut = false
        val tell: (DownloadStatus) -> Unit = { s ->
            last.set(s); lastAt.set(System.currentTimeMillis())
            if (s.stage == DownloadStage.DONE || s.stage == DownloadStage.FAILED) ended.set(true)
            report(s)
        }
        fun fail(message: String) { if (ended.compareAndSet(false, true)) report(DownloadStatus(DownloadStage.FAILED, message, last.get()?.source)) }
        try {
            coroutineScope {
                val job = launch { block(tell) }
                val watch = launch {
                    val step = (quietMs / 4).coerceIn(20L, 5_000L)
                    while (isActive) {
                        delay(step)
                        if (!ended.get() && System.currentTimeMillis() - lastAt.get() > quietMs) { timedOut = true; job.cancel(); break }
                    }
                }
                try { job.join() } finally { watch.cancel() }
            }
        } catch (e: CancellationException) {
            fail("The download stopped before it finished. Try again.")
            if (!currentCoroutineContext().isActive) throw e
            return
        } catch (e: Exception) {
            fail("Something went wrong while downloading (${e.javaClass.simpleName}). Try again.")
            return
        }
        if (timedOut) fail("Nothing happened for ${quietMs / 60_000L} minutes after \"${last.get()?.message ?: "starting"}\", so it was stopped. Try again.".replace("for 0 minutes", "for a while"))
        else fail("The download stopped before it finished. Try again.")
    }
}
