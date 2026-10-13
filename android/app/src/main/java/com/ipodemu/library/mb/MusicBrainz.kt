package com.ipodemu.library.mb

import com.ipodemu.library.Track
import com.ipodemu.library.editionRe
import com.ipodemu.library.normTitle
import com.ipodemu.library.primaryArtist
import kotlinx.coroutines.delay
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.security.MessageDigest

/** One recording MusicBrainz offers for a song. */
class MbCandidate(
    val mbid: String, val title: String, val artist: String, val album: String, val releaseMbid: String?, val releaseGroupMbid: String?,
    val year: Int?, val lengthMs: Int?, val country: String?, val apiScore: Int,
) { val url: String get() = "https://musicbrainz.org/recording/$mbid" }

/** How well a MusicBrainz candidate fits a song of the library, 0 to 100 (the same arithmetic as FLACie Web). Nothing is ever applied because of it: the person chooses. */
object MbMatching {
    const val LIKELY = 80
    private fun albumNorm(s: String) = s.lowercase().replace(editionRe, "").filter { it.isLetterOrDigit() }

    fun score(t: Track, c: MbCandidate): Double {
        var s = 0.0
        val tt = normTitle(t.title); val ct = normTitle(c.title)
        if (tt.isNotEmpty() && tt == ct) s += 40 else if (tt.isNotEmpty() && ct.isNotEmpty() && (tt.contains(ct) || ct.contains(tt))) s += 20
        val ta = primaryArtist(t.artist.ifEmpty { t.albumArtist }); val ca = primaryArtist(c.artist)
        if (ta.isNotEmpty() && ta == ca) s += 30 else if (ta.isNotEmpty() && ca.isNotEmpty() && (ta.contains(ca) || ca.contains(ta))) s += 15
        val tb = albumNorm(t.album); val cb = albumNorm(c.album)
        if (tb.isNotEmpty() && tb == cb) s += 15 else if (tb.isNotEmpty() && cb.isNotEmpty() && (tb.contains(cb) || cb.contains(tb))) s += 7
        val l = c.lengthMs
        if (t.durationMs > 0 && l != null && l > 0) { val d = Math.abs(t.durationMs - l); s += if (d <= 3_000) 15 else if (d <= 10_000) 8 else 0 }
        return s
    }

    fun rank(t: Track, list: List<MbCandidate>): List<Pair<MbCandidate, Double>> =
        list.withIndex().map { (i, c) -> Triple(c, score(t, c), i) }.sortedWith(compareByDescending<Triple<MbCandidate, Double, Int>> { it.second }.thenByDescending { it.first.apiScore }.thenBy { it.third }).map { it.first to it.second }
}

class MbResponse(val code: Int, val body: String, val retryAfterSec: Long? = null)
fun interface MbHttp { fun get(url: String, headers: Map<String, String>): MbResponse }

/** The real thing: HttpURLConnection, redirects followed. */
class UrlConnectionHttp : MbHttp {
    override fun get(url: String, headers: Map<String, String>): MbResponse {
        val c = java.net.URL(url).openConnection() as java.net.HttpURLConnection
        c.connectTimeout = 10_000; c.readTimeout = 20_000; c.instanceFollowRedirects = true
        for ((k, v) in headers) c.setRequestProperty(k, v)
        return try {
            val code = c.responseCode
            val body = (if (code in 200..299) c.inputStream else c.errorStream)?.use { String(it.readBytes(), Charsets.UTF_8) } ?: ""
            MbResponse(code, body, c.getHeaderField("Retry-After")?.trim()?.toLongOrNull())
        } finally { c.disconnect() }
    }
}

/**
 * MusicBrainz asks for about one request a second from one address (503 beyond that) and a descriptive User-Agent. Everything the app asks MusicBrainz or the Cover Art
 * Archive goes through one of these: requests are one after the other, at least [gapMs] apart, and a "retry later" answer pushes the next turn out.
 */
class MbGate(private val gapMs: Long = 1100, private val now: () -> Long = { System.currentTimeMillis() }, private val sleep: suspend (Long) -> Unit = { delay(it) }) {
    private val lock = Mutex()
    private var next = 0L
    var requests = 0; private set

    suspend fun <T> run(block: suspend () -> T): T = lock.withLock {
        val due = next - now()
        if (due > 0) sleep(due)
        next = now() + gapMs; requests++
        block()
    }

    /** The server said "not before this long": nobody asks again until then. */
    fun pause(ms: Long) { val until = now() + ms; if (until > next) next = until }
}

class MbClient(private val http: MbHttp, private val userAgent: String, private val cacheDir: File? = null, private val gate: MbGate = MbGate(), private val now: () -> Long = { System.currentTimeMillis() }) {
    var requestsMade = 0; private set
    private val cacheForMs = 30L * 24 * 3600 * 1000
    private val cacheNoneMs = 7L * 24 * 3600 * 1000

    companion object {
        const val DEFAULT_CONTACT = "https://github.com/asch-bh08/FLACie"
        fun userAgentFor(version: String, contact: String? = null) = "FLACie/$version ( ${contact?.trim().takeUnless { it.isNullOrEmpty() } ?: DEFAULT_CONTACT} )"
        private fun quote(s: String) = "\"" + s.replace("\\", "\\\\").replace("\"", "\\\"") + "\""

        fun queryFor(t: Track): String {
            val parts = arrayListOf("recording:" + quote(t.title))
            val artist = t.artist.ifEmpty { t.albumArtist }
            if (artist.isNotEmpty()) parts += "artist:" + quote(artist)
            if (t.album.isNotEmpty()) parts += "release:" + quote(t.album)
            return parts.joinToString(" AND ")
        }

        fun parse(json: String): List<MbCandidate> {
            val out = ArrayList<MbCandidate>()
            val arr = try { JSONObject(json).optJSONArray("recordings") } catch (_: Exception) { null } ?: return out
            for (i in 0 until arr.length()) {
                val r = arr.optJSONObject(i) ?: continue
                val id = r.optString("id"); if (id.isEmpty()) continue
                val credits = r.optJSONArray("artist-credit")
                val artist = buildString { if (credits != null) for (j in 0 until credits.length()) { val c = credits.optJSONObject(j) ?: continue; append(c.optString("name").ifEmpty { c.optJSONObject("artist")?.optString("name") ?: "" }); append(c.optString("joinphrase")) } }
                val rel = r.optJSONArray("releases")?.optJSONObject(0)
                val date = rel?.optString("date").orEmpty()
                val year = if (date.length >= 4) date.substring(0, 4).toIntOrNull() else null
                out += MbCandidate(id, r.optString("title"), artist, rel?.optString("title").orEmpty(), rel?.optString("id")?.ifEmpty { null }, rel?.optJSONObject("release-group")?.optString("id")?.ifEmpty { null },
                    year, if (r.has("length")) r.optInt("length") else null, rel?.optString("country")?.ifEmpty { null }, r.optInt("score"))
            }
            return out
        }
    }

    suspend fun search(t: Track): List<MbCandidate> {
        if (t.title.isBlank()) return emptyList()
        val query = queryFor(t)
        cached(query)?.let { return it }
        val url = "https://musicbrainz.org/ws/2/recording?query=" + java.net.URLEncoder.encode(query, "UTF-8") + "&fmt=json&limit=8"
        for (attempt in 0 until 3) {
            val r = gate.run { requestsMade++; http.get(url, mapOf("User-Agent" to userAgent, "Accept" to "application/json")) }
            if (r.code == 200) { val list = parse(r.body); store(query, r.body); return list }
            if ((r.code == 503 || r.code == 429) && attempt < 2) { gate.pause(minOf(60L, r.retryAfterSec ?: (2L + attempt * 3)) * 1000); continue }
            throw java.io.IOException("MusicBrainz answered ${r.code}")
        }
        throw java.io.IOException("MusicBrainz is busy")
    }

    private fun file(query: String): File? = cacheDir?.let { File(it, MessageDigest.getInstance("SHA-1").digest(query.toByteArray()).joinToString("") { b -> "%02x".format(b) } + ".json") }

    private fun cached(query: String): List<MbCandidate>? {
        val f = file(query) ?: return null
        return try {
            if (!f.exists()) return null
            val o = JSONObject(f.readText()); val hits = parse(o.getString("body"))
            val ttl = if (hits.isEmpty()) cacheNoneMs else cacheForMs
            if (now() - o.getLong("t") <= ttl) hits else null
        } catch (_: Exception) { null }
    }

    private fun store(query: String, body: String) {
        val f = file(query) ?: return
        try { f.parentFile?.mkdirs(); val tmp = File(f.path + ".tmp"); tmp.writeText(JSONObject().put("t", now()).put("body", body).toString()); if (!tmp.renameTo(f)) { f.delete(); tmp.renameTo(f) } } catch (_: Exception) {}
    }
}

/** A picture the Cover Art Archive holds for a release. */
class CaaImage(val id: String, val thumb: String, val full: String, val front: Boolean, val back: Boolean, val types: List<String>)

class CaaClient(private val http: MbHttp, private val userAgent: String, private val gate: MbGate) {
    companion object {
        const val MAX_BYTES = 12L * 1024 * 1024
        private fun https(u: String) = if (u.startsWith("http://", true)) "https://" + u.substring(7) else u

        fun parse(json: String): List<CaaImage> {
            val arr = try { JSONObject(json).optJSONArray("images") } catch (_: Exception) { null } ?: return emptyList()
            val out = ArrayList<CaaImage>()
            for (i in 0 until arr.length()) {
                val o = arr.optJSONObject(i) ?: continue
                val id = o.opt("id")?.toString().orEmpty(); val full = o.optString("image")
                if (id.isEmpty() || full.isEmpty()) continue
                val th = o.optJSONObject("thumbnails")
                fun pick(vararg k: String) = k.map { th?.optString(it).orEmpty() }.firstOrNull { it.isNotEmpty() }
                val types = o.optJSONArray("types")?.let { a -> (0 until a.length()).map { a.optString(it) } } ?: emptyList()
                out += CaaImage(id, https(pick("250", "small") ?: full), https(pick("1200", "large", "500") ?: full), o.optBoolean("front"), o.optBoolean("back"), types)
            }
            return out.sortedWith(compareByDescending<CaaImage> { it.front }.thenBy { it.back })
        }

        /** "image/jpeg", "image/png", "image/webp" by the first bytes; null for anything else. */
        fun sniff(b: ByteArray): String? = when {
            b.size >= 3 && b[0] == 0xFF.toByte() && b[1] == 0xD8.toByte() && b[2] == 0xFF.toByte() -> "image/jpeg"
            b.size >= 8 && b[0] == 0x89.toByte() && b[1] == 0x50.toByte() && b[2] == 0x4E.toByte() && b[3] == 0x47.toByte() -> "image/png"
            b.size >= 12 && String(b, 0, 4, Charsets.ISO_8859_1) == "RIFF" && String(b, 8, 4, Charsets.ISO_8859_1) == "WEBP" -> "image/webp"
            else -> null
        }
    }

    /** The release's pictures; when it has none (404), those of its release group. */
    suspend fun list(releaseMbid: String?, releaseGroupMbid: String?): List<CaaImage> {
        for (url in listOfNotNull(releaseMbid?.takeIf { it.isNotEmpty() }?.let { "https://coverartarchive.org/release/$it" }, releaseGroupMbid?.takeIf { it.isNotEmpty() }?.let { "https://coverartarchive.org/release-group/$it" })) {
            val r = gate.run { http.get(url, mapOf("User-Agent" to userAgent, "Accept" to "application/json")) }
            if (r.code == 200) { val l = parse(r.body); if (l.isNotEmpty()) return l }
            else if (r.code != 404) throw java.io.IOException("Cover Art Archive answered ${r.code}")
        }
        return emptyList()
    }
}

/** What the person chose as the MusicBrainz match for a song, kept beside the library: never written into the file or into Jellyfin. */
class MbChoice(val mbid: String, val title: String, val artist: String, val album: String, val releaseMbid: String?, val releaseGroupMbid: String?, val year: Int?, val chosenMs: Long)

class MbOverlay(private val file: File) {
    private var map: LinkedHashMap<String, MbChoice>? = null
    private fun load(): LinkedHashMap<String, MbChoice> {
        map?.let { return it }
        val m = LinkedHashMap<String, MbChoice>()
        try {
            if (file.exists()) { val o = JSONObject(file.readText()); for (k in o.keys()) { val c = o.getJSONObject(k)
                m[k] = MbChoice(c.getString("m"), c.optString("t"), c.optString("a"), c.optString("al"), c.optString("r").ifEmpty { null }, c.optString("g").ifEmpty { null }, if (c.has("y")) c.optInt("y") else null, c.optLong("c")) } }
        } catch (_: Exception) { m.clear() }
        map = m; return m
    }

    @Synchronized fun get(key: String): MbChoice? = load()[key]
    @Synchronized fun set(key: String, c: MbCandidate, nowMs: Long = System.currentTimeMillis()) { load()[key] = MbChoice(c.mbid, c.title, c.artist, c.album, c.releaseMbid, c.releaseGroupMbid, c.year, nowMs); save() }
    @Synchronized fun clear(key: String) { if (load().remove(key) != null) save() }

    private fun save() {
        try {
            val o = JSONObject(); for ((k, c) in load()) o.put(k, JSONObject().put("m", c.mbid).put("t", c.title).put("a", c.artist).put("al", c.album).put("r", c.releaseMbid ?: "").put("g", c.releaseGroupMbid ?: "").also { j -> c.year?.let { j.put("y", it) } }.put("c", c.chosenMs))
            file.parentFile?.mkdirs(); val tmp = File(file.path + ".tmp"); tmp.writeText(o.toString()); if (!tmp.renameTo(file)) { file.delete(); tmp.renameTo(file) }
        } catch (_: Exception) {}
    }
}

/** Covers a person chose to replace a cover with (one picture file per art key); the original cover and the song files are never touched. */
class CoverPins(private val dir: File) {
    private fun name(key: String) = MessageDigest.getInstance("SHA-1").digest(key.toByteArray()).joinToString("") { "%02x".format(it) }.take(24)
    fun file(key: String): File? = File(dir, name(key) + ".img").takeIf { it.exists() && it.length() > 0 }
    fun has(key: String) = file(key) != null
    /** Keeps [bytes] (checked to be a picture) as the cover for [key]; false when they are not a picture. */
    fun set(key: String, bytes: ByteArray): Boolean {
        if (CaaClient.sniff(bytes) == null || bytes.size > CaaClient.MAX_BYTES) return false
        dir.mkdirs(); val f = File(dir, name(key) + ".img"); val tmp = File(f.path + ".tmp"); tmp.writeBytes(bytes)
        if (!tmp.renameTo(f)) { f.delete(); if (!tmp.renameTo(f)) return false }
        return true
    }
    fun clear(key: String) { File(dir, name(key) + ".img").delete() }
}
