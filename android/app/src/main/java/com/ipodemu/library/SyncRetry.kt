package com.ipodemu.library

import kotlinx.coroutines.delay

/** Runs a sync again after each pause while it fails, rethrowing the last failure when the pauses run out. The sync at app start
 * competes with the covers for the network, and the phone's request gate turns some requests away; one failure there used to leave
 * Playlists and Favourites empty until Sync now was pressed by hand. With [thenEvery] it never gives up: it goes on trying at that
 * interval after the pauses, so a server outage of any length ends with the playlists back without anyone pressing anything. */
object SyncRetry {
    suspend fun attempt(pauses: List<Long>, thenEvery: Long? = null, block: suspend () -> Unit) {
        for (pause in pauses) {
            try { block(); return } catch (e: Exception) { if (e is kotlinx.coroutines.CancellationException) throw e }
            delay(pause)
        }
        if (thenEvery != null) while (true) {
            try { block(); return } catch (e: Exception) { if (e is kotlinx.coroutines.CancellationException) throw e }
            delay(thenEvery)
        }
        block()
    }
}
