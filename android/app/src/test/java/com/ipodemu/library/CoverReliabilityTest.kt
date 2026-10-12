package com.ipodemu.library

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.advanceUntilIdle
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertSame
import org.junit.Assert.assertTrue
import org.junit.Test
import kotlin.random.Random

/** The cover work of beta 56: wrong covers vetted out, failed covers asked for again, visible rows never queued one at a time, the saved Home shelves. */
@OptIn(ExperimentalCoroutinesApi::class)
class CoverReliabilityTest {
    private fun id(n: Int) = "%032x".format(n)
    private fun jf(n: Int, album: String, key: String?, source: TrackSource = TrackSource.JELLYFIN) = Track(
        path = "http://jf/Audio/${id(n)}/stream?static=true", title = "Song $n", artist = "Artist", album = album, albumArtist = "Artist", genre = "", trackNo = n, discNo = 1,
        durationMs = 1000, year = 2000, isMusic = true, artKey = key, mtime = 0, size = 0, source = source)

    // ---- covers that match the song -------------------------------------------------------------------------------

    @Test fun aSongsOwnPictureIsAlwaysKept() {
        // its own picture, even when other songs of "the same AlbumId" disagree about the album
        val ts = listOf(jf(1, "A", "jf" + id(1)), jf(2, "B", "jf" + id(2)), jf(3, "", "jf" + id(3)))
        assertSame(ts, CoverVetting.vet(ts))
    }

    @Test fun anAlbumFallbackIsKeptWhenAllItsSongsAreOfOneAlbum() {
        val album = "jf" + id(900)
        val ts = listOf(jf(1, "Wasting Light", album), jf(2, "Wasting Light (Deluxe Edition)", album), jf(3, "wasting light", album))
        assertSame("editions and case do not make it another album", ts, CoverVetting.vet(ts))
    }

    @Test fun anAlbumFallbackSharedByDifferentAlbumsIsDroppedForEverySong() {
        // one AlbumId for songs of two albums: the picture is of one of them, nobody can say which
        val album = "jf" + id(900)
        val ts = listOf(jf(1, "You Could Have It So Much Better", album), jf(2, "Franz Ferdinand", album), jf(3, "Franz Ferdinand", album))
        val out = CoverVetting.vet(ts)
        assertTrue(out.all { it.artKey == null })
    }

    @Test fun songsWithNoAlbumNameNeverBorrowAnAlbumsPicture() {
        val album = "jf" + id(900)
        val ts = listOf(jf(1, "Athlete", album), jf(2, "", album), jf(3, "<Unknown>", album))
        val out = CoverVetting.vet(ts)
        assertEquals(album, out[0].artKey)
        assertNull("blank album", out[1].artKey)
        assertEquals("<Unknown> is not a name, but the song is no album either: it is of no album, not of 'Athlete'", null, out[2].artKey)
    }

    @Test fun aGarbageAlbumIdWithManySongsGivesThemAllNoCover() {
        val album = "jf" + id(900)
        val ts = (1..50).map { jf(it, if (it % 3 == 0) "" else "Album $it", album) }
        assertTrue(CoverVetting.vet(ts).all { it.artKey == null })
    }

    @Test fun otherSourcesAndMissingKeysAreLeftAlone() {
        val ts = listOf(jf(1, "A", "px123", TrackSource.PLEX), jf(2, "A", null), jf(3, "A", "nfabc", TrackSource.NAS))
        assertSame(ts, CoverVetting.vet(ts))
    }

    @Test fun vettingTwiceGivesTheSameResult() {
        val album = "jf" + id(900)
        val ts = listOf(jf(1, "A", album), jf(2, "B", album), jf(3, "C", "jf" + id(3)))
        val once = CoverVetting.vet(ts)
        assertEquals(once, CoverVetting.vet(once))
    }

    // ---- failed covers are asked for again ------------------------------------------------------------------------

    @Test fun aTemporaryFailureIsRetriedWithGrowingPauses() = runTest {
        var calls = 0; val sleeps = ArrayList<Long>()
        val r = retryTransient(sleep = { sleeps += it }, random = { 0.5 }, exhausted = { "gave up" }) { if (++calls <= 3) throw TransientFailure() else "cover" }
        assertEquals("cover", r)
        assertEquals(4, calls)
        assertEquals(listOf(400L, 800L, 1600L), sleeps)
    }

    @Test fun thePauseNeverGrowsPastFifteenSeconds() {
        assertEquals(400, backoffMs(1, 0.5))
        assertEquals(12_800, backoffMs(6, 0.5))
        assertEquals(15_000, backoffMs(7, 0.5))
        assertEquals(15_000, backoffMs(40, 0.5))
        assertTrue(backoffMs(3, 0.0) < backoffMs(3, 1.0))
    }

    @Test fun backgroundWorkGivesUpAfterItsAttempts() = runTest {
        var calls = 0
        val r = retryTransient(attempts = 3, sleep = {}, exhausted = { "gave up" }) { calls++; throw TransientFailure() }
        assertEquals("gave up", r)
        assertEquals(3, calls)
    }

    @Test fun aRowThatKeepsWaitingKeepsAsking() = runTest {
        var calls = 0
        val r = retryTransient(sleep = {}, exhausted = { "gave up" }) { if (++calls < 50) throw TransientFailure() else "cover" }
        assertEquals("cover", r); assertEquals(50, calls)
    }

    @Test fun leavingTheScreenStopsTheRetries() = runTest {
        var calls = 0
        val job = launch { retryTransient(exhausted = { 0 }) { calls++; throw TransientFailure() } }
        runCurrent(); assertEquals(1, calls)
        testScheduler.advanceTimeBy(1_000); runCurrent()
        val before = calls
        job.cancel(); advanceUntilIdle()
        testScheduler.advanceTimeBy(60_000); runCurrent()
        assertEquals("nothing asks after the row is gone", before, calls)
    }

    @Test fun aSettledAnswerIsNotRetried() = runTest {
        var calls = 0
        val r = retryTransient(sleep = {}, exhausted = { null }) { calls++; "no picture" }
        assertEquals("no picture", r); assertEquals(1, calls)
    }

    // ---- the gate: visible rows are never queued one at a time ----------------------------------------------------

    private class Rig(ts: kotlinx.coroutines.test.TestScope, cfg: GateConfig, transport: suspend (GateRequest) -> GateResponse) {
        val gate = JellyfinGate(cfg, transport, CoroutineScope(SupervisorJob() + StandardTestDispatcher(ts.testScheduler)),
            now = { ts.testScheduler.currentTime }, sleep = { delay(it) }, random = Random(1))
    }

    private val ok = GateResponse(200, "ok".toByteArray())

    @Test fun visibleRowsRunFourAtOnceEvenWhenTheLimitHasDroppedToOne() = runTest {
        var cur = 0; var max = 0
        val r = Rig(this, GateConfig(initialLimit = 1, minLimit = 1, maxLimit = 1, playingReserve = 0, visibleFloor = 4)) { cur++; max = maxOf(max, cur); delay(100); cur--; ok }
        (1..20).map { async { r.gate.execute(GateRequest(url = "http://x/c$it", kind = "cover"), Priority.VISIBLE) } }.awaitAll()
        assertEquals(4, max)
    }

    @Test fun backgroundWorkStaysWithinTheLimit() = runTest {
        var cur = 0; var max = 0
        val r = Rig(this, GateConfig(initialLimit = 2, minLimit = 2, maxLimit = 2, playingReserve = 0, visibleFloor = 4)) { cur++; max = maxOf(max, cur); delay(100); cur--; ok }
        (1..12).map { async { r.gate.execute(GateRequest(url = "http://x/b$it", kind = "taste"), Priority.BACKGROUND) } }.awaitAll()
        assertTrue("at most the limit, never the visible floor: $max", max <= 2)
    }

    @Test fun aSlowCoverDoesNotShrinkTheLimitButASlowListDoes() = runTest {
        val r = Rig(this, GateConfig(initialLimit = 6, maxRetries = 0, slowMs = 2500)) { req -> delay(6_000); ok }
        repeat(3) { r.gate.execute(GateRequest(url = "http://x/c$it", kind = "cover"), Priority.VISIBLE) }
        assertEquals("a server resizing pictures for the first time is not a loaded server", 6, r.gate.currentLimit)
        assertEquals("and its time does not hold background work back", 0L, r.gate.stats().avgMs)
        r.gate.execute(GateRequest(url = "http://x/item", kind = "info"), Priority.VISIBLE)
        assertEquals(3, r.gate.currentLimit)
    }

    @Test fun aFailingCoverStillCountsAgainstTheServer() = runTest {
        val r = Rig(this, GateConfig(initialLimit = 6, maxRetries = 0, breakerFailures = 100)) { GateResponse(503, ByteArray(0)) }
        r.gate.execute(GateRequest(url = "http://x/c", kind = "cover"), Priority.VISIBLE)
        assertEquals(3, r.gate.currentLimit)
    }

    // ---- the saved Home ----------------------------------------------------------------------------------------------

    @Test fun homeShelvesSurviveTheFile() {
        val a = jf(1, "Album A", "jf" + id(1)); val b = jf(2, "Album B", "jf" + id(2))
        val data = HomeSnapshot.Data(
            listOf(HomeShelf("listen-again", "Listen again", "Your recent plays", listOf(a, b)),
                HomeShelf("albums-for-you", "Albums for you", "From your library", albums = listOf(Group("Album B", listOf(b, a), "jf" + id(2))))),
            listOf("Relax", "Focus"))
        val back = HomeSnapshot.fromJson(HomeSnapshot.toJson(data))
        assertNotNull(back)
        assertEquals(listOf("Relax", "Focus"), back!!.moods)
        assertEquals(listOf("listen-again", "albums-for-you"), back.shelves.map { it.id })
        assertEquals(listOf(a, b), back.shelves[0].tracks)
        val g = back.shelves[1].albums.single()
        assertEquals("Album B", g.name); assertEquals("jf" + id(2), g.artKey); assertEquals("an album card keeps only its first song", listOf(b), g.tracks)
    }

    @Test fun anUnusableSnapshotIsNoSnapshot() {
        assertNull(HomeSnapshot.fromJson("not json"))
        assertNull(HomeSnapshot.fromJson("{\"v\":99,\"shelves\":[]}"))
        assertNull(HomeSnapshot.fromJson("{\"v\":1,\"shelves\":[]}"))
    }

    @Test fun theFirstCoversAreWarmedInDrawOrder() {
        val a = jf(1, "A", "jf" + id(1)); val b = jf(2, "B", "jf" + id(2)); val c = jf(3, "C", null)
        val data = HomeSnapshot.Data(listOf(HomeShelf("x", "X", "", listOf(a, c, a)), HomeShelf("y", "Y", "", albums = listOf(Group("B", listOf(b), "jf" + id(2))))), emptyList())
        assertEquals(listOf("jf" + id(1) to true, "jf" + id(2) to false), HomeSnapshot.coverKeys(data))
        assertEquals("only as many as asked", 1, HomeSnapshot.coverKeys(data, 1).size)
    }
}
