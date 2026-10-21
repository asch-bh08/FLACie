package com.ipodemu.library.offline

import com.ipodemu.library.Track
import com.ipodemu.library.TrackSource
import org.json.JSONArray
import org.json.JSONObject

/** Where a downloaded song is in its life. */
enum class OfflineState { QUEUED, DOWNLOADING, DONE, FAILED }

/** Why a song is kept offline: the person asked (MANUAL, never removed by itself) or it is a favourite (FAVORITE, removed again when it stops being one). */
enum class OfflineReason { MANUAL, FAVORITE }

/** One song of the offline library. [path] is the song's normal identity (its server address), so the queue, favourites and playlists keep working with it. */
class OfflineEntry(
    val key: String, val path: String, val title: String, val artist: String, val album: String, val albumArtist: String, val genre: String,
    val durationMs: Long, val artKey: String?, val trackNo: Int, val discNo: Int, val year: Int,
    var state: OfflineState, var reason: OfflineReason, var bytes: Long, var total: Long, val addedMs: Long, var error: String? = null,
    /** The file extension the server's answer gave (flac, mp3 ...), known once the download has started. */
    var ext: String = "",
) {
    fun toTrack(): Track = Track(
        path = path, title = title, artist = artist, album = album, albumArtist = albumArtist, genre = genre, trackNo = trackNo, discNo = discNo,
        durationMs = durationMs, year = year, isMusic = true, artKey = artKey, mtime = addedMs, size = if (total > 0) total else bytes, source = TrackSource.JELLYFIN,
    )

    val progress: Float get() = if (total > 0) (bytes.toFloat() / total).coerceIn(0f, 1f) else 0f
}

/**
 * The list of songs kept offline, saved as one JSON text. Pure (org.json only), unit-tested. The files themselves are named by [OfflineMath.fileName];
 * this class only knows which songs exist and in what state, so a crash between a file and the list loses at most a re-download.
 */
class OfflineIndex {
    private val map = LinkedHashMap<String, OfflineEntry>()

    val entries: List<OfflineEntry> get() = map.values.toList()
    val size: Int get() = map.size
    operator fun get(key: String): OfflineEntry? = map[key]
    fun has(key: String): Boolean = key in map

    /** Adds a song to download (or, when it is already there, upgrades FAVORITE to MANUAL and re-queues a failed one). Returns the entry. */
    fun add(t: Track, reason: OfflineReason, nowMs: Long): OfflineEntry {
        val key = OfflineMath.keyOf(t.path)
        val have = map[key]
        if (have != null) {
            if (reason == OfflineReason.MANUAL) have.reason = OfflineReason.MANUAL
            if (have.state == OfflineState.FAILED) { have.state = OfflineState.QUEUED; have.error = null }
            return have
        }
        val e = OfflineEntry(key, t.path, t.title, t.artist, t.album, t.albumArtist, t.genre, t.durationMs, t.artKey, t.trackNo, t.discNo, t.year, OfflineState.QUEUED, reason, 0L, 0L, nowMs)
        map[key] = e
        return e
    }

    fun remove(key: String): OfflineEntry? = map.remove(key)
    fun clear() = map.clear()

    /** The next song to download: queued ones in the order they were added; a song left "downloading" by a crash counts as queued. */
    fun next(): OfflineEntry? = map.values.firstOrNull { it.state == OfflineState.QUEUED || it.state == OfflineState.DOWNLOADING }

    val doneBytes: Long get() = map.values.filter { it.state == OfflineState.DONE }.sumOf { it.bytes }
    val doneCount: Int get() = map.values.count { it.state == OfflineState.DONE }
    val pendingCount: Int get() = map.values.count { it.state == OfflineState.QUEUED || it.state == OfflineState.DOWNLOADING }
    val failedCount: Int get() = map.values.count { it.state == OfflineState.FAILED }

    fun encode(): String {
        val a = JSONArray()
        for (e in map.values) a.put(JSONObject()
            .put("k", e.key).put("p", e.path).put("ti", e.title).put("ar", e.artist).put("al", e.album).put("aa", e.albumArtist).put("g", e.genre)
            .put("d", e.durationMs).put("ak", e.artKey ?: "").put("tn", e.trackNo).put("dn", e.discNo).put("y", e.year)
            .put("st", e.state.name).put("r", e.reason.name).put("b", e.bytes).put("t", e.total).put("ad", e.addedMs).put("er", e.error ?: "").put("x", e.ext))
        return JSONObject().put("v", VERSION).put("e", a).toString()
    }

    companion object {
        const val VERSION = 1

        /** A broken or foreign file gives an empty list: the files on disk are then found by [OfflineMath.orphans] and removed. */
        fun decode(json: String?): OfflineIndex {
            val idx = OfflineIndex()
            if (json.isNullOrBlank()) return idx
            val root = try { JSONObject(json) } catch (_: Exception) { return idx }
            if (root.optInt("v", 0) != VERSION) return idx
            val arr = root.optJSONArray("e") ?: return idx
            for (i in 0 until arr.length()) {
                val o = arr.optJSONObject(i) ?: continue
                val key = o.optString("k"); val path = o.optString("p")
                if (key.isEmpty() || path.isEmpty()) continue
                val state = runCatching { OfflineState.valueOf(o.optString("st")) }.getOrDefault(OfflineState.QUEUED)
                val reason = runCatching { OfflineReason.valueOf(o.optString("r")) }.getOrDefault(OfflineReason.MANUAL)
                idx.map[key] = OfflineEntry(key, path, o.optString("ti"), o.optString("ar"), o.optString("al"), o.optString("aa"), o.optString("g"), o.optLong("d"),
                    o.optString("ak").ifEmpty { null }, o.optInt("tn"), o.optInt("dn"), o.optInt("y"), state, reason, o.optLong("b"), o.optLong("t"), o.optLong("ad"), o.optString("er").ifEmpty { null }, o.optString("x"))
            }
            return idx
        }
    }
}

/** What to do with a partly downloaded file after the server answered a request that asked to continue from byte [OfflineMath.rangeStart]. */
sealed interface ResumeDecision {
    /** Keep the .part file and append the body (the server honoured the range). */
    data class Append(val from: Long) : ResumeDecision
    /** Throw the .part file away and write the body from the start (the server sent the whole file). */
    data object Restart : ResumeDecision
    /** The part file already is the whole song. */
    data object Complete : ResumeDecision
    /** The answer is an error. */
    data class Fail(val code: Int) : ResumeDecision
}

object OfflineMath {
    /** The same stable key the speed memory uses: the Jellyfin item id, so the key survives an address change of the server. */
    fun keyOf(path: String): String = com.ipodemu.playback.SpeedPolicy.keyOf(path)

    /** A file name that is safe on every file system: the key with anything unusual turned into an underscore, plus the extension. */
    fun fileName(key: String, ext: String): String {
        val safe = key.map { if (it.isLetterOrDigit() || it == '-' || it == '_') it else '_' }.joinToString("").take(80).ifEmpty { "item" }
        val e = ext.lowercase().filter { it.isLetterOrDigit() }.take(5).ifEmpty { "bin" }
        return "$safe.$e"
    }

    /** The extension from a server Content-Type or Content-Disposition (falls back to the container name Jellyfin sent, else "bin"). */
    fun extensionFor(contentType: String?, disposition: String?): String {
        Regex("""filename\*?=(?:UTF-8'')?"?([^";]+)"?""", RegexOption.IGNORE_CASE).find(disposition ?: "")?.groupValues?.get(1)?.substringAfterLast('.', "")?.takeIf { it.length in 2..5 }?.let { return it.lowercase() }
        return when (contentType?.substringBefore(';')?.trim()?.lowercase()) {
            "audio/flac", "audio/x-flac" -> "flac"; "audio/mpeg", "audio/mp3" -> "mp3"; "audio/mp4", "audio/m4a", "audio/x-m4a", "audio/aac" -> "m4a"
            "audio/ogg", "audio/vorbis" -> "ogg"; "audio/opus" -> "opus"; "audio/wav", "audio/x-wav", "audio/wave" -> "wav"; "audio/x-ms-wma" -> "wma"; else -> "bin"
        }
    }

    /** The Range header to continue a part file, or null to start from the beginning. */
    fun rangeHeader(partBytes: Long): String? = if (partBytes > 0) "bytes=$partBytes-" else null

    /** [contentRange] is the answer's Content-Range ("bytes 100-999/1000"); [contentLength] the answer's Content-Length (-1 unknown). */
    fun decide(partBytes: Long, code: Int, contentRange: String?, contentLength: Long): ResumeDecision {
        if (code == 416) return if (partBytes > 0) ResumeDecision.Complete else ResumeDecision.Fail(code)   // asked for bytes past the end: we have it all
        if (code == 206) {
            val start = Regex("""bytes\s+(\d+)-""").find(contentRange ?: "")?.groupValues?.get(1)?.toLongOrNull()
            return if (start == partBytes) ResumeDecision.Append(partBytes) else ResumeDecision.Restart   // a range that does not start where we stopped cannot be appended
        }
        if (code == 200) return ResumeDecision.Restart
        return ResumeDecision.Fail(code)
    }

    /** The whole size of the song from an answer (Content-Range total wins over Content-Length plus what we have); -1 when unknown. */
    fun totalSize(code: Int, partBytes: Long, contentRange: String?, contentLength: Long): Long {
        if (code == 206) Regex("""/(\d+)\s*$""").find(contentRange ?: "")?.groupValues?.get(1)?.toLongOrNull()?.let { return it }
        if (code == 206 && contentLength >= 0) return partBytes + contentLength
        return if (code == 200 && contentLength >= 0) contentLength else -1L
    }

    /** Whether downloading may go on now: never with no network, and not on mobile data when the person asked for Wi-Fi only. */
    fun networkAllowed(wifiOnly: Boolean, connected: Boolean, metered: Boolean): Boolean = connected && !(wifiOnly && metered)

    /** One downloaded album's share of the space: its name, artist, how many songs and bytes (finished downloads only). */
    data class AlbumUse(val album: String, val artist: String, val songs: Int, val bytes: Long, val paths: List<String>)

    /** The downloaded albums by the space they take, biggest first (the Offline screen's "By album"). */
    fun byAlbum(entries: List<OfflineEntry>): List<AlbumUse> =
        entries.filter { it.state == OfflineState.DONE }
            .groupBy { it.albumArtist.ifEmpty { it.artist }.lowercase() + "|" + it.album.lowercase() }
            .map { (_, l) -> AlbumUse(l[0].album.ifEmpty { "Unknown album" }, l[0].albumArtist.ifEmpty { l[0].artist }, l.size, l.sumOf { it.bytes }, l.map { it.path }) }
            .sortedByDescending { it.bytes }

    /** Why downloading waits right now, in the words the Offline screen and the notification show; null when it may go on. */
    fun waitReason(paused: Boolean, wifiOnly: Boolean, connected: Boolean, metered: Boolean): String? = when {
        paused -> "Paused"
        networkAllowed(wifiOnly, connected, metered) -> null
        wifiOnly && connected -> "Waiting for Wi-Fi"
        else -> "Waiting for a connection"
    }

    /** Files in the offline folder that no entry owns (left by a crash or an older list): safe to delete. Part files of queued entries are kept. */
    fun orphans(files: List<String>, index: OfflineIndex): List<String> {
        val owned = HashSet<String>()
        for (e in index.entries) { owned += stem(fileName(e.key, "x")) }
        return files.filter { f -> val s = stem(f.removeSuffix(".part")); s !in owned }
    }
    private fun stem(name: String) = name.substringBeforeLast('.', name).let { s -> if (s.isEmpty()) name else s }

    /** "1 song", "3 songs". */
    fun songs(n: Int): String = if (n == 1) "1 song" else "$n songs"

    /** "1.4 GB", "230 MB", "12 KB". */
    fun formatBytes(b: Long): String {
        val v = b.coerceAtLeast(0L).toDouble()
        return when {
            v >= 1e9 -> "%.1f GB".format(java.util.Locale.ROOT, v / 1e9)
            v >= 1e6 -> "%.0f MB".format(java.util.Locale.ROOT, v / 1e6)
            v >= 1e3 -> "%.0f KB".format(java.util.Locale.ROOT, v / 1e3)
            else -> "${b.coerceAtLeast(0L)} B"
        }
    }

    /** The size to expect for a song whose size the server did not say: a typical lossless file is about 7 MB a minute; with no length, 40 MB. */
    fun estimateBytes(t: Track): Long = if (t.size > 0) t.size else if (t.durationMs > 0) t.durationMs / 60_000L * 7_000_000L + 3_000_000L else 40_000_000L
}

/** Which favourites to download and which downloaded favourites to let go of. Pure. */
object AutoDownloadPlan {
    class Plan(val add: List<Track>, val removeKeys: List<String>)

    /**
     * [favorites]: the favourite songs now (Jellyfin songs only). [index]: what is offline. [limitBytes]: the most the person wants kept offline (0 = no limit).
     * Songs are added in the order given until the estimated total would pass the limit; a FAVORITE entry whose song is no longer a favourite is removed
     * (a MANUAL one never is).
     */
    fun plan(favorites: List<Track>, index: OfflineIndex, limitBytes: Long): Plan {
        val favKeys = favorites.mapTo(HashSet()) { OfflineMath.keyOf(it.path) }
        val remove = index.entries.filter { it.reason == OfflineReason.FAVORITE && it.key !in favKeys }.map { it.key }
        var used = index.entries.filter { it.key !in remove }.sumOf { if (it.total > 0) it.total else if (it.bytes > 0) it.bytes else OfflineMath.estimateBytes(it.toTrack()) }
        val add = ArrayList<Track>()
        for (t in favorites) {
            if (t.source != TrackSource.JELLYFIN) continue
            if (index.has(OfflineMath.keyOf(t.path))) continue
            val est = OfflineMath.estimateBytes(t)
            if (limitBytes > 0 && used + est > limitBytes) break
            used += est; add += t
        }
        return Plan(add, remove)
    }
}
