package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** What FLACie Web's /api/settings tells the phone, including whether the server downloads at all and whether this person may import playlists. */
class ServerSettingsTest {
    private fun json(extra: String) = """{"admin":true,"prefetch":false,"minFreeGb":10,$extra
        "charts":{"enabled":false,"lists":[0],"perList":10,"lastRun":"","lastNote":""},
        "available":[{"id":0,"name":"Top Songs"}],
        "storage":{"musicFree":1,"musicTotal":2,"nasSongs":0,"nasBytes":0,"diskFree":0,"diskTotal":0}}"""

    @Test fun aServerWithDownloadsOffSaysSo() {
        val s = FlacieWebClient.parseSettings(json(""""downloads":false,"bulk":false,"""))
        assertFalse(s.downloads)
        assertFalse(s.bulk)
        assertFalse(s.prefetch)
        assertEquals(10, s.minFreeGb)
    }

    @Test fun aServerThatLetsThisPersonImportSaysSo() {
        val s = FlacieWebClient.parseSettings(json(""""downloads":true,"bulk":true,"""))
        assertTrue(s.downloads)
        assertTrue(s.bulk)
    }

    /** A FLACie Web from before the switch didn't send it: it downloaded, so the phone carries on as it did. */
    @Test fun anOlderServerCountsAsDownloading() {
        val s = FlacieWebClient.parseSettings(json(""))
        assertTrue(s.downloads)
        assertTrue(s.bulk)
    }
}
