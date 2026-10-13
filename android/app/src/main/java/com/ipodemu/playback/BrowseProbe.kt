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
                val album = kids("albums", 5).firstOrNull()
                val songs = album?.let { kids(it.mediaId, 10) } ?: emptyList()
                Log.i(TAG, "album '${album?.mediaMetadata?.title}' has ${songs.size} songs")
                val hits = call { b.getSearchResult("song 1", 0, 10, null) }.value?.size
                Log.i(TAG, "search 'song 1': $hits")
                if (songs.isNotEmpty()) {
                    Handler(ht.looper).post { b.setMediaItem(songs[0]); b.prepare(); b.play() }
                    Thread.sleep(4000)
                    val app = App.of(ctx)
                    // the player is read on the main thread, where it lives
                    Handler(android.os.Looper.getMainLooper()).post { Log.i(TAG, "playing: '${app.player.current?.title}' queue=${app.player.queue.size} wantsToPlay=${app.player.wantsToPlay}") }
                    Thread.sleep(500)
                }
                Log.i(TAG, "probe done")
            } catch (e: Throwable) { Log.e(TAG, "probe failed", e) }
        }.start()
    }
}
