package com.ipodemu.playback

/**
 * A seek decided now but made later (a song held back under the DJ, a retry after a wait) is made only while the song it was meant for is still the one
 * playing (owner, 5 Nov: a song that had just finished came back for half a second after the next one began). Songs are told apart by their path, not by
 * their place in the queue, which moves when the queue is changed or the next song is reached meanwhile. Pure.
 */
object DeferredSeek {
    /** True when [meant] (the path noted when the seek was planned) is the song playing now ([playing]); with either unknown, nothing is done. */
    fun stillMeant(meant: String?, playing: String?): Boolean = !meant.isNullOrEmpty() && meant == playing
}
