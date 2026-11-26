package com.ipodemu.playback

import android.content.ComponentName
import android.content.Context
import android.os.Handler
import android.os.HandlerThread
import android.util.Log
import androidx.media3.common.MediaItem
import androidx.media3.common.util.UnstableApi
import androidx.media3.session.MediaBrowser
import androidx.media3.session.LibraryResult
import androidx.media3.session.SessionToken
import com.google.common.util.concurrent.ListenableFuture
import com.google.common.util.concurrent.SettableFuture
import com.ipodemu.App

/**
 * A test aid, like the fake and clearcovers extras: `am start --ez browseprobe true` connects to this app's own media library service the way Android Auto would, browses
 * every folder, searches, plays an album's first song through the browse id and logs what it saw (adb logcat -s FLACieBrowse). It is how the library service is checked
 * on an emulator, where there is no car.
 */
@UnstableApi
object BrowseProbe {
    private const val TAG = "FLACieBrowse"

    fun run(ctx: Context) {
        val ht = HandlerThread("browse-probe").also { it.start() }
        Thread {
            try {
                val token = SessionToken(ctx, ComponentName(ctx, PlaybackService::class.java))
                val future = MediaBrowser.Builder(ctx, token).setApplicationLooper(ht.looper).buildAsync()
                val b = future.get()
                // a media controller may only be used on the thread it was built for: every call is posted there and waited for here
                fun <T> call(block: () -> ListenableFuture<T>): T { val f = SettableFuture.create<T>(); Handler(ht.looper).post { try { f.setFuture(block()) } catch (e: Throwable) { f.setException(e) } }; return f.get() }
                val root = call { b.getLibraryRoot(null) }
                Log.i(TAG, "root: ${root.value?.mediaId} browsable=${root.value?.mediaMetadata?.isBrowsable} resultCode=${root.resultCode}")
                fun kids(id: String, n: Int = 50): List<MediaItem> = call { b.getChildren(id, 0, n, null) }.value?.toList() ?: emptyList()
                val top = kids("root")
                Log.i(TAG, "root children: " + top.joinToString { it.mediaMetadata.title.toString() })
                for (n in top) {
                    val l = kids(n.mediaId, 10)
                    Log.i(TAG, "${n.mediaId}: ${l.size} items, first='${l.firstOrNull()?.mediaMetadata?.title}' art=${l.firstOrNull()?.mediaMetadata?.artworkUri}")
                }
                // Home's sections and each list's speed (owner, 26 Nov: the car's Home, grouped search, nothing slow)
                val groupKey = "android.media.browse.CONTENT_STYLE_GROUP_TITLE_HINT"
                fun groups(l: List<MediaItem>) = l.mapNotNull { it.mediaMetadata.extras?.getString(groupKey) }.distinct()
                for (id in listOf("home", "playlists", "albums", "artists")) {
                    val t0 = android.os.SystemClock.elapsedRealtime(); val l = kids(id, 100); val ms = android.os.SystemClock.elapsedRealtime() - t0
                    Log.i(TAG, "timing $id: ${l.size} items in $ms ms; covers=${l.count { it.mediaMetadata.artworkUri != null }}")
                }
                val home = kids("home", 100)
                Log.i(TAG, "home sections: " + groups(home).joinToString(" | "))
                Log.i(TAG, "home first: '${home.firstOrNull()?.mediaMetadata?.title}' / '${home.firstOrNull()?.mediaMetadata?.subtitle}' id=${home.firstOrNull()?.mediaId}")
                call { b.search("song", null) }
                val found = call { b.getSearchResult("song", 0, 100, null) }.value?.toList() ?: emptyList()
                Log.i(TAG, "search 'song': ${found.size} in sections " + groups(found).joinToString(" | "))
                val album = kids("albums", 5).firstOrNull()
                val songs = album?.let { kids(it.mediaId, 10) } ?: emptyList()
                Log.i(TAG, "album '${album?.mediaMetadata?.title}' has ${songs.size} songs")
                val hits = call { b.getSearchResult("song 1", 0, 10, null) }.value?.size
                Log.i(TAG, "search 'song 1': $hits")
                if (songs.isNotEmpty()) {
                    // Shuffle all: a long queue, so the queue check has songs to tap
                    Handler(ht.looper).post { b.setMediaItem(MediaItem.Builder().setMediaId(BrowseTree.SHUFFLE).build()); b.prepare(); b.play() }
                    Thread.sleep(4000)
                    val app = App.of(ctx)
                    // the player is read on the main thread, where it lives
                    Handler(android.os.Looper.getMainLooper()).post { Log.i(TAG, "playing: '${app.player.current?.title}' queue=${app.player.queue.size} wantsToPlay=${app.player.wantsToPlay}") }
                    Thread.sleep(500)
                    // the queue the car shows, and the buttons with their states before and after a tap on shuffle
                    fun buttons() = call { SettableFuture.create<String>().also { it.set(b.customLayout.joinToString { c -> c.displayName.toString() }) } }
                    val q = call { SettableFuture.create<String>().also { f -> val tl = b.currentTimeline
                        f.set("'${b.playlistMetadata.title}' ${tl.windowCount} items, now ${b.currentMediaItemIndex}: " + (0 until minOf(3, tl.windowCount)).joinToString { i -> b.getMediaItemAt(i).mediaMetadata.let { m -> "${m.title} (${m.artist}) art=${m.artworkUri != null}" } }) } }
                    Log.i(TAG, "queue: $q")
                    Log.i(TAG, "buttons: ${buttons()}")
                    call { b.sendCustomCommand(androidx.media3.session.SessionCommand(PlaybackService.CMD_SHUFFLE, android.os.Bundle.EMPTY), android.os.Bundle.EMPTY) }
                    Thread.sleep(300)
                    Log.i(TAG, "buttons after shuffle: ${buttons()}")
                    call { b.sendCustomCommand(androidx.media3.session.SessionCommand(PlaybackService.CMD_SHUFFLE, android.os.Bundle.EMPTY), android.os.Bundle.EMPTY) }
                    if (call { SettableFuture.create<Int>().also { it.set(b.currentTimeline.windowCount) } } > 2) { Handler(ht.looper).post { b.seekToDefaultPosition(2) }; Thread.sleep(1500)
                        Handler(android.os.Looper.getMainLooper()).post { Log.i(TAG, "after tapping queue item 2: '${app.player.current?.title}'") }; Thread.sleep(300) }
                    // the queue saved before the next start reads it for Continue (a skip's burst holds the save back; the app was force-stopped in between)
                    Thread.sleep(2500); Handler(android.os.Looper.getMainLooper()).post { app.player.persistNow() }; Thread.sleep(1500)
                }
                Log.i(TAG, "probe done")
            } catch (e: Throwable) { Log.e(TAG, "probe failed", e) }
        }.start()
    }
}
