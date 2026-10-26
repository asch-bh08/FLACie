package com.ipodemu.library

import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File
import java.io.IOException
import java.nio.file.Files

/** What the phone heard and liked reaches the server once each, in order, however long the phone was offline. */
class ListenQueueTest {
    private fun dir(): File = Files.createTempDirectory("listen").toFile()
    private fun ev(i: Int) = ListenEventJson("id$i", ListenKind.PLAY, 1_000L + i, "p$i", "Song $i", "Artist", 40.0, 200.0, "autoplay")

    @Test fun Days_offline_send_every_event_once_in_order() = runBlocking {
        val d = dir()
        val q = ListenQueue(d)
        repeat(500) { q.add(ev(it)) }
        // the app restarts while still offline: the events are still there
        val again = ListenQueue(d)
        assertEquals(500, again.pending().size)
        val received = ArrayList<ListenEventJson>(); var calls = 0
        SyncRetry.attempt(listOf(0L)) {
            again.flush { batch -> calls++; if (calls == 1) throw IOException("offline"); received.addAll(batch) }
        }
        assertEquals((0 until 500).map { "id$it" }, received.map { it.id })
        assertEquals(500, received.map { it.id }.toSet().size)
        assertTrue(again.pending().isEmpty())
        assertTrue(ListenQueue(d).pending().isEmpty())
    }

    @Test fun A_failed_send_keeps_the_events() {
        val q = ListenQueue(dir())
        q.add(ev(1)); q.add(ev(2))
        try { q.flush { throw IOException("down") } } catch (_: IOException) { }
        assertEquals(listOf("id1", "id2"), q.pending().map { it.id })
    }

    @Test fun The_body_is_what_the_server_reads() {
        val o = org.json.JSONObject(ListenQueue.body(listOf(ev(7))))
        val e = o.getJSONArray("events").getJSONObject(0)
        assertEquals("id7", e.getString("id")); assertEquals(0, e.getInt("kind")); assertEquals(1_007L, e.getLong("at"))
        assertEquals("p7", e.getString("path")); assertEquals(40.0, e.getDouble("heard"), 0.0); assertEquals(200.0, e.getDouble("length"), 0.0)
        assertEquals("autoplay", e.getString("context"))
        assertEquals(3, ListenKind.THUMB_DOWN); assertEquals(4, ListenKind.LESS_LIKE_THIS)
    }

    @Test fun Skip_and_finish_are_measured_from_heard_time() {
        // 20 s heard, then on to the next song: a skip
        val m = PlayMeter()
        m.start(0L, playing = true)
        assertTrue(ListenRules.isSkip(m.end(20_000L)))
        // half the song heard, then a seek to the end: the position reached the end but only 100 s were heard, so not a finish
        m.start(0L, playing = true)
        m.seeked()
        val heard = m.end(100_000L)
        assertEquals(100.0, heard, 0.01)
        assertFalse(ListenRules.isFinish(heard, 200.0))
        assertFalse(ListenRules.isSkip(heard))
        // paused for a while in the middle: the pause does not count
        m.start(0L, playing = true); m.playing(false, 60_000L); m.playing(true, 600_000L)
        assertEquals(180.0, m.end(720_000L), 0.01)
        assertTrue(ListenRules.isFinish(180.0, 200.0))
        // at double speed, 100 s of listening is 200 s of song
        m.start(0L, playing = true, rate = 2f)
        assertTrue(ListenRules.isFinish(m.end(100_000L), 200.0))
    }
}
