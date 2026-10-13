package com.ipodemu.autosim

import android.app.Activity
import android.content.ComponentName
import android.os.Bundle
import android.os.Handler
import android.os.HandlerThread
import android.util.Log
import androidx.media3.common.MediaItem
import androidx.media3.session.MediaBrowser
import androidx.media3.session.SessionCommand
import androidx.media3.session.SessionToken
import com.google.common.util.concurrent.ListenableFuture
import com.google.common.util.concurrent.SettableFuture

/** Browses and plays through FLACie from another app (another uid), the way Android Auto does; logs what it sees to FLACieAutoSim. */
class SimActivity : Activity() {
    private val tag = "FLACieAutoSim"

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val ht = HandlerThread("sim").also { it.start() }
        val h = Handler(ht.looper)
        Thread {
            try {
                val token = SessionToken(this, ComponentName("com.ipodemu", "com.ipodemu.playback.PlaybackService"))
                val b = MediaBrowser.Builder(this, token).setApplicationLooper(ht.looper).buildAsync().get()
                fun <T> call(block: () -> ListenableFuture<T>): T { val f = SettableFuture.create<T>(); h.post { try { f.setFuture(block()) } catch (e: Throwable) { f.setException(e) } }; return f.get() }
                fun <T> onLooper(block: () -> T): T { val f = SettableFuture.create<T>(); h.post { try { f.set(block()) } catch (e: Throwable) { f.setException(e) } }; return f.get() }
                Log.i(tag, "connected as ${packageName} uid=${android.os.Process.myUid()}")
                // "observe": the phone is already playing a big queue; look at what a head unit is shown of it and drive it
                if (intent.getBooleanExtra("observe", false)) {
                    fun describe(tag2: String) = onLooper { "$tag2: items=${b.mediaItemCount} index=${b.currentMediaItemIndex} title='${b.mediaMetadata.title}' playing=${b.isPlaying} shuffle=${b.shuffleModeEnabled} repeat=${b.repeatMode} speed=${b.playbackParameters.speed}" }
                    Log.i(tag, describe("seen"))
                    val titles = onLooper { (0 until minOf(b.mediaItemCount, 400)).map { b.getMediaItemAt(it).mediaMetadata.title.toString() } }
                    Log.i(tag, "window first=${titles.firstOrNull()} last=${titles.lastOrNull()} size=${titles.size}")
                    val art = onLooper { b.getMediaItemAt(b.currentMediaItemIndex).mediaMetadata.artworkUri?.toString() }
                    Log.i(tag, "current artwork uri: $art")
                    onLooper { b.seekToDefaultPosition(minOf(b.currentMediaItemIndex + 5, b.mediaItemCount - 1)) }; Thread.sleep(2500)
                    Log.i(tag, describe("after picking the 5th upcoming item"))
                    onLooper { b.seekToNextMediaItem() }; Thread.sleep(2000)
                    Log.i(tag, describe("after next"))
                    onLooper { b.seekToPreviousMediaItem() }; Thread.sleep(2000)
                    Log.i(tag, describe("after previous"))
                    onLooper { b.shuffleModeEnabled = true }; Thread.sleep(1500)
                    Log.i(tag, describe("after shuffle on"))
                    val sh = onLooper { (0 until minOf(b.mediaItemCount, 400)).map { b.getMediaItemAt(it).mediaMetadata.title.toString() } }
                    Log.i(tag, "shuffled window starts: ${sh.take(6)}")
                    onLooper { b.shuffleModeEnabled = false; b.repeatMode = androidx.media3.common.Player.REPEAT_MODE_ALL }; Thread.sleep(1500)
                    Log.i(tag, describe("after repeat all"))
                    onLooper { b.repeatMode = androidx.media3.common.Player.REPEAT_MODE_OFF; b.setPlaybackSpeed(1.5f) }; Thread.sleep(1500)
                    Log.i(tag, describe("after speed 1.5"))
                    onLooper { b.setPlaybackSpeed(1f); b.pause() }; Thread.sleep(1000)
                    Log.i(tag, describe("after pause"))
                    Log.i(tag, "sim done")
                    return@Thread
                }
                val root = call { b.getLibraryRoot(null) }
                Log.i(tag, "root=${root.value?.mediaId} code=${root.resultCode}")
                fun kids(id: String, n: Int = 50): List<MediaItem> = call { b.getChildren(id, 0, n, null) }.value?.toList() ?: emptyList()
                val top = kids("root")
                Log.i(tag, "folders: " + top.joinToString { it.mediaMetadata.title.toString() })
                var artOk = 0; var artBad = 0
                for (n in top) {
                    val l = kids(n.mediaId, 20)
                    // covers: a head unit reads them through the content address, as another app
                    for (m in l.take(5)) {
                        val u = m.mediaMetadata.artworkUri ?: continue
                        try { contentResolver.openInputStream(u)?.use { s -> val bytes = s.readBytes(); val png = bytes.size > 8 && bytes[0] == 0x89.toByte() && bytes[1] == 0x50.toByte(); val jpg = bytes.size > 3 && bytes[0] == 0xFF.toByte() && bytes[1] == 0xD8.toByte(); if (png || jpg) artOk++ else artBad++ } ?: artBad++ } catch (e: Exception) { artBad++; Log.i(tag, "cover failed: ${e.javaClass.simpleName} ${e.message} for $u") }
                    }
                    Log.i(tag, "${n.mediaMetadata.title}: ${l.size} items; first='${l.firstOrNull()?.mediaMetadata?.title}' browsable=${l.firstOrNull()?.mediaMetadata?.isBrowsable} playable=${l.firstOrNull()?.mediaMetadata?.isPlayable}")
                }
                Log.i(tag, "covers read through the provider: ok=$artOk failed=$artBad")
                val hits = call { b.getSearchResult("song 1", 0, 10, null) }.value?.size
                Log.i(tag, "search 'song 1': $hits results")
                val albums = kids("albums", 5); val album = albums.firstOrNull()
                val songs = album?.let { kids(it.mediaId, 10) } ?: emptyList()
                Log.i(tag, "album '${album?.mediaMetadata?.title}': ${songs.size} songs")
                val fav = kids("favorites", 10)
                if (songs.isNotEmpty()) {
                    onLooper { b.setMediaItem(songs[0]); b.prepare(); b.play() }
                    Thread.sleep(5000)
                    Log.i(tag, "playing: ${onLooper { b.isPlaying }} title='${onLooper { b.mediaMetadata.title }}' artist='${onLooper { b.mediaMetadata.artist }}' items=${onLooper { b.mediaItemCount }} pos=${onLooper { b.currentPosition }}")
                    onLooper { b.seekToNext() }; Thread.sleep(2500)
                    Log.i(tag, "after next: title='${onLooper { b.mediaMetadata.title }}' playing=${onLooper { b.isPlaying }}")
                    val r = call { b.sendCustomCommand(SessionCommand("com.ipodemu.SPEED", Bundle.EMPTY), Bundle.EMPTY) }
                    Log.i(tag, "custom command speed: code=${r.resultCode}")
                    val layout = onLooper { b.customLayout.map { it.displayName.toString() } }
                    Log.i(tag, "custom layout: $layout")
                    onLooper { b.pause() }; Thread.sleep(800)
                    Log.i(tag, "after pause: playing=${onLooper { b.isPlaying }}")
                }
                Log.i(tag, "favourites folder: ${fav.size} items")
                Log.i(tag, "sim done")
            } catch (e: Throwable) { Log.e(tag, "sim failed", e) }
        }.start()
    }
}
