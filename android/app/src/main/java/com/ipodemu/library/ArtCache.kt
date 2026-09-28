package com.ipodemu.library

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.os.Handler
import android.os.Looper
import android.util.LruCache
import java.io.File
import java.util.concurrent.Executors

/**
 * Cover-art store. Thumbnails are 320px JPEGs on disk; [get] never blocks: it returns a cached bitmap or null
 * and decodes in the background, then calls [onLoaded] so the UI redraws.
 */
class ArtCache(private val dir: File) {
    private val mem = object : LruCache<String, Bitmap>(24 * 1024 * 1024) {
        override fun sizeOf(k: String, v: Bitmap) = v.byteCount
    }
    private val missing = HashSet<String>()
    private val pending = HashSet<String>()
    private val exec = Executors.newSingleThreadExecutor()
    private val main = Handler(Looper.getMainLooper())
    private var notifyQueued = false

    var onLoaded: (() -> Unit)? = null

    init { dir.mkdirs() }

    fun file(key: String) = File(dir, "$key.jpg")
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
            val f = file(key)
            val b = try {
                if (f.exists()) BitmapFactory.decodeFile(f.path, BitmapFactory.Options().apply { if (thumb) inSampleSize = 3 }) else null
            } catch (_: Exception) { null }
            synchronized(this) {
                pending.remove(ck)
                if (b == null) missing.add(ck) else mem.put(ck, b)
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

    /** Loads (or returns the cached) bitmap for Compose callers; decoding runs on IO. */
    suspend fun load(key: String?, thumb: Boolean = false): Bitmap? {
        if (key == null) return null
        val ck = if (thumb) "t$key" else key
        synchronized(this) { mem.get(ck)?.let { return it } }
        return kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) {
            val f = file(key)
            val b = try {
                if (f.exists()) BitmapFactory.decodeFile(f.path, BitmapFactory.Options().apply { if (thumb) inSampleSize = 3 }) else null
            } catch (_: Exception) { null }
            if (b != null) synchronized(this@ArtCache) { mem.put(ck, b) }
            b
        }
    }

    @Synchronized fun forgetMisses() = missing.clear()

    companion object { const val MAX_PX = 320 }
}
