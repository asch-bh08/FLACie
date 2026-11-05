package com.ipodemu.library

/** One finished play from the phone's own listening history: the song's match key, seconds heard, the song's length in seconds, and when (ms). */
data class PlayRecord(val key: String, val heard: Double, val length: Double, val at: Long)

/**
 * The songs really listened to, for Home's "Frequently played" row (the same rules as FLACie.Core's PlayCounts on the server).
 * A real play is heard to 60 % of the song or 4 minutes; a skip (under 30 s heard, not a real play) counts against the song; a play in between counts
 * for nothing. Newer plays weigh more (half-life 30 days). A song needs 3 real plays to show. One copy of each song, best first.
 */
object PlayCounts {
    const val MIN_REAL = 3
    private const val HALF_LIFE_MS = 30.0 * 86_400_000.0

    fun isReal(heard: Double, length: Double) = heard >= 240 || (length > 0 && heard >= length * 0.6)
    fun isSkip(heard: Double, length: Double) = !isReal(heard, length) && ListenRules.isSkip(heard)

    /** Song keys, most listened first. */
    fun rank(plays: List<PlayRecord>, nowMs: Long): List<String> {
        val score = HashMap<String, Double>(); val real = HashMap<String, Int>(); val last = HashMap<String, Long>()
        for (p in plays) {
            val w = Math.pow(0.5, (nowMs - p.at).coerceAtLeast(0L) / HALF_LIFE_MS)
            when {
                isReal(p.heard, p.length) -> { score.merge(p.key, w, Double::plus); real.merge(p.key, 1, Int::plus); last.merge(p.key, p.at, ::maxOf) }
                isSkip(p.heard, p.length) -> score.merge(p.key, -w, Double::plus)
            }
        }
        return real.filter { it.value >= MIN_REAL && (score[it.key] ?: 0.0) > 0.0 }.keys
            .sortedWith(compareByDescending<String> { score[it] ?: 0.0 }.thenByDescending { last[it] ?: 0L }.thenBy { it })
    }
}
