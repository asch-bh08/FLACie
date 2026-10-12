package com.ipodemu.library

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** The pure rules behind the download tags: age text, the 60-minute window, who wins when several sources know a song, and the wording. */
class DownloadTagsTest {
    private val now = 1_760_270_000_000L
    private val min = 60_000L

    private fun track(title: String, artist: String, source: TrackSource, mtime: Long) = Track(
        path = "http://mover/file?path=${title.replace(' ', '+')}.flac", title = title, artist = artist, album = "Album", albumArtist = artist, genre = "", trackNo = 1, discNo = 1,
        durationMs = 180_000, year = 2020, isMusic = true, artKey = null, mtime = mtime, size = 0, source = source)

    // ---- age label ---------------------------------------------------------------------------------------------------------------------

    @Test fun ageLabelFollowsTheContract() {
        assertEquals("just now", DownloadTags.ageLabel(now, now))
        assertEquals("just now", DownloadTags.ageLabel(now, now - 4_999))
        assertEquals("5s ago", DownloadTags.ageLabel(now, now - 5_000))
        assertEquals("7s ago", DownloadTags.ageLabel(now, now - 7_400))
        assertEquals("59s ago", DownloadTags.ageLabel(now, now - 59_999))
        assertEquals("1 min ago", DownloadTags.ageLabel(now, now - 60_000))
        assertEquals("2 min ago", DownloadTags.ageLabel(now, now - 2 * min - 30_000))
        assertEquals("59 min ago", DownloadTags.ageLabel(now, now - 59 * min - 59_999))
    }

    @Test fun nothingIsRecentAfterTheWindow() {
        assertEquals(60 * min, DownloadTags.WINDOW_MS)
        assertNull("exactly an hour is over", DownloadTags.ageLabel(now, now - 60 * min))
        assertNull(DownloadTags.ageLabel(now, now - 61 * min))
        assertNull("a week ago", DownloadTags.ageLabel(now, now - 7 * 24 * 60 * min))
    }

    @Test fun aFinishTimeInTheFutureIsOnlyBelievedWithinAMinute() {
        assertEquals("just now", DownloadTags.ageLabel(now, now + 30_000))
        assertEquals("just now", DownloadTags.ageLabel(now, now + 60_000))
        assertNull(DownloadTags.ageLabel(now, now + 60_001))
        assertNull("a client clock a day ahead", DownloadTags.ageLabel(now, now + 24 * 60 * min))
    }

    // ---- lookup precedence -----------------------------------------------------------------------------------------------------------------

    @Test fun aRunningJobBeatsEverything() {
        val tag = DownloadTags.resolve(now, "Searching Soulseek...", finishedMs = now - 1_000, failedMs = now - 500, cloudAddedMs = now - 2_000)
        assertEquals(DownloadTag.Downloading("Searching Soulseek..."), tag)
    }

    @Test fun aFinishedDownloadBeatsTheCloudAddedTime() {
        val tag = DownloadTags.resolve(now, null, finishedMs = now - 10 * min, failedMs = null, cloudAddedMs = now - 40 * min)
        assertEquals(DownloadTag.Recent(now - 10 * min), tag)
    }

    @Test fun aCloudSongCountsFromItsOwnAddedTimeWhenNothingElseKnowsIt() {
        assertEquals(DownloadTag.Recent(now - 20 * min), DownloadTags.resolve(now, null, null, null, now - 20 * min))
        assertEquals("a Cloud song from yesterday is a normal song", DownloadTag.None, DownloadTags.resolve(now, null, null, null, now - 24 * 60 * min))
        assertEquals("not a Cloud song", DownloadTag.None, DownloadTags.resolve(now, null, null, null, null))
    }

    @Test fun anOldFinishFallsThroughToTheNextSource() {
        // finished two hours ago, but the Cloud copy was filed 10 minutes ago (a second download of the same song)
        val tag = DownloadTags.resolve(now, null, finishedMs = now - 120 * min, failedMs = null, cloudAddedMs = now - 10 * min)
        assertEquals(DownloadTag.Recent(now - 10 * min), tag)
        assertEquals(DownloadTag.None, DownloadTags.resolve(now, null, now - 120 * min, null, now - 130 * min))
    }

    @Test fun aRecentFailureShowsUntilALaterSuccess() {
        assertEquals(DownloadTag.Failed(now - 5 * min), DownloadTags.resolve(now, null, null, now - 5 * min, null))
        assertEquals("a failure after a success is the news", DownloadTag.Failed(now - 5 * min), DownloadTags.resolve(now, null, now - 30 * min, now - 5 * min, null))
        assertEquals("a success after a failure wins", DownloadTag.Recent(now - 2 * min), DownloadTags.resolve(now, null, now - 2 * min, now - 5 * min, null))
        assertEquals("an old failure is gone", DownloadTag.None, DownloadTags.resolve(now, null, null, now - 90 * min, null))
    }

    // ---- text ----------------------------------------------------------------------------------------------------------------------------

    @Test fun theTagTextIsTheSameAsTheWebs() {
        assertEquals("Recently downloaded · 7s ago", DownloadTags.text(DownloadTag.Recent(now - 7_000), now))
        assertEquals("Recently downloaded · just now", DownloadTags.text(DownloadTag.Recent(now), now))
        assertEquals("Recently downloaded · 12 min ago", DownloadTags.text(DownloadTag.Recent(now - 12 * min), now))
        assertEquals("Downloading · Searching Soulseek...", DownloadTags.text(DownloadTag.Downloading("Searching Soulseek..."), now))
        assertEquals("Downloading", DownloadTags.text(DownloadTag.Downloading(""), now))
        assertEquals("Couldn't download", DownloadTags.text(DownloadTag.Failed(now - min), now))
        assertNull(DownloadTags.text(DownloadTag.None, now))
    }

    @Test fun theTextOfARecentTagIsGoneOnceTheWindowHasPassed() {
        val tag = DownloadTag.Recent(now)
        assertEquals("Recently downloaded · 59 min ago", DownloadTags.text(tag, now + 59 * min + 30_000))
        assertNull(DownloadTags.text(tag, now + 60 * min))
        assertNull(DownloadTags.text(DownloadTag.Failed(now), now + 61 * min))
        assertEquals(now + 60 * min, DownloadTags.expiresAt(tag))
        assertNull(DownloadTags.expiresAt(DownloadTag.None))
    }

    @Test fun onlyAFinishedDownloadsAgeTicks() {
        assertTrue(DownloadTags.ticks(DownloadTag.Recent(now)))
        assertFalse(DownloadTags.ticks(DownloadTag.Downloading("x")))
        assertFalse(DownloadTags.ticks(DownloadTag.Failed(now)))
        assertFalse(DownloadTags.ticks(DownloadTag.None))
    }

    @Test fun theRequestItselfReadsAsStarting() {
        assertEquals("Starting...", DownloadTags.stageText(DownloadStage.REQUESTED, "Requested \"P\""))
        assertEquals("Searching Soulseek...", DownloadTags.stageText(DownloadStage.SEARCHING, "Searching Soulseek..."))
        assertEquals("Starting...", DownloadTags.stageText(DownloadStage.SEARCHING, " "))
    }

    // ---- the store, fed the way Library feeds it ----------------------------------------------------------------------------------------

    private fun st(stage: DownloadStage, msg: String = "", source: String? = null, miss: Boolean = false, tracks: List<Track> = emptyList()) =
        DownloadStatus(stage, msg, source, newTracks = tracks, miss = miss)

    @Test fun aSongGoesFromRunningToRecent() {
        val s = DownloadTagStore()
        val key = matchKey("P", "JME")
        assertEquals(DownloadTag.None, s.statusOf(key, now))
        s.onStatus(key, st(DownloadStage.REQUESTED, "Requested \"P\""), now)
        assertEquals(DownloadTag.Downloading("Starting..."), s.statusOf(key, now))
        s.onStatus(key, st(DownloadStage.SEARCHING, "Searching Soulseek...", "soulseek"), now + 1_000)
        assertEquals(DownloadTag.Downloading("Searching Soulseek..."), s.statusOf(key, now + 1_000))
        s.onStatus(key, st(DownloadStage.DOWNLOADING, "Downloading 42%...", "soulseek"), now + 5_000)
        assertEquals(DownloadTag.Downloading("Downloading 42%..."), s.statusOf(key, now + 5_000))
        s.onStatus(key, st(DownloadStage.DONE, "\"P\" is ready to play", "soulseek", tracks = listOf(track("P", "JME", TrackSource.CLOUD, now + 9_000))), now + 9_000)
        assertEquals(DownloadTag.Recent(now + 9_000), s.statusOf(key, now + 10_000))
        assertEquals("Recently downloaded · just now", DownloadTags.text(s.statusOf(key, now + 10_000), now + 10_000))
        assertEquals("Recently downloaded · 6s ago", DownloadTags.text(s.statusOf(key, now + 15_000), now + 15_000))
        // and an hour later it is a normal song again
        assertEquals(DownloadTag.None, s.statusOf(key, now + 9_000 + 60 * min))
    }

    @Test fun aHadNothingNoteDoesNotReplaceTheStage() {
        val s = DownloadTagStore(); val key = matchKey("P", "JME")
        s.onStatus(key, st(DownloadStage.SEARCHING, "Searching Soulseek...", "soulseek"), now)
        s.onStatus(key, st(DownloadStage.SEARCHING, "Soulseek had nothing it could finish", "soulseek", miss = true), now + 1)
        assertEquals(DownloadTag.Downloading("Searching Soulseek..."), s.statusOf(key, now + 1))
    }

    @Test fun aFailedFetchIsTaggedForTheWindow() {
        val s = DownloadTagStore(); val key = matchKey("Song", "Artist")
        s.onStatus(key, st(DownloadStage.SEARCHING, "Searching Soulseek...", "soulseek"), now)
        s.onStatus(key, st(DownloadStage.FAILED, "Not found on Soulseek"), now + 20_000)
        assertEquals(DownloadTag.Failed(now + 20_000), s.statusOf(key, now + 30_000))
        assertEquals(DownloadTag.None, s.statusOf(key, now + 20_000 + 60 * min))
        // retried: running again, then done
        s.onStatus(key, st(DownloadStage.SEARCHING, "Searching Soulseek...", "soulseek"), now + 40_000)
        assertTrue(s.statusOf(key, now + 41_000) is DownloadTag.Downloading)
        s.onStatus(key, st(DownloadStage.DONE, "ready", "soulseek"), now + 50_000)
        assertEquals(DownloadTag.Recent(now + 50_000), s.statusOf(key, now + 51_000))
    }

    @Test fun anAlbumTagsEverySongItFiledAndNothingElse() {
        val s = DownloadTagStore()
        val songs = listOf(track("One", "Band", TrackSource.CLOUD, now), track("Two", "Band", TrackSource.CLOUD, now))
        s.onStatus(null, st(DownloadStage.SEARCHING, "Searching Soulseek for \"Album\"...", "soulseek"), now)   // an album has no song key while it runs
        s.onStatus(null, st(DownloadStage.DONE, "\"Album\" is ready to play", "soulseek", tracks = songs), now + 30_000)
        assertEquals(DownloadTag.Recent(now + 30_000), s.statusOf(matchKey("One", "Band"), now + 31_000))
        assertEquals(DownloadTag.Recent(now + 30_000), s.statusOf(matchKey("Two", "Band"), now + 31_000))
        assertEquals(DownloadTag.None, s.statusOf(matchKey("Three", "Band"), now + 31_000))
    }

    @Test fun theSameSongUnderAnotherSpellingSharesOneTag() {
        val s = DownloadTagStore()
        s.onStatus(matchKey("Hello (feat. Guest)", "The Band"), st(DownloadStage.DONE, "ready", "lidarr"), now)
        assertEquals(DownloadTag.Recent(now), s.statusOf(matchKey("hello", "Band"), now + 1))
    }

    @Test fun seededHistoryKeepsOnlyWhatIsInsideTheWindow() {
        val s = DownloadTagStore()
        s.seed(listOf(Triple("P", "JME", now - 10 * min), Triple("Old", "Artist", now - 3 * 24 * 60 * min), Triple("Later", "Artist", now + 60 * 60_000L)), now)
        assertEquals(DownloadTag.Recent(now - 10 * min), s.statusOf(matchKey("P", "JME"), now))
        assertEquals("a week-old download is not recent", DownloadTag.None, s.statusOf(matchKey("Old", "Artist"), now))
        assertEquals("a finish time in the future is not believed", DownloadTag.None, s.statusOf(matchKey("Later", "Artist"), now))
    }

    @Test fun aCloudTrackWithoutAnyJobStillCountsFromItsAddedTime() {
        val s = DownloadTagStore()
        val fresh = track("Fresh", "Artist", TrackSource.CLOUD, now - 5 * min)
        val old = track("Old", "Artist", TrackSource.CLOUD, now - 5 * 24 * 60 * min)
        val jf = track("Jf", "Artist", TrackSource.JELLYFIN, now - 5 * min)   // Jellyfin's own add time is not a download
        assertEquals(DownloadTag.Recent(now - 5 * min), s.statusOf(fresh.matchKey, now, DownloadTags.cloudAdded(fresh)))
        assertEquals(DownloadTag.None, s.statusOf(old.matchKey, now, DownloadTags.cloudAdded(old)))
        assertNull(DownloadTags.cloudAdded(jf))
        assertNull("no added time recorded", DownloadTags.cloudAdded(track("Z", "A", TrackSource.CLOUD, 0)))
    }

    // ---- wording ---------------------------------------------------------------------------------------------------------------------------

    @Test fun theBadgeAndTheSourceLineSayTheSameAsTheWeb() {
        assertEquals("New download", SourceLabel.badge(TrackSource.CLOUD))
        assertEquals("Streaming from new download", SourceLabel.line(TrackSource.CLOUD))
        assertEquals("NAS", SourceLabel.badge(TrackSource.NAS))
        assertEquals("Jellyfin", SourceLabel.badge(TrackSource.JELLYFIN))
        assertEquals("Jellyfin", SourceLabel.line(TrackSource.JELLYFIN))
        assertEquals("Local", SourceLabel.line(TrackSource.LOCAL))
        for (s in TrackSource.entries) assertFalse("nobody sees the word Streaming alone as a badge", SourceLabel.badge(s) == "Streaming")
    }

    // ---- the one ticker ------------------------------------------------------------------------------------------------------------------

    @Test fun theTickerRunsOnlyWhileATaggedRowIsOnScreen() {
        val r = TickerRefs()
        assertTrue("first row starts it", r.acquire())
        assertFalse("second row shares it", r.acquire())
        assertFalse("one still on screen", r.release())
        assertTrue("the last one stops it", r.release())
        assertEquals(0, r.users)
        assertFalse("a stray release never goes negative", r.release())
        assertEquals(0, r.users)
        assertTrue("and it can start again", r.acquire())
    }

    // ---- the doubled word, narrow forms, the quiet "New download" ---------------------------------------------------------------------------

    @Test fun theWordDownloadingIsNeverDoubled() {
        assertEquals("Downloading 55%...", DownloadTags.runningText("Downloading 55%..."))
        assertEquals("Downloading from Jamendo...", DownloadTags.runningText("Downloading from Jamendo..."))
        assertEquals("downloading 3%", DownloadTags.runningText("downloading 3%"))
        assertEquals("Downloading · Searching Soulseek...", DownloadTags.runningText("Searching Soulseek..."))
        assertEquals("Downloading · Waiting for a peer...", DownloadTags.runningText("Waiting for a peer..."))
        assertEquals("Downloading", DownloadTags.runningText(""))
        assertEquals("Downloading", DownloadTags.runningText("   "))
        assertEquals("Downloading 40%...", DownloadTags.text(DownloadTag.Downloading("Downloading 40%..."), now))
        assertEquals("Downloading · Starting...", DownloadTags.text(DownloadTag.Downloading("Starting..."), now))
    }

    @Test fun theCompactFormKeepsTheAge() {
        val t = DownloadTag.Recent(now - 4 * min)
        assertEquals("Recently downloaded · 4 min ago", DownloadTags.text(t, now, TagForm.FULL))
        assertEquals("Downloaded · 4m ago", DownloadTags.text(t, now, TagForm.COMPACT))
        assertEquals("4m ago", DownloadTags.text(t, now, TagForm.AGE_ONLY))
        val fresh = DownloadTag.Recent(now)
        assertEquals("Downloaded · just now", DownloadTags.text(fresh, now, TagForm.COMPACT))
        assertEquals("just now", DownloadTags.text(fresh, now, TagForm.AGE_ONLY))
        assertEquals("Downloaded · 23s ago", DownloadTags.text(DownloadTag.Recent(now - 23_000), now, TagForm.COMPACT))
        assertEquals("Downloaded · 59m ago", DownloadTags.text(DownloadTag.Recent(now - 59 * min - 5_000), now, TagForm.COMPACT))
        assertNull("the window is the same in every form", DownloadTags.text(DownloadTag.Recent(now - 60 * min), now, TagForm.COMPACT))
        assertEquals("1m ago", DownloadTags.ageLabel(now, now - 60_000, compact = true))
        assertEquals("1 min ago", DownloadTags.ageLabel(now, now - 60_000))
    }

    @Test fun otherTagsStayShortInNarrowForms() {
        assertEquals("Downloading 55%...", DownloadTags.text(DownloadTag.Downloading("Downloading 55%..."), now, TagForm.COMPACT))
        assertEquals("Downloading", DownloadTags.text(DownloadTag.Downloading("Searching Soulseek..."), now, TagForm.COMPACT))
        assertEquals("Couldn't download", DownloadTags.text(DownloadTag.Failed(now - min), now, TagForm.AGE_ONLY))
        assertEquals("New download", DownloadTags.text(DownloadTag.NewDownload, now, TagForm.COMPACT))
    }

    @Test fun aCloudSongPastTheHourKeepsAQuietNewDownloadMarker() {
        // Cloud, added three hours ago, no job: not recent, but still not a library song
        assertEquals(DownloadTag.NewDownload, DownloadTags.resolve(now, null, null, null, now - 180 * min, isCloud = true))
        assertEquals("New download", DownloadTags.text(DownloadTag.NewDownload, now))
        assertFalse("no age, no ticking", DownloadTags.ticks(DownloadTag.NewDownload))
        assertNull(DownloadTags.expiresAt(DownloadTag.NewDownload))
        // inside the hour it is the age; a running job still wins; an old finish still falls to the marker
        assertEquals(DownloadTag.Recent(now - 5 * min), DownloadTags.resolve(now, null, null, null, now - 5 * min, isCloud = true))
        assertEquals(DownloadTag.Downloading("x"), DownloadTags.resolve(now, "x", null, null, now - 180 * min, isCloud = true))
        assertEquals(DownloadTag.NewDownload, DownloadTags.resolve(now, null, now - 120 * min, null, null, isCloud = true))
        // a library (Jellyfin) song never gets it
        assertEquals(DownloadTag.None, DownloadTags.resolve(now, null, null, null, null, isCloud = false))
    }

    @Test fun theStoreSaysNewDownloadForAnOldCloudTrackOnly() {
        val s = DownloadTagStore()
        val cloud = track("Old", "Artist", TrackSource.CLOUD, now - 5 * 24 * 60 * min)
        val jf = track("Jf", "Artist", TrackSource.JELLYFIN, now - 5 * 24 * 60 * min)
        assertEquals(DownloadTag.NewDownload, s.statusOf(cloud.matchKey, now, DownloadTags.cloudAdded(cloud), DownloadTags.isCloud(cloud)))
        assertEquals(DownloadTag.None, s.statusOf(jf.matchKey, now, DownloadTags.cloudAdded(jf), DownloadTags.isCloud(jf)))
    }
}
