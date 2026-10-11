package com.ipodemu.library

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.os.Handler
import android.os.Looper
import android.util.LruCache
import java.io.File
import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.asCoroutineDispatcher
import kotlinx.coroutines.withContext
import java.util.concurrent.Executors
import java.util.concurrent.ThreadFactory

/**
 * Cover-art store. Thumbnails are 320px JPEGs on disk; [get] never blocks: it returns a cached bitmap or null
 * and decodes in the background, then calls [onLoaded] so the UI redraws.
 */
class ArtCache(private val dir: File) {
    private val mem = object : LruCache<String, Bitmap>(maxOf(24 * 1024 * 1024L, Runtime.getRuntime().maxMemory() / 6).toInt()) {
        override fun sizeOf(k: String, v: Bitmap) = v.byteCount
    }
    private val missing = HashSet<String>()
    private val pending = HashSet<String>()
    /** Decode and download threads run at background priority: on a low-power SoC they must never compete with the UI and render threads. */
    private val bgThreads = ThreadFactory { r ->
        Thread({ try { android.os.Process.setThreadPriority(android.os.Process.THREAD_PRIORITY_BACKGROUND) } catch (_: Exception) {}; r.run() }, "art").apply { isDaemon = true }
    }
    private val exec = Executors.newFixedThreadPool(4, bgThreads)
    /** Bounded decoder for Compose rows: a fast fling used to start up to 64 JPEG decodes at once (Dispatchers.IO). Two threads; a row that scrolled away
     * is cancelled before it ever runs. */
    private val decoder: CoroutineDispatcher = Executors.newFixedThreadPool(2, bgThreads).asCoroutineDispatcher()
    /** inSampleSize for row thumbnails (320 px / n). 4 gives 80 px, still sharper than a 44 dp row at 240 dpi (66 px); set by the app from the screen density. */
    @Volatile var thumbSample = 3
    private val main = Handler(Looper.getMainLooper())
    private var notifyQueued = false

    var onLoaded: (() -> Unit)? = null

    /** Downloads cover bytes for a key that has no file yet (Jellyfin/Plex/downloads); null = none. Runs on a background thread. */
    @Volatile var fetcher: ((String) -> ByteArray?)? = null
    /** When each key last failed: retried after a few minutes, since a failure is often a timeout, not a missing cover. */
    private val fetchFailed = HashMap<String, Long>()
    /** A search lists dozens of rows at once; unlimited parallel downloads timed most of them out over the internet. */
    private val fetchSlots = java.util.concurrent.Semaphore(4)

    private fun ensure(key: String): File {
        val f = file(key)
        // a Jellyfin cover can be replaced on the server: a saved copy is trusted for three days, then fetched again
        if (f.exists() && key.startsWith("jf") && fetcher != null && System.currentTimeMillis() - f.lastModified() > 3L * 86_400_000L) f.delete()
        if (f.exists()) return f
        val fx = fetcher ?: return f
        synchronized(this) { fetchFailed[key]?.let { if (System.currentTimeMillis() - it < 3 * 60_000) return f } }
        fetchSlots.acquire()
        try {
            if (f.exists()) return f // another request fetched it while this one waited
            val bytes = try { fx(key) } catch (_: Exception) { null }
            if (bytes != null) try { save(key, bytes) } catch (_: Exception) {}
        } finally { fetchSlots.release() }
        synchronized(this) { if (!f.exists()) fetchFailed[key] = System.currentTimeMillis() else fetchFailed.remove(key) }
        return f
    }

    init { dir.mkdirs() }

    // long keys (NAS folder covers carry their folder path) are hashed: file names stop at 255 characters
    fun file(key: String) = File(dir, if (key.length <= 120) "$key.jpg" else "h" + java.security.MessageDigest.getInstance("SHA-1").digest(key.toByteArray()).joinToString("") { "%02x".format(it) } + ".jpg")
    fun has(key: String) = file(key).exists()

    fun save(key: String, bytes: ByteArray) {
        if (has(key)) return
        val opts = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeByteArray(bytes, 0, bytes.size, opts)
        var sample = 1
        while (opts.outWidth / (sample * 2) >= MAX_PX) sample *= 2
        val bmp = BitmapFactory.decodeByteArray(bytes, 0, bytes.size, BitmapFactory.Options().apply { inSampleSize = sample })
            ?: return
        val scaled = if (bmp.width > MAX_PX) Bitmap.createScaledBitmap(bmp, MAX_PX, MAX_PX * bmp.height / bmp.width, true) else bmp
        file(key).outputStream().use { scaled.compress(Bitmap.CompressFormat.JPEG, 88, it) }
    }

    /** [thumb] = small (~110px) version for list rows; otherwise the full 320px image. */
    fun get(key: String?, thumb: Boolean = false): Bitmap? {
        if (key == null) return null
        val ck = if (thumb) "t$key" else key
        synchronized(this) {
            mem.get(ck)?.let { return it }
            if (ck in missing || ck in pending) return null
            pending.add(ck)
        }
        exec.execute {
            val f = ensure(key)
            val b = try {
                if (f.exists()) BitmapFactory.decodeFile(f.path, BitmapFactory.Options().apply { if (thumb) inSampleSize = thumbSample }) else null
            } catch (_: Exception) { null }
            synchronized(this) {
                pending.remove(ck)
                // a remote cover that failed may be retried later (see ensure); only a local miss is final
                if (b == null) { if (fetcher == null || has(key)) missing.add(ck) } else { mem.put(ck, b) }
                Unit
            }
            if (b != null) main.post { onLoaded?.invoke() }
        }
        return null
    }

    /** Cached bitmap if already in memory (never loads). */
    fun peek(key: String?, thumb: Boolean = false): Bitmap? {
        if (key == null) return null
        return synchronized(this) { mem.get(if (thumb) "t$key" else key) }
    }

    /** Loads (or returns the cached) bitmap for Compose callers. The download (remote covers only) runs on IO, the decode on the bounded [decoder]. */
    suspend fun load(key: String?, thumb: Boolean = false): Bitmap? {
        if (key == null) return null
        val ck = if (thumb) "t$key" else key
        synchronized(this) { mem.get(ck)?.let { return it }; if (ck in missing) return null }
        if (fetcher != null) withContext(Dispatchers.IO) { ensure(key) }
        return withContext(decoder) {
            // many rows share one album cover: the second one queued finds the first one's bitmap here instead of decoding it again
            synchronized(this@ArtCache) { mem.get(ck) }?.let { return@withContext it }
            val f = file(key)
            val b = try {
                if (f.exists()) BitmapFactory.decodeFile(f.path, BitmapFactory.Options().apply { if (thumb) inSampleSize = thumbSample }) else null
            } catch (_: Exception) { null }
            synchronized(this@ArtCache) {
                // only a local miss is final (a remote cover that failed is retried, see ensure)
                if (b != null) mem.put(ck, b) else if (fetcher == null) missing.add(ck)
                Unit
            }
            b
        }
    }

    /** Downloads the cover file if missing (background), then calls [done] on the main thread when it exists. */
    fun prefetch(key: String?, done: (() -> Unit)? = null) {
        if (key == null) return
        if (has(key)) { done?.let { main.post(it) }; return }
        exec.execute { if (ensure(key).exists()) done?.let { main.post(it) } }
    }

    @Synchronized fun forgetMisses() { missing.clear(); fetchFailed.clear() }

    companion object { const val MAX_PX = 320 }
}
