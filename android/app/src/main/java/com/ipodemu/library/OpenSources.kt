package com.ipodemu.library

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Deferred
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull
import org.json.JSONArray
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL
import java.net.URLEncoder
import kotlin.math.abs

/** A file one of the open sources offers: a plain https URL the file mover can fetch. [source] is the tag shown with the download. */
data class OpenHit(val source: String, val label: String, val url: String, val ext: String, val durationSec: Int)

/**
 * Supplementary download sources that only offer music which is free to fetch: the Internet Archive's curated live-music and
 * netlabel collections, Jamendo (only tracks whose artist allows downloads) and Audius. They look for a match at the same time and
 * the first one that has the song wins; the rest are cancelled. They never download anything themselves (that happens only if
 * Soulseek found nothing), so they cannot slow or block the Soulseek/Lidarr path. Same behaviour as the FLACie Web OpenSources.
 */
object OpenSources {
    private val allowedSuffixes = listOf("archive.org", "jamendo.com", "audius.co", "audius.org")

    /** Audius serves from whichever discovery node api.audius.co names, so any https host is fine for it. */
    fun allowed(hit: OpenHit): Boolean {
        val u = try { URL(hit.url) } catch (_: Exception) { return false }
        if (u.protocol != "https") return false
        return hit.source == "audius" || allowedSuffixes.any { u.host == it || u.host.endsWith(".$it") }
    }

    /** The finders (not yt-dlp, which downloads in one go) running for one song; call [cancel] when the song is settled. */
    class Finders(val tasks: Map<String, Deferred<OpenHit?>>) { fun cancel() = tasks.values.forEach { it.cancel() } }

    /** Starts a search in every enabled finder source (Internet Archive, Audius, Jamendo) at once, each answering on its own task; [budgetMs] bounds each. */
    fun start(scope: CoroutineScope, artist: String, title: String, durationSec: Int, cfg: OpenSourceSettings, budgetMs: Long = 25_000): Finders {
        val tasks = LinkedHashMap<String, Deferred<OpenHit?>>()
        for (id in cfg.active()) {
            if (id == OpenSourceSettings.YTDL) continue
            tasks[id] = scope.async(Dispatchers.IO) {
                try {
                    withTimeoutOrNull(budgetMs) {
                        when (id) {
                            OpenSourceSettings.ARCHIVE -> archive(artist, title, durationSec)
                            OpenSourceSettings.AUDIUS -> audius(artist, title, durationSec)
                            else -> jamendo(cfg.jamendoId, artist, title, durationSec)
                        }
                    }?.takeIf { allowed(it) }
                } catch (e: CancellationException) { throw e } catch (_: Exception) { null }
            }
        }
        return Finders(tasks)
    }

    /** Takes the hit of the highest-priority finder source that has the song (the settings order): the first-ranked source's answer is waited for, and
     * only if it has nothing the next one's (already running) is used. Null when nobody has it. */
    suspend fun find(artist: String, title: String, durationSec: Int, cfg: OpenSourceSettings, budgetMs: Long = 25_000): OpenHit? =
        coroutineScope {
            val finders = start(this, artist, title, durationSec, cfg, budgetMs)
            var found: OpenHit? = null
            for (id in cfg.active()) { val hit = finders.tasks[id]?.await(); if (hit != null) { found = hit; break } }
            finders.cancel()
            found
        }

    /** Same song: the title (feat. and punctuation ignored) equal, the artist's lead name equal, and a length within 8 seconds when both are known. */
    fun matches(wantArtist: String, wantTitle: String, wantSec: Int, gotArtist: String, gotTitle: String, gotSec: Int): Boolean {
        if (normTitle(wantTitle) != normTitle(gotTitle)) return false
        val a = primaryArtist(wantArtist); val b = primaryArtist(gotArtist)
        if (a.isEmpty() || b.isEmpty() || !(a == b || a.contains(b) || b.contains(a))) return false
        return wantSec <= 0 || gotSec <= 0 || abs(wantSec - gotSec) <= 8
    }

    private suspend fun getJson(url: String): Any? = withContext(Dispatchers.IO) {
        val c = URL(url).openConnection() as HttpURLConnection
        c.connectTimeout = 8000; c.readTimeout = 12000
        c.setRequestProperty("User-Agent", "FLACie/1.0 (personal music player)")
        try {
            if (c.responseCode !in 200..299) null
            else org.json.JSONTokener(c.inputStream.bufferedReader().readText()).nextValue()
        } finally { c.disconnect() }
    }

    private fun enc(s: String) = URLEncoder.encode(s, "UTF-8")
    private fun sec(v: Any?): Int = when (v) {
        is Number -> v.toInt()
        is String -> v.toDoubleOrNull()?.toInt() ?: v.split(':').fold(0) { t, p -> t * 60 + (p.toIntOrNull() ?: return 0) }
        else -> 0
    }

    /** Internet Archive, restricted to the curated music collections (Live Music Archive "etree" and "netlabels"), never a general search. */
    private suspend fun archive(artist: String, title: String, durationSec: Int): OpenHit? {
        val who = primaryArtist(artist).replace(Regex("[^\\p{L}\\p{N} ]"), " ").trim()
        if (who.isEmpty()) return null
        val q = "(collection:etree OR collection:netlabels) AND (mediatype:etree OR mediatype:audio) AND creator:($who)"
        val docs = ((getJson("https://archive.org/advancedsearch.php?q=${enc(q)}&fl%5B%5D=identifier&sort%5B%5D=downloads+desc&rows=6&output=json") as? JSONObject)
            ?.optJSONObject("response")?.optJSONArray("docs")) ?: return null
        for (i in 0 until docs.length()) {
            val id = docs.getJSONObject(i).optString("identifier").takeIf { it.isNotEmpty() } ?: continue
            val files = (getJson("https://archive.org/metadata/${enc(id)}/files") as? JSONObject)?.optJSONArray("result") ?: continue
            var best: OpenHit? = null; var bestRank = 0
            for (j in 0 until files.length()) {
                val f = files.getJSONObject(j)
                val name = f.optString("name"); val fmt = f.optString("format"); val ft = f.optString("title")
                if (name.isEmpty() || ft.isEmpty()) continue
                val rank = when {
                    fmt.equals("Flac", true) || fmt.equals("24bit Flac", true) -> 3
                    fmt.contains("MP3", true) && !fmt.contains("64Kbps") -> 2
                    else -> 0
                }
                if (rank <= bestRank || !matches(artist, title, durationSec, artist, ft, sec(f.opt("length")))) continue
                best = OpenHit("archive", "the Internet Archive", "https://archive.org/download/${enc(id)}/" + name.split('/').joinToString("/") { enc(it).replace("+", "%20") },
                    name.substringAfterLast('.').lowercase(), sec(f.opt("length")))
                bestRank = rank
            }
            if (best != null) return best
        }
        return null
    }

    /** Jamendo, only tracks the artist allows to be downloaded (audiodownload_allowed). Needs a free client id. */
    private suspend fun jamendo(clientId: String, artist: String, title: String, durationSec: Int): OpenHit? {
        val url = "https://api.jamendo.com/v3.0/tracks/?client_id=${enc(clientId)}&format=json&limit=10" +
            "&namesearch=${enc(normTitle(title))}&artist_name=${enc(primaryArtist(artist))}"
        val results = (getJson(url) as? JSONObject)?.optJSONArray("results") ?: return null
        for (i in 0 until results.length()) {
            val r = results.getJSONObject(i)
            // asked for in the query and checked again: a track that does not say so explicitly is not downloaded
            if (r.optBoolean("audiodownload_allowed", false).not()) continue
            val dl = r.optString("audiodownload").takeIf { it.isNotEmpty() } ?: continue
            if (!matches(artist, title, durationSec, r.optString("artist_name"), r.optString("name"), sec(r.opt("duration")))) continue
            return OpenHit("jamendo", "Jamendo", dl, "mp3", sec(r.opt("duration")))
        }
        return null
    }

    /** Audius: the public REST API, no key. A node is picked from api.audius.co, tracks are found by search and fetched from the stream endpoint. */
    private suspend fun audius(artist: String, title: String, durationSec: Int): OpenHit? {
        val hosts = (getJson("https://api.audius.co") as? JSONObject)?.optJSONArray("data") ?: return null
        val host = (0 until hosts.length()).map { hosts.optString(it) }.firstOrNull { it.startsWith("https://") }?.trimEnd('/') ?: return null
        val q = "${primaryArtist(artist)} ${normTitle(title)}"
        val results = (getJson("$host/v1/tracks/search?query=${enc(q)}&app_name=FLACie&limit=10") as? JSONObject)?.optJSONArray("data") ?: return null
        for (i in 0 until results.length()) {
            val r = results.getJSONObject(i)
            val id = r.optString("id").takeIf { it.isNotEmpty() } ?: continue
            if (r.has("is_streamable") && !r.optBoolean("is_streamable", true)) continue
            if (!matches(artist, title, durationSec, r.optJSONObject("user")?.optString("name").orEmpty(), r.optString("title"), sec(r.opt("duration")))) continue
            return OpenHit("audius", "Audius", "$host/v1/tracks/$id/stream?app_name=FLACie", "mp3", sec(r.opt("duration")))
        }
        return null
    }
}

/** Settings > Downloads > Open sources: checks a Jamendo client id by asking the API for one track. */
suspend fun testJamendoClientId(clientId: String): Pair<Boolean, String?> = withContext(Dispatchers.IO) {
    try {
        val c = URL("https://api.jamendo.com/v3.0/tracks/?client_id=${URLEncoder.encode(clientId, "UTF-8")}&format=json&limit=1").openConnection() as HttpURLConnection
        c.connectTimeout = 8000; c.readTimeout = 12000
        try {
            val h = JSONObject(c.inputStream.bufferedReader().readText()).optJSONObject("headers")
            if (h?.optString("status") == "success") true to null else false to (h?.optString("error_message")?.takeIf { it.isNotBlank() } ?: "Jamendo did not accept that client id")
        } finally { c.disconnect() }
    } catch (e: Exception) { false to (e.message ?: "Could not reach Jamendo") }
}

/** Which open sources are on and in what order they are preferred; kept in the account profile's `services.opensources` (the Jamendo client id in
 * `services.jamendo.id`) so every device and FLACie Web share one setup. Jamendo only counts as on with a client id. */
data class OpenSourceSettings(val archive: Boolean, val audius: Boolean, val jamendo: Boolean, val jamendoId: String, val order: List<String>, val ytdl: Boolean = false) {
    companion object {
        const val ARCHIVE = "archive"; const val AUDIUS = "audius"; const val JAMENDO = "jamendo"; const val YTDL = "ytdl"
        val ALL = listOf(ARCHIVE, AUDIUS, JAMENDO, YTDL)
    }
    /** The saved order with anything unknown dropped and anything missing appended, so every source appears exactly once. */
    fun fullOrder(): List<String> = order.filter { it in ALL }.distinct() + ALL.filter { it !in order }
    fun isOn(id: String) = when (id) { ARCHIVE -> archive; AUDIUS -> audius; YTDL -> ytdl; else -> jamendo }
    /** The sources that will actually be searched, best first. */
    fun active(): List<String> = fullOrder().filter { isOn(it) && (it != JAMENDO || jamendoId.isNotBlank()) }
    fun any() = active().isNotEmpty()
}
