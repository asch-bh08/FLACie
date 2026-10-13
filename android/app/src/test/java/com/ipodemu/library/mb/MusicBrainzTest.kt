package com.ipodemu.library.mb

import com.ipodemu.library.Track
import com.ipodemu.library.TrackSource
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File
import java.io.IOException

class MusicBrainzTest {
    private fun t(title: String = "Out of Space", artist: String = "The Prodigy", album: String = "Experience", dur: Long = 152_000) = Track(
        path = "http://jf/Audio/a1b2c3d4e5f60718293a4b5c6d7e8f90/stream?static=true", title = title, artist = artist, album = album, albumArtist = artist, genre = "", trackNo = 1, discNo = 1,
        durationMs = dur, year = 1992, isMusic = true, artKey = null, mtime = 0, size = 0, source = TrackSource.JELLYFIN)

    private val json = """
    {"recordings":[
      {"id":"r1","score":100,"title":"Out of Space","length":152000,"artist-credit":[{"name":"The Prodigy","joinphrase":""}],"releases":[{"id":"rel1","title":"Experience","date":"1992-09-28","country":"GB","release-group":{"id":"rg1"}}]},
      {"id":"r2","score":90,"title":"Out of Space (Live)","length":300000,"artist-credit":[{"name":"The Prodigy"}],"releases":[{"id":"rel2","title":"Live at X","date":"1995"}]},
      {"id":"r3","score":60,"title":"Out of Space","length":150000,"artist-credit":[{"name":"Someone","joinphrase":" & "},{"name":"Else"}],"releases":[]}
    ]}"""

    private class Clock { var now = 1_000_000L; val sleeps = ArrayList<Long>(); suspend fun sleep(ms: Long) { sleeps += ms; now += ms } }

    private fun client(http: MbHttp, c: Clock, cache: File? = null) = MbClient(http, MbClient.userAgentFor("1.0-test"), cache, MbGate(1100, { c.now }, { c.sleep(it) }), { c.now })

    @Test fun userAgentNamesTheAppAndAContact() {
        assertEquals("FLACie/1.0-beta61 ( https://github.com/asch-bh08/FLACie )", MbClient.userAgentFor("1.0-beta61"))
        assertEquals("FLACie/2 ( me@example.org )", MbClient.userAgentFor("2", " me@example.org "))
        assertEquals("FLACie/2 ( https://github.com/asch-bh08/FLACie )", MbClient.userAgentFor("2", "  "))
    }

    @Test fun queryIsQuotedAndEscaped() {
        assertEquals("recording:\"Out of Space\" AND artist:\"The Prodigy\" AND release:\"Experience\"", MbClient.queryFor(t()))
        assertTrue(MbClient.queryFor(t(title = "Say \"Hi\"")).contains("recording:\"Say \\\"Hi\\\"\""))
    }

    @Test fun parseReadsArtistsReleasesAndYear() {
        val l = MbClient.parse(json)
        assertEquals(3, l.size)
        assertEquals("r1", l[0].mbid); assertEquals("The Prodigy", l[0].artist); assertEquals("Experience", l[0].album); assertEquals("rel1", l[0].releaseMbid); assertEquals("rg1", l[0].releaseGroupMbid)
        assertEquals(1992, l[0].year); assertEquals(152000, l[0].lengthMs); assertEquals("GB", l[0].country); assertEquals(100, l[0].apiScore)
        assertEquals(1995, l[1].year); assertNull(l[1].releaseGroupMbid); assertEquals("", l[2].album); assertEquals("Someone & Else", l[2].artist)
        assertTrue(MbClient.parse("{}").isEmpty()); assertTrue(MbClient.parse("not json").isEmpty()); assertTrue(MbClient.parse("""{"recordings":[{"title":"no id"}]}""").isEmpty())
    }

    @Test fun theRightRecordingScoresHighest() {
        val ranked = MbMatching.rank(t(), MbClient.parse(json))
        assertEquals("r1", ranked[0].first.mbid); assertTrue(ranked[0].second >= MbMatching.LIKELY)
        assertTrue(ranked[1].second < ranked[0].second); assertTrue(ranked[2].second < MbMatching.LIKELY)
    }

    @Test fun scoreIgnoresCaseEditionsAndUsesTheLength() {
        val c = MbCandidate("x", "OUT OF SPACE", "the prodigy", "Experience (Expanded Edition)", null, null, 1992, 151_000, null, 50)
        assertEquals(100.0, MbMatching.score(t(), c), 0.0)
        assertEquals(85.0, MbMatching.score(t(), MbCandidate("x", "OUT OF SPACE", "the prodigy", "Experience (Expanded Edition)", null, null, 1992, 400_000, null, 50)), 0.0)
        assertTrue(MbMatching.score(t(artist = "Someone Else"), c) < MbMatching.LIKELY)
    }

    @Test fun theRequestCarriesTheUserAgentAndRequestsAreSpaced() = runBlocking {
        val seen = ArrayList<Pair<String, Map<String, String>>>(); val c = Clock()
        val cl = client({ u, h -> seen += u to h; MbResponse(200, json) }, c)
        cl.search(t("A")); cl.search(t("B")); cl.search(t("C"))
        assertEquals(3, seen.size)
        assertTrue(seen[0].first.startsWith("https://musicbrainz.org/ws/2/recording?query=")); assertTrue(seen[0].first.contains("fmt=json"))
        assertEquals("FLACie/1.0-test ( https://github.com/asch-bh08/FLACie )", seen[0].second["User-Agent"])
        assertEquals(2, c.sleeps.size); assertTrue(c.sleeps.all { it in 1099..1101 })   // the first goes at once, the next two wait out the gap
    }

    @Test fun aRetryLaterAnswerIsWaitedOutAndTriedAgain() = runBlocking {
        var n = 0; val c = Clock()
        val l = client({ _, _ -> n++; if (n == 1) MbResponse(503, "", 7) else MbResponse(200, json) }, c).search(t())
        assertEquals(3, l.size); assertEquals(2, n); assertTrue(c.sleeps.any { it in 6_900..7_200 })
    }

    @Test fun givesUpAfterThreeBusyAnswersAndFailsAtOnceOnOtherErrors() = runBlocking {
        var busy = 0
        try { client({ _, _ -> busy++; MbResponse(503, "") }, Clock()).search(t()); assertTrue("should have thrown", false) } catch (_: IOException) { }
        assertEquals(3, busy)
        var bad = 0
        try { client({ _, _ -> bad++; MbResponse(400, "") }, Clock()).search(t()); assertTrue("should have thrown", false) } catch (_: IOException) { }
        assertEquals(1, bad)
    }

    @Test fun theSecondAskComesFromTheDiskCacheAndExpires() = runBlocking {
        val dir = File(System.getProperty("java.io.tmpdir"), "mbt-" + System.nanoTime())
        try {
            var n = 0; val c = Clock(); val cl = client({ _, _ -> n++; MbResponse(200, json) }, c, dir)
            cl.search(t()); cl.search(t()); assertEquals(1, n)
            client({ _, _ -> n++; MbResponse(200, json) }, c, dir).search(t()); assertEquals("a restart: the cache is on disk", 1, n)
            c.now += 31L * 24 * 3600 * 1000
            client({ _, _ -> n++; MbResponse(200, json) }, c, dir).search(t()); assertEquals(2, n)
        } finally { dir.deleteRecursively() }
    }

    @Test fun nothingFoundIsRememberedLessLong() = runBlocking {
        val dir = File(System.getProperty("java.io.tmpdir"), "mbt-" + System.nanoTime())
        try {
            var n = 0; val c = Clock(); val empty = """{"recordings":[]}"""
            val cl = client({ _, _ -> n++; MbResponse(200, empty) }, c, dir)
            assertTrue(cl.search(t()).isEmpty()); assertTrue(cl.search(t()).isEmpty()); assertEquals(1, n)
            c.now += 8L * 24 * 3600 * 1000
            client({ _, _ -> n++; MbResponse(200, empty) }, c, dir).search(t()); assertEquals(2, n)
        } finally { dir.deleteRecursively() }
    }

    @Test fun aSongWithNoTitleAsksNothing() = runBlocking {
        var n = 0
        assertTrue(client({ _, _ -> n++; MbResponse(200, json) }, Clock()).search(t(title = "  ")).isEmpty()); assertEquals(0, n)
    }

    // ---- Cover Art Archive ----
    private val caaJson = """
    {"images":[
      {"id":2,"image":"http://coverartarchive.org/release/r/2.jpg","front":false,"back":true,"types":["Back"],"thumbnails":{"250":"http://coverartarchive.org/release/r/2-250.jpg","1200":"http://coverartarchive.org/release/r/2-1200.jpg"}},
      {"id":1,"image":"http://coverartarchive.org/release/r/1.jpg","front":true,"back":false,"types":["Front"],"thumbnails":{"small":"http://coverartarchive.org/release/r/1-250.jpg","large":"http://coverartarchive.org/release/r/1-500.jpg"}},
      {"image":"http://x/no-id.jpg"}
    ]}"""

    @Test fun caaListIsFrontFirstHttpsAndSkipsBrokenOnes() {
        val l = CaaClient.parse(caaJson)
        assertEquals(2, l.size); assertEquals("1", l[0].id); assertTrue(l[0].front)
        assertEquals("https://coverartarchive.org/release/r/1-250.jpg", l[0].thumb); assertEquals("https://coverartarchive.org/release/r/1-500.jpg", l[0].full)
        assertEquals("https://coverartarchive.org/release/r/2-1200.jpg", l[1].full); assertTrue(CaaClient.parse("{}").isEmpty())
    }

    @Test fun aReleaseWithNoPicturesFallsBackToItsReleaseGroup() = runBlocking {
        val seen = ArrayList<String>()
        val l = CaaClient({ u, _ -> seen += u; if (u.contains("/release/")) MbResponse(404, "") else MbResponse(200, caaJson) }, "ua", MbGate(0, { 0L }, { })).list("rel", "rg")
        assertEquals(2, l.size); assertEquals(listOf("https://coverartarchive.org/release/rel", "https://coverartarchive.org/release-group/rg"), seen)
        assertTrue(CaaClient({ _, _ -> MbResponse(404, "") }, "ua", MbGate(0, { 0L }, { })).list("a", "b").isEmpty())
        assertTrue(CaaClient({ _, _ -> MbResponse(404, "") }, "ua", MbGate(0, { 0L }, { })).list(null, null).isEmpty())
        try { CaaClient({ _, _ -> MbResponse(503, "") }, "ua", MbGate(0, { 0L }, { })).list("a", null); assertTrue("should have thrown", false) } catch (_: IOException) { }
    }

    @Test fun picturesAreRecognisedByTheirFirstBytes() {
        assertEquals("image/jpeg", CaaClient.sniff(byteArrayOf(0xFF.toByte(), 0xD8.toByte(), 0xFF.toByte(), 0xE0.toByte())))
        assertEquals("image/png", CaaClient.sniff(byteArrayOf(0x89.toByte(), 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)))
        assertEquals("image/webp", CaaClient.sniff("RIFF\u0000\u0000\u0000\u0000WEBPVP8 ".toByteArray(Charsets.ISO_8859_1)))
        assertNull(CaaClient.sniff("<html>".toByteArray())); assertNull(CaaClient.sniff(ByteArray(0)))
    }

    // ---- the choice and the pins ----
    @Test fun theChoiceSurvivesARestartAndCanBeCleared() {
        val f = File(System.getProperty("java.io.tmpdir"), "mbo-" + System.nanoTime() + ".json")
        try {
            val o = MbOverlay(f); val c = MbClient.parse(json)[0]
            assertNull(o.get("k")); o.set("k", c, 5L)
            val back = MbOverlay(f).get("k")!!
            assertEquals("r1", back.mbid); assertEquals("rel1", back.releaseMbid); assertEquals(1992, back.year); assertEquals(5L, back.chosenMs)
            o.clear("k"); assertNull(MbOverlay(f).get("k"))
        } finally { f.delete() }
    }

    @Test fun aPinnedCoverIsKeptOnlyIfItIsAPictureAndCanBeUndone() {
        val dir = File(System.getProperty("java.io.tmpdir"), "pins-" + System.nanoTime())
        try {
            val p = CoverPins(dir); val png = byteArrayOf(0x89.toByte(), 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3)
            assertFalse(p.has("jfabc")); assertFalse(p.set("jfabc", "<html>".toByteArray())); assertFalse(p.has("jfabc"))
            assertTrue(p.set("jfabc", png)); assertTrue(p.has("jfabc")); assertTrue(CoverPins(dir).has("jfabc")); assertFalse(p.has("jfother"))
            assertEquals(png.toList(), p.file("jfabc")!!.readBytes().toList())
            p.clear("jfabc"); assertFalse(p.has("jfabc"))
        } finally { dir.deleteRecursively() }
    }
}
