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
    class Entry(var stored: String?, var want: String?, var etag: String?)

    private val map = HashMap<String, Entry>()
    @Volatile var dirty = false; private set

    @Synchronized fun entry(key: String): Entry? = map[key]

    /** A file that exists is current unless the server reported another tag for it. A missing file always needs a fetch. */
    @Synchronized fun needsFetch(key: String, fileExists: Boolean): Boolean {
        if (!fileExists) return true
        val e = map[key] ?: return false
        return e.want != null && e.want != e.stored
    }

    /** The server's listing says these covers have these tags. Returns the keys whose saved picture is now out of date. */
    @Synchronized fun noteWanted(tags: Map<String, String>): List<String> {
        val changed = ArrayList<String>()
        for ((k, t) in tags) {
            for (key in listOf(k, "F$k")) {
                val e = map[key]
                if (e == null) { map[key] = Entry(null, t, null); dirty = true }
                else if (e.want != t) { e.want = t; dirty = true; if (e.stored != null && e.stored != t) changed += key }
            }
        }
        return changed
    }

    /** A fetch finished: the file now holds the picture with this tag (or the server said 304 for it). */
    @Synchronized fun saved(key: String, tag: String?, etag: String?) {
        val e = map.getOrPut(key) { Entry(null, tag, null) }
        e.stored = tag ?: e.want; e.etag = etag ?: e.etag
        if (tag != null) e.want = e.want ?: tag
        dirty = true
    }

    /** The file was deleted (cache eviction, cleaning): forget what it held. */
    @Synchronized fun forgetFile(key: String) { map[key]?.let { it.stored = null; it.etag = null; dirty = true } }

    @Synchronized fun clear() { map.clear(); dirty = true }
    @Synchronized fun size() = map.size

    @Synchronized fun toJson(): String {
        val o = JSONObject()
        for ((k, e) in map) o.put(k, org.json.JSONArray().put(e.stored ?: "").put(e.want ?: "").put(e.etag ?: ""))
        dirty = false
        return o.toString()
    }

    @Synchronized fun load(raw: String) {
        val o = JSONObject(raw)
        for (k in o.keys()) {
            val a = o.getJSONArray(k)
            map[k] = Entry(a.optString(0).ifEmpty { null }, a.optString(1).ifEmpty { null }, a.optString(2).ifEmpty { null })
        }
    }
}

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

    /** Deletes the oldest [files] until the shortfall (plus a 5 % margin of the target) is covered. Returns the deleted files. */
    fun evict(files: List<File>, info: StorageInfo): List<File> {
        val short = needed(info)
        if (short <= 0) return emptyList()
        val goal = short + (maxOf((info.totalBytes() * MIN_FREE_FRACTION).toLong(), MIN_FREE_BYTES) / 20)
        var freed = 0L
        val out = ArrayList<File>()
        for (f in files.sortedBy { it.lastModified() }) {
            if (freed >= goal) break
            val len = f.length()
            if (f.delete()) { freed += len; out += f }
        }
        return out
    }
}
