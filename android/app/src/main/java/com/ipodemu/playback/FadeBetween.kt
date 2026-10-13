package com.ipodemu.playback

import com.ipodemu.library.Track

/**
 * "Fade between songs": the end of a song fades out and the start of the next fades in, so a change is soft instead of abrupt. This is a fade, not an overlap (a true
 * crossfade needs two players at once). Songs that belong together are left alone: when the next song is the next track of the same album (a live album, a classical work, a
 * mix) there is no fade at that join, so gapless albums stay gapless. Pure and unit-tested.
 */
object FadeBetween {
    /** The choices in Settings, in seconds; 0 = off. */
    val CHOICES = listOf(0, 2, 4, 6, 8, 12)

    private fun norm(s: String) = s.trim().lowercase()

    /** True when [next] follows [cur] on the same album as the very next track (so the join is meant to be seamless). */
    fun continuous(cur: Track?, next: Track?): Boolean {
        if (cur == null || next == null) return false
        if (norm(cur.album).isEmpty() || norm(cur.album) != norm(next.album)) return false
        if (norm(cur.albumArtist.ifBlank { cur.artist }) != norm(next.albumArtist.ifBlank { next.artist })) return false
        return if (cur.trackNo > 0 && next.trackNo > 0) (next.discNo == cur.discNo && next.trackNo == cur.trackNo + 1) || (next.discNo == cur.discNo + 1 && next.trackNo == 1)
        else true   // no track numbers: songs of one album that are adjacent in the queue are taken as a sequence
    }

    class Flags(val fadeIn: Boolean, val fadeOut: Boolean)

    /** Whether the start of [cur] fades in and its end fades out, given its neighbours in the queue. */
    fun flags(prev: Track?, cur: Track?, next: Track?): Flags = Flags(fadeIn = !continuous(prev, cur), fadeOut = !continuous(cur, next))

    /** The multiplier for the volume at [positionMs] of a song [durationMs] long: 1 in the middle, falling to 0 at a faded end. */
    fun gain(positionMs: Long, durationMs: Long, fadeMs: Long, flags: Flags): Float {
        if (fadeMs <= 0L || durationMs <= 0L) return 1f
        // a song shorter than two fades is faded over a third of its length each side, so there is always a middle
        val f = minOf(fadeMs, durationMs / 3)
        val up = if (flags.fadeIn) SleepPlan.fadeGain(positionMs.coerceAtLeast(0L), f) else 1f
        val down = if (flags.fadeOut) SleepPlan.fadeGain((durationMs - positionMs).coerceAtLeast(0L), f) else 1f
        return minOf(up, down)
    }
}
