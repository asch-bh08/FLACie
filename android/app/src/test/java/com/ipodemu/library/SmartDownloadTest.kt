package com.ipodemu.library

import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.IOException

/** Smart downloads on the phone: FLACie Web does the downloading when it can, the phone's own code is the backup (docs/superpowers/specs/2026-11-01-smart-downloads-design.md). */
class SmartDownloadTest {
    // ---- which way a download goes -------------------------------------------------------------------------------------------------------------------
    @Test fun aReachableFlacieWebDoesTheDownload() {
        assertEquals(DownloadRoute.SERVER, SmartDownload.choose(webSetUp = true, reachable = true, phoneToo = true))
        assertEquals(DownloadRoute.SERVER, SmartDownload.choose(webSetUp = true, reachable = true, phoneToo = false))
    }
    @Test fun anUnreachableOrMissingFlacieWebFallsBackToThePhoneWhenAllowed() {
        assertEquals(DownloadRoute.PHONE, SmartDownload.choose(webSetUp = true, reachable = false, phoneToo = true))
        assertEquals(DownloadRoute.PHONE, SmartDownload.choose(webSetUp = false, reachable = false, phoneToo = true))
    }
    @Test fun withTheQuietSwitchOffThePhoneNeverDownloads() {
        assertEquals(DownloadRoute.OFF, SmartDownload.choose(webSetUp = true, reachable = false, phoneToo = false))
        assertEquals(DownloadRoute.OFF, SmartDownload.choose(webSetUp = false, reachable = false, phoneToo = false))
    }
    @Test fun theOffMessageSaysWhatIsNeeded() = assertEquals("Downloads need FLACie Web", SmartDownload.OFF_MESSAGE)

    // ---- quick or Hi-Res for fetch ahead -------------------------------------------------------------------------------------------------------------
    @Test fun songsThreeOrMorePlacesAheadAskForHiRes() {
        assertEquals("quick", SmartDownload.modeFor(placesAhead = 0, hiRes = false))
        assertEquals("quick", SmartDownload.modeFor(placesAhead = 2, hiRes = false))
        assertEquals("hires", SmartDownload.modeFor(placesAhead = 3, hiRes = false))
        assertEquals("hires", SmartDownload.modeFor(placesAhead = 9, hiRes = false))
        assertEquals("hires", SmartDownload.modeFor(placesAhead = 0, hiRes = true))
    }

    // ---- what the server says ------------------------------------------------------------------------------------------------------------------------
    @Test fun startAnswerIsTheJobId() = assertEquals("abc123", FlacieWebClient.parseStarted("""{"id":"abc123"}"""))
    @Test fun startAnswerWithoutAnIdIsAnError() {
        try { FlacieWebClient.parseStarted("""{"error":"no"}"""); throw AssertionError("should have thrown") } catch (e: IOException) { assertTrue(e.message!!.isNotBlank()) }
    }
    @Test fun statusIsRead() {
        val j = FlacieWebClient.parseJobStatus("""{"stage":"downloading","message":"Downloading from YouTube...","source":"ytdl","done":false,"failed":false,"path":null,"format":null}""")
        assertEquals("downloading", j.stage); assertEquals("ytdl", j.source); assertFalse(j.done); assertFalse(j.failed); assertNull(j.path); assertNull(j.format)
    }
    @Test fun aFinishedStatusCarriesThePathAndFormat() {
        val j = FlacieWebClient.parseJobStatus("""{"stage":"done","message":"ready","source":"soulseek","done":true,"failed":false,"path":"Queen/Queen - Radio Ga Ga.flac","format":"FLAC"}""")
        assertTrue(j.done); assertEquals("Queen/Queen - Radio Ga Ga.flac", j.path); assertEquals("FLAC", j.format)
    }
    @Test fun serverSettingsSayWhetherDownloadsAreAllowed() {
        val base = """"admin":true,"prefetch":true,"minFreeGb":5,"charts":{"enabled":false,"lists":[],"perList":10},"available":[],"storage":{}"""
        assertFalse(FlacieWebClient.parseSettings("{$base,\"downloads\":false}").downloads)
        assertTrue(FlacieWebClient.parseSettings("{$base,\"downloads\":true}").downloads)
        // an older server says nothing: downloads are not switched off
        assertTrue(FlacieWebClient.parseSettings("{$base}").downloads)
    }

    // ---- server stages in the phone's own words ------------------------------------------------------------------------------------------------------
    @Test fun stagesMapToThePhonesStages() {
        fun st(stage: String, done: Boolean = false, failed: Boolean = false) = SmartDownload.statusOf(FlacieWebClient.JobStatus(stage, "m", "soulseek", done, failed, null, null), null)
        assertEquals(DownloadStage.SEARCHING, st("searching").stage)
        assertEquals(DownloadStage.DOWNLOADING, st("downloading").stage)
        assertEquals(DownloadStage.IMPORTING, st("filing").stage)
        assertEquals(DownloadStage.SCANNING, st("scanning").stage)
        assertEquals(DownloadStage.DONE, st("whatever", done = true).stage)
        assertEquals(DownloadStage.FAILED, st("whatever", failed = true).stage)
        assertEquals(DownloadStage.SEARCHING, st("queued").stage)
    }
    @Test fun messageAndSourceCarryOver() {
        val s = SmartDownload.statusOf(FlacieWebClient.JobStatus("downloading", "Downloading from YouTube...", "ytdl", false, false, null, null), null)
        assertEquals("Downloading from YouTube...", s.message); assertEquals("ytdl", s.source)
    }
    @Test fun aFinishedSongPlaysFromWhatTheServerFiled() {
        val j = FlacieWebClient.JobStatus("done", "ready", "soulseek", true, false, "Queen/Queen - Radio Ga Ga.flac", "FLAC")
        val t = SmartDownload.trackFor("http://frank:8099/", j.path!!, "Queen", "Radio Ga Ga", "The Works", 346_000)
        assertEquals("http://frank:8099/file?path=Queen%2FQueen+-+Radio+Ga+Ga.flac", t.path)
        assertEquals("Radio Ga Ga", t.title); assertEquals(346_000L, t.durationMs)
        val s = SmartDownload.statusOf(j, t)
        assertEquals(listOf(t), s.newTracks)
    }

    // ---- following a job ----------------------------------------------------------------------------------------------------------------------------
    private val ask = SmartDownload.Ask("Queen", "Radio Ga Ga", "The Works", 346_000, "quick")
    private fun job(stage: String, msg: String = stage, done: Boolean = false, failed: Boolean = false, path: String? = null) = FlacieWebClient.JobStatus(stage, msg, "ytdl", done, failed, path, null)

    @Test fun followsAJobUntilItIsDone() {
        val seen = mutableListOf<DownloadStatus>()
        val script = ArrayDeque(listOf(job("searching", "Looking"), job("downloading", "Downloading"), job("done", "ready", done = true, path = "Queen/Queen - Radio Ga Ga.mp3")))
        val handled = runBlocking { SmartDownload.run(ask, "http://frank:8099", { "j1" }, { script.removeFirst() }, { seen += it }, pollMs = 1, heartbeatMs = 1_000_000) }
        assertTrue(handled)
        assertEquals(listOf(DownloadStage.SEARCHING, DownloadStage.DOWNLOADING, DownloadStage.DONE), seen.map { it.stage })
        assertNotNull(seen.last().newTrack)
    }
    @Test fun aSameAsBeforeStatusIsNotReportedAgain() {
        val seen = mutableListOf<DownloadStatus>()
        val script = ArrayDeque(listOf(job("searching", "Looking"), job("searching", "Looking"), job("searching", "Looking"), job("done", "ok", done = true)))
        runBlocking { SmartDownload.run(ask, "", { "j1" }, { script.removeFirst() }, { seen += it }, pollMs = 1, heartbeatMs = 1_000_000) }
        assertEquals(listOf(DownloadStage.SEARCHING, DownloadStage.DONE), seen.map { it.stage })
    }
    @Test fun aFailedJobEndsFailedWithTheServersWords() {
        val seen = mutableListOf<DownloadStatus>()
        val handled = runBlocking { SmartDownload.run(ask, "", { "j1" }, { job("failed", "Not found anywhere", failed = true) }, { seen += it }, pollMs = 1) }
        assertTrue(handled)
        assertEquals(DownloadStage.FAILED, seen.last().stage); assertEquals("Not found anywhere", seen.last().message)
    }
    @Test fun anUnreachableServerAtStartHandsBackToThePhone() {
        val seen = mutableListOf<DownloadStatus>()
        val handled = runBlocking { SmartDownload.run(ask, "", { throw java.net.ConnectException("refused") }, { error("no") }, { seen += it }, pollMs = 1) }
        assertFalse(handled); assertTrue(seen.isEmpty())
    }
    @Test fun aRefusalFromTheServerIsShownNotRetriedOnThePhone() {
        val seen = mutableListOf<DownloadStatus>()
        val handled = runBlocking { SmartDownload.run(ask, "", { throw FlacieWebClient.Refused("Too many downloads. Try again in a minute.") }, { error("no") }, { seen += it }, pollMs = 1) }
        assertTrue(handled)
        assertEquals(DownloadStage.FAILED, seen.single().stage); assertEquals("Too many downloads. Try again in a minute.", seen.single().message)
    }
    @Test fun lostTouchWhileFollowingEndsFailed() {
        val seen = mutableListOf<DownloadStatus>()
        runBlocking { SmartDownload.run(ask, "", { "j1" }, { throw IOException("down") }, { seen += it }, pollMs = 1, maxMisses = 3) }
        assertEquals(DownloadStage.FAILED, seen.last().stage)
    }
    @Test fun aBlipWhileFollowingIsForgiven() {
        val seen = mutableListOf<DownloadStatus>()
        var n = 0
        runBlocking { SmartDownload.run(ask, "", { "j1" }, { if (n++ == 1) throw IOException("blip") else if (n < 4) job("downloading", "x") else job("done", "ok", done = true) }, { seen += it }, pollMs = 1, maxMisses = 3) }
        assertEquals(DownloadStage.DONE, seen.last().stage)
    }
}
