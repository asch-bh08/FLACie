package com.ipodemu.library

import com.ipodemu.Prefs
import org.json.JSONArray
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL

/**
 * Hands playlist files to FLACie Web, which does all the searching and downloading on the server (nothing is downloaded to the phone), and asks
 * how far it got. The phone proves who it is with its Jellyfin token and says which Jellyfin it uses; the server's address comes from the shared
 * account profile (FLACie Web writes it there when you open it in a browser).
 */
class FlacieWebClient(private val prefs: Prefs) {
    class Job(val id: String, val file: String, val state: String, val total: Int, val done: Int, val failed: Int, val note: String?)

    val available: Boolean get() = prefs.flacieWebUrl.isNotBlank() && prefs.hasJellyfinAccount

    private fun open(path: String, method: String): HttpURLConnection {
        val c = URL(prefs.flacieWebUrl.trimEnd('/') + path).openConnection() as HttpURLConnection
        c.requestMethod = method; c.connectTimeout = 10_000; c.readTimeout = 60_000
        c.setRequestProperty("X-Emby-Token", prefs.accountToken)
        c.setRequestProperty("X-Jellyfin-Server", prefs.accountServer.trimEnd('/'))
        return c
    }

    private fun parse(o: JSONObject) = Job(o.optString("id"), o.optString("file"), o.optString("state"), o.optInt("total"), o.optInt("done"), o.optInt("failed"), o.optString("note").takeIf { it.isNotBlank() && it != "null" })

    /** Sends the file; returns the new job, or throws with the server's message. */
    fun submit(name: String, bytes: ByteArray): Job {
        val c = open("/api/import?name=" + java.net.URLEncoder.encode(name, "UTF-8"), "POST")
        c.doOutput = true; c.setRequestProperty("Content-Type", "application/octet-stream"); c.setFixedLengthStreamingMode(bytes.size)
        try {
            c.outputStream.use { it.write(bytes) }
            val code = c.responseCode
            val text = (if (code in 200..299) c.inputStream else c.errorStream)?.bufferedReader()?.use { it.readText() } ?: ""
            if (code == 401) throw java.io.IOException("FLACie Web doesn't know this account. Open FLACie Web in a browser once, signed in to the same Jellyfin.")
            if (code !in 200..299) throw java.io.IOException(try { JSONObject(text).optString("error") } catch (_: Exception) { "" }.ifBlank { "HTTP $code" })
            return parse(JSONObject(text))
        } finally { c.disconnect() }
    }

    /** The server's own settings and storage, the same numbers as FLACie Web's Settings page. */
    class ServerSettings(
        val admin: Boolean, val prefetch: Boolean, val minFreeGb: Int, val chartsOn: Boolean, val lists: List<Int>, val perList: Int, val lastRun: String, val lastNote: String,
        val available: List<Pair<Int, String>>, val musicFree: Long, val musicTotal: Long, val nasSongs: Int, val nasBytes: Long,
    )
    class AccountStats(val songs: Int, val albums: Int, val artists: Int, val playlists: Int, val favourites: Int, val admin: Boolean = false)
    class ChartSong(val rank: Int, val title: String, val artist: String, val art: String?)

    private fun send(path: String, method: String, body: String? = null): String {
        val c = open(path, method)
        try {
            if (body != null) { val b = body.toByteArray(); c.doOutput = true; c.setRequestProperty("Content-Type", "application/json"); c.setFixedLengthStreamingMode(b.size); c.outputStream.use { it.write(b) } }
            val code = c.responseCode
            if (code == 401) throw java.io.IOException("FLACie Web doesn't know this account. Open FLACie Web in a browser once, signed in to the same Jellyfin.")
            if (code !in 200..299) throw java.io.IOException("HTTP $code")
            return c.inputStream.bufferedReader().use { it.readText() }
        } finally { c.disconnect() }
    }

    private fun parseSettings(t: String): ServerSettings {
        val o = JSONObject(t); val ch = o.getJSONObject("charts"); val st = o.getJSONObject("storage")
        val av = o.getJSONArray("available"); val ls = ch.getJSONArray("lists")
        return ServerSettings(o.optBoolean("admin"), o.optBoolean("prefetch"), o.optInt("minFreeGb"), ch.optBoolean("enabled"), List(ls.length()) { ls.getInt(it) }, ch.optInt("perList", 10),
            ch.optString("lastRun").takeIf { it != "null" } ?: "", ch.optString("lastNote").takeIf { it != "null" } ?: "",
            List(av.length()) { av.getJSONObject(it).let { a -> a.getInt("id") to a.getString("name") } },
            st.optLong("musicFree", -1), st.optLong("musicTotal", -1), st.optInt("nasSongs"), st.optLong("nasBytes"))
    }

    fun settings(): ServerSettings = parseSettings(send("/api/settings", "GET"))
    /** Sends only the fields given; returns the settings as the server now has them. */
    fun saveSettings(prefetch: Boolean? = null, minFreeGb: Int? = null, chartsOn: Boolean? = null, perList: Int? = null, lists: List<Int>? = null): ServerSettings {
        val o = JSONObject()
        prefetch?.let { o.put("prefetch", it) }; minFreeGb?.let { o.put("minFreeGb", it) }; chartsOn?.let { o.put("chartsEnabled", it) }; perList?.let { o.put("perList", it) }
        lists?.let { o.put("lists", JSONArray(it)) }
        return parseSettings(send("/api/settings", "POST", o.toString()))
    }
    fun runCharts() { send("/api/charts/run", "POST", "{}") }

    /** What the server is downloading right now and the log of what it fetched for this account (searches on the web, Autoplay, charts, imports). */
    class ServerDownloads(val running: List<RunningRow>, val log: List<DownloadRecord>)
    class RunningRow(val id: String, val label: String, val kind: String, val startedAt: Long, val message: String, val current: String?, val artKey: String?)
    fun downloads(): ServerDownloads {
        val o = JSONObject(send("/api/downloads", "GET"))
        fun ms(s: String) = try { java.time.Instant.parse(s).toEpochMilli() } catch (_: Exception) { 0L }
        val run = o.optJSONArray("running")?.let { a -> List(a.length()) { a.getJSONObject(it).let { r ->
            RunningRow(r.optString("id"), r.optString("label"), r.optString("kind"), ms(r.optString("startedAt")), r.optString("message"), r.optString("current").takeIf { c -> c.isNotEmpty() && !r.isNull("current") }, r.optString("artKey").takeIf { c -> c.isNotEmpty() && !r.isNull("artKey") })
        } } } ?: emptyList()
        val log = o.optJSONArray("log")?.let { a -> List(a.length()) { a.getJSONObject(it).let { r ->
            DownloadRecord(r.optString("id"), r.optString("label"), r.optString("kind"), ms(r.optString("startedAt")), ms(r.optString("finishedAt")), r.optString("outcome") == "done",
                r.optString("source").takeIf { c -> c.isNotEmpty() && !r.isNull("source") }, r.optString("message"), r.optString("file").takeIf { c -> c.isNotEmpty() && !r.isNull("file") },
                r.optString("artKey").takeIf { c -> c.isNotEmpty() && !r.isNull("artKey") },
                r.optJSONArray("trail")?.let { t -> List(t.length()) { i -> t.getJSONObject(i).let { s -> TrailStep(ms(s.optString("at")), s.optString("source").takeIf { c -> c.isNotEmpty() && !s.isNull("source") }, s.optString("text"), s.optBoolean("miss")) } } } ?: emptyList(),
                "server")
        } } } ?: emptyList()
        return ServerDownloads(run, log)
    }
    fun account(): AccountStats = JSONObject(send("/api/account", "GET")).let { AccountStats(it.optInt("songs"), it.optInt("albums"), it.optInt("artists"), it.optInt("playlists"), it.optInt("favourites"), it.optBoolean("admin")) }

    /** A chart (0 = Top Songs; the ids come from [settings]), as the server read it from the public charts. */
    fun chart(id: Int): List<ChartSong> { val a = JSONArray(send("/api/charts/$id", "GET")); return List(a.length()) { a.getJSONObject(it).let { o -> ChartSong(o.optInt("rank"), o.optString("title"), o.optString("artist"), o.optString("art").takeIf { s -> s.isNotBlank() && s != "null" }) } } }

    fun jobs(): List<Job> {
        val c = open("/api/import", "GET")
        try {
            val code = c.responseCode
            if (code !in 200..299) throw java.io.IOException("HTTP $code")
            val a = JSONArray(c.inputStream.bufferedReader().use { it.readText() })
            return List(a.length()) { parse(a.getJSONObject(it)) }
        } finally { c.disconnect() }
    }
}
