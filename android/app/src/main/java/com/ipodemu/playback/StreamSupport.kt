package com.ipodemu.playback

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.Uri
import androidx.media3.common.C
import androidx.media3.common.util.UnstableApi
import androidx.media3.database.StandaloneDatabaseProvider
import androidx.media3.datasource.DataSource
import androidx.media3.datasource.DataSpec
import androidx.media3.datasource.TransferListener
import androidx.media3.datasource.cache.CacheDataSource
import androidx.media3.datasource.cache.CacheWriter
import androidx.media3.datasource.cache.LeastRecentlyUsedCacheEvictor
import androidx.media3.datasource.cache.SimpleCache
import androidx.media3.exoplayer.DefaultLoadControl
import androidx.media3.exoplayer.LoadControl
import java.io.File
import java.util.concurrent.Executors
import java.util.concurrent.Future

/**
 * Everything that keeps playback going on a weak or flaky connection (see PlayerController):
 *  - [loadControl] buffers far ahead (up to ten minutes, memory permitting), so a song is usually fully on the phone long before it ends and a
 *    pause, a screen lock or a dip in signal doesn't touch what is playing;
 *  - [cache] keeps what was downloaded, so going back, pausing and resuming or a retry never fetches the same bytes again;
 *  - [Prefetcher] fetches the next songs completely, when the connection can afford it.
 */
@UnstableApi
object StreamSupport {
    @Volatile private var cache: SimpleCache? = null
    fun cache(ctx: Context): SimpleCache = cache ?: synchronized(this) {
        cache ?: SimpleCache(File(ctx.cacheDir, "stream"), LeastRecentlyUsedCacheEvictor(768L * 1024 * 1024), StandaloneDatabaseProvider(ctx)).also { cache = it }
    }

    /** Buffers 3-10 minutes ahead (a FLAC is about 7 MB a minute), starts after 1.5 s of audio, and after running dry waits for 6 s so it doesn't stutter in and out. */
    fun loadControl(): LoadControl = DefaultLoadControl.Builder()
        .setAllocator(androidx.media3.exoplayer.upstream.DefaultAllocator(true, C.DEFAULT_BUFFER_SEGMENT_SIZE))
        .setBufferDurationsMs(180_000, 600_000, 1_500, 6_000)
        .setTargetBufferBytes(64 * 1024 * 1024)
        .setPrioritizeTimeOverSizeThresholds(false)
        .setBackBuffer(30_000, true)
        .build()

    /** A song that is cached when it is a streamed file (not a local file, and not a transcoded HLS stream, whose pieces are one-offs). */
    fun cacheable(uri: Uri): Boolean {
        val s = uri.scheme ?: return false
        if (s == "smb") return true
        if (s != "http" && s != "https") return false
        val u = uri.toString()
        return !u.contains("/universal") && !u.contains(".m3u8") && !u.contains(".ts") && !u.contains("/hls")
    }
}

/** Sends streamed songs through the cache and everything else (local files, HLS pieces) straight to the plain source. */
@UnstableApi
class RoutingDataSource(private val cached: DataSource, private val plain: DataSource) : DataSource {
    private var active: DataSource = plain
    override fun addTransferListener(l: TransferListener) { cached.addTransferListener(l); plain.addTransferListener(l) }
    override fun open(dataSpec: DataSpec): Long {
        active = if (StreamSupport.cacheable(dataSpec.uri)) cached else plain
        return active.open(dataSpec)
    }
    override fun read(buffer: ByteArray, offset: Int, length: Int): Int = active.read(buffer, offset, length)
    override fun getUri(): Uri? = active.uri
    override fun getResponseHeaders(): Map<String, List<String>> = active.responseHeaders
    override fun close() { active.close() }
}

/** Downloads upcoming songs into the cache in the background, one at a time, and stops when asked. */
@UnstableApi
class Prefetcher(private val ctx: Context, private val newSource: () -> CacheDataSource) {
    private val pool = Executors.newSingleThreadExecutor { r -> Thread(r, "prefetch").also { it.isDaemon = true } }
    private var job: Future<*>? = null
    @Volatile private var cancelled = false
    @Volatile var lastNote: String = ""; private set

    /** Fetches [uris] in order (skipping what is already there). Replaces any earlier request. */
    fun fetch(uris: List<Uri>) {
        job?.cancel(true); cancelled = true
        if (uris.isEmpty()) return
        cancelled = false
        val mine = uris
        job = pool.submit {
            for (u in mine) {
                if (cancelled || Thread.currentThread().isInterrupted) return@submit
                try {
                    val writer = CacheWriter(newSource(), DataSpec(u), null, null)
                    lastNote = "caching ${u.lastPathSegment}"
                    writer.cache()
                    lastNote = "cached ${u.lastPathSegment}"
                } catch (_: Exception) {
                    // a failed piece is simply fetched by the player when its turn comes
                    return@submit
                }
            }
        }
    }

    fun stop() { cancelled = true; job?.cancel(true) }
}

/** Tells when the connection came back or changed, so a stalled stream can be nudged at once instead of waiting out a timeout. */
class NetworkWatch(ctx: Context, private val onUp: () -> Unit) {
    private val cm = ctx.getSystemService(Context.CONNECTIVITY_SERVICE) as ConnectivityManager
    private val cb = object : ConnectivityManager.NetworkCallback() {
        override fun onAvailable(network: Network) { onUp() }
        override fun onCapabilitiesChanged(network: Network, caps: android.net.NetworkCapabilities) {}
    }
    init { try { cm.registerDefaultNetworkCallback(cb) } catch (_: Exception) { } }
    val metered: Boolean get() = try { cm.isActiveNetworkMetered } catch (_: Exception) { true }
    fun stop() { try { cm.unregisterNetworkCallback(cb) } catch (_: Exception) { } }
}
