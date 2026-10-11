package com.ipodemu.library

import com.ipodemu.library.tags.ArrayAccess
import com.ipodemu.library.tags.NasEntry
import com.ipodemu.library.tags.NasTagCache
import com.ipodemu.library.tags.NasTagEnricher
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicInteger

/** The background tag scan must leave the screen alone: no file is read while the user is touching it, and it resumes afterwards. */
class NasEnrichPauseTest {
    private fun fixture(name: String): ByteArray =
        (javaClass.getResourceAsStream("/tags/$name") ?: error("missing $name")).use { it.readBytes() }

    private fun entries(n: Int): List<NasEntry> = (0 until n).map { i ->
        val d = fixture("mp3_v23_art.mp3")
        NasEntry(Track(
            path = "smb://h/s/Artist/Album$i/x.mp3", title = "t", artist = "a", album = "b", albumArtist = "a", genre = "", trackNo = 0, discNo = 0,
            durationMs = 0, year = 0, isMusic = true, artKey = null, mtime = i.toLong(), size = d.size.toLong(), source = TrackSource.NAS,
        ), "smb://h/s/Artist/Album$i/", false)
    }

    @Test fun no_file_is_read_while_paused_and_reading_resumes() = runBlocking {
        val es = entries(40)
        val cacheFile = File.createTempFile("nastags", ".bin")
        try {
            val paused = AtomicBoolean(true)
            val opened = AtomicInteger()
            val enr = NasTagEnricher(NasTagCache(cacheFile), parallel = 2, pauseWhile = { paused.get() })
            val (_, tags) = enr.fromCache(es)
            val job = launch(kotlinx.coroutines.Dispatchers.Default) {
                enr.run(es, tags, open = { opened.incrementAndGet(); ArrayAccess(fixture("mp3_v23_art.mp3")) },
                    hasArt = { true }, saveArt = { _, _ -> }, publish = { _, _ -> })
            }
            delay(1500)                                   // paused: the scan is running but must not read anything
            assertEquals("files read while the screen was in use", 0, opened.get())
            paused.set(false)                             // the screen is left alone: reading resumes and finishes
            job.join()
            assertEquals(es.size, opened.get())
        } finally { cacheFile.delete() }
    }

    @Test fun a_publish_gap_holds_back_intermediate_publishes_but_not_the_last() = runBlocking {
        val es = entries(600)
        val cacheFile = File.createTempFile("nastags", ".bin")
        try {
            val publishes = AtomicInteger()
            val enr = NasTagEnricher(NasTagCache(cacheFile), parallel = 2, minPublishGapMs = 60_000)
            val (_, tags) = enr.fromCache(es)
            enr.run(es, tags, open = { ArrayAccess(fixture("mp3_v23_art.mp3")) }, hasArt = { true }, saveArt = { _, _ -> }, publish = { _, _ -> publishes.incrementAndGet() })
            assertEquals("only the final publish with a 60 s gap", 1, publishes.get())
            assertTrue(true)
        } finally { cacheFile.delete() }
    }
}
