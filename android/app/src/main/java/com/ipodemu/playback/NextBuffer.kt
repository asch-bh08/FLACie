package com.ipodemu.playback

/**
 * The next song's start, kept ready while the current one plays so a skip or the change between songs is instant (F4.1). Pure, unit-tested; [PlayerController]
 * does the fetching ([PrefetchPlan] decides when) and frees what is no longer wanted. Nothing is kept after it has played: songs to keep are Offline downloads.
 */
object NextBuffer {
    /** The start that is buffered: this much of the song. */
    const val HEAD_MS = 30_000L
    private const val MIN_BYTES = 768L * 1024
    private const val MAX_BYTES = 12L * 1024 * 1024

    /** Bytes for the first [HEAD_MS] of a song of [sizeBytes] and [durationMs] (a little over, since bitrates vary); a fixed size when either is not known. */
    fun headBytes(sizeBytes: Long, durationMs: Long): Long {
        if (sizeBytes <= 0 || durationMs <= 0) return 3L * 1024 * 1024
        if (durationMs <= HEAD_MS) return sizeBytes
        return (sizeBytes.toDouble() * HEAD_MS / durationMs * 1.1).toLong().coerceIn(MIN_BYTES, MAX_BYTES)
    }

    /** What to free now: the buffered songs that are no longer the playing one or one of the next ones ([wanted]) -- played, or skipped past. */
    fun stale(buffered: Collection<String>, wanted: Collection<String>): List<String> = buffered.filter { it !in wanted }
}
