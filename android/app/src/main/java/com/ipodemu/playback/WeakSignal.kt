package com.ipodemu.playback

/**
 * Where a song in the queue is played from, with no player in it (so it can be tested; [PlayerController.item] applies it).
 * A song kept on the phone always plays from its file: no signal needed, no data used. A streamed song is the original file, read straight through
 * (with a big buffer and the disk cache behind it), never Jellyfin's live conversion just because the signal is weak: that conversion (HLS, made
 * on the fly in short pieces) lost bits of songs on a weak signal in the car, "real slim ... please stand up" (owner, 30 Oct; docs/playback-audit-2026-10-30.md).
 * The conversion is kept only where it is needed: a format this phone cannot decode, or "Data saver" chosen in Settings by the person.
 */
object PlaySource {
    enum class From {
        /** FLACie Web's own Apple Lossless decoder (a phone with no ALAC decoder). */
        OWN_ALAC,
        /** The downloaded file on the phone. */
        LOCAL,
        /** Jellyfin's conversion (HLS). */
        HLS,
        /** The original file from the server (or the NAS, or the phone's own music). */
        STREAM,
    }

    fun choose(ownAlac: Boolean, local: Boolean, needsTranscode: Boolean, dataSaverPinned: Boolean, hlsPossible: Boolean): From = when {
        ownAlac -> From.OWN_ALAC
        // a copy on the phone is no use when the phone can't decode it
        needsTranscode && hlsPossible -> From.HLS
        local -> From.LOCAL
        dataSaverPinned && hlsPossible -> From.HLS
        else -> From.STREAM
    }

    /** A download finished: a queued song still set to stream switches to the file, unless it is the one playing (it keeps what it has buffered; switching
     *  it would throw that away and start it again). */
    fun swapToLocal(isPlaying: Boolean, itemIsLocal: Boolean, now: From): Boolean = !isPlaying && !itemIsLocal && now == From.LOCAL
}

/**
 * How long a lost signal is waited out. In the car a dead zone of a minute or two is normal: the player must pause and carry on from exactly where it
 * stopped, never give up on the song or move to the next one. Two layers: the loader retries a failed read itself ([loaderDelayMs], [LOADER_RETRIES];
 * the audio already buffered keeps playing meanwhile), and once it does report an error the player prepares again from the same position
 * ([playerRetryDelayMs]). A server that says the song is not there ([hopeless]) is not waited for.
 */
object NetPatience {
    /** Failed reads in a row before the loader reports an error. With [loaderDelayMs] that is well over two minutes of no signal at all. */
    const val LOADER_RETRIES = 32
    /** Times the player prepares the song again (from where it stopped) after an error before it gives up and says so. */
    const val PLAYER_RETRIES = 12

    /** 1 s after the first failure, then a second more each time, never more than 5 s: a returning signal is used within seconds. */
    fun loaderDelayMs(errorCount: Int): Long = (errorCount.coerceAtLeast(1) * 1_000L).coerceAtMost(5_000L)

    /** An answer that no amount of waiting changes: not allowed, not there, gone, a range past the end. */
    fun hopeless(httpCode: Int): Boolean = httpCode in setOf(401, 403, 404, 410, 416)

    /** The wait before preparing again after error number [attempt] (1, 2, 4, 8, 15, then 30 s), or null to give up. A [hopeless] error gets one more
     *  try (the server may have been restarting). */
    fun playerRetryDelayMs(attempt: Int, hopeless: Boolean): Long? = when {
        attempt < 1 -> 1_000L
        hopeless -> if (attempt <= 1) 2_000L else null
        attempt > PLAYER_RETRIES -> null
        attempt <= 4 -> 1_000L shl (attempt - 1)
        attempt == 5 -> 15_000L
        else -> 30_000L
    }
}

/**
 * The songs fetched ahead into the disk cache, so a dead zone is covered by audio already on the phone. The player itself reads ahead through the same
 * cache as soon as the playing song is in (up to ten minutes), so the fetcher only starts when the player has stopped loading for now: it never takes
 * the line from the song that is playing. Any connection, mobile data too (two songs ahead, three on Wi-Fi), unless the person chose "Data saver".
 */
object PrefetchPlan {
    fun count(dataSaverPinned: Boolean, metered: Boolean, playerLoading: Boolean): Int = when {
        dataSaverPinned || playerLoading -> 0
        metered -> 2
        else -> 3
    }

    /** The first [n] of the upcoming songs' real addresses that are worth caching (not a downloaded file, not a conversion's one-off pieces). */
    fun targets(upcoming: List<String>, n: Int, cacheable: (String) -> Boolean): List<String> = if (n <= 0) emptyList() else upcoming.filter(cacheable).take(n)

    /** The download queue (songs kept offline) holds back while a streamed song is coming in: on a weak signal the two shared the line and the music
     *  stalled. [networkNearby]: the playing song or the next one is streamed. */
    fun streamNeedsLine(loading: Boolean, wantsToPlay: Boolean, networkNearby: Boolean): Boolean = loading && wantsToPlay && networkNearby
}
