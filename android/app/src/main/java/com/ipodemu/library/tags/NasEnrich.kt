package com.ipodemu.library.tags

import com.ipodemu.library.NasCover
import com.ipodemu.library.Track

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import java.util.concurrent.atomic.AtomicInteger

/** A [RandomAccess] that knows when a read failed (network error), so that is not cached as "nothing in this file". */
interface ReadFailure { val failed: Boolean }

/** One audio file found by the listing: the name-guessed track, its folder and whether that folder holds a cover image. */
class NasEntry(val base: Track, val folderUrl: String, val folderHasImage: Boolean) {
    val fileName: String get() = base.path.trimEnd('/').substringAfterLast('/')
}

/** Art keys for covers read out of the audio files themselves. */
object NasArtKeys {
    private val enc = java.util.Base64.getUrlEncoder().withoutPadding()
    private val dec = java.util.Base64.getUrlDecoder()

    /** One key per album folder + album tag (all songs of an album share one stored thumbnail); a song with no album tag has its own. */
    fun embedded(folderUrl: String, album: String, fileName: String): String {
        val a = album.trim().lowercase()
        return "ne" + enc.encodeToString("$folderUrl\n$a\n${if (a.isEmpty()) fileName else ""}".toByteArray())
    }

    fun parse(key: String): Triple<String, String, String>? = try {
        val p = String(dec.decode(key.removePrefix("ne"))).split('\n')
        if (p.size >= 3) Triple(p[0], p[1], p[2]) else null
    } catch (_: Exception) { null }
}

/** How file tags override the guesses made from file and folder names. */
object NasTagMerge {
    private val junkExact = setOf("unknown", "unknown artist", "unknown album", "<unknown>", "[unknown]", "untitled", "n/a", "none", "no title", "no artist", "artist", "title", "album", "various")
    private val junkTrack = Regex("""(?i)^(track|titel|piste|pista|audiotrack|audio track)\s*\d+$""")

    /** Tag text that says nothing (an empty field, "Unknown Artist", "Track 01"): the filename guess is better. */
    fun junk(s: String?): Boolean {
        if (s == null) return true
        val t = s.trim().lowercase()
        return t.isEmpty() || t in junkExact || junkTrack.matches(t)
    }

    fun apply(base: Track, c: CachedTags?): Track {
        if (c == null || !c.ok) return base
        val title = c.title.takeUnless { junk(it) } ?: base.title
        val tagArtist = c.artist.takeUnless { junk(it) }
        val tagAlbumArtist = c.albumArtist.takeUnless { junk(it) }
        val artist = tagArtist ?: tagAlbumArtist ?: base.artist
        val album = c.album.takeUnless { junk(it) } ?: base.album
        // no album-artist tag: an "Artist/Album/song" folder keeps the folder's artist, so one album stays one album
        val albumArtist = tagAlbumArtist
            ?: if (base.album.isNotBlank() && base.albumArtist.isNotBlank()) base.albumArtist else (tagArtist ?: base.albumArtist)
        return base.copy(
            title = title, artist = artist, album = album, albumArtist = albumArtist,
            genre = c.genre.ifBlank { base.genre },
            trackNo = if (c.trackNo > 0) c.trackNo else base.trackNo,
            discNo = if (c.discNo > 0) c.discNo else base.discNo,
            year = if (c.year > 0) c.year else base.year,
            durationMs = if (c.durationMs > 0) c.durationMs else base.durationMs,
        )
    }

    /** Every entry as a final [Track]: tags applied, and the cover key chosen (folder image, else embedded art, else an online lookup by the real names). */
    fun resolve(entries: List<NasEntry>, tags: List<CachedTags?>): List<Track> {
        // an album whose songs carry embedded art lends it to the siblings that have none
        val albumArt = HashMap<String, String>()
        for (i in entries.indices) {
            val c = tags[i] ?: continue
            if (c.ok && c.hasArt && c.album.isNotBlank() && !entries[i].folderHasImage)
                albumArt.putIfAbsent(entries[i].folderUrl + "\n" + c.album.trim().lowercase(), NasArtKeys.embedded(entries[i].folderUrl, c.album, entries[i].fileName))
        }
        return List(entries.size) { i ->
            val e = entries[i]
            val c = tags[i]
            val t = apply(e.base, c)
            if (c == null || !c.ok) return@List t
            val key = when {
                e.folderHasImage -> e.base.artKey
                c.hasArt -> NasArtKeys.embedded(e.folderUrl, c.album, e.fileName)
                c.album.isNotBlank() && albumArt.containsKey(e.folderUrl + "\n" + c.album.trim().lowercase()) -> albumArt[e.folderUrl + "\n" + c.album.trim().lowercase()]
                else -> NasCover.key(e.folderUrl, t.artist, t.album, if (t.album.isNotBlank()) "" else t.title)
            }
            if (key == t.artKey) t else t.copy(artKey = key)
        }
    }
}

/**
 * Fills in tags for the NAS files the cache has not seen: a few files at a time, publishing the improved list every
 * few hundred files (or seconds) so the library fills in while it is in use, and saving the cache as it goes.
 */
class NasTagEnricher(private val cache: NasTagCache, private val parallel: Int = 2, private val minPublishGapMs: Long = 0, private val pauseWhile: () -> Boolean = { false }) {
    /** What is known right now (from the cache only): used to show the list immediately. */
    fun fromCache(entries: List<NasEntry>): Pair<List<Track>, List<CachedTags?>> {
        val tags = entries.map { cache.get(it.base.path, it.base.size, it.base.mtime) }
        return NasTagMerge.resolve(entries, tags) to tags
    }

    suspend fun run(
        entries: List<NasEntry>,
        initial: List<CachedTags?>,
        open: (NasEntry) -> RandomAccess?,
        hasArt: (String) -> Boolean,
        saveArt: (String, ByteArray) -> Unit,
        publish: (tracks: List<Track>, tags: List<CachedTags?>) -> Unit,
    ) {
        val tags = ArrayList<CachedTags?>(initial)
        val todo = entries.indices.filter { tags[it] == null }
        if (todo.isEmpty()) return
        val next = AtomicInteger(0)
        val lock = Any()
        var sinceLast = 0
        var sinceSave = 0
        var lastPublish = System.nanoTime()

        fun snapshot() = synchronized(lock) { NasTagMerge.resolve(entries, tags) to ArrayList(tags) }
        fun publishNow() { val (t, g) = snapshot(); publish(t, g) }

        try {
            coroutineScope {
                repeat(parallel.coerceAtLeast(1)) {
                    launch(Dispatchers.IO) {
                        try { while (isActive) {
                            while (isActive && pauseWhile()) kotlinx.coroutines.delay(500)
                            val k = next.getAndIncrement()
                            if (k >= todo.size) break
                            val i = todo[k]
                            val e = entries[i]
                            val size = e.base.size; val mtime = e.base.mtime
                            var cached: CachedTags? = null
                            var src: RandomAccess? = null
                            try {
                                src = open(e)
                                if (src != null) {
                                    val t = TagReader.read(src, e.fileName, wantArt = !e.folderHasImage)
                                    val failed = (src as? ReadFailure)?.failed == true
                                    if (!failed) {
                                        var art = false
                                        val bytes = t?.art
                                        if (t != null && bytes != null) {
                                            val key = NasArtKeys.embedded(e.folderUrl, t.album, e.fileName)
                                            try { if (!hasArt(key)) saveArt(key, bytes); art = true } catch (_: Exception) {}
                                        }
                                        cached = CachedTags.of(size, mtime, t, art)
                                    }
                                }
                            } catch (_: Exception) { /* unreadable now: tried again on the next scan */ }
                            finally { try { (src as? java.io.Closeable)?.close() } catch (_: Exception) {} }
                            if (cached != null) {
                                cache.put(e.base.path, cached)
                                synchronized(lock) { tags[i] = cached }
                            }
                            var doPublish = false; var doSave = false
                            synchronized(lock) {
                                sinceLast++; sinceSave++
                                val now = System.nanoTime()
                                if ((sinceLast >= PUBLISH_EVERY || (sinceLast > 0 && now - lastPublish > 3_000_000_000L)) && now - lastPublish >= minPublishGapMs * 1_000_000L) { doPublish = true; sinceLast = 0; lastPublish = now }
                                if (sinceSave >= 1000) { doSave = true; sinceSave = 0 }
                            }
                            if (doSave) cache.save()
                            if (doPublish) publishNow()
                        } } finally { try { android.os.Process.setThreadPriority(android.os.Process.THREAD_PRIORITY_DEFAULT) } catch (_: Throwable) {} }
                    }
                }
            }
        } finally {
            cache.save()
        }
        publishNow()
    }

    companion object { const val PUBLISH_EVERY = 250 }
}

/** When the user last touched the screen or a key (stamped by MainActivity); background work waits while it is recent. */
object UserActivity {
    @Volatile var last = 0L
    fun recent(withinMs: Long = 3000) = android.os.SystemClock.elapsedRealtime() - last < withinMs
}
