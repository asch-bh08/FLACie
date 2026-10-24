package com.ipodemu.playback

import android.content.ComponentName
import android.net.Uri
import android.os.Bundle
import androidx.media3.common.MediaItem
import androidx.media3.session.LibraryResult
import androidx.media3.session.MediaBrowser
import androidx.media3.session.MediaLibraryService.LibraryParams
import androidx.media3.session.SessionToken
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.google.common.collect.ImmutableList
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

/**
 * A stand-in car: it connects to FLACie's playback service the way Android Auto does (a Media3 browser) and checks what a car is given. Run on the emulator
 * (./gradlew :app:connectedDebugAndroidTest), no car or head unit needed. Each check here is something that went wrong in a real car screen:
 * plain lists instead of covers, a search that spun for ever, empty squares for covers.
 */
@RunWith(AndroidJUnit4::class)
class CarBrowseTest {
    private val ctx = InstrumentationRegistry.getInstrumentation().targetContext
    private lateinit var browser: MediaBrowser
    private val searched = CountDownLatch(1)

    @Before fun connect() {
        val token = SessionToken(ctx, ComponentName(ctx, PlaybackService::class.java))
        val listener = object : MediaBrowser.Listener {
            override fun onSearchResultChanged(browser: MediaBrowser, query: String, itemCount: Int, params: LibraryParams?) { searched.countDown() }
        }
        val f = MediaBrowser.Builder(ctx, token).setListener(listener).setApplicationLooper(android.os.Looper.getMainLooper()).buildAsync()
        browser = f.get(20, TimeUnit.SECONDS)
    }

    @After fun release() { InstrumentationRegistry.getInstrumentation().runOnMainSync { browser.release() } }

    private fun <T> main(block: () -> com.google.common.util.concurrent.ListenableFuture<T>): T {
        var f: com.google.common.util.concurrent.ListenableFuture<T>? = null
        InstrumentationRegistry.getInstrumentation().runOnMainSync { f = block() }
        return f!!.get(40, TimeUnit.SECONDS)
    }
    private fun children(id: String): ImmutableList<MediaItem> = main { browser.getChildren(id, 0, 100, null) }.value!!

    @Test fun the_root_says_it_draws_covers_and_can_search() {
        val root: LibraryResult<MediaItem> = main { browser.getLibraryRoot(null) }
        val extras: Bundle = root.params!!.extras
        assertTrue("content styles are switched on (without it the car ignores every hint)", extras.getBoolean("android.media.browse.CONTENT_STYLE_SUPPORTED"))
        assertTrue(extras.getBoolean("android.media.browse.SEARCH_SUPPORTED"))
    }

    @Test fun four_tabs_with_icons_and_the_cover_tabs_are_grids() {
        val tabs = children(BrowseTree.ROOT)
        assertEquals(listOf("Home", "Playlists", "Albums", "Artists"), tabs.map { it.mediaMetadata.title.toString() })
        assertTrue(tabs.all { it.mediaMetadata.artworkUri != null })
        for (t in tabs.drop(1)) assertEquals("${t.mediaMetadata.title} as covers", 2, t.mediaMetadata.extras!!.getInt("android.media.browse.CONTENT_STYLE_BROWSABLE_HINT"))
    }

    @Test fun home_starts_with_shuffle_all_and_your_music() {
        val home = children(BrowseTree.HOME)
        assertEquals("Shuffle all", home[0].mediaMetadata.title.toString()); assertEquals(true, home[0].mediaMetadata.isPlayable)
        assertTrue(home.any { it.mediaMetadata.title.toString() == "Favourites" && it.mediaMetadata.extras?.getString("android.media.browse.CONTENT_STYLE_GROUP_TITLE_HINT") == "Your music" })
    }

    @Test fun a_search_is_answered() {
        main { browser.search("a", null) }
        assertTrue("the car is told the results are ready (it spun for ever before)", searched.await(30, TimeUnit.SECONDS))
        assertNotNull(main { browser.getSearchResult("a", 0, 50, null) }.value)
    }

    @Test fun now_playing_has_shuffle_and_repeat_buttons() {
        var names = emptyList<String>()
        InstrumentationRegistry.getInstrumentation().runOnMainSync { names = browser.customLayout.map { it.displayName.toString() } }
        assertTrue(names.toString(), names.any { it.startsWith("Shuffle") })
        assertTrue(names.toString(), names.any { it.startsWith("Repeat") })
    }

    @Test fun the_shuffle_button_switches_shuffle() {
        val app = com.ipodemu.App.of(ctx)
        val before = app.prefs.shuffle
        val cmd = androidx.media3.session.SessionCommand(PlaybackService.CMD_SHUFFLE, Bundle.EMPTY)
        main { browser.sendCustomCommand(cmd, Bundle.EMPTY) }
        assertEquals(!before, app.prefs.shuffle)
        main { browser.sendCustomCommand(cmd, Bundle.EMPTY) }
        assertEquals(before, app.prefs.shuffle)
    }

    @Test fun play_on_the_card_carries_on_with_the_queue_of_last_time() {
        val app = com.ipodemu.App.of(ctx)
        val t = com.ipodemu.library.Track(path = "http://127.0.0.1:9/Audio/0123456789abcdef0123456789abcdef/stream?static=true", title = "Saved Song", artist = "Saved Artist",
            album = "A", albumArtist = "Saved Artist", genre = "", trackNo = 1, discNo = 1, durationMs = 60_000, year = 0, isMusic = true, artKey = null, mtime = 0, size = 0,
            source = com.ipodemu.library.TrackSource.JELLYFIN)
        InstrumentationRegistry.getInstrumentation().runOnMainSync { app.player.exo.stop(); app.player.exo.clearMediaItems() }
        java.io.File(ctx.filesDir, "queue.json").writeText(QueueStore.encode(listOf(t), 0, 5_000, null))
        InstrumentationRegistry.getInstrumentation().runOnMainSync { browser.setMediaItem(MediaItem.Builder().setMediaId(BrowseTree.RESUME).build()) }
        Thread.sleep(1500)
        var title: String? = null
        InstrumentationRegistry.getInstrumentation().runOnMainSync { title = app.player.current?.title; app.player.exo.pause() }
        assertEquals("Saved Song", title)
    }

    @Test fun download_and_play_from_the_car_starts_the_download() {
        val app = com.ipodemu.App.of(ctx)
        val c = BrowseTree.CatalogSong("Robyn", "Dancing On My Own", "Body Talk", 280_000)
        InstrumentationRegistry.getInstrumentation().runOnMainSync { browser.setMediaItem(MediaItem.Builder().setMediaId(BrowseTree.downloadId(c)).build()) }
        Thread.sleep(1000)
        val key = app.library.songKey(c.artist, c.title)
        assertTrue("the car's pick reached the downloader", app.library.runningDownloads.containsKey(key) || app.library.downloads.containsKey(key))
    }

    @Test fun a_cover_with_no_picture_is_the_logo_not_an_empty_square() {
        val uri = Uri.parse("content://com.ipodemu.art/" + Uri.encode("no-such-cover-anywhere"))
        val size = ctx.contentResolver.openFileDescriptor(uri, "r")!!.use { it.statSize }
        assertTrue(size > 0)
    }
}
