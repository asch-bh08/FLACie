package com.ipodemu.library

import org.json.JSONObject
import java.io.File

/**
 * What the app knows about each saved Jellyfin cover, so a picture is requested once and never again unless the server's picture changed:
 *  - [stored]: the image tag of the picture on disk (the file the server sent), and its ETag;
 *  - [want]: the image tag the server's listing last reported for that item. A tag is a hash of the picture: when it differs from
 *    [stored] the picture was replaced, and only then is it fetched again (with If-None-Match, so an unchanged file costs a 304).
 * Keys are the cover keys ("jf" + item id); the Now Playing size has its own entries (prefix "F").
 */
class CoverIndex {
    /** [missAt] / [missTag]: the server said it has no picture (404) at that time while the listing's tag was [missTag]: not asked again until the tag changes or [MISS_TTL_MS] passes. */
    class Entry(var stored: String?, var want: String?, var etag: String?, var missAt: Long = 0L, var missTag: String? = null)

    private val map = HashMap<String, Entry>()
    @Volatile var dirty = false; private set

    @Synchronized fun entry(key: String): Entry? = map[key]

    /**
     * A file that exists is current unless the server reported another tag for it. A missing file needs a fetch, except when the server
     * already said "no picture" for this very tag a short while ago (an album without art would otherwise be asked for again and again).
     */
    @Synchronized fun needsFetch(key: String, fileExists: Boolean, now: Long = System.currentTimeMillis()): Boolean {
        val e = map[key]
        if (!fileExists) {
            if (e != null && e.missAt > 0 && e.missTag == e.want && now - e.missAt < MISS_TTL_MS) return false
            return true
        }
        e ?: return false
        return e.want != null && e.want != e.stored
    }

    /** The server answered 404: there is no picture for [key] at its current tag. */
    @Synchronized fun noPicture(key: String, now: Long = System.currentTimeMillis()) {
        val e = map.getOrPut(key) { Entry(null, null, null) }
        e.missAt = now; e.missTag = e.want; dirty = true
    }

    /** The server's listing says these covers have these tags. Returns the keys whose saved picture is now out of date. */
    @Synchronized fun noteWanted(tags: Map<String, String>): List<String> {
        val changed = ArrayList<String>()
        for ((k, t) in tags) {
            // the Now Playing size has an entry only once it was fetched (not a second entry for every cover of a 10,000-song library)
            for (key in listOf(k, "F$k")) {
                val e = map[key]
                if (e == null) { if (key == k) { map[key] = Entry(null, t, null); dirty = true } }
                else if (e.want != t) { e.want = t; dirty = true; if (e.stored != null && e.stored != t) changed += key }
            }
        }
        return changed
    }

    /** A fetch finished: the file now holds the picture with this tag (or the server said 304 for it). */
    @Synchronized fun saved(key: String, tag: String?, etag: String?) {
        val e = map.getOrPut(key) { Entry(null, tag, null) }
        e.stored = tag ?: e.want; e.etag = etag ?: e.etag
        e.missAt = 0L; e.missTag = null
        if (tag != null) e.want = e.want ?: tag
        dirty = true
    }

    /** The file was deleted (cache eviction, cleaning): forget what it held. */
    @Synchronized fun forgetFile(key: String) { map[key]?.let { it.stored = null; it.etag = null; dirty = true } }

    @Synchronized fun clear() { map.clear(); dirty = true }
    @Synchronized fun size() = map.size

    @Synchronized fun toJson(): String {
        val o = JSONObject()
        for ((k, e) in map) o.put(k, org.json.JSONArray().put(e.stored ?: "").put(e.want ?: "").put(e.etag ?: "").put(e.missAt).put(e.missTag ?: ""))
        dirty = false
        return o.toString()
    }

    @Synchronized fun load(raw: String) {
        val o = JSONObject(raw)
        for (k in o.keys()) {
            val a = o.getJSONArray(k)
            map[k] = Entry(a.optString(0).ifEmpty { null }, a.optString(1).ifEmpty { null }, a.optString(2).ifEmpty { null }, a.optLong(3, 0L), a.optString(4).ifEmpty { null })
        }
    }
}

/** How long a "the server has no picture" answer is believed (a new picture normally arrives with a new tag in the listing, which ends it at once). */
private const val MISS_TTL_MS = 24L * 3600_000L

/** Cover URLs: one fixed size per use, so the server's own resized-image cache is hit every time. */
object JellyfinCovers {
    const val ROW_PX = 240
    const val ROW_QUALITY = 80
    const val FULL_PX = 960
    const val FULL_QUALITY = 85

    fun url(base: String, itemId: String, full: Boolean, tag: String?, webp: Boolean): String {
        val w = if (full) FULL_PX else ROW_PX; val q = if (full) FULL_QUALITY else ROW_QUALITY
        val t = if (tag.isNullOrEmpty()) "" else "&tag=$tag"
        return "${base.trimEnd('/')}/Items/$itemId/Images/Primary?maxWidth=$w&quality=$q&format=${if (webp) "webp" else "jpg"}$t"
    }
}

/** Free storage of the volume the cache lives on (a StatFs on the device, a fake in tests). */
interface StorageInfo {
    fun freeBytes(): Long
    fun totalBytes(): Long
}

/**
 * No fixed cap on the covers: everything stays while there is room. Only when the device's free storage is low (under 10 % or 1.5 GB)
 * are the least recently used covers deleted, oldest first, until there is room again.
 */
object CacheEvictor {
    const val MIN_FREE_BYTES = 1_500_000_000L
    const val MIN_FREE_FRACTION = 0.10

    fun needed(info: StorageInfo): Long {
        val target = maxOf((info.totalBytes() * MIN_FREE_FRACTION).toLong(), MIN_FREE_BYTES)
        return (target - info.freeBytes()).coerceAtLeast(0)
    }

    /** Files touched in the last half hour are never deleted: on a device that stays short of space the pictures being used right now must survive. */
    const val PROTECT_MS = 30 * 60_000L

    /**
     * Deletes the least recently used [files] (oldest first) until the shortfall (plus a 5 % margin of the target) is covered, and only while
     * the device is short of space. Returns the deleted files.
     */
    fun evict(files: List<File>, info: StorageInfo, now: Long = System.currentTimeMillis()): List<File> {
        val short = needed(info)
        if (short <= 0) return emptyList()
        val goal = short + (maxOf((info.totalBytes() * MIN_FREE_FRACTION).toLong(), MIN_FREE_BYTES) / 20)
        var freed = 0L
        val out = ArrayList<File>()
        for (f in files.sortedBy { it.lastModified() }) {
            if (freed >= goal) break
            if (now - f.lastModified() < PROTECT_MS) break   // sorted oldest first: everything after this is newer still
            val len = f.length()
            if (f.delete()) { freed += len; out += f }
        }
        return out
    }
}

/**
 * Which pictures are still being asked for. The canvas themes draw rows every frame and cannot cancel anything: each draw of a row calls
 * [draw] + [want]. A request whose key was not asked for during the last [after] draws belongs to a row that scrolled away. A screen that
 * is not redrawn never counts as scrolled away (nothing newer was asked for).
 */
class ScrollWatch(private val after: Int = 80) {
    private val draws = java.util.concurrent.atomic.AtomicLong()
    private val wanted = HashMap<String, Long>()

    /** One picture was asked for by a draw; returns the draw number to pass to [want]. */
    fun draw(): Long = draws.incrementAndGet()
    @Synchronized fun want(key: String, n: Long) { wanted[key] = n }
    @Synchronized fun done(key: String) { wanted.remove(key) }
    @Synchronized fun scrolledOff(key: String): Boolean { val last = wanted[key] ?: return true; return draws.get() - last > after }
}

/** A failure that is probably temporary (no answer, a 5xx / 429, an empty body, the gate refusing): worth asking again. */
internal class TransientFailure : Exception()

/** The pause before the [n]th new try (1-based): 0.4 s, 0.8 s, 1.6 s ... up to 15 s, each between 70 % and 130 % of that so many rows do not all ask at the same instant. */
internal fun backoffMs(n: Int, random: Double): Long = ((400L shl (n - 1).coerceIn(0, 6)).coerceAtMost(15_000L) * (0.7 + 0.6 * random)).toLong()

/**
 * Runs [block], and again after a [backoffMs] pause each time it throws [TransientFailure], at most [attempts] times (unlimited by default: a row on screen keeps asking until it
 * gets its picture or scrolls away, which cancels this). [exhausted] gives the result when the attempts ran out. Cancellation always goes through.
 */
internal suspend fun <T> retryTransient(
    attempts: Int = Int.MAX_VALUE,
    sleep: suspend (Long) -> Unit = { kotlinx.coroutines.delay(it) },
    random: () -> Double = { kotlin.random.Random.nextDouble() },
    exhausted: () -> T,
    block: suspend () -> T,
): T {
    var n = 0
    while (true) {
        try { return block() }
        catch (_: TransientFailure) {
            if (++n >= attempts) return exhausted()
            sleep(backoffMs(n, random()))
        }
    }
}

/** Saving a cover picture so that rows asking for the same one at the same moment can never see, or leave behind, a missing or half-written file. */
object CoverFiles {
    private val counter = java.util.concurrent.atomic.AtomicLong()

    /**
     * Writes [bytes] to a file of its own next to [target] and renames it over [target] in one step. Before, every writer used one shared temporary name and deleted
     * [target] before the rename: with several songs of one album on screen (they share one cover), two downloads of the same picture wrote the same temporary file,
     * and one of them deleted the finished picture the other was about to decode, so some rows kept the placeholder for good.
     */
    fun saveAtomically(target: File, bytes: ByteArray) {
        target.parentFile?.mkdirs()
        val tmp = File(target.parentFile, target.name + "." + java.lang.Long.toHexString(System.nanoTime()) + "." + counter.incrementAndGet() + ".tmp")
        try {
            tmp.writeBytes(bytes)
            // one step that replaces an existing file (File.renameTo does not on Windows, where the tests run)
            try { java.nio.file.Files.move(tmp.toPath(), target.toPath(), java.nio.file.StandardCopyOption.ATOMIC_MOVE, java.nio.file.StandardCopyOption.REPLACE_EXISTING) }
            catch (_: java.nio.file.AtomicMoveNotSupportedException) { java.nio.file.Files.move(tmp.toPath(), target.toPath(), java.nio.file.StandardCopyOption.REPLACE_EXISTING) }
        } finally { if (tmp.exists()) tmp.delete() }
    }
}
