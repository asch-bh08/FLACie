package com.ipodemu.library

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** Devices, "Where you were" (owner, 25 Nov): the server's day of states read on the phone, and the same words as the web page. */
class WhereYouWereTest {
    private val min = 60_000L
    private val list = """{"now":1000000,"devices":[
        {"deviceId":"web:Edge on Windows","device":"Edge on Windows","kind":"Browser","lastSeen":500000,"states":[
          {"id":"w1","at":500000,"title":"Harbour 3","artist":"Aurora Vale","album":null,"artKey":"jfs3~x","itemId":"s3","path":null,"positionMs":5000,"durationMs":171000,"paused":false,"context":null,"queueLength":1}]},
        {"deviceId":"phone-1","device":"Pixel 8","kind":"","lastSeen":900000,"states":[
          {"id":"p1","at":300000,"title":"Paper 5","artist":"Aurora Vale","positionMs":1000,"durationMs":185000,"paused":true,"context":"Road trip","queueLength":3},
          {"id":"p2","at":900000,"title":"Lantern 4","artist":"Aurora Vale","itemId":"s4","positionMs":65000,"durationMs":178000,"paused":false,"context":"Road trip","queueLength":3}]},
        {"deviceId":"empty","device":"Nothing","kind":"Phone","lastSeen":999999,"states":[]}]}"""

    @Test fun devices_come_seen_last_first_with_moments_newest_first() {
        val d = WhereYouWere.parse(list)
        assertEquals(listOf("phone-1", "web:Edge on Windows"), d.map { it.deviceId })   // a device with no moments is left out
        assertEquals(listOf("p2", "p1"), d[0].moments.map { it.id })
        assertEquals("Phone", d[0].kind)                                               // no kind sent: guessed from the name
        assertEquals("Road trip", d[0].moments[0].context)
        assertNull(d[1].moments[0].context)
        assertNull(d[1].moments[0].album)
        assertTrue(d[0].moments[1].paused)
    }

    @Test fun one_moment_comes_with_its_queue() {
        val f = WhereYouWere.parseOne("""{"id":"p2","at":900000,"title":"Lantern 4","artist":"A","itemId":"s4","positionMs":65000,"durationMs":178000,"paused":false,"context":"Road trip","queue":["s3","s4","s5"],"index":1}""")
        assertEquals(listOf("s3", "s4", "s5"), f.queue)
        assertEquals(1, f.index)
        assertEquals(65_000L, f.moment.positionMs)
    }

    @Test fun nothing_kept_yet_is_an_empty_list() = assertEquals(0, WhereYouWere.parse("""{"now":1,"devices":[]}""").size)

    @Test fun relative_times_as_on_the_web() {
        val now = 100 * 24 * 60 * min
        assertEquals("just now", WhereYouWere.ago(now - 10_000, now))
        assertEquals("5 min ago", WhereYouWere.ago(now - 5 * min, now))
        assertEquals("1 h ago", WhereYouWere.ago(now - 61 * min, now))
        assertEquals("23 h ago", WhereYouWere.ago(now - 23 * 60 * min, now))
        assertEquals("yesterday", WhereYouWere.ago(now - 25 * 60 * min, now))
        assertEquals("just now", WhereYouWere.ago(now + 5_000, now))   // a clock a little ahead is not "-1 min ago"
    }

    @Test fun positions_and_kinds_in_words() {
        assertEquals("at 2:13 in Road trip", WhereYouWere.where(133_000, "Road trip"))
        assertEquals("at 1:02:05", WhereYouWere.where(3_725_000, null))
        assertEquals("at 0:00", WhereYouWere.where(-5, " "))
        assertEquals("Phone", WhereYouWere.kind("SM-S918B"))
        assertEquals("Browser", WhereYouWere.kind("Edge on Windows"))
        assertEquals("Computer", WhereYouWere.kind("FLACie for Windows"))
        assertEquals("Player", WhereYouWere.kind("Kitchen"))
    }

    @Test fun the_phone_tells_the_server_on_a_new_song_a_pause_or_once_a_minute() {
        assertTrue(WhereYouWere.shouldSend(null, null, 0, "s1", false, 1000))
        assertFalse(WhereYouWere.shouldSend("s1", false, 1000, "s1", false, 30_000))
        assertTrue(WhereYouWere.shouldSend("s1", false, 1000, "s1", false, 61_000))
        assertTrue(WhereYouWere.shouldSend("s1", false, 1000, "s2", false, 2000))
        assertTrue(WhereYouWere.shouldSend("s1", false, 1000, "s1", true, 2000))
        assertFalse(WhereYouWere.shouldSend("s1", true, 1000, "s1", true, 10 * min))   // paused: nothing new however long
    }

    @Test fun the_report_carries_the_song_the_queue_and_where_it_came_from() {
        val t = Track(path = "http://jf/Audio/s4/stream", title = "Lantern 4", artist = "Aurora Vale", album = "Aurora Album 1", albumArtist = "Aurora Vale", genre = "",
            trackNo = 4, discNo = 1, durationMs = 178_000, year = 1990, isMusic = true, artKey = "jfs4", mtime = 0, size = 0, source = TrackSource.JELLYFIN)
        val o = JSONObject(WhereYouWere.report("dev-1", "Pixel 8", t, "s4", 65_000, false, listOf("s3", "s4", "/sdcard/x.flac"), 1, "Road trip"))
        assertEquals("dev-1", o.getString("deviceId"))
        assertEquals("s4", o.getString("itemId"))
        assertTrue(o.isNull("path"))   // a Jellyfin song travels by its id
        assertEquals(3, o.getJSONArray("queue").length())
        assertEquals(1, o.getInt("index"))
        assertEquals("Road trip", o.getString("context"))
        val local = JSONObject(WhereYouWere.report("dev-1", "Pixel 8", t, null, 0, true, emptyList(), 0, null))
        assertEquals("http://jf/Audio/s4/stream", local.getString("path"))
    }
}
