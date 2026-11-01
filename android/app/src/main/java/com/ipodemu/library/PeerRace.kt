package com.ipodemu.library

/**
 * How long each peer gets while one song downloads from Soulseek (the same rules as the server's PeerRace). Up to three peers are tried at once
 * until one is sending. With someone else left to try, a silent peer is swapped after 20s. The last peer gets a minute to start sending and 45s
 * if it stalls, then the download moves on to the next source; it used to keep its chance for 8 minutes of "Waiting for a peer...". A peer that
 * is sending keeps going however long the song takes.
 */
object PeerRace {
    const val MAX_AT_ONCE = 3

    fun giveUp(bytes: Long, sinceStartMs: Long, sinceMovedMs: Long, othersToTry: Boolean): Boolean =
        if (othersToTry) sinceMovedMs > 20_000 else if (bytes == 0L) sinceStartMs > 60_000 else sinceMovedMs > 45_000

    fun startAnother(active: Int, anySending: Boolean, waiting: Int): Boolean =
        waiting > 0 && (active == 0 || (!anySending && active < MAX_AT_ONCE))
}
