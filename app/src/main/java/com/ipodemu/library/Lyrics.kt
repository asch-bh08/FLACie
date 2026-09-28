package com.ipodemu.library

import com.ipodemu.App
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.net.HttpURLConnection
import java.net.URL
import java.net.URLEncoder

class LyricLine(val timeMs: Long, val text: String)

/** [synced] = every line has a timestamp (karaoke-style highlighting); otherwise plain text. */
class Lyrics(val lines: List<LyricLine>, val synced: Boolean, val source: String)

/**
 * Lyrics for any track, first hit wins:
 *  1. Jellyfin's own lyrics for a Jellyfin item (.lrc files / lyrics plugins on the server),
 *  2. a `.lrc` next to a local file,
 *  3. LRCLIB (lrclib.net, the free open lyrics database LRCGET uses): exact artist/title/album/duration lookup, then
 *     a search on title + lead artist.
 * Results -- including "none found" -- are cached on disk per song (a miss is retried after a week).
 * Only the song's title, artist, album and length are sent to LRCLIB.
 */
class LyricsProvider(private val app: App) {
    private val dir = File(app.filesDir, "lyrics").apply { mkdirs() }
    private val mem = HashMap<String, Lyrics?>()

    suspend fun get(t: Track): Lyrics? = withContext(Dispatchers.IO) {
        val key = cacheKey(t)
        synchronized(mem) { if (mem.containsKey(key)) return@withContext mem[key] }
        val f = File(dir, "$key.lrc")
        val cached = if (f.exists()) f.readText() else null
        val result = when {
            cached == null -> fetch(t).also { save(f, it) }
            cached.startsWith(MISS) -> if (System.currentTimeMillis() - f.lastModified() > WEEK) fetch(t).also { save(f, it) } else null
            else -> parse(cached.substringAfter('\n'), cached.substringBefore('\n'))
        }
        synchronized(mem) { mem[key] = result }
        result
    }

    private fun save(f: File, l: Lyrics?) {
        try {
            f.writeText(if (l == null) MISS else l.source + "\n" + l.lines.joinToString("\n") { if (l.synced) "[${stamp(it.timeMs)}]${it.text}" else it.text })
        } catch (_: Exception) {}
    }

    private fun fetch(t: Track): Lyrics? =
        fromJellyfin(t) ?: fromSidecar(t) ?: fromLrclib(t)

    private val jfId = Regex("/Audio/([0-9a-fA-F]{32})/stream")

    private fun fromJellyfin(t: Track): Lyrics? {
        val p = app.prefs
        val id = jfId.find(t.path)?.groupValues?.get(1)
            ?: app.library.jellyfinByKey()[t.matchKey]?.let { jfId.find(it.path)?.groupValues?.get(1) } ?: return null
        if (p.jellyfinUrl.isBlank() || p.jellyfinApiKey.isBlank()) return null
        val body = http("${p.jellyfinUrl.trimEnd('/')}/Audio/$id/Lyrics", mapOf("X-Emby-Token" to p.jellyfinApiKey)) ?: return null
        return try {
            val arr = JSONObject(body).optJSONArray("Lyrics") ?: return null
            val lines = List(arr.length()) { i -> arr.getJSONObject(i).let { LyricLine(if (it.has("Start")) it.optLong("Start") / 10_000 else -1, it.optString("Text")) } }
            if (lines.isEmpty()) null else Lyrics(lines, lines.all { it.timeMs >= 0 }, "Jellyfin")
        } catch (_: Exception) { null }
    }

    private fun fromSidecar(t: Track): Lyrics? {
        if (t.source != TrackSource.LOCAL) return null
        val f = File(t.path.substringBeforeLast('.') + ".lrc")
        return if (f.exists()) parse(f.readText(), "Local .lrc") else null
    }

    private fun fromLrclib(t: Track): Lyrics? {
        val title = t.title.trim(); val artist = t.artist.trim()
        if (title.isEmpty() || !t.isMusic) return null
        fun enc(s: String) = URLEncoder.encode(s, "UTF-8")
        val lead = primaryArtistDisplay(artist)
        val exact = buildString {
            append("$LRCLIB/get?track_name=${enc(title)}&artist_name=${enc(lead.ifEmpty { artist })}")
            if (t.album.isNotBlank()) append("&album_name=${enc(t.album)}")
            if (t.durationMs > 0) append("&duration=${t.durationMs / 1000}")
        }
        http(exact, UA)?.let { b -> fromLrclibJson(JSONObject(b))?.let { return it } }
        // no exact match (different album/edition, or length off): search, and take the closest-length hit with lyrics
        val q = http("$LRCLIB/search?track_name=${enc(stripFeat(title))}&artist_name=${enc(lead.ifEmpty { artist })}", UA) ?: return null
        val arr = try { JSONArray(q) } catch (_: Exception) { return null }
        val hits = List(arr.length()) { arr.getJSONObject(it) }
            .filter { !it.optString("syncedLyrics").isNullOrBlank() || !it.optString("plainLyrics").isNullOrBlank() }
        val best = hits.minByOrNull { h ->
            val d = if (t.durationMs > 0) kotlin.math.abs(h.optDouble("duration", 0.0) - t.durationMs / 1000.0) else 0.0
            d + if (h.optString("syncedLyrics").isNullOrBlank()) 5 else 0   // prefer synced when lengths are close
        } ?: return null
        if (t.durationMs > 0 && kotlin.math.abs(best.optDouble("duration", 0.0) - t.durationMs / 1000.0) > 20) return null   // a different recording
        return fromLrclibJson(best)
    }

    private fun fromLrclibJson(o: JSONObject): Lyrics? {
        if (o.optBoolean("instrumental")) return Lyrics(listOf(LyricLine(-1, "Instrumental")), false, "LRCLIB")
        val synced = o.optString("syncedLyrics").takeIf { it.isNotBlank() && it != "null" }
        val plain = o.optString("plainLyrics").takeIf { it.isNotBlank() && it != "null" }
        return synced?.let { parse(it, "LRCLIB") } ?: plain?.let { parse(it, "LRCLIB") }
    }

    private fun http(url: String, headers: Map<String, String>): String? = try {
        val c = URL(url).openConnection() as HttpURLConnection
        c.connectTimeout = 6000; c.readTimeout = 10000
        headers.forEach { (k, v) -> c.setRequestProperty(k, v) }
        try { if (c.responseCode in 200..299) c.inputStream.bufferedReader().use { it.readText() } else null } finally { c.disconnect() }
    } catch (_: Exception) { null }

    companion object {
        private const val LRCLIB = "https://lrclib.net/api"
        private val UA = mapOf("User-Agent" to "ipodplayer/0.8 (Android music player)")
        private const val MISS = "#none"
        private const val WEEK = 7L * 24 * 3600 * 1000
        private val stampRe = Regex("""\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?]""")

        private fun cacheKey(t: Track): String {
            val md = java.security.MessageDigest.getInstance("MD5").digest(t.matchKey.toByteArray())
            return md.joinToString("") { "%02x".format(it) }
        }

        private fun stamp(ms: Long) = "%02d:%02d.%02d".format(ms / 60000, ms / 1000 % 60, ms % 1000 / 10)

        private fun stripFeat(s: String) = s.replace(Regex("""\s*[(\[](feat\.?|ft\.?|featuring|with)\s[^)\]]*[)\]]""", RegexOption.IGNORE_CASE), "").trim()

        /** Lead artist with its original casing ("Lady Gaga; Colby O'Donis" -> "Lady Gaga"). */
        private fun primaryArtistDisplay(s: String) =
            s.split(Regex("""\s*(;|,|&|/|\s+x\s+|\s+(feat\.?|ft\.?|featuring|with)\s+)\s*""", RegexOption.IGNORE_CASE)).firstOrNull { it.isNotBlank() }.orEmpty().trim()

        /** LRC ("[01:23.45]line", several stamps per line allowed, [ar:]/[ti:] tags ignored) or plain text. */
        fun parse(text: String, source: String): Lyrics? {
            val out = ArrayList<LyricLine>()
            var sawStamp = false
            for (raw in text.lines()) {
                val stamps = stampRe.findAll(raw).toList()
                if (stamps.isEmpty()) {
                    if (raw.trimStart().startsWith("[") && raw.contains(":") && raw.trimEnd().endsWith("]")) continue   // metadata tag
                    out += LyricLine(-1, raw.trim()); continue
                }
                sawStamp = true
                val words = raw.substring(stamps.last().range.last + 1).trim()
                for (m in stamps) {
                    val (mm, ss, frac) = m.destructured
                    val f = when (frac.length) { 0 -> 0; 1 -> frac.toInt() * 100; 2 -> frac.toInt() * 10; else -> frac.take(3).toInt() }
                    out += LyricLine(mm.toLong() * 60000 + ss.toLong() * 1000 + f, words)
                }
            }
            val lines = if (sawStamp) out.filter { it.timeMs >= 0 }.sortedBy { it.timeMs } else out.dropWhile { it.text.isEmpty() }.dropLastWhile { it.text.isEmpty() }
            return if (lines.none { it.text.isNotBlank() }) null else Lyrics(lines, sawStamp, source)
        }
    }
}
