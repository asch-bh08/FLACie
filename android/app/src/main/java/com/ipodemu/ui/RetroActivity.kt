package com.ipodemu.ui

import android.annotation.SuppressLint
import android.graphics.Bitmap
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.view.WindowManager
import android.webkit.JavascriptInterface
import android.webkit.WebResourceRequest
import android.webkit.WebResourceResponse
import android.webkit.WebView
import android.webkit.WebViewClient
import androidx.activity.ComponentActivity
import androidx.annotation.OptIn
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import androidx.media3.common.util.UnstableApi
import com.ipodemu.App
import com.ipodemu.library.Track
import com.ipodemu.playback.LiveAnalysis
import org.json.JSONArray
import org.json.JSONObject
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream

/**
 * Retro: FLACie's iPods, the desk and the listening scenes, as on the web. The page is FLACie.Retro built for the browser (ipodsync/src/FLACie.RetroWasm,
 * copied into assets/retro by the retroWasm Gradle task) and runs from the app's own files, offline: no server. It plays nothing itself: its player
 * calls come here ([RetroBridge]) and go to the app's player, which goes on as before (queue, notification, headphones); the page reads back what plays,
 * the library and covers come from the app's library, and the visualisers get the spectrum of the decoded sound (no microphone). Back returns to FLACie.
 */
class RetroActivity : ComponentActivity() {
    private val app get() = App.of(this)
    private lateinit var web: WebView
    private lateinit var bridge: RetroBridge

    @SuppressLint("SetJavaScriptEnabled")
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        WindowCompat.setDecorFitsSystemWindows(window, false)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        window.attributes = window.attributes.also { it.layoutInDisplayCutoutMode = WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES }
        WindowInsetsControllerCompat(window, window.decorView).apply {
            hide(WindowInsetsCompat.Type.systemBars())
            systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
        }
        if (0 != (applicationInfo.flags and android.content.pm.ApplicationInfo.FLAG_DEBUGGABLE)) WebView.setWebContentsDebuggingEnabled(true)
        bridge = RetroBridge(app, this)
        web = WebView(this).apply {
            setBackgroundColor(0xFF0B0B0E.toInt())
            settings.javaScriptEnabled = true
            settings.domStorageEnabled = true
            settings.mediaPlaybackRequiresUserGesture = false
            settings.allowFileAccess = false
            settings.allowContentAccess = false
            addJavascriptInterface(bridge, "FLACieAndroid")
            webViewClient = RetroFiles(this@RetroActivity)
        }
        setContentView(web)
        web.loadUrl("https://$HOST/retro")
    }

    override fun onResume() { super.onResume(); bridge.watch(true) }
    override fun onPause() { bridge.watch(false); super.onPause() }
    override fun onDestroy() { web.removeJavascriptInterface("FLACieAndroid"); web.destroy(); super.onDestroy() }

    companion object {
        /** The page's own address: served from the APK (see [RetroFiles]), never fetched from the network. */
        const val HOST = "appassets.androidplatform.net"
    }
}

/** Serves the Retro page from assets/retro, covers from the app's cover cache (/art/<key>), and the page itself for its routes (/retro, /ipod). */
private class RetroFiles(private val activity: RetroActivity) : WebViewClient() {
    private val app get() = App.of(activity)

    override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean = request.url.host != RetroActivity.HOST

    override fun shouldInterceptRequest(view: WebView, request: WebResourceRequest): WebResourceResponse? {
        val url = request.url
        if (url.host != RetroActivity.HOST) return null
        val path = url.path ?: "/"
        if (path.startsWith("/art/")) return art(url.lastPathSegment ?: "", url.getQueryParameter("s") == "full")
        val name = path.trimStart('/')
        val file = if (name.isEmpty() || !name.substringAfterLast('/').contains('.')) "index.html" else name.replaceFirst("_framework/", "framework/")
        return try {
            WebResourceResponse(mime(file), if (file.endsWith(".html") || file.endsWith(".js") || file.endsWith(".css") || file.endsWith(".json")) "utf-8" else null, activity.assets.open("retro/$file"))
                .also { it.responseHeaders = mapOf("Cache-Control" to "no-cache") }
        } catch (_: java.io.IOException) {
            WebResourceResponse("text/plain", "utf-8", 404, "Not found", emptyMap(), ByteArrayInputStream(ByteArray(0)))
        }
    }

    private fun art(key: String, full: Boolean): WebResourceResponse {
        val bmp: Bitmap? = try { kotlinx.coroutines.runBlocking { app.art.load(key, full = full) } } catch (_: Exception) { null }
        if (bmp == null) return WebResourceResponse("image/png", null, 404, "No cover", emptyMap(), ByteArrayInputStream(ByteArray(0)))
        val out = ByteArrayOutputStream()
        bmp.compress(Bitmap.CompressFormat.JPEG, 90, out)
        return WebResourceResponse("image/jpeg", null, 200, "OK", mapOf("Cache-Control" to "max-age=86400"), ByteArrayInputStream(out.toByteArray()))
    }

    private fun mime(f: String) = when (f.substringAfterLast('.').lowercase()) {
        "html" -> "text/html"; "js" -> "text/javascript"; "css" -> "text/css"; "json" -> "application/json"; "wasm" -> "application/wasm"
        "svg" -> "image/svg+xml"; "png" -> "image/png"; "jpg", "jpeg" -> "image/jpeg"; "webp" -> "image/webp"; "gif" -> "image/gif"
        "woff2" -> "font/woff2"; "woff" -> "font/woff"; "ttf" -> "font/ttf"; "otf" -> "font/otf"; "dat" -> "application/octet-stream"
        else -> "application/octet-stream"
    }
}

/**
 * What the Retro page asks of the app (window.FLACieAndroid.call(name, arg) in bridge.js; FLACie.RetroWasm/Bridge.cs). Calls come on the WebView's own
 * thread and are answered at once; anything that touches the player runs on the main thread, waited for.
 */
@OptIn(UnstableApi::class)
class RetroBridge(private val app: App, private val activity: ComponentActivity) {
    private val main = Handler(Looper.getMainLooper())
    private val player get() = app.player
    private val analysis = LiveAnalysis(player.live) { player.volume }
    private var analysedAt = 0L
    @Volatile private var byPath: Map<String, Track> = emptyMap()

    /** What the player is doing, as the main thread last saw it (ten times a second while Retro is open). The page reads this, never the player:
     * a call that waited for the main thread could deadlock, as the main thread itself waits for the WebView to draw. */
    private class Snap(val path: String?, val playing: Boolean, val pos: Long, val dur: Long, val index: Int, val queue: List<String>, val ready: Boolean, val volume: Float)
    @Volatile private var snap = Snap(null, false, 0, 0, -1, emptyList(), false, 1f)
    private var queueRev = -1
    private var queueNow: List<String> = emptyList()
    @Volatile private var queueSongs: List<Track> = emptyList()
    private val tick = object : Runnable { override fun run() { capture(); main.postDelayed(this, 100) } }

    private fun capture() {
        val p = player
        if (p.queueRev != queueRev || queueNow.isEmpty()) { queueRev = p.queueRev; queueSongs = p.queueTracks(); queueNow = queueSongs.map { it.path } }
        snap = Snap(p.current?.path, p.wantsToPlay, p.positionMs, p.durationMs, p.exo.currentMediaItemIndex, queueNow, p.buffered, p.volume)
    }

    fun watch(on: Boolean) {
        player.live.watching = on
        main.removeCallbacks(tick)
        if (on) main.post(tick)
    }

    /** Runs on the main thread, later; the snapshot is taken again straight after. */
    private fun post(block: () -> Unit) { main.post { try { block() } catch (e: Exception) { android.util.Log.w("FLACie", "retro: ${e.message}") }; capture() } }

    @JavascriptInterface
    fun call(name: String, arg: String): String = try {
        when (name) {
            "version" -> try { activity.packageManager.getPackageInfo(activity.packageName, 0).versionName ?: "" } catch (_: Exception) { "" }
            "library" -> library()
            "lists" -> lists()
            "fetch" -> fetch(arg)
            "fetched" -> fetches.remove(arg) ?: ""
            "state" -> state(arg == "queue")
            "load" -> { load(JSONObject(arg)); "" }
            "toggle" -> { post { player.toggle() }; "" }
            "play" -> { post { player.resume() }; "" }
            "pause" -> { post { player.pause() }; "" }
            "seek" -> { arg.toLongOrNull()?.let { ms -> post { player.seekTo(ms) } }; "" }
            "ready" -> if (snap.ready) "1" else "0"
            "volume" -> { val v = arg.toFloatOrNull(); if (v != null) post { player.volume = v }; (v ?: snap.volume).toString() }
            "speed" -> { arg.toFloatOrNull()?.let { s -> post { player.setSpeed(s) } }; "" }
            "facts" -> facts(arg)
            "frame" -> frame()
            "close" -> { main.post { activity.finish() }; "" }
            else -> ""
        }
    } catch (e: Exception) { android.util.Log.w("FLACie", "retro $name: ${e.message}"); "" }

    /** Every song of the app's library (all its sources), in the short form Bridge.cs reads, with the songs of the queue and of the playlists
     * that the library list lacks (a mix from Home, a song on another server): Retro then knows the song playing and every playlist's songs. */
    private fun library(): String {
        val songs = app.library.songs()
        val all = LinkedHashMap<String, Track>(songs.size + 64)
        for (t in songs) all[t.path] = t
        for (t in queueSongs) all.putIfAbsent(t.path, t)
        for (pl in com.ipodemu.player.shownPlaylists(app)) for (p in pl.paths.toList()) if (p !in all) app.library.resolve(p, app.userData.meta[p])?.let { all[p] = it }
        for (p in app.userData.favorites.toList()) if (p !in all) app.library.resolve(p, app.userData.meta[p])?.let { all[p] = it }
        byPath = all
        val a = JSONArray()
        for (t in all.values) a.put(JSONObject().apply {
            put("p", t.path); put("t", t.title); put("a", t.artist); put("al", t.album); put("aa", t.albumArtist); put("g", t.genre)
            put("n", t.trackNo); put("d", t.discNo); put("ms", t.durationMs); put("y", t.year); put("art", t.artKey ?: JSONObject.NULL)
            put("add", t.mtime); put("sz", t.size); put("f", t.filePath)
        })
        return a.toString()
    }

    /** The playlists the Playlists screen shows and the favourites (newest first), as the songs' paths: {"pl":[{"n":name,"p":[paths]}],"fav":[paths]}. */
    private fun lists(): String = JSONObject().apply {
        put("pl", JSONArray().apply { for (pl in com.ipodemu.player.shownPlaylists(app)) put(JSONObject().put("n", pl.name).put("p", JSONArray(pl.paths.toList()))) })
        put("fav", JSONArray(app.userData.favorites.toList().asReversed()))
    }.toString()

    private val fetches = java.util.concurrent.ConcurrentHashMap<String, String>()
    private val fetchId = java.util.concurrent.atomic.AtomicInteger()

    /** Starts a GET for the page (lyrics, the catalog: https only) on a background thread; the page asks "fetched" for "<status>\n<body>" until it is in. */
    private fun fetch(url: String): String {
        if (!url.startsWith("https://")) return ""
        val id = fetchId.incrementAndGet().toString()
        Thread {
            fetches[id] = try {
                val c = java.net.URL(url).openConnection() as java.net.HttpURLConnection
                c.connectTimeout = 8_000; c.readTimeout = 10_000
                c.setRequestProperty("User-Agent", "FLACie/1.0 (Android music player)")
                try { val code = c.responseCode; "$code\n" + (if (code in 200..299) c.inputStream else c.errorStream)?.bufferedReader()?.use { it.readText() }.orEmpty() } finally { c.disconnect() }
            } catch (_: Exception) { "502\n" }
        }.start()
        return id
    }

    private fun state(withQueue: Boolean): String {
        val n = snap
        return JSONObject().apply {
            put("p", n.path ?: JSONObject.NULL); put("pl", n.playing); put("pos", n.pos); put("dur", n.dur); put("i", n.index)
            if (withQueue) put("q", JSONArray(n.queue))
        }.toString()
    }

    /** The page chose a song (with its queue): the app plays it. Asking for the song already playing changes nothing, so the two never fight. */
    private fun load(o: JSONObject) {
        val paths = o.getJSONArray("q").let { q -> List(q.length()) { q.getString(it) } }
        val i = o.optInt("i", 0).coerceIn(0, (paths.size - 1).coerceAtLeast(0))
        val play = o.optBoolean("play", true)
        val ms = o.optLong("ms", 0L)
        if (paths.isEmpty()) return
        // until the main thread has done it, the page is told the chosen song plays (else it would follow the old one back)
        snap.let { n -> snap = Snap(paths[i], play, ms, n.dur, n.index, n.queue, false, n.volume) }
        post {
            val p = player
            val want = paths[i]
            if (p.current?.path == want) {
                if (ms > 0 && kotlin.math.abs(p.positionMs - ms) > 2_500) p.seekTo(ms)
                if (play && !p.wantsToPlay) p.resume()
                return@post
            }
            val queue = p.queueTracks()
            if (queue.map { it.path } == paths) { p.skipTo(i); if (!play) p.pause(); return@post }
            val known = byPath.ifEmpty { app.library.songs().associateBy { it.path } }
            val inQueue = queue.associateBy { it.path }
            val tracks = paths.map { known[it] ?: inQueue[it] }
            val list = tracks.filterNotNull()
            if (list.isEmpty()) return@post
            val at = tracks.take(i).count { it != null }.coerceIn(0, list.size - 1)
            p.playFrom(list, at, ms, paused = !play)
        }
    }

    private fun facts(path: String): String {
        val t = byPath[path] ?: return ""
        val f = com.ipodemu.library.AudioFacts.of(t) ?: return ""
        return JSONObject().apply { put("codec", f.codec); put("rate", f.rate); put("bits", f.depth); put("kbps", f.kbps) }.toString()
    }

    /** One frame for the visualisers: "rate|playing|pos|dur|bins" (1024 levels, 0-255, as a browser's analyser gives them). */
    @Synchronized private fun frame(): String {
        val now = android.os.SystemClock.elapsedRealtime()
        if (now - analysedAt >= 25) { analysedAt = now; player.live.watching = true; analysis.update() }
        val n = snap
        val playing = n.playing; val pos = n.pos; val dur = n.dur
        val sb = StringBuilder(4200)
        sb.append(analysis.sampleRate).append('|').append(if (playing) '1' else '0').append('|').append(pos).append('|').append(dur).append('|')
        val f = analysis.freq
        for (k in f.indices) { if (k > 0) sb.append(','); sb.append(f[k]) }
        return sb.toString()
    }
}
