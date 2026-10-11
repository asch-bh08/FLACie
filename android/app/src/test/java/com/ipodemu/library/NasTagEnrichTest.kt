package com.ipodemu.library

import com.ipodemu.library.tags.ArrayAccess
import com.ipodemu.library.tags.CachedTags
import com.ipodemu.library.tags.NasEntry
import com.ipodemu.library.tags.NasTagCache
import com.ipodemu.library.tags.NasTagEnricher
import com.ipodemu.library.tags.NasTagMerge
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertTrue
import org.junit.Assert.assertFalse
import org.junit.Test
import java.io.File
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.atomic.AtomicInteger

class NasTagEnrichTest {
    private fun fixture(name: String): ByteArray =
        (javaClass.getResourceAsStream("/tags/$name") ?: error("missing $name")).use { it.readBytes() }

    private fun base(path: String, title: String = "guess title", artist: String = "Folder Artist", album: String = "Folder Album", size: Long = 1000, mtime: Long = 5) = Track(
        path = path, title = title, artist = artist, album = album, albumArtist = artist, genre = "", trackNo = 0, discNo = 0, durationMs = 0, year = 0,
        isMusic = true, artKey = NasCover.key("smb://h/s/a/", artist, album, ""), mtime = mtime, size = size, source = TrackSource.NAS,
    )

    private fun cached(title: String = "", artist: String = "", albumArtist: String = "", album: String = "", art: Boolean = false, dur: Long = 0) =
        CachedTags(1000, 5, true, "mp3", title, artist, albumArtist, album, "Rock", 3, 1, 2011, dur, 44100, 16, 128, 2, art)

    @Test fun tags_override_name_guesses() {
        val t = NasTagMerge.apply(base("smb://h/s/a/01 - x.mp3"), cached("Real Title", "Real Artist", "Real AA", "Real Album", dur = 123_000))
        assertEquals("Real Title", t.title); assertEquals("Real Artist", t.artist); assertEquals("Real AA", t.albumArtist)
        assertEquals("Real Album", t.album); assertEquals(3, t.trackNo); assertEquals(1, t.discNo); assertEquals(2011, t.year)
        assertEquals("Rock", t.genre); assertEquals(123_000, t.durationMs)
        assertEquals(TrackSource.NAS, t.source); assertEquals("smb://h/s/a/01 - x.mp3", t.path)
    }

    @Test fun junk_tags_fall_back_to_filename_guesses() {
        val t = NasTagMerge.apply(base("smb://h/s/a/b.mp3", title = "Guess", artist = "Folder Artist", album = "Folder Album"),
            cached(title = "Track 01", artist = "Unknown Artist", album = "  "))
        assertEquals("Guess", t.title); assertEquals("Folder Artist", t.artist); assertEquals("Folder Album", t.album)
        assertTrue(NasTagMerge.junk("<unknown>")); assertTrue(NasTagMerge.junk("")); assertFalse(NasTagMerge.junk("Unknown Mortal Orchestra"))
    }

    @Test fun album_stays_one_album_without_album_artist_tag() {
        val a = NasTagMerge.apply(base("smb://h/s/a/1.mp3"), cached("S1", "Artist feat. Guest", album = "Album"))
        val b = NasTagMerge.apply(base("smb://h/s/a/2.mp3"), cached("S2", "Artist", album = "Album"))
        assertEquals(a.albumKey, b.albumKey)
    }

    @Test fun tagged_song_matches_the_jellyfin_copy_key() {
        val nas = NasTagMerge.apply(base("smb://h/s/Artist/Album/03 - rosanna.flac", title = "03 - rosanna"), cached("Rosanna", "Toto", album = "Toto IV"))
        val jf = base("/media/x.flac", title = "Rosanna", artist = "Toto", album = "Toto IV (1982)").copy(source = TrackSource.JELLYFIN)
        assertEquals(jf.matchKey, nas.matchKey)
    }

    @Test fun art_key_prefers_folder_image_then_embedded_then_online() {
        val folder = "smb://h/s/a/"
        val withImg = NasEntry(base("smb://h/s/a/1.mp3"), folder, folderHasImage = true)
        val embedded = NasEntry(base("smb://h/s/b/2.mp3"), "smb://h/s/b/", folderHasImage = false)
        val sibling = NasEntry(base("smb://h/s/b/3.mp3"), "smb://h/s/b/", folderHasImage = false)
        val lone = NasEntry(base("smb://h/s/c/4.mp3"), "smb://h/s/c/", folderHasImage = false)
        val tags = listOf(cached("A", "X", album = "Al", art = true), cached("B", "X", album = "Al", art = true), cached("C", "X", album = "Al"), cached("D", "Y", album = "Z"))
        val out = NasTagMerge.resolve(listOf(withImg, embedded, sibling, lone), tags)
        assertEquals(withImg.base.artKey, out[0].artKey)              // folder image wins: key unchanged ("nf")
        assertTrue(out[1].artKey!!.startsWith("ne"))                  // embedded
        assertEquals(out[1].artKey, out[2].artKey)                    // the sibling without art shares the album's embedded cover
        assertTrue(out[3].artKey!!.startsWith("nf"))                  // nothing local: online lookup, by the tagged names
        assertNotEquals(lone.base.artKey, out[3].artKey)
    }

    @Test fun cache_roundtrip_and_invalidation() {
        val f = File.createTempFile("nastags", ".bin")
        try {
            val c = NasTagCache(f)
            c.put("smb://h/s/a.mp3", cached("T", "A", album = "B", art = true, dur = 42))
            c.save()
            val d = NasTagCache(f); d.load()
            val got = d.get("smb://h/s/a.mp3", 1000, 5)!!
            assertEquals("T", got.title); assertEquals(42, got.durationMs); assertTrue(got.hasArt)
            assertEquals(null, d.get("smb://h/s/a.mp3", 1001, 5))   // size changed
            assertEquals(null, d.get("smb://h/s/a.mp3", 1000, 6))   // mtime changed
            f.writeBytes(byteArrayOf(1, 2, 3))                       // corrupt cache file is just empty
            val e = NasTagCache(f); e.load(); assertEquals(0, e.count)
        } finally { f.delete() }
    }

    @Test fun enricher_reads_each_file_once_and_publishes_progressively() = runBlocking {
        val files = listOf("mp3_v23_art.mp3", "flac_art.flac", "m4a_aac_moovend.m4a", "ogg_vorbis.ogg", "wav_tags.wav", "mp3_notags.mp3")
        // enough entries to need several publishes (the fixtures repeat under different paths)
        val entries = (0 until 600).map { i ->
            val name = files[i % files.size]
            val d = fixture(name)
            NasEntry(base("smb://h/s/Artist/Album$i/$name", size = d.size.toLong(), mtime = i.toLong()), "smb://h/s/Artist/Album$i/", false)
        }
        val cacheFile = File.createTempFile("nastags", ".bin")
        try {
            val opened = AtomicInteger()
            val saved = ConcurrentHashMap<String, Int>()
            val publishes = ArrayList<Int>()
            fun run(cache: NasTagCache) = runBlocking {
                val enr = NasTagEnricher(cache, parallel = 4)
                val (_, tags) = enr.fromCache(entries)
                enr.run(entries, tags,
                    open = { e -> opened.incrementAndGet(); ArrayAccess(fixture(e.fileName)) },
                    hasArt = { saved.containsKey(it) },
                    saveArt = { k, b -> saved[k] = b.size },
                    publish = { t, g -> synchronized(publishes) { publishes += g.count { it != null } }; assertEquals(entries.size, t.size) })
            }
            val c1 = NasTagCache(cacheFile)
            run(c1)
            assertEquals(entries.size, opened.get())
            assertTrue("progressive publishes: $publishes", publishes.size >= 3)
            assertEquals(entries.size, publishes.last())
            // art stored per album folder (one key per entry here), only for files that have art
            assertTrue(saved.isNotEmpty())

            // a second scan with a persisted cache reads nothing
            val c2 = NasTagCache(cacheFile); c2.load()
            opened.set(0)
            run(c2)
            assertEquals(0, opened.get())
            val (tracks, _) = NasTagEnricher(c2).fromCache(entries)
            assertEquals("Test_Title", tracks[0].title)
            assertTrue(Math.abs(tracks[0].durationMs - 2000) <= 50)
            assertEquals(2011, tracks[1].year)
        } finally { cacheFile.delete() }
    }
}
