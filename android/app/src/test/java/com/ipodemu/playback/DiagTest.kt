package com.ipodemu.playback

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/** Owner, 2 Nov: a bad drive should explain itself; the phone keeps its problems for the server, bounded, and loses none when a send fails. */
class DiagTest {
    @Test fun eventsGoInBatchesAndComeBackWhenASendFails() {
        Diag.clear()
        repeat(70) { Diag.add("stall", "song $it", at = it.toLong()) }
        val (body, events) = Diag.take()!!
        assertEquals(Diag.BATCH, JSONObject(body).getJSONArray("events").length())
        assertEquals(20, Diag.size)
        Diag.giveBack(events)
        assertEquals(70, Diag.size)
        assertEquals("song 0", JSONObject(Diag.take()!!.first).getJSONArray("events").getJSONObject(0).getString("text"))
        Diag.clear(); assertNull(Diag.take())
    }
    @Test fun theOldestGoWhenTooMany() {
        Diag.clear()
        repeat(Diag.MAX + 5) { Diag.add("error", "e$it") }
        assertEquals(Diag.MAX, Diag.size)
        assertEquals("e5", JSONObject(Diag.take()!!.first).getJSONArray("events").getJSONObject(0).getString("text"))
        Diag.clear()
    }
}
