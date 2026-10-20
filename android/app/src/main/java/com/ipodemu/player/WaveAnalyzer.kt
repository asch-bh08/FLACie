package com.ipodemu.player

import android.content.Context
import android.media.MediaCodec
import android.media.MediaExtractor
import android.media.MediaFormat
import android.net.ConnectivityManager
import android.net.Uri
import com.ipodemu.App
import com.ipodemu.Prefs
import com.ipodemu.library.Track
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.channelFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import java.io.File
import java.net.HttpURLConnection
import java.net.URL
import java.nio.ByteOrder
import kotlin.coroutines.coroutineContext

/**
 * Works out the song's own loudness shape for the frame seek bar round the cover, the same 200 numbers the web player draws.
 * In order: the copy kept on this phone (cacheDir/waves, 200 bytes a song); the FLACie server's copy (GET /api/wave, when the phone is signed in to one),
 * which the web made for everyone; else the song is decoded once here with MediaExtractor and MediaCodec, in the background and one song at a time, and
 * the result is kept and sent to the server so the web can use it too. Until it is ready the bars are level. A song that arrives over mobile data is not
 * downloaded a second time just for its picture, and nothing here ever runs on the main thread.
 */
object WaveAnalyzer {
    private val one = Mutex()
    private const val LONGEST_STREAMED_MS = 20 * 60_000L

    private fun dir(ctx: Context) = File(ctx.cacheDir, "waves")

    /** The saved shape, else the server's, else decoded now (null when it cannot or should not be: no length, an unreadable source, mobile data, a cancelled screen). */
    suspend fun shapeFor(app: App, t: Track): IntArray? {
        val saved = withContext(Dispatchers.IO) { WaveCache.read(dir(app), t.path) }
        if (saved != null) return saved
        return one.withLock {
            // another screen (or the prefetch) may have made it while this one waited its turn
            withContext(Dispatchers.IO) {
                WaveCache.read(dir(app), t.path) ?: fromServer(app.prefs, t.path)?.let { (shape, rough) ->
                    // a rough shape (the server guessed it from the FLAC frames, X-Wave: rough) is shown but not kept, so a measured one replaces it later
                    if (!rough) WaveCache.write(dir(app), t.path, shape)
                    shape
                }
            }
                ?: if (t.durationMs <= 0 && !isLocal(app, t)) null else withContext(Dispatchers.Default) {
                    val shape = try { decode(app, t) } catch (e: kotlinx.coroutines.CancellationException) { throw e } catch (_: Throwable) { null }
                    if (shape != null && shape.any { it > 0 }) {
                        WaveCache.write(dir(app), t.path, shape)
                        withContext(Dispatchers.IO) { toServer(app.prefs, t.path, shape) }
                        shape
                    } else null
                }
        }
    }

    /**
     * The song's shape: a saved one at once; else the frame-size shape (FlacWave) as soon as the player's cache holds the whole file (no download of its
     * own); and the exact one (server or decode) replaces it when it comes. Never a half-known shape: the frame is a still picture of the song.
     * A phone streams straight from Jellyfin, so the server's /api/wave is only an upgrade, never something to wait for.
     */
    fun shapes(app: App, t: Track): Flow<IntArray> = channelFlow {
        val dir = dir(app)
        val exact = withContext(Dispatchers.IO) { WaveCache.read(dir, t.path) }
        if (exact != null) { send(exact); return@channelFlow }
        val rough = withContext(Dispatchers.IO) { WaveCache.read(dir, t.path, rough = true) }
        // the frame is a still picture of the song (the owner does not want it to move like a VU meter): a shape is only shown once it is complete for
        // the whole song, and at most replaced once by the exact one. Until then the frame rests level.
        val lock = Mutex()
        var exactDone = false
        if (rough != null) send(rough)
        val bytes = if (rough != null) null else launch(Dispatchers.IO) {
            try {
                fromBytes(app, t) { s, whole ->
                    if (whole) { WaveCache.write(dir, t.path, s, rough = true); lock.withLock { if (!exactDone) send(s) } }
                }
            } catch (e: kotlinx.coroutines.CancellationException) { throw e } catch (_: Throwable) { }
        }
        launch { shapeFor(app, t)?.let { s -> bytes?.cancel(); lock.withLock { exactDone = true; send(s) } } }
    }


    /**
     * Measures the playing song's shape from the sound as it is heard (the player's LiveAudio tap, which only reads), four times a second on the main
     * thread (the player lives there). A song heard from start to end has its exact shape kept by item id, so its next play shows it at once.
     */
    @androidx.annotation.OptIn(androidx.media3.common.util.UnstableApi::class)
    suspend fun hear(app: App) = withContext(Dispatchers.Main) {
        val live = app.player.live
        live.metering = true
        var path: String? = null
        var slicer: HeardShape? = null
        var saved = false
        var lastAt = android.os.SystemClock.elapsedRealtime()
        try {
            while (true) {
                kotlinx.coroutines.delay(250)
                val now = android.os.SystemClock.elapsedRealtime()
                val (sq, n) = live.drainMeter()
                val t = app.player.current
                val dur = app.player.durationMs
                if (t == null || dur <= 0) { lastAt = now; continue }
                if (t.path != path) {
                    // a new song: its energy until now belonged to the last one's end, so this look only starts the new count
                    path = t.path; slicer = HeardShape(dur); saved = withContext(Dispatchers.IO) { WaveCache.read(dir(app), t.path) } != null; lastAt = now; continue
                }
                val s = slicer ?: continue
                if (saved || !app.player.isPlaying) { lastAt = now; continue }
                s.add(app.player.positionMs, sq, n, now - lastAt)
                lastAt = now
                // measured quietly and kept for the next play only: the frame on screen never changes while the song plays
                if (s.complete) s.shape()?.let { shape -> saved = true; withContext(Dispatchers.IO) { WaveCache.write(dir(app), t.path, shape) } }
            }
        } finally { live.metering = false }
    }

    /** How often the player's cache is looked at again while the song downloads. */
    private const val POLL_MS = 400L
    /** A download that has not grown for this long has stopped (the song was skipped, or the connection went): the shape stays as it is. */
    private const val IDLE_GIVE_UP_MS = 120_000L

    /**
     * The frame-size shape from bytes already on the phone: a song kept offline or a local file is read straight away; a streamed one is read from the
     * player's own cache (StreamSupport.cache, filled by the player and the prefetcher) as it fills, never downloaded again for this.
     */
    @androidx.annotation.OptIn(androidx.media3.common.util.UnstableApi::class)
    private suspend fun fromBytes(app: App, t: Track, emit: suspend (IntArray, Boolean) -> Unit) {
        val b = FlacWave.Builder()
        val chunk = ByteArray(256 * 1024)
        val local = app.offline.localFile(t) ?: t.path.takeIf { !it.contains("://") }?.let { File(it) }?.takeIf { it.isFile }
        if (local != null || t.path.startsWith("content:") || t.path.startsWith("file:")) {
            val input = if (local != null) local.inputStream() else app.contentResolver.openInputStream(Uri.parse(t.path)) ?: return
            input.use { s -> while (true) { coroutineContext.ensureActive(); val n = s.read(chunk); if (n < 0) break; b.feed(chunk, 0, n); if (b.notFlac) return } }
            b.finish()?.let { emit(it, true) }
            return
        }
        val uri = Uri.parse(t.path)
        if (!com.ipodemu.playback.StreamSupport.cacheable(uri)) return
        val cache = com.ipodemu.playback.StreamSupport.cache(app)
        val key = uri.toString()
        var fed = 0L
        var lastGrowth = System.currentTimeMillis()
        while (true) {
            coroutineContext.ensureActive()
            val got = readCached(cache, key, fed) { buf, n -> b.feed(buf, 0, n) }
            if (b.notFlac) return
            fed += got
            val length = androidx.media3.datasource.cache.ContentMetadata.getContentLength(cache.getContentMetadata(key))
            if (length > 0 && fed >= length) { b.finish(length)?.let { emit(it, true) }; return }
            if (got > 0) {
                lastGrowth = System.currentTimeMillis()
            } else if (System.currentTimeMillis() - lastGrowth > IDLE_GIVE_UP_MS) return
            kotlinx.coroutines.delay(POLL_MS)
        }
    }

    /**
     * Hands [sink] the cached bytes of [key] that follow on from [from] without a gap, straight from the cache's own files (a committed piece is
     * complete; the piece being written is not visible until it is), and returns how many there were.
     */
    @androidx.annotation.OptIn(androidx.media3.common.util.UnstableApi::class)
    private fun readCached(cache: androidx.media3.datasource.cache.Cache, key: String, from: Long, sink: (ByteArray, Int) -> Unit): Long {
        var at = from
        val buf = ByteArray(256 * 1024)
        for (span in cache.getCachedSpans(key)) {
            if (span.position + span.length <= at) continue
            if (span.position > at || !span.isCached) break
            val f = span.file ?: break
            try {
                java.io.RandomAccessFile(f, "r").use { r ->
                    r.seek(at - span.position)
                    while (true) { val n = r.read(buf); if (n <= 0) break; sink(buf, n); at += n }
                }
            } catch (_: Exception) { break }   // the piece was evicted or replaced meanwhile: the next look starts here
        }
        return at - from
    }

    /**
     * While a song plays the next ones' shapes are made in the background, so each is drawn the moment it starts (the web does the same): from the bytes
     * the prefetcher or the player has already cached (nothing is downloaded for this on mobile data), and, on Wi-Fi only, the exact decode as before.
     */
    suspend fun prefetch(app: App, tracks: List<Track>) {
        val dir = dir(app)
        for (t in tracks) {
            coroutineContext.ensureActive()
            if (withContext(Dispatchers.IO) { WaveCache.read(dir, t.path) ?: WaveCache.read(dir, t.path, rough = true) } != null) continue
            withContext(Dispatchers.IO) { cachedWhole(app, t)?.let { WaveCache.write(dir, t.path, it, rough = true) } }
        }
        if (metered(app)) return
        for (t in tracks) { coroutineContext.ensureActive(); shapeFor(app, t) }
    }

    /** The frame-size shape of a song whose bytes are all in the player's cache already, else null (without waiting for them). */
    @androidx.annotation.OptIn(androidx.media3.common.util.UnstableApi::class)
    private fun cachedWhole(app: App, t: Track): IntArray? {
        val uri = Uri.parse(t.path)
        if (!com.ipodemu.playback.StreamSupport.cacheable(uri)) return null
        val cache = com.ipodemu.playback.StreamSupport.cache(app)
        val key = uri.toString()
        val length = androidx.media3.datasource.cache.ContentMetadata.getContentLength(cache.getContentMetadata(key))
        if (length <= 0 || cache.getCachedLength(key, 0, length) < length) return null
        val b = FlacWave.Builder()
        readCached(cache, key, 0) { buf, n -> b.feed(buf, 0, n) }
        return b.finish(length)
    }

    private fun isLocal(app: App, t: Track) = app.offline.localFile(t) != null || !(t.path.startsWith("http:") || t.path.startsWith("https:"))

    private fun metered(ctx: Context): Boolean = try { (ctx.getSystemService(Context.CONNECTIVITY_SERVICE) as ConnectivityManager).isActiveNetworkMetered } catch (_: Exception) { true }

    private fun serverOn(prefs: Prefs) = prefs.flacieWebUrl.isNotBlank() && prefs.hasJellyfinAccount

    private fun waveUrl(prefs: Prefs, path: String) = URL(prefs.flacieWebUrl.trimEnd('/') + "/api/wave?p=" + java.net.URLEncoder.encode(path, "UTF-8"))

    private fun HttpURLConnection.signIn(prefs: Prefs) {
        connectTimeout = 5_000; readTimeout = 8_000
        setRequestProperty("X-Emby-Token", prefs.accountToken)
        setRequestProperty("X-Jellyfin-Server", prefs.accountServer.trimEnd('/'))
    }

    /** The server's shape for this song and whether it is only a rough one, or null (not signed in, not known there yet, or no answer). */
    private fun fromServer(prefs: Prefs, path: String): Pair<IntArray, Boolean>? {
        if (!serverOn(prefs)) return null
        return try {
            val c = waveUrl(prefs, path).openConnection() as HttpURLConnection
            c.signIn(prefs)
            try {
                if (c.responseCode != 200) null
                else WaveCache.parseJson(c.inputStream.bufferedReader().use { it.readText() })?.let { it to c.getHeaderField("X-Wave").equals("rough", true) }
            } finally { c.disconnect() }
        } catch (_: Exception) { null }
    }

    /** Hands a shape worked out here to the server, which keeps the first one it is sent for everyone. Failures are quiet: the phone has its own copy. */
    private fun toServer(prefs: Prefs, path: String, shape: IntArray) {
        if (!serverOn(prefs) || shape.size != WaveSlicer.SLICES) return
        try {
            val c = waveUrl(prefs, path).openConnection() as HttpURLConnection
            c.signIn(prefs); c.requestMethod = "POST"; c.doOutput = true
            val b = shape.joinToString(",", "[", "]").toByteArray()
            c.setRequestProperty("Content-Type", "application/json"); c.setFixedLengthStreamingMode(b.size)
            try { c.outputStream.use { it.write(b) }; c.responseCode } finally { c.disconnect() }
        } catch (_: Exception) { }
    }

    /**
     * The same sign-in headers the player sends for this address (PlayerController's data source). Without them a Jellyfin, Plex or FLACie stream
     * answers 401, MediaExtractor fails, and every song on a server got level bars.
     */
    internal fun headersFor(prefs: Prefs, uri: String): Map<String, String> {
        val jf = prefs.jellyfinUrl.trimEnd('/'); val plex = prefs.plexUrl.trimEnd('/'); val mover = prefs.fileMoverUrl.trimEnd('/'); val acct = prefs.accountServer.trimEnd('/')
        return when {
            jf.isNotBlank() && uri.startsWith(jf) -> mapOf("Authorization" to com.ipodemu.library.JellyfinAuth.header(prefs.jellyfinApiKey))
            plex.isNotBlank() && uri.startsWith(plex) -> mapOf("X-Plex-Token" to prefs.plexToken)
            mover.isNotBlank() && uri.startsWith(mover) -> mapOf("X-Api-Key" to prefs.fileMoverApiKey)
            prefs.flacieWebUrl.isNotBlank() && uri.startsWith(prefs.flacieWebUrl.trimEnd('/') + "/api/") -> mapOf("X-Emby-Token" to prefs.accountToken, "X-Jellyfin-Server" to acct)
            acct.isNotBlank() && uri.startsWith(acct) && prefs.accountToken.isNotBlank() -> mapOf("X-Emby-Token" to prefs.accountToken)
            else -> emptyMap()
        }
    }

    private suspend fun decode(app: App, t: Track): IntArray? {
        val local = app.offline.localFile(t)
        val p = t.path
        val ex = MediaExtractor()
        var codec: MediaCodec? = null
        try {
            when {
                local != null -> ex.setDataSource(local.path)
                p.startsWith("content:") || p.startsWith("file:") -> ex.setDataSource(app, Uri.parse(p), null)
                p.startsWith("http:") || p.startsWith("https:") -> {
                    // a streamed song is fetched again for this: not on mobile data, and not for very long items
                    if (metered(app) || t.durationMs > LONGEST_STREAMED_MS) return null
                    ex.setDataSource(app, Uri.parse(p), headersFor(app.prefs, p))
                }
                p.startsWith("smb:") -> return null   // a network share: MediaExtractor cannot read it
                else -> ex.setDataSource(p)
            }
            val track = (0 until ex.trackCount).firstOrNull { ex.getTrackFormat(it).getString(MediaFormat.KEY_MIME)?.startsWith("audio/") == true } ?: return null
            ex.selectTrack(track)
            val format = ex.getTrackFormat(track)
            val durationUs = if (format.containsKey(MediaFormat.KEY_DURATION)) format.getLong(MediaFormat.KEY_DURATION) else t.durationMs * 1000
            val slicer = WaveSlicer.orNull(WaveSlicer.SLICES, durationUs) ?: return null
            codec = MediaCodec.createDecoderByType(format.getString(MediaFormat.KEY_MIME) ?: return null)
            codec.configure(format, null, null, 0)
            codec.start()
            val info = MediaCodec.BufferInfo()
            var channels = format.getIntegerOr(MediaFormat.KEY_CHANNEL_COUNT, 2)
            var inputDone = false; var outputDone = false
            var pcm = ShortArray(8192)
            // a decoder that never reaches the end of the song must not keep the phone busy for good
            val giveUpAt = System.nanoTime() + 3 * 60_000_000_000L
            while (!outputDone) {
                coroutineContext.ensureActive()
                if (System.nanoTime() > giveUpAt) return null
                if (!inputDone) {
                    val i = codec.dequeueInputBuffer(5_000)
                    if (i >= 0) {
                        val buf = codec.getInputBuffer(i)!!
                        val n = ex.readSampleData(buf, 0)
                        if (n < 0) { codec.queueInputBuffer(i, 0, 0, 0, MediaCodec.BUFFER_FLAG_END_OF_STREAM); inputDone = true }
                        else { codec.queueInputBuffer(i, 0, n, ex.sampleTime, 0); ex.advance() }
                    }
                }
                val o = codec.dequeueOutputBuffer(info, 5_000)
                when {
                    o == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED -> channels = codec.outputFormat.getIntegerOr(MediaFormat.KEY_CHANNEL_COUNT, channels)
                    o >= 0 -> {
                        if (info.size > 0) {
                            // the decoder writes little-endian PCM, but a ByteBuffer reads big-endian until told otherwise: read the other way round every
                            // sample is noise, and every song came out as the same flat band
                            val out = codec.getOutputBuffer(o)!!.apply { position(info.offset); limit(info.offset + info.size) }.order(ByteOrder.nativeOrder()).asShortBuffer()
                            val n = out.remaining()
                            if (pcm.size < n) pcm = ShortArray(n)
                            out.get(pcm, 0, n)
                            slicer.add(info.presentationTimeUs, pcm, n, channels)
                        }
                        codec.releaseOutputBuffer(o, false)
                        if (info.flags and MediaCodec.BUFFER_FLAG_END_OF_STREAM != 0) outputDone = true
                    }
                }
            }
            return slicer.result()
        } finally {
            try { codec?.stop() } catch (_: Exception) { }
            try { codec?.release() } catch (_: Exception) { }
            ex.release()
        }
    }

    private fun MediaFormat.getIntegerOr(key: String, default: Int) = if (containsKey(key)) getInteger(key) else default
}
