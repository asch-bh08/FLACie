package com.ipodemu.library

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.os.Handler
import android.os.Looper
import android.os.StatFs
import android.util.LruCache
import java.io.File
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.asCoroutineDispatcher
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withContext
import java.util.concurrent.Executors
import java.util.concurrent.ThreadFactory

/**
 * Cover-art store.
 *
 * Jellyfin covers: ONE fixed size per use, requested once and kept (see [CoverIndex]): 240 px WebP for list rows and cards, and a 960 px
 * picture only for Now Playing (only the last [FULL_KEEP] are kept). They are saved exactly as the server sent them (no re-encoding),
 * never requested again unless the picture's tag changed, never capped by size: they are deleted, least recently used first, only when
 * the device runs low on free storage ([CacheEvictor]). Requests go through the Jellyfin gate with the priority of whoever asked and are
 * cancelled when the asker leaves the screen.
 *
 * Other covers (local files, NAS, Plex, lookups) are 320 px JPEGs as before. [get] never blocks: it returns a cached bitmap or null
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
    /** inSampleSize for the 320 px row thumbnails of non-Jellyfin covers; set by the app from the screen density. */
    @Volatile var thumbSample = 3
    private val main = Handler(Looper.getMainLooper())
    private var notifyQueued = false
    private val ioScope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    var onLoaded: (() -> Unit)? = null

    /** Downloads cover bytes for a key that has no file yet (Plex/downloads/NAS); null = none. Runs on a background thread. */
    @Volatile var fetcher: ((String) -> ByteArray?)? = null
    /** Jellyfin covers: (item id, full size?, image tag, etag of the saved copy, priority) -> the server's answer, or null when no server is set up. */
    @Volatile var jfFetch: (suspend (String, Boolean, String?, String?, Priority) -> GateResponse?)? = null
    /** When each key last failed: retried after a few minutes, since a failure is often a timeout, not a missing cover. */
    private val fetchFailed = HashMap<String, Long>()
    /** A search lists dozens of rows at once; unlimited parallel downloads timed most of them out over the internet. */
    private val fetchSlots = java.util.concurrent.Semaphore(4)

    // ---- Jellyfin covers ----------------------------------------------------------------------------------------

    private val jfDir = File(dir, "jf")
    val index = CoverIndex()
    private val indexFile = File(jfDir, "index.json")
    @Volatile var storage: StorageInfo = object : StorageInfo {
        override fun freeBytes() = try { StatFs(dir.path).availableBytes } catch (_: Exception) { Long.MAX_VALUE }
        override fun totalBytes() = try { StatFs(dir.path).totalBytes } catch (_: Exception) { 0L }
    }
    @Volatile private var savesSinceCheck = 0
    @Volatile private var indexLoaded = false
    /** inSampleSize for the 240 px row covers on screens that show them small (44-56 dp rows: 120 px is sharp even at 3x density). */
    @Volatile var jfThumbSample = 2

    private fun isJf(key: String) = key.startsWith("jf") && key.length > 2
    private fun jfId(key: String) = key.substring(2)
    private fun fullFile(key: String) = File(jfDir, jfId(key) + ".f")

    /** Called once at startup, off the main thread: loads what is known about saved covers, drops the old-format copies, checks free space. */
    fun start() {
        ioScope.launch {
            loadIndex()
            try { dir.listFiles { f -> f.isFile && f.name.startsWith("jf") }?.forEach { it.delete() } } catch (_: Exception) {}
            checkSpace()
        }
    }

    private fun loadIndex() {
        if (indexLoaded) return
        synchronized(index) {
            if (indexLoaded) return
            try { if (indexFile.exists()) index.load(indexFile.readText()) } catch (_: Exception) {}
            indexLoaded = true
        }
    }

    private var indexSaveQueued = false
    private fun saveIndexSoon() {
        synchronized(this) { if (indexSaveQueued) return; indexSaveQueued = true }
        ioScope.launch {
            kotlinx.coroutines.delay(8_000)
            synchronized(this@ArtCache) { indexSaveQueued = false }
            try { jfDir.mkdirs(); val tmp = File(indexFile.path + ".tmp"); tmp.writeText(index.toJson()); indexFile.delete(); tmp.renameTo(indexFile) } catch (_: Exception) {}
        }
    }

    /** The listing told us these covers' current tags: a saved picture with another tag is stale and is fetched again when it is next shown. */
    fun noteTags(tags: Map<String, String>) {
        if (tags.isEmpty()) return
        loadIndex()
        val stale = index.noteWanted(tags)
        if (stale.isNotEmpty()) synchronized(this) { for (k in stale) { mem.remove(k); mem.remove("t$k"); missing.remove(k) } }
        saveIndexSoon()
    }

    private fun touch(f: File) { val n = System.currentTimeMillis(); if (n - f.lastModified() > 6 * 3600_000L) try { f.setLastModified(n) } catch (_: Exception) {} }

    private suspend fun ensureJf(key: String, full: Boolean, p: Priority): File? {
        loadIndex()
        val f = if (full) fullFile(key) else file(key)
        val ik = if (full) "F$key" else key
        val exists = f.exists()
        if (!index.needsFetch(ik, exists)) { touch(f); return f }
        val fx = jfFetch ?: return f.takeIf { exists }
        synchronized(this) { fetchFailed[ik]?.let { if (System.currentTimeMillis() - it < 3 * 60_000) return f.takeIf { exists } } }
        val e = index.entry(ik)
        val r = try { fx(jfId(key), full, e?.want, if (exists) e?.etag else null, p) }
        catch (c: CancellationException) { throw c }
        catch (_: Exception) { null }
        if (r == null) { synchronized(this) { fetchFailed[ik] = System.currentTimeMillis() }; return f.takeIf { exists } }
        if (r.code == 304 && exists) {
            index.saved(ik, e?.want, e?.etag); saveIndexSoon(); touch(f)
            return f
        }
        if (r.code in 200..299 && r.body.isNotEmpty()) {
            withContext(Dispatchers.IO) {
                jfDir.mkdirs()
                val tmp = File(f.path + ".tmp"); tmp.writeBytes(r.body); f.delete(); tmp.renameTo(f)
            }
            index.saved(ik, e?.want, r.headers["etag"]); saveIndexSoon()
            synchronized(this) { fetchFailed.remove(ik); mem.remove(ik); mem.remove("t$key") }
            if (full) trimFull() else if (++savesSinceCheck >= 40) { savesSinceCheck = 0; withContext(Dispatchers.IO) { checkSpace() } }
            return f
        }
        synchronized(this) { fetchFailed[ik] = System.currentTimeMillis() }
        return f.takeIf { exists }
    }

    /** Only the last few Now Playing pictures are kept. */
    private fun trimFull() {
        try {
            val fulls = jfDir.listFiles { x -> x.name.endsWith(".f") }?.sortedByDescending { it.lastModified() } ?: return
            for (x in fulls.drop(FULL_KEEP)) { x.delete(); index.forgetFile("Fjf" + x.name.removeSuffix(".f")); synchronized(this) { mem.remove("Fjf" + x.name.removeSuffix(".f")) } }
        } catch (_: Exception) {}
    }

    /** Deletes the least recently used covers when the device is low on storage; nothing happens while there is room. */
    fun checkSpace(): Int {
        val files = allFiles()
        val gone = CacheEvictor.evict(files, storage)
        for (g in gone) forgetByName(g.name)
        if (gone.isNotEmpty()) { android.util.Log.d("FLACieCache", "low storage: evicted ${gone.size} covers"); saveIndexSoon() }
        return gone.size
    }

    private fun allFiles(): List<File> {
        val out = ArrayList<File>()
        dir.listFiles()?.forEach { if (it.isFile) out += it }
        jfDir.listFiles()?.forEach { if (it.isFile && it.name != "index.json" && !it.name.endsWith(".tmp")) out += it }
        return out
    }

    private fun forgetByName(name: String) {
        val key = when { name.endsWith(".r") -> "jf" + name.removeSuffix(".r"); name.endsWith(".f") -> "Fjf" + name.removeSuffix(".f"); else -> name.removeSuffix(".jpg") }
        index.forgetFile(key)
        synchronized(this) { mem.remove(key); mem.remove("t$key") }
    }

    /** Bytes the covers use on disk. */
    fun sizeBytes(): Long = allFiles().sumOf { it.length() } + (if (indexFile.exists()) indexFile.length() else 0L)

    /** Settings > Clear cache: every saved cover goes; they are fetched again as needed. */
    fun clearAll() {
        try { dir.listFiles()?.forEach { if (it.isFile) it.delete() }; jfDir.listFiles()?.forEach { it.delete() } } catch (_: Exception) {}
        index.clear()
        synchronized(this) { mem.evictAll(); missing.clear(); fetchFailed.clear(); pending.clear() }
    }

    // ---- other covers -------------------------------------------------------------------------------------------

    private fun ensure(key: String): File {
        if (isJf(key)) return runBlocking { ensureJf(key, false, Priority.VISIBLE) } ?: file(key)
        val f = file(key)
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

    init { dir.mkdirs(); jfDir.mkdirs() }

    // long keys (NAS folder covers carry their folder path) are hashed: file names stop at 255 characters
    fun file(key: String) = if (isJf(key)) File(jfDir, jfId(key) + ".r")
        else File(dir, if (key.length <= 120) "$key.jpg" else "h" + java.security.MessageDigest.getInstance("SHA-1").digest(key.toByteArray()).joinToString("") { "%02x".format(it) } + ".jpg")
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

    private fun memKey(key: String, thumb: Boolean, full: Boolean) = if (full) "F$key" else if (thumb) "t$key" else key
    private fun sampleFor(key: String, thumb: Boolean, full: Boolean) = if (full) 1 else if (!thumb) 1 else if (isJf(key)) jfThumbSample else thumbSample

    /** [thumb] = small version for list rows; otherwise the full-size image ([full] = the Now Playing size, Jellyfin covers only). */
    fun get(key: String?, thumb: Boolean = false): Bitmap? {
        if (key == null) return null
        val ck = memKey(key, thumb, false)
        synchronized(this) {
            mem.get(ck)?.let { return it }
            if (ck in missing || ck in pending) return null
            pending.add(ck)
        }
        exec.execute {
            val f = ensure(key)
            val b = try {
                if (f.exists()) BitmapFactory.decodeFile(f.path, BitmapFactory.Options().apply { inSampleSize = sampleFor(key, thumb, false) }) else null
            } catch (_: Exception) { null }
            synchronized(this) {
                pending.remove(ck)
                // a remote cover that failed may be retried later (see ensure); only a local miss is final
                if (b == null) { if ((fetcher == null && !isJf(key)) || has(key)) missing.add(ck) } else { mem.put(ck, b) }
                Unit
            }
            if (b != null) main.post { onLoaded?.invoke() }
        }
        return null
    }

    /** Cached bitmap if already in memory (never loads). */
    fun peek(key: String?, thumb: Boolean = false, full: Boolean = false): Bitmap? {
        if (key == null) return null
        return synchronized(this) { mem.get(memKey(key, thumb, full)) }
    }

    /**
     * Loads (or returns the cached) bitmap for Compose callers. A missing Jellyfin cover is requested through the gate with [priority]
     * (rows on screen: Visible); if the caller is cancelled (the row scrolled away) the request is cancelled with it. The decode runs on
     * the bounded [decoder].
     */
    suspend fun load(key: String?, thumb: Boolean = false, full: Boolean = false, priority: Priority = Priority.VISIBLE): Bitmap? {
        if (key == null) return null
        val ck = memKey(key, thumb, full)
        synchronized(this) { mem.get(ck)?.let { return it }; if (ck in missing) return null }
        val jf = isJf(key)
        val f: File = if (jf) ensureJf(key, full, priority) ?: return null
            else { if (fetcher != null) withContext(Dispatchers.IO) { ensure(key) }; file(key) }
        return withContext(decoder) {
            // many rows share one album cover: the second one queued finds the first one's bitmap here instead of decoding it again
            synchronized(this@ArtCache) { mem.get(ck) }?.let { return@withContext it }
            val b = try {
                if (f.exists()) BitmapFactory.decodeFile(f.path, BitmapFactory.Options().apply { inSampleSize = sampleFor(key, thumb, full) }) else null
            } catch (_: Exception) { null }
            synchronized(this@ArtCache) {
                // only a local miss is final (a remote cover that failed is retried, see ensure)
                if (b != null) mem.put(ck, b) else if (fetcher == null && !jf) missing.add(ck)
                Unit
            }
            b
        }
    }

    /** Downloads the cover file if missing (background priority), then calls [done] on the main thread when it exists. */
    fun prefetch(key: String?, done: (() -> Unit)? = null) {
        if (key == null) return
        if (has(key) && !(isJf(key) && index.needsFetch(key, true))) { done?.let { main.post(it) }; return }
        if (isJf(key)) { ioScope.launch { try { if (ensureJf(key, false, Priority.BACKGROUND)?.exists() == true) done?.let { main.post(it) } } catch (_: Exception) {} }; return }
        exec.execute { if (ensure(key).exists()) done?.let { main.post(it) } }
    }

    /** Fetches the Now Playing size of this song's cover (cheap when already saved) so the big cover is sharp the moment it is shown. */
    fun prefetchFull(key: String?) {
        if (key == null || !isJf(key)) return
        ioScope.launch { try { ensureJf(key, true, Priority.PLAYING) } catch (_: Exception) {} }
    }

    @Synchronized fun forgetMisses() { missing.clear(); fetchFailed.clear() }

    companion object { const val MAX_PX = 320; const val FULL_KEEP = 30 }
}
