package com.ipodemu.library

import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.async
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.yield
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** The Jellyfin song list sync, against a fake server: first pass paged and progressive, then deltas since the cursor, then deletions. */
class JellyfinSyncTest {
    private fun id(n: Int) = "%032x".format(n)
    private fun stamp(n: Int) = "2026-01-01T%02d:%02d:%02d.0000000Z".format(n / 3600, n / 60 % 60, n % 60)

    private fun item(n: Int, saved: Int = n, title: String = "Song %03d".format(n), coverTag: String? = "tag$n") = JellyfinDirectClient.Item(
        id(n),
        Track(path = "http://jf/Audio/${id(n)}/stream?static=true", title = title, artist = "Artist", album = "Album", albumArtist = "Artist", genre = "", trackNo = 0, discNo = 0,
            durationMs = 1000, year = 0, isMusic = true, artKey = "jf${id(n)}", mtime = 0, size = 1, source = TrackSource.JELLYFIN, filePath = "/m/a/b/$n.flac"),
        AudioFacts.Fact("flac", 44_100, 16, 900), stamp(saved), "jf${id(n)}", coverTag,
    )

    /** A server: items by id with their save stamp; counts the requests it gets. */
    private class Server(val items: LinkedHashMap<String, JellyfinDirectClient.Item>) : JfApi {
        val pages = ArrayList<Triple<Int, Int, String?>>()
        var counts = 0; var idCalls = 0; val byIdCalls = ArrayList<List<String>>()
        var failPageAt = -1
        override suspend fun page(startIndex: Int, limit: Int, minSaved: String?): JellyfinDirectClient.Page {
            val fail = failPageAt == pages.size
            pages += Triple(startIndex, limit, minSaved)
            if (fail) throw java.io.IOException("down")
            val all = items.values.filter { minSaved == null || it.savedAt >= minSaved }.sortedBy { it.track.title }
            return JellyfinDirectClient.Page(all.drop(startIndex).take(limit), all.size)
        }
        override suspend fun count(): Int { counts++; return items.size }
        override suspend fun allIds(): Set<String> { idCalls++; return items.keys.toSet() }
        override suspend fun byIds(ids: List<String>): List<JellyfinDirectClient.Item> { byIdCalls.add(ids); return ids.mapNotNull { items[it] } }
    }

    private class Sink : JfSink {
        val published = ArrayList<Pair<Int, Boolean>>()   // (size, complete)
        var last: List<Track> = emptyList()
        val facts = HashMap<String, AudioFacts.Fact>(); val removedFacts = ArrayList<String>()
        val tags = HashMap<String, String>()
        var persists = 0; val cursors = ArrayList<String>()
        override fun clean(tracks: List<Track>, wholeLibrary: Boolean) = tracks
        override fun publish(tracks: List<Track>, complete: Boolean) { published += tracks.size to complete; last = tracks }
        override fun facts(add: Map<String, AudioFacts.Fact>, removedKeys: List<String>) { facts.putAll(add); removedFacts += removedKeys; removedKeys.forEach { facts.remove(it) } }
        override fun coverTags(tags: Map<String, String>) { this.tags.putAll(tags) }
        override fun cursorChanged(cursor: String) { cursors += cursor }
        override fun persist() { persists++ }
    }

    private fun server(n: Int) = Server(LinkedHashMap<String, JellyfinDirectClient.Item>().also { m -> for (i in 1..n) m[id(i)] = item(i) })
    private fun ids(tracks: List<Track>) = tracks.map { JellyfinIndex.ID_RE.find(it.path)!!.groupValues[1] }.toSet()

    @Test fun firstPassPagesThroughEverythingAndPublishesProgressively() = runBlocking {
        val s = server(25); val sink = Sink(); var t = 0L
        val sync = JellyfinSync(s, JellyfinIndex(), sink, pageSize = 10, clock = { t += 2000; t }, publishGapMs = 1500)
        sync.sync()
        assertEquals(listOf(Triple(0, 10, null), Triple(10, 10, null), Triple(20, 10, null)), s.pages)
        // songs show up as pages arrive (the first pages publish an incomplete list), then the complete list lands
        assertEquals(listOf(10 to false, 20 to false, 25 to true), sink.published.take(3))
        assertEquals(25, sink.last.size)
        assertEquals(25, sync.index.size)
        assertEquals("the stream facts came with the listing", 25, sink.facts.size)
        assertEquals("cover tags came with the listing", 25, sink.tags.size)
        assertEquals(stamp(25), sync.index.cursor)
        assertEquals(1, sync.fullPasses)
        assertEquals(0, s.idCalls)
    }

    @Test fun anOlderListStaysOnScreenUntilTheNewOneIsComplete() = runBlocking {
        val s = server(25); val sink = Sink()
        val idx = JellyfinIndex(); idx.replaceAll((1..5).map { item(it).track })
        JellyfinSync(s, idx, sink, pageSize = 10).sync()
        assertTrue("no partial publish while an older list is shown", sink.published.none { !it.second })
        assertEquals(25, sink.last.size)
    }

    @Test fun deltaAsksOnlyForWhatChangedSinceTheCursor() = runBlocking {
        val s = server(5); val sink = Sink()
        val sync = JellyfinSync(s, JellyfinIndex(), sink, pageSize = 10)
        sync.sync()
        s.pages.clear()
        s.items[id(3)] = item(3, saved = 30, title = "Song 3 renamed", coverTag = "new3")
        s.items[id(9)] = item(9, saved = 31)
        sync.sync()
        assertEquals(1, s.pages.size)
        assertEquals(0, s.pages[0].first)
        assertEquals("only items saved since the cursor are asked for", stamp(5), s.pages[0].third)
        assertEquals(1, sync.deltaPasses)
        assertEquals(6, sync.index.size)
        assertEquals("Song 3 renamed", sink.last.first { it.path.contains(id(3)) }.title)
        assertEquals("new3", sink.tags["jf" + id(3)])
        assertEquals(stamp(31), sync.index.cursor)
    }

    @Test fun aDeltaWithNothingNewIsOneCheapRequestAndChangesNothing() = runBlocking {
        val s = server(5); val sink = Sink()
        val sync = JellyfinSync(s, JellyfinIndex(), sink, pageSize = 10, clock = { 1_000_000L })
        sync.sync()
        val pubs = sink.published.size; val per = sink.persists
        s.pages.clear(); s.counts = 0
        sync.sync()   // the newest song comes back (MinDateLastSaved is inclusive) but it is not a change
        assertEquals(1, s.pages.size)
        assertEquals("nothing published", pubs, sink.published.size)
        assertEquals("nothing rewritten", per, sink.persists)
        assertEquals("the count check is not due yet", 0, s.counts)
    }

    @Test fun theCountIsCheckedOnlyWhenDue() = runBlocking {
        val s = server(5); val sink = Sink(); var now = 1_000_000L
        val sync = JellyfinSync(s, JellyfinIndex(), sink, pageSize = 10, clock = { now }, countEveryMs = 30 * 60_000L)
        sync.sync()
        assertEquals("a full pass ends with a count check", 1, s.counts)
        now += 5 * 60_000L; sync.sync()
        assertEquals(1, s.counts)
        now += 26 * 60_000L; sync.sync()
        assertEquals("due again after 30 minutes", 2, s.counts)
        sync.wantCount(); sync.sync()
        assertEquals("a library change message makes it due", 3, s.counts)
    }

    @Test fun countMismatchDropsSongsDeletedOnTheServer() = runBlocking {
        val s = server(6); val sink = Sink()
        val sync = JellyfinSync(s, JellyfinIndex(), sink, pageSize = 10, countEveryMs = 0)
        sync.sync()
        s.idCalls = 0
        s.items.remove(id(2)); s.items.remove(id(4))
        sync.sync()
        assertEquals("ids are compared only because the counts differ", 1, s.idCalls)
        assertEquals(setOf(id(1), id(3), id(5), id(6)), ids(sink.last))
        assertEquals(4, sync.index.size)
        assertEquals("the facts of deleted songs are dropped", 2, sink.removedFacts.size)
        assertTrue(s.byIdCalls.isEmpty())
        // nothing differs now: no id comparison again
        s.idCalls = 0; sync.sync()
        assertEquals(0, s.idCalls)
    }

    @Test fun countMismatchFetchesSongsThePhoneMissed() = runBlocking {
        val s = server(6); val sink = Sink()
        val sync = JellyfinSync(s, JellyfinIndex(), sink, pageSize = 10, countEveryMs = 0)
        sync.sync()
        s.items[id(9)] = item(9, saved = 1)   // appeared with a save date older than the cursor: no delta can see it
        s.idCalls = 0
        sync.sync()
        assertEquals(1, s.idCalls)
        assertEquals(listOf(id(9)), s.byIdCalls.single())
        assertTrue(id(9) in ids(sink.last))
        assertEquals(7, sync.index.size)
        assertEquals("its facts and cover tag arrived with it", "tag9", sink.tags["jf" + id(9)])
    }

    @Test fun removedDropsSongsAtOnceWithoutAnyRequest() = runBlocking {
        val s = server(5); val sink = Sink()
        val sync = JellyfinSync(s, JellyfinIndex(), sink, pageSize = 10)
        sync.sync()
        val calls = s.pages.size + s.counts + s.idCalls
        sync.removed(listOf(id(1), id(2).uppercase(), "ffffffffffffffffffffffffffffffff"))
        assertEquals(3, sync.index.size)
        assertEquals(3, sink.last.size)
        assertTrue(sink.published.last().second)
        assertEquals("no request was needed", calls, s.pages.size + s.counts + s.idCalls)
        assertEquals(2, sink.removedFacts.size)
    }

    @Test fun callsWhileAPassRunsMakeOneMorePassNotMany() = runBlocking {
        val s = server(3); val sink = Sink()
        val hold = CompletableDeferred<Unit>()
        val slow = object : JfApi by s {
            override suspend fun page(startIndex: Int, limit: Int, minSaved: String?): JellyfinDirectClient.Page {
                if (s.pages.isEmpty()) hold.await()
                return s.page(startIndex, limit, minSaved)
            }
        }
        val sync = JellyfinSync(slow, JellyfinIndex(), sink, pageSize = 10)
        val first = async { sync.sync() }
        yield()
        repeat(5) { sync.sync() }   // five messages arrive while the first pass runs: they collapse into one extra pass
        hold.complete(Unit)
        first.await()
        assertEquals(1, sync.fullPasses)
        assertEquals("one extra (delta) pass, not five", 1, sync.deltaPasses)
    }

    @Test fun aFailedPassKeepsWhatWasThereAndReportsTheError() = runBlocking {
        val s = server(5); val sink = Sink()
        val sync = JellyfinSync(s, JellyfinIndex(), sink, pageSize = 10)
        sync.sync()
        s.failPageAt = s.pages.size
        try { sync.sync(); throw AssertionError("should have thrown") } catch (e: java.io.IOException) { assertEquals("down", e.message) }
        assertEquals(5, sync.index.size)
        assertEquals("down", sync.lastError)
        sync.sync()   // and it works again afterwards
        assertNull(sync.lastError)
        assertFalse(sync.index.cursor.isEmpty())
    }

    @Test fun anEmptyLibraryIsAFinishedPassToo() = runBlocking {
        val s = server(0); val sink = Sink()
        val sync = JellyfinSync(s, JellyfinIndex(), sink, pageSize = 10)
        sync.sync(); s.pages.clear(); sync.sync()
        assertEquals(1, sync.fullPasses); assertEquals(1, sync.deltaPasses)
    }
}
