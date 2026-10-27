package com.ipodemu.playback

import kotlin.math.abs
import kotlin.math.exp
import kotlin.math.max
import kotlin.math.min
import kotlin.math.roundToInt
import kotlin.math.sin

/**
 * The DJ's words on screen while it talks: a short line is one caption for the whole clip; a long one is split into two or three phrases at its natural
 * breaks, each shown for its share of the clip by length. The same as DjCaptions.cs in FLACie.Core; keep the two the same.
 */
object DjCaptions {
    data class Phrase(val text: String, val atMs: Int, val untilMs: Int)

    const val ONE_LINE = 70
    const val LINGER_MS = 600
    /** How long the cue sound plays before the voice. */
    const val CUE_LEAD_MS = 250

    fun plan(text: String, clipMs: Int): List<Phrase> {
        val t = text.trim().split(Regex("\\s+")).filter { it.isNotEmpty() }.joinToString(" ")
        if (t.isEmpty()) return emptyList()
        val clip = max(1000, clipMs)
        val parts = split(t)
        val total = parts.sumOf { it.length }
        val res = ArrayList<Phrase>(); var at = 0
        parts.forEachIndexed { i, p ->
            val last = i == parts.size - 1
            val end = if (last) clip else at + (clip.toDouble() * p.length / total).roundToInt()
            res.add(Phrase(p, at, if (last) end + LINGER_MS else end))
            at = end
        }
        return res
    }

    fun split(t: String): List<String> {
        if (t.length <= ONE_LINE) return listOf(t)
        val n = if (t.length > ONE_LINE * 2) 3 else 2
        val cuts = ArrayList<Int>()
        for (k in 1 until n) {
            val cut = best(t, t.length * k / n, cuts.lastOrNull() ?: 0)
            if (cut > 0 && cut < t.length) cuts.add(cut)
        }
        val res = ArrayList<String>(); var from = 0
        for (c in cuts + t.length) { val p = t.substring(from, c).trim(); if (p.isNotEmpty()) res.add(p); from = c }
        return res
    }

    private fun best(t: String, want: Int, after: Int): Int {
        val reach = max(12, t.length / 6)
        var best = -1; var bestDist = Int.MAX_VALUE
        for (i in max(after + 1, want - reach) until min(t.length - 1, want + reach))
            if (t[i] == ' ' && i > 0 && t[i - 1] in ".,;!?:-—" && abs(i - want) < bestDist) { best = i; bestDist = abs(i - want) }
        if (best > 0) return best
        for (d in 0 until t.length) {
            if (want + d < t.length && want + d > after && t[want + d] == ' ') return want + d
            if (want - d > after && want - d > 0 && t[want - d] == ' ') return want - d
        }
        return -1
    }

    /** The cue before the voice: a quarter-second swoosh of soft band-limited noise, made here (no recording, nothing to license), as 16-bit mono PCM. */
    fun swoosh(sampleRate: Int = 44_100, seed: Long = 7L, peak: Float = 0.18f): ShortArray {
        val n = (sampleRate * 0.24).toInt()
        val rnd = java.util.Random(seed)
        val out = ShortArray(n)
        var lp = 0.0; var lp2 = 0.0
        for (i in 0 until n) {
            val x = i.toDouble() / n
            // a cutoff that rises through the swoosh (two one-pole stages: low rumble out, hiss softened)
            val k = 0.05 + 0.45 * x
            lp += k * (rnd.nextDouble() * 2 - 1 - lp)
            lp2 += 0.02 * (lp - lp2)
            val band = lp - lp2
            val env = sin(Math.PI * min(1.0, x / 0.4)).let { if (x < 0.4) it else exp(-(x - 0.4) * 9) }
            out[i] = (band * env * peak * 3 * Short.MAX_VALUE).coerceIn(-32767.0, 32767.0).toInt().toShort()
        }
        return out
    }

    /** Equaliser levels (millibels, per band) that lift a voice over the music: the lowest band cut (below about 150 Hz), the band nearest 3 kHz lifted.
     *  [centresMilliHz]: each band's centre; [min] / [max]: the levels the device allows. */
    fun voiceEq(centresMilliHz: IntArray, min: Short, max: Short): ShortArray = ShortArray(centresMilliHz.size) { i ->
        val hz = centresMilliHz[i] / 1000
        val nearest3k = centresMilliHz.indices.minByOrNull { abs(centresMilliHz[it] / 1000 - 3000) }
        val mb = when {
            hz < 150 -> -600
            i == nearest3k -> 300
            else -> 0
        }
        mb.coerceIn(min.toInt(), max.toInt()).toShort()
    }
}
