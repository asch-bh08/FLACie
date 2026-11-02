package com.ipodemu.playback

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** Driving through patchy signal (owner, 30 Oct): where each song plays from, how long a dead zone is waited out, and what is fetched ahead. */
class WeakSignalTest {
    private fun choose(own: Boolean = false, local: Boolean = false, transcode: Boolean = false, pinned: Boolean = false, hls: Boolean = true) =
        PlaySource.choose(ownAlac = own, local = local, needsTranscode = transcode, dataSaverPinned = pinned, hlsPossible = hls)

    @Test fun aDownloadedSongPlaysFromThePhone() {
        assertEquals(PlaySource.From.LOCAL, choose(local = true))
        // even with "Data saver" pinned: the file on the phone uses no data at all
        assertEquals(PlaySource.From.LOCAL, choose(local = true, pinned = true))
    }

    @Test fun aStreamedSongIsTheOriginalFileNotTheLiveConversion() {
        // the automatic weak-signal switch to Jellyfin's live HLS conversion is gone: it jumped forward on a weak signal
        assertEquals(PlaySource.From.STREAM, choose())
        assertEquals(PlaySource.From.STREAM, choose(pinned = false, hls = true))
    }

    @Test fun theConversionIsOnlyForWhatThePhoneCannotPlayOrWhenThePersonPinnedDataSaver() {
        assertEquals(PlaySource.From.HLS, choose(transcode = true))
        // a file the phone can't decode is converted even when a copy is on the phone (the copy would be silent)
        assertEquals(PlaySource.From.HLS, choose(transcode = true, local = true))
        assertEquals(PlaySource.From.HLS, choose(pinned = true))
        // not a Jellyfin song (the NAS, a file on the phone): there is no conversion to use
        assertEquals(PlaySource.From.STREAM, choose(pinned = true, hls = false))
        assertEquals(PlaySource.From.OWN_ALAC, choose(own = true, local = true, transcode = true, pinned = true))
    }

    @Test fun aFinishedDownloadReplacesUpcomingStreamsButNeverThePlayingSong() {
        assertTrue(PlaySource.swapToLocal(isPlaying = false, itemIsLocal = false, now = PlaySource.From.LOCAL))
        assertFalse("the playing song keeps what it has buffered", PlaySource.swapToLocal(isPlaying = true, itemIsLocal = false, now = PlaySource.From.LOCAL))
        assertFalse(PlaySource.swapToLocal(isPlaying = false, itemIsLocal = true, now = PlaySource.From.LOCAL))
        assertFalse(PlaySource.swapToLocal(isPlaying = false, itemIsLocal = false, now = PlaySource.From.STREAM))
    }

    @Test fun theLoaderWaitsOutADeadZoneOfTwoMinutes() {
        // no signal: every try fails at once; the waits between tries must add up to more than two minutes before the player reports an error
        var waited = 0L
        for (n in 1..NetPatience.LOADER_RETRIES) waited += NetPatience.loaderDelayMs(n)
        assertTrue("waited $waited ms", waited >= 120_000L)
        // but a returning signal is never left unused for long
        for (n in 1..100) assertTrue(NetPatience.loaderDelayMs(n) in 1_000L..5_000L)
    }

    @Test fun aSongThatIsGoneFailsAtOnceInsteadOfWaiting() {
        for (c in listOf(401, 403, 404, 410, 416)) assertTrue("$c", NetPatience.hopeless(c))
        // no answer at all, a timeout, a busy or restarting server: worth waiting for
        for (c in listOf(0, 408, 429, 500, 502, 503, 504)) assertFalse("$c", NetPatience.hopeless(c))
    }

    @Test fun thePlayerKeepsRetryingFromTheSamePlaceWithGrowingWaits() {
        val waits = (1..NetPatience.PLAYER_RETRIES).map { NetPatience.playerRetryDelayMs(it, hopeless = false) }
        waits.forEach { assertNotNull(it) }
        assertEquals(1_000L, waits.first())
        assertTrue(waits.zipWithNext().all { (a, b) -> b!! >= a!! })
        assertTrue(waits.all { it!! <= 30_000L })
        assertNull("it gives up only after many tries", NetPatience.playerRetryDelayMs(NetPatience.PLAYER_RETRIES + 1, hopeless = false))
        // a song that is gone is tried once more (the server may have been mid-restart), then left
        assertNotNull(NetPatience.playerRetryDelayMs(1, hopeless = true))
        assertNull(NetPatience.playerRetryDelayMs(2, hopeless = true))
    }

    @Test fun songsAheadAreFetchedOnAnyConnectionOnceThePlayerHasWhatItNeeds() {
        assertEquals(2, PrefetchPlan.count(dataSaverPinned = false, metered = true, playerLoading = false))
        assertEquals(3, PrefetchPlan.count(dataSaverPinned = false, metered = false, playerLoading = false))
        // the song playing is still coming in: it has the line to itself
        assertEquals(0, PrefetchPlan.count(dataSaverPinned = false, metered = true, playerLoading = true))
        assertEquals(0, PrefetchPlan.count(dataSaverPinned = true, metered = false, playerLoading = false))
    }

    @Test fun onlyStreamedSongsAreFetchedNotOnesAlreadyOnThePhone() {
        val ups = listOf("file:///offline/a.flac", "https://jf/Audio/1/stream", "https://jf/Audio/2/universal?x", "https://jf/Audio/3/stream", "https://jf/Audio/4/stream")
        val cacheable = { u: String -> u.startsWith("https") && !u.contains("/universal") }
        assertEquals(listOf("https://jf/Audio/1/stream", "https://jf/Audio/3/stream"), PrefetchPlan.targets(ups.take(4), 2, cacheable))
        assertEquals(emptyList<String>(), PrefetchPlan.targets(ups, 0, cacheable))
    }

    @Test fun downloadsGiveWayWhileAStreamedSongIsComingIn() {
        assertTrue(PrefetchPlan.streamNeedsLine(loading = true, wantsToPlay = true, networkNearby = true))
        assertFalse(PrefetchPlan.streamNeedsLine(loading = false, wantsToPlay = true, networkNearby = true))
        assertFalse(PrefetchPlan.streamNeedsLine(loading = true, wantsToPlay = false, networkNearby = true))
        assertFalse("downloaded songs need no line", PrefetchPlan.streamNeedsLine(loading = true, wantsToPlay = true, networkNearby = false))
    }

    // a skip or a tap on the next song starts within a second (owner, 1 Nov): the first seconds of the next two songs are on the phone early, even while
    // the playing song is still coming in on a weak signal
    @Test fun theStartsOfTheNextSongsAreFetchedWhileThePlayingOneLoads() {
        assertEquals(2, PrefetchPlan.heads(dataSaverPinned = false, playerLoading = true, bufferedAheadMs = 20_000))
        assertEquals("not before the playing song has some in hand", 0, PrefetchPlan.heads(dataSaverPinned = false, playerLoading = true, bufferedAheadMs = 5_000))
        assertEquals("the whole songs are fetched then", 0, PrefetchPlan.heads(dataSaverPinned = false, playerLoading = false, bufferedAheadMs = 60_000))
        assertEquals(0, PrefetchPlan.heads(dataSaverPinned = true, playerLoading = true, bufferedAheadMs = 60_000))
    }

    // owner, 2 Nov: a 50-song playlist in the car stopped after about 20; a song the server doesn't have stopped the whole queue (the message was on the phone, not the car)
    @Test fun aSongTheServerDoesNotHaveIsSkippedButALostSignalStillWaits() {
        assertTrue(NetPatience.skipsWhenGivingUp(hopeless = true, hasNext = true))
        assertFalse("the last song: nothing to skip to", NetPatience.skipsWhenGivingUp(hopeless = true, hasNext = false))
        assertFalse("no signal: skipping would run through the whole queue", NetPatience.skipsWhenGivingUp(hopeless = false, hasNext = true))
    }

    // owner, 2 Nov: Wild Ones "stays silent, infinite loading", every time; the file is fine on the server. A song stuck loading at its start, with signal, is
    // fetched fresh (not from a saved copy), and skipped if that doesn't start either; with no signal it waits as before
    @Test fun aSongStuckLoadingAtItsStartIsFetchedFreshThenSkipped() {
        assertEquals(StartWatch.Act.WAIT, StartWatch.act(loadingMs = 5_000, online = true, freshTried = false, hasNext = true))
        assertEquals(StartWatch.Act.FRESH, StartWatch.act(loadingMs = StartWatch.FRESH_MS, online = true, freshTried = false, hasNext = true))
        assertEquals(StartWatch.Act.WAIT, StartWatch.act(loadingMs = StartWatch.FRESH_MS + 1_000, online = true, freshTried = true, hasNext = true))
        assertEquals(StartWatch.Act.SKIP, StartWatch.act(loadingMs = StartWatch.SKIP_MS, online = true, freshTried = true, hasNext = true))
        assertEquals("the last song: nothing to skip to", StartWatch.Act.WAIT, StartWatch.act(loadingMs = StartWatch.SKIP_MS, online = true, freshTried = true, hasNext = false))
        assertEquals("no signal: waiting is right", StartWatch.Act.WAIT, StartWatch.act(loadingMs = 600_000, online = false, freshTried = false, hasNext = true))
        assertTrue(StartWatch.SKIP_MS > StartWatch.FRESH_MS)
    }
}
