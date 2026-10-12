package com.ipodemu.library

import kotlinx.coroutines.runBlocking
import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.IOException

/** The phone's report of a finished download to FLACie Web: what is sent, and that a failure is retried once and then left alone. */
class DownloadReportTest {
    private val now = 1_760_270_000_000L
    private val mover = "https://mover.example"

    private fun track(title: String, artist: String, album: String, dest: String, ms: Long = 123_000) = Track(
        path = "$mover/file?path=" + java.net.URLEncoder.encode(dest, "UTF-8"), title = title, artist = artist, album = album, albumArtist = artist, genre = "",
        trackNo = 0, discNo = 0, durationMs = ms, year = 0, isMusic = true, artKey = null, mtime = now, size = 0, source = TrackSource.CLOUD)

    @Test fun theBodyCarriesTheContractFields() {
        val t = track("P", "JME", "Famous?", "Music/JME/JME - P.flac")
        val item = DownloadReport.itemsFor("JME", "P", "Famous?", 0, DownloadStatus(DownloadStage.DONE, "ready", "soulseek", t), now).single()
        val o = JSONObject(DownloadReport.json(item))
        assertEquals("JME", o.getString("artist"))
        assertEquals("P", o.getString("title"))
        assertEquals("Famous?", o.getString("album"))
        assertEquals(123_000L, o.getLong("durationMs"))
        assertEquals(t.path, o.getString("path"))   // exactly as the phone has it
        assertEquals("soulseek", o.getString("source"))
        assertEquals(now, o.getLong("finishedAtMs"))
    }

    @Test fun aLidarrGrabIsReportedWithoutAPath() {
        val items = DownloadReport.itemsFor("Band", "Song", "LP", 200_000, DownloadStatus(DownloadStage.DONE, "should appear within 45s", "lidarr"), now)
        assertEquals(1, items.size)
        val o = JSONObject(DownloadReport.json(items[0]))
        assertFalse(o.has("path"))
        assertEquals("lidarr", o.getString("source"))
        assertEquals("Song", o.getString("title"))
    }

    @Test fun anAlbumIsReportedSongBySongAndCapped() {
        val tracks = (1..60).map { track("Song $it", "Band", "LP", "Music/Band/LP/%02d - Song $it.flac".format(it)) }
        val items = DownloadReport.itemsFor("Band", "", "LP", 0, DownloadStatus(DownloadStage.DONE, "ready", "soulseek", newTracks = tracks), now)
        assertEquals(DownloadReport.MAX_PER_DOWNLOAD, items.size)
        assertEquals("Song 1", items[0].title)
        assertTrue(items.all { it.path!!.startsWith("$mover/file?path=") })
    }

    @Test fun nothingIsSentWithoutAnArtistAndATitle() {
        assertTrue(DownloadReport.itemsFor("Band", "", "LP", 0, DownloadStatus(DownloadStage.DONE, "ready", "lidarr"), now).isEmpty())
        assertTrue(DownloadReport.itemsFor("", "Song", "LP", 0, DownloadStatus(DownloadStage.DONE, "ready", "lidarr"), now).isEmpty())
    }

    @Test fun onlyKnownSourcesAreNamed() {
        for (s in listOf("soulseek", "lidarr", "ytdl", "archive", "audius", "jamendo")) assertEquals(s, DownloadReport.sourceId(s))
        assertEquals("soulseek", DownloadReport.sourceId("Soulseek"))
        assertNull(DownloadReport.sourceId("youtube-music"))
        assertNull(DownloadReport.sourceId(null))
        val o = JSONObject(DownloadReport.json(DownloadReport.Item("A", "T", "", 0, null, "mystery", now)))
        assertFalse(o.has("source")); assertFalse(o.has("durationMs")); assertFalse(o.has("path"))
    }

    @Test fun theBodyCarriesNoSecret() {
        val body = DownloadReport.json(DownloadReport.Item("A", "T", "Al", 1, "$mover/file?path=x", "soulseek", now))
        assertEquals(setOf("artist", "title", "album", "durationMs", "path", "source", "finishedAtMs"), JSONObject(body).keySet())
    }

    // ---- delivery: fire and forget, one retry -------------------------------------------------------------------------------------------

    private val item = DownloadReport.Item("A", "T", "", 0, null, "soulseek", now)

    @Test fun anAcceptedReportIsSentOnce() = runBlocking {
        var calls = 0
        assertTrue(DownloadReport.deliver(item, { calls++; 200 }, 0))
        assertEquals(1, calls)
    }

    @Test fun aServerErrorIsRetriedOnceThenGivenUp() = runBlocking {
        var calls = 0
        assertFalse(DownloadReport.deliver(item, { calls++; 503 }, 0))
        assertEquals(2, calls)
    }

    @Test fun aNetworkErrorIsRetriedOnceAndCanSucceed() = runBlocking {
        var calls = 0
        val ok = DownloadReport.deliver(item, { calls++; if (calls == 1) throw IOException("unreachable") else 200 }, 0)
        assertTrue(ok); assertEquals(2, calls)
    }

    @Test fun aRefusalIsFinal() = runBlocking {
        for (code in listOf(400, 401, 403, 404, 429)) {
            var calls = 0
            assertFalse("HTTP $code", DownloadReport.deliver(item, { calls++; code }, 0))
            assertEquals("HTTP $code is not retried", 1, calls)
        }
    }

    @Test fun twoNetworkErrorsNeverThrow() = runBlocking {
        var calls = 0
        assertFalse(DownloadReport.deliver(item, { calls++; throw IOException("down") }, 0))
        assertEquals(2, calls)
    }
}
