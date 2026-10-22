package com.ipodemu.library

import kotlinx.coroutines.delay

/** Runs a sync again after each pause while it fails, rethrowing the last failure when the pauses run out. The sync at app start
 * competes with the covers for the network, and the phone's request gate turns some requests away; one failure there used to leave
 * Playlists and Favourites empty until Sync now was pressed by hand. */
object SyncRetry {
    suspend fun attempt(pauses: List<Long>, block: suspend () -> Unit) {
        for (pause in pauses) {
            try { block(); return } catch (e: Exception) { if (e is kotlinx.coroutines.CancellationException) throw e }
            delay(pause)
        }
        block()
    }
}
