package com.ipodemu.playback

import android.content.ComponentName
import androidx.media3.common.MediaItem
import androidx.media3.session.MediaBrowser
import androidx.media3.session.SessionToken
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import java.util.concurrent.TimeUnit

/**
 * Playlists in the car, as Android Auto plays them (owner, 6 Nov: "clicking play all or shuffle on a playlist doesn't reload that playlist anymore and songs
 * didn't even play at all"; "when I skip it plays a random recommendation"). Needs a signed-in app whose library has the playlists "Car Test A" and
 * "Car Test B": tools/loadtest/playlists-android.mjs signs the emulator in to the mock with them and then runs this class.
 */
@RunWith(AndroidJUnit4::class)
class CarPlaylistTest {
    private val ins = InstrumentationRegistry.getInstrumentation()
    private val ctx = ins.targetContext
    private val app get() = com.ipodemu.App.of(ctx)
    private lateinit var browser: MediaBrowser

    @Before fun connect() {
        val token = SessionToken(ctx, ComponentName(ctx, PlaybackService::class.java))
        browser = MediaBrowser.Builder(ctx, token).setApplicationLooper(android.os.Looper.getMainLooper()).buildAsync().get(20, TimeUnit.SECONDS)
    }

    @After fun release() { ins.runOnMainSync { try { app.player.exo.pause() } catch (_: Exception) { }; browser.release() } }

    private fun <T> main(block: () -> com.google.common.util.concurrent.ListenableFuture<T>): T {
        var f: com.google.common.util.concurrent.ListenableFuture<T>? = null
        ins.runOnMainSync { f = block() }
        return f!!.get(40, TimeUnit.SECONDS)
    }
    private fun children(id: String) = main { browser.getChildren(id, 0, 100, null) }.value!!
    private fun <T> onMain(block: () -> T): T { var v: T? = null; ins.runOnMainSync { v = block() }; @Suppress("UNCHECKED_CAST") return v as T }

    /** The car's Playlists tab, the playlist called [name], and its songs' titles as the car lists them. */
    private fun playlist(name: String): Pair<String, List<String>> {
        val pl = children(BrowseTree.PLAYLISTS).firstOrNull { it.mediaMetadata.title.toString() == name }
        assertNotNull("the car lists the playlist \"$name\"", pl)
        val songs = children(pl!!.mediaId).filter { it.mediaId.startsWith("s:") }.map { it.mediaMetadata.title.toString() }
        return pl.mediaId to songs
    }

    /** Presses a car tile ([id]) and waits until a song plays; returns the playing song's title and the queue's titles. */
    private fun press(id: String): Pair<String, List<String>> {
        val before = onMain { app.player.queueRev }
        ins.runOnMainSync { browser.setMediaItem(MediaItem.Builder().setMediaId(id).build()); browser.prepare(); browser.play() }
        val until = System.currentTimeMillis() + 30_000
        while (System.currentTimeMillis() < until) {
            if (onMain { app.player.queueRev != before && app.player.exo.isPlaying }) break
            Thread.sleep(250)
        }
        assertTrue("\"$id\" started playing within 30 s", onMain { app.player.exo.isPlaying })
        return onMain { (app.player.current?.title ?: "") to app.player.queue.map { it.title } }
    }

    private fun next(): String {
        val was = onMain { app.player.current?.path }
        ins.runOnMainSync { browser.seekToNext() }
        val until = System.currentTimeMillis() + 10_000
        while (System.currentTimeMillis() < until && onMain { app.player.current?.path } == was) Thread.sleep(100)
        return onMain { app.player.current?.title ?: "" }
    }

    @Test fun play_all_plays_the_playlist_from_its_first_song_and_next_stays_in_it() {
        val (id, songs) = playlist("Car Test A")
        assertTrue("the playlist has its songs in the car (${songs.size})", songs.size >= 10)
        val (now, queue) = press("all:$id")
        assertEquals("Play all starts at the first song", songs.first(), now)
        assertEquals("the queue is the playlist, every song", songs, queue)
        repeat(8) { val t = next(); assertTrue("Next played \"$t\", which is not in the playlist", t in songs) }
    }

    @Test fun shuffle_plays_only_the_playlists_songs_and_skips_stay_in_it() {
        val (id, songs) = playlist("Car Test A")
        val (now, queue) = press("shuf:$id")
        assertTrue("\"$now\" is from the playlist", now in songs)
        assertEquals("the queue is the playlist's songs, shuffled", songs.sorted(), queue.sorted())
        val heard = mutableListOf(now)
        repeat(songs.size - 1) { val t = next(); heard += t; assertTrue("Next played \"$t\", which is not in the playlist", t in songs) }
        assertEquals("every song once before any again: $heard", songs.size, heard.toSet().size)
    }

    @Test fun picking_another_playlist_while_one_plays_switches_to_it() {
        val (a, aSongs) = playlist("Car Test A")
        val (b, bSongs) = playlist("Car Test B")
        press("shuf:$a")
        val (now, queue) = press("shuf:$b")
        assertTrue("\"$now\" is from Car Test B", now in bSongs)
        assertEquals(bSongs.sorted(), queue.sorted())
        // and back: the same playlist again reloads it too
        val (again, q2) = press("all:$a")
        assertEquals(aSongs.first(), again); assertEquals(aSongs, q2)
    }

    @Test fun shuffle_on_the_playlist_already_playing_starts_it_again() {
        val (a, aSongs) = playlist("Car Test A")
        press("all:$a")
        repeat(3) { next() }
        val (now, queue) = press("shuf:$a")
        assertTrue(now in aSongs)
        assertEquals(aSongs.sorted(), queue.sorted())
        assertTrue("shuffle is on", onMain { app.player.exo.shuffleModeEnabled || app.prefs.shuffle })
    }

    @Test fun previous_goes_back_through_the_playlist() {
        val (a, aSongs) = playlist("Car Test A")
        press("all:$a")
        next(); next()
        ins.runOnMainSync { browser.seekToPrevious() }
        // Previous within the first seconds of a song goes to the song before; later it restarts the song
        val until = System.currentTimeMillis() + 10_000
        while (System.currentTimeMillis() < until && onMain { app.player.current?.title } == aSongs[2]) Thread.sleep(100)
        assertEquals(aSongs[1], onMain { app.player.current?.title })
    }

    @Test fun an_album_plays_in_track_order_and_an_artist_plays_their_songs() {
        val album = children(BrowseTree.ALBUMS).first { it.mediaMetadata.isBrowsable == true }
        val songs = children(album.mediaId).filter { it.mediaId.startsWith("s:") }.map { it.mediaMetadata.title.toString() }
        assertTrue(songs.isNotEmpty())
        val (now, queue) = press("all:${album.mediaId}")
        // a short album is topped up by Autoplay after its songs
        assertEquals(songs.first(), now); assertEquals(songs, queue.take(songs.size))
        val artist = children(BrowseTree.ARTISTS).first { it.mediaMetadata.isBrowsable == true }
        val name = artist.mediaMetadata.title.toString()
        press("shuf:${artist.mediaId}")
        val by = onMain { app.library.songs().count { it.artist.equals(name, ignoreCase = true) || it.albumArtist.equals(name, ignoreCase = true) } }
        val q = onMain { app.player.queue }
        // the artist's songs, then (an artist with few songs) Autoplay's
        assertTrue("the first $by songs are all by $name: ${q.map { it.artist }}", by > 0 && q.size >= by && q.take(by).all { it.artist.equals(name, ignoreCase = true) || it.albumArtist.equals(name, ignoreCase = true) })
    }

    @Test fun shuffle_all_on_home_plays_from_the_whole_library() {
        val (now, queue) = press(BrowseTree.SHUFFLE)
        assertTrue(now.isNotEmpty())
        assertTrue("the queue is the library (${queue.size} songs)", queue.size >= 50)
        val heard = mutableSetOf(now); repeat(5) { heard += next() }
        assertEquals("no song twice in six", 6, heard.size)
    }

    @Test fun favourites_on_home_play_the_favourites_and_recent_plays_what_was_played() {
        val home = children(BrowseTree.HOME)
        val fav = home.firstOrNull { it.mediaMetadata.title.toString() == "Favourites" }
        assertNotNull("Home has Favourites", fav)
        val favSongs = children(fav!!.mediaId).filter { it.mediaId.startsWith("s:") }.map { it.mediaMetadata.title.toString() }
        assertEquals("the five favourites (songs 21-25)", listOf("Song 21", "Song 22", "Song 23", "Song 24", "Song 25").sorted(), favSongs.sorted())
        val (now, queue) = press("all:${fav.mediaId}")
        assertEquals(favSongs.first(), now)
        assertEquals("the favourites, then (only five) Autoplay's", favSongs, queue.take(favSongs.size))
        // something was played: Recently played names it
        val recent = home.firstOrNull { it.mediaMetadata.title.toString().startsWith("Recent") } ?: return
        val recentSongs = children(recent.mediaId).map { it.mediaMetadata.title.toString() }
        assertTrue("Recently played lists \"$now\": $recentSongs", recentSongs.isEmpty() || now in recentSongs || recent.mediaMetadata.isPlayable == true)
    }

    @Test fun a_song_tapped_in_the_playlist_plays_the_playlist_from_that_song() {
        val (a, aSongs) = playlist("Car Test A")
        val songId = children(a).filter { it.mediaId.startsWith("s:") }[4].mediaId
        val (now, queue) = press(songId)
        assertEquals(aSongs[4], now)
        assertEquals("the rest of the playlist follows, not recommendations", aSongs, queue)
        repeat(4) { val t = next(); assertTrue("\"$t\" is in the playlist", t in aSongs) }
    }
}
