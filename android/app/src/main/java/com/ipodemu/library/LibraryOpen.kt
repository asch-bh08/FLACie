package com.ipodemu.library

import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.withTimeoutOrNull

/**
 * The saved song list is read at start (several seconds for 10,000 songs on a slow phone). A server sync that began meanwhile saw an empty list, so it ran as a
 * first pass and put its first pages (500, then a few thousand) over the full list that had just been read: the Songs list showed 4,500-6,000 of 10,000 until the
 * pass was done (owner, 26 Nov, emulator smoke). The sync now waits at this gate for the saved list ([opened]), and a part of a list never replaces a longer one.
 */
class SavedListGate {
    private val open = CompletableDeferred<Unit>()
    fun opened() { open.complete(Unit) }
    /** Waits for the saved list, at most [timeoutMs] (a list that never loads must not stop the sync for good). */
    suspend fun waitForSaved(timeoutMs: Long = 30_000) { if (!open.isCompleted) withTimeoutOrNull(timeoutMs) { open.await() } }
}

/** F3: the phone opens from the library it saved, then asks the network. [shown] runs as soon as the saved list is up, so nothing waits on the network. */
object LibraryOpen {
    suspend fun run(loadSaved: suspend () -> Unit, shown: () -> Unit, refresh: suspend () -> Unit, gate: SavedListGate? = null) {
        try { loadSaved() } finally { gate?.opened() }
        shown()
        refresh()
    }

    /** Whether the first pages of a pass ([part] songs) may be put on screen in place of what shows now ([shown] songs): only when they hold more. */
    fun partMayReplace(shown: Int, part: Int): Boolean = part > shown
}
