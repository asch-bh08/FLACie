package com.ipodemu.library

/**
 * The first frame waits for Home's covers (owner, 9 Nov: "make them already there like Spotify"): the screen is held while the covers the disk has
 * are decoded into memory, never longer than [capMs]. For [SYNC_MS] after start a cover still missing from memory is decoded on the spot from the disk
 * rather than drawn as a placeholder first; later (scrolling) it loads in the background as before, so lists stay smooth.
 */
class FirstFrameGate(private val startMs: Long, private val capMs: Long = CAP_MS) {
    private val pending = java.util.concurrent.atomic.AtomicInteger(0)
    fun began() { pending.incrementAndGet() }
    fun ended() { pending.decrementAndGet() }
    fun ready(nowMs: Long): Boolean = pending.get() <= 0 || nowMs - startMs >= capMs
    fun decodeNow(nowMs: Long): Boolean = nowMs - startMs < SYNC_MS

    companion object { const val CAP_MS = 400L; const val SYNC_MS = 3_000L }
}
