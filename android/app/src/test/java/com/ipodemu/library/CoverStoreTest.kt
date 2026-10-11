package com.ipodemu.library

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

/** Covers are fetched once, again only when the picture's tag changed, never at other sizes, and deleted only when the device runs low on space. */
class CoverStoreTest {
    // ---- CoverIndex ---------------------------------------------------------------------------------------------

    @Test fun aPictureWithTheSameTagIsNeverRequestedAgain() {
        val ix = CoverIndex()
        assertTrue("nothing on disk: fetch", ix.needsFetch("jfA", false))
        ix.noteWanted(mapOf("jfA" to "t1"))
        assertTrue(ix.needsFetch("jfA", false))
        ix.saved("jfA", "t1", "\"etag1\"")
        assertFalse(ix.needsFetch("jfA", true))
        // every later listing reports the same tag: still nothing to fetch
        repeat(5) { assertTrue(ix.noteWanted(mapOf("jfA" to "t1")).isEmpty()) }
        assertFalse(ix.needsFetch("jfA", true))
        assertEquals("\"etag1\"", ix.entry("jfA")!!.etag)
    }

    @Test fun aChangedTagMeansExactlyOneRefetchWithTheOldEtag() {
        val ix = CoverIndex()
        ix.noteWanted(mapOf("jfA" to "t1")); ix.saved("jfA", "t1", "\"e1\"")
        val stale = ix.noteWanted(mapOf("jfA" to "t2"))
        assertEquals(listOf("jfA"), stale)
        assertTrue("the saved file is out of date", ix.needsFetch("jfA", true))
        assertEquals("the old etag goes with the request (If-None-Match)", "\"e1\"", ix.entry("jfA")!!.etag)
        assertEquals("t2", ix.entry("jfA")!!.want)
        ix.saved("jfA", "t2", "\"e2\"")
        assertFalse(ix.needsFetch("jfA", true))
        assertEquals("\"e2\"", ix.entry("jfA")!!.etag)
    }

    @Test fun aNotModifiedAnswerEndsTheRefetchWithoutNewBytes() {
        val ix = CoverIndex()
        ix.noteWanted(mapOf("jfA" to "t1")); ix.saved("jfA", "t1", "\"e1\"")
        ix.noteWanted(mapOf("jfA" to "t2"))
        assertTrue(ix.needsFetch("jfA", true))
        ix.saved("jfA", "t2", null)   // 304: the same file is still right; the etag stays
        assertFalse(ix.needsFetch("jfA", true))
        assertEquals("\"e1\"", ix.entry("jfA")!!.etag)
    }

    @Test fun theNowPlayingSizeIsTrackedOnlyOnceItWasFetched() {
        val ix = CoverIndex()
        ix.noteWanted(mapOf("jfA" to "t1", "jfB" to "t9"))
        assertNull("no second entry per cover for a whole library", ix.entry("FjfA"))
        assertEquals(2, ix.size())
        ix.saved("jfA", "t1", "\"e1\"")
        ix.saved("FjfA", "t1", "\"f1\"")
        val stale = ix.noteWanted(mapOf("jfA" to "t2"))
        assertEquals(setOf("jfA", "FjfA"), stale.toSet())
        assertTrue(ix.needsFetch("FjfA", true))
    }

    @Test fun anAlbumWithoutAPictureIsAskedForOnceADayAndAtOnceWhenItGetsATag() {
        val ix = CoverIndex(); val t0 = 1_000_000_000L
        ix.noteWanted(mapOf("jfA" to "t1"))
        ix.noPicture("jfA", t0)   // 404
        assertFalse(ix.needsFetch("jfA", false, t0 + 60_000))
        assertFalse(ix.needsFetch("jfA", false, t0 + 23 * 3600_000L))
        assertTrue("a day later it is tried again", ix.needsFetch("jfA", false, t0 + 25 * 3600_000L))
        // a new picture arrives with a new tag in the listing: fetched at once
        ix.noteWanted(mapOf("jfA" to "t2"))
        assertTrue(ix.needsFetch("jfA", false, t0 + 60_000))
    }

    @Test fun theIndexSurvivesARestart() {
        val ix = CoverIndex()
        ix.noteWanted(mapOf("jfA" to "t1", "jfB" to "t2")); ix.saved("jfA", "t1", "\"e\""); ix.noPicture("jfB", 5L)
        val again = CoverIndex(); again.load(ix.toJson())
        assertFalse(again.needsFetch("jfA", true))
        assertEquals("\"e\"", again.entry("jfA")!!.etag)
        assertEquals(5L, again.entry("jfB")!!.missAt)
        assertEquals(2, again.size())
    }

    @Test fun clearForgetsEverything() {
        val ix = CoverIndex()
        ix.noteWanted(mapOf("jfA" to "t1")); ix.saved("jfA", "t1", "\"e\"")
        ix.clear()
        assertEquals(0, ix.size())
        assertNull(ix.entry("jfA"))
        assertTrue("with the file gone it is fetched again", ix.needsFetch("jfA", false))
    }

    @Test fun aDeletedFileIsForgottenButItsWantedTagStays() {
        val ix = CoverIndex()
        ix.noteWanted(mapOf("jfA" to "t1")); ix.saved("jfA", "t1", "\"e\"")
        ix.forgetFile("jfA")
        assertNull(ix.entry("jfA")!!.etag)
        assertEquals("t1", ix.entry("jfA")!!.want)
        assertTrue(ix.needsFetch("jfA", false))
    }

    // ---- urls ----------------------------------------------------------------------------------------------------

    @Test fun oneFixedSizePerUse() {
        val row = JellyfinCovers.url("http://jf:8096/", "abc", full = false, tag = "T", webp = true)
        assertEquals("http://jf:8096/Items/abc/Images/Primary?maxWidth=240&quality=80&format=webp&tag=T", row)
        val big = JellyfinCovers.url("http://jf:8096", "abc", full = true, tag = "T", webp = true)
        assertEquals("http://jf:8096/Items/abc/Images/Primary?maxWidth=960&quality=85&format=webp&tag=T", big)
        assertTrue(JellyfinCovers.url("http://jf", "abc", false, null, false).endsWith("quality=80&format=jpg"))
        // only these two widths exist, however often it is asked
        val widths = (1..50).flatMap { listOf(false, true).map { f -> Regex("maxWidth=(\\d+)").find(JellyfinCovers.url("http://jf", "i$it", f, "t$it", true))!!.groupValues[1] } }.toSet()
        assertEquals(setOf("240", "960"), widths)
    }

    // ---- the fetcher through the gate -----------------------------------------------------------------------------

    private fun gateOf(transport: suspend (GateRequest) -> GateResponse) =
        JellyfinGate(GateConfig(), transport, CoroutineScope(SupervisorJob() + Dispatchers.Default))

    @Test fun theFetcherSendsTheEtagAndTheTagAndAcceptsA304() = runBlocking {
        val seen = ArrayList<GateRequest>()
        val f = CoverFetcher(gateOf { seen += it; GateResponse(304, ByteArray(0), emptyMap()) })
        val r = f.fetch("http://jf", "TOKEN", "abc", full = false, tag = "T1", etag = "\"e1\"", priority = Priority.VISIBLE)
        assertEquals(304, r.code)
        assertEquals(1, seen.size)
        assertEquals("\"e1\"", seen[0].headers["If-None-Match"])
        assertTrue(seen[0].url.contains("maxWidth=240") && seen[0].url.contains("&tag=T1") && seen[0].url.contains("format=webp"))
        assertEquals("cover", seen[0].kind)
    }

    @Test fun theNowPlayingSizeIsTheOnlyOtherSize() = runBlocking {
        val seen = ArrayList<GateRequest>()
        val f = CoverFetcher(gateOf { seen += it; GateResponse(200, ByteArray(3), emptyMap()) })
        f.fetch("http://jf", "T", "abc", full = true, tag = "T1", etag = null, priority = Priority.PLAYING)
        assertTrue(seen.single().url.contains("maxWidth=960"))
        assertFalse(seen.single().headers.containsKey("If-None-Match"))
        assertEquals("cover-full", seen.single().kind)
    }

    @Test fun aServerThatCannotMakeWebpIsAskedForJpegFromThenOn() = runBlocking {
        val seen = ArrayList<String>()
        val f = CoverFetcher(gateOf { r -> seen += r.url; if (r.url.contains("format=webp")) GateResponse(400, ByteArray(0), emptyMap()) else GateResponse(200, ByteArray(5), emptyMap()) })
        val a = f.fetch("http://jf", "T", "a", false, "t", null, Priority.VISIBLE)
        assertEquals(200, a.code)
        assertEquals(2, seen.size)
        assertFalse(f.webpOk)
        f.fetch("http://jf", "T", "b", false, "t", null, Priority.VISIBLE)
        assertEquals("no webp attempt any more", 3, seen.size)
        assertTrue(seen.last().contains("format=jpg"))
    }

    // ---- eviction -----------------------------------------------------------------------------------------------

    private class Fake(var free: Long, val total: Long = 100_000_000_000L) : StorageInfo {
        override fun freeBytes() = free
        override fun totalBytes() = total
    }

    /** A file that exists only as a name, a size and a last-used time (no disk is used); remembers whether it was deleted. */
    private class FakeFile(name: String, private val len: Long, private val mtime: Long) : File("/cache/$name") {
        var deleted = false
        override fun length() = len
        override fun lastModified() = mtime
        override fun delete(): Boolean { deleted = true; return true }
    }

    private val now = 10_000_000_000L
    private val day = 24 * 3600_000L
    private fun ff(name: String, mb: Long, ageMs: Long) = FakeFile(name, mb * 1_000_000L, now - ageMs)

    @Test fun nothingIsDeletedWhileThereIsRoom() {
        val files = (1..10).map { ff("a$it", 200, day * it) }
        val info = Fake(free = 50_000_000_000L)
        assertEquals(0L, CacheEvictor.needed(info))
        assertTrue(CacheEvictor.evict(files, info, now).isEmpty())
        assertTrue(files.none { it.deleted })
    }

    @Test fun whenSpaceIsLowTheLeastRecentlyUsedGoFirstAndItStopsAsSoonAsEnoughIsFreed() {
        // 100 GB total: the target is 10 GB free; 100 MB short, plus a 500 MB margin = 600 MB to free
        val files = (1..10).map { ff("b$it", 200, day * it) }   // b10 is the oldest, b1 the newest
        val info = Fake(free = 10_000_000_000L - 100_000_000L)
        assertEquals(100_000_000L, CacheEvictor.needed(info))
        val gone = CacheEvictor.evict(files.shuffled(), info, now)
        assertEquals("oldest first, three files = 600 MB", listOf("b10", "b9", "b8"), gone.map { it.name })
        assertTrue(files.filter { it.name !in setOf("b10", "b9", "b8") }.none { it.deleted })
    }

    @Test fun whatWasUsedInTheLastHalfHourIsNeverDeleted() {
        val old = ff("old", 10, 3 * day)
        val fresh = (1..3).map { ff("fresh$it", 10, 60_000L * it) }
        val gone = CacheEvictor.evict(fresh + old, Fake(free = 1_000_000_000L), now)   // far below the target
        assertEquals(listOf("old"), gone.map { it.name })
        assertTrue(fresh.none { it.deleted })
    }

    @Test fun thresholdIsTenPercentOrOneAndAHalfGigabytes() {
        assertEquals(500_000_000L, CacheEvictor.needed(Fake(free = 1_000_000_000L, total = 8_000_000_000L)))   // 10 % of 8 GB < 1.5 GB: 1.5 GB is the target
        assertEquals(1_000_000_000L, CacheEvictor.needed(Fake(free = 9_000_000_000L, total = 100_000_000_000L)))
        assertEquals(0L, CacheEvictor.needed(Fake(free = 2_000_000_000L, total = 8_000_000_000L)))
    }
}
