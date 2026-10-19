package com.ipodemu.player

import android.content.Context
import android.media.MediaCodec
import android.media.MediaExtractor
import android.media.MediaFormat
import android.net.ConnectivityManager
import android.net.Uri
import com.ipodemu.App
import com.ipodemu.library.Track
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import java.io.File
import kotlin.coroutines.coroutineContext

/**
 * Works out the song's own loudness shape for the Living Sleeve seek bar, on the phone (the web gets it from its server; the app has no such server).
 * The song is decoded once with MediaExtractor and MediaCodec, in the background and one song at a time, and the 200 loudness numbers are kept in
 * cacheDir/waves, so the next time it is a 200-byte read. Until it is ready the bar shows level bars. A song that arrives over mobile data is not
 * downloaded a second time just for its picture, and nothing here ever runs on the main thread.
 */
object WaveAnalyzer {
    private val one = Mutex()
    private const val LONGEST_STREAMED_MS = 20 * 60_000L

    private fun dir(ctx: Context) = File(ctx.cacheDir, "waves")

    /** The saved shape, else decoded now (null when it cannot or should not be: no length, an unreadable source, mobile data, a cancelled screen). */
    suspend fun shapeFor(app: App, t: Track): IntArray? {
        val saved = withContext(Dispatchers.IO) { WaveCache.read(dir(app), t.path) }
        if (saved != null) return saved
        if (t.durationMs <= 0) return null
        return one.withLock {
            // another screen may have made it while this one waited its turn
            withContext(Dispatchers.IO) { WaveCache.read(dir(app), t.path) } ?: withContext(Dispatchers.Default) {
                val shape = try { decode(app, t) } catch (e: kotlinx.coroutines.CancellationException) { throw e } catch (_: Throwable) { null }
                if (shape != null) WaveCache.write(dir(app), t.path, shape)
                shape
            }
        }
    }

    private fun metered(ctx: Context): Boolean = try { (ctx.getSystemService(Context.CONNECTIVITY_SERVICE) as ConnectivityManager).isActiveNetworkMetered } catch (_: Exception) { true }

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
                    ex.setDataSource(app, Uri.parse(p), null)
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
                            val out = codec.getOutputBuffer(o)!!.apply { position(info.offset); limit(info.offset + info.size) }.asShortBuffer()
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
