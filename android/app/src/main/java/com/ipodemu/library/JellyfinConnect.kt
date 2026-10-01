package com.ipodemu.library

import android.os.Handler
import android.os.Looper
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import com.ipodemu.App
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import org.json.JSONArray
import org.json.JSONObject
import java.util.concurrent.TimeUnit

/**
 * Connect, like Spotify Connect, on Jellyfin's own session API: this device reports what it plays (so the web UI, the
 * Windows app or another phone signed in to the same Jellyfin user can see it), accepts remote commands over Jellyfin's
 * WebSocket (play/pause, next, seek, play these songs), and can list, control, take over from and send to the user's
 * other sessions. Only songs Jellyfin has can travel between devices; local files play only where they are.
 * Jams (SyncPlay) listen on the same WebSocket through [listeners].
 */
class JellyfinConnect(private val app: App) {
    /** Another session of this user (or this device, [isSelf]) and what it is playing. */
    data class Session(
        val id: String, val device: String, val client: String, val user: String, val isSelf: Boolean, val controllable: Boolean,
        val itemId: String?, val title: String, val artist: String, val positionMs: Long, val durationMs: Long, val paused: Boolean,
        val queue: List<String>, val queueIndex: Int,
    )

    var sessions by mutableStateOf<List<Session>>(emptyList()); private set
    var connected by mutableStateOf(false); private set
    /** Extra handlers for WebSocket messages (MessageType, Data); Jams use this for SyncPlay. */
    val listeners = java.util.concurrent.CopyOnWriteArrayList<(String, JSONObject?) -> Unit>()

    private val prefs get() = app.prefs
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val main = Handler(Looper.getMainLooper())
    private val http = OkHttpClient.Builder().connectTimeout(10, TimeUnit.SECONDS).readTimeout(0, TimeUnit.SECONDS).pingInterval(25, TimeUnit.SECONDS).build()
    private var ws: WebSocket? = null
    private var wantSocket = false
    private var reportedItem: String? = null
    private var lastProgressAt = 0L
    private var lastPaused: Boolean? = null

    val available: Boolean get() = prefs.hasJellyfinAccount

    /** Called at start and after sign-in/out: opens (or closes) the live connection and starts reporting. */
    fun refresh() {
        if (!available) { stop(); return }
        if (ws == null) { wantSocket = true; scope.launch { postCapabilities(); openSocket() } }
    }

    fun stop() { wantSocket = false; ws?.close(1000, null); ws = null; connected = false; sessions = emptyList(); reportedItem = null }

    // ---- reporting what this device plays ----------------------------------------------------------------------

    private var reporterAttached = false
    /** Hooks the player: a new song, pause/resume and a seek report at once; progress every 10s while playing. */
    fun attach() {
        if (reporterAttached) return
        reporterAttached = true
        app.player.observe { report(false) }
        main.post(object : Runnable { override fun run() { report(true); main.postDelayed(this, 10_000) } })
    }

    private fun report(tick: Boolean) {
        if (!available || !connected) return
        val p = app.player
        val t = p.current
        val id = t?.let { app.library.jellyfinIdOf(it) }
        val paused = !p.wantsToPlay
        val pos = p.positionMs
        when {
            id == null -> if (reportedItem != null) { val old = reportedItem!!; reportedItem = null; post("/Sessions/Playing/Stopped", JSONObject().put("ItemId", old)) }
            id != reportedItem -> { reportedItem = id; lastPaused = paused; lastProgressAt = System.currentTimeMillis(); post("/Sessions/Playing", playState(id, pos, paused)) }
            paused != lastPaused || (tick && !paused && System.currentTimeMillis() - lastProgressAt >= 9_000) -> {
                lastPaused = paused; lastProgressAt = System.currentTimeMillis()
                post("/Sessions/Playing/Progress", playState(id, pos, paused).put("EventName", if (tick) "TimeUpdate" else if (paused) "Pause" else "Unpause"))
            }
        }
    }

    private fun playState(itemId: String, posMs: Long, paused: Boolean): JSONObject {
        // the queue travels as Jellyfin ids, so another device can take over the whole thing
        val q = app.player.queueTracks().mapNotNull { app.library.jellyfinIdOf(it) }.take(200)
        return JSONObject().put("ItemId", itemId).put("PositionTicks", posMs * 10_000).put("IsPaused", paused).put("CanSeek", true)
            .put("PlayMethod", "DirectPlay").put("RepeatMode", when (prefs.repeat) { 1 -> "RepeatAll"; 2 -> "RepeatOne"; else -> "RepeatNone" })
            .put("NowPlayingQueue", JSONArray().also { a -> q.forEachIndexed { i, x -> a.put(JSONObject().put("Id", x).put("PlaylistItemId", "q$i")) } })
            .put("PlaylistItemId", "q${q.indexOf(itemId).coerceAtLeast(0)}")
    }

    private fun postCapabilities() {
        post("/Sessions/Capabilities/Full", JSONObject().put("PlayableMediaTypes", JSONArray().put("Audio"))
            .put("SupportedCommands", JSONArray(listOf("SetVolume", "Mute", "Unmute", "DisplayMessage", "PlayState", "Play", "PlayNext", "PlayMediaSource")))
            .put("SupportsMediaControl", true).put("SupportsPersistentIdentifier", true))
    }

    // ---- the user's sessions -----------------------------------------------------------------------------------

    /** Lists this user's other active sessions (last 10 minutes) and what each plays. */
    fun loadSessions() = scope.launch {
        val arr = try { JSONArray(get("/Sessions?ControllableByUserId=${prefs.accountUserId}&ActiveWithinSeconds=600")) } catch (_: Exception) { return@launch }
        val out = (0 until arr.length()).map { arr.getJSONObject(it) }.filter { it.optString("UserId").equals(prefs.accountUserId, true) || it.optBoolean("SupportsRemoteControl") }
            .map { o ->
                val np = o.optJSONObject("NowPlayingItem"); val ps = o.optJSONObject("PlayState")
                val q = o.optJSONArray("NowPlayingQueue")?.let { a -> (0 until a.length()).map { a.getJSONObject(it).optString("Id") } } ?: emptyList()
                Session(
                    o.optString("Id"), o.optString("DeviceName"), o.optString("Client"), o.optString("UserName"),
                    o.optString("DeviceId") == prefs.deviceId, o.optBoolean("SupportsRemoteControl"),
                    np?.optString("Id"), np?.optString("Name").orEmpty(), np?.optJSONArray("Artists")?.optString(0) ?: np?.optString("AlbumArtist").orEmpty(),
                    (ps?.optLong("PositionTicks") ?: 0) / 10_000, (np?.optLong("RunTimeTicks") ?: 0) / 10_000, ps?.optBoolean("IsPaused") ?: true,
                    q, q.indexOf(np?.optString("Id")).coerceAtLeast(0),
                )
            }
            .sortedWith(compareBy({ !it.isSelf }, { it.itemId == null }, { it.device }))
        withContext(Dispatchers.Main) { sessions = out }
    }

    /** "PlayPause", "Pause", "Unpause", "NextTrack", "PreviousTrack", "Stop". */
    fun command(s: Session, cmd: String) = scope.launch { post("/Sessions/${s.id}/Playing/$cmd", null); delay(600); loadSessions() }

    fun seek(s: Session, ms: Long) = scope.launch { post("/Sessions/${s.id}/Playing/Seek?SeekPositionTicks=${ms * 10_000}", null) }

    /** Continues [s]'s queue here from the same second, and stops it there. */
    fun takeOver(s: Session) = scope.launch {
        val ids = s.queue.ifEmpty { listOfNotNull(s.itemId) }
        val tracks = ids.mapNotNull { id -> app.library.jellyfinTrack(id) ?: streamTrack(id) }
        if (tracks.isEmpty()) return@launch
        val idx = ids.indexOf(s.itemId).coerceAtLeast(0).coerceAtMost(tracks.lastIndex)
        withContext(Dispatchers.Main) { app.player.playFrom(tracks, idx, s.positionMs, paused = false) }
        post("/Sessions/${s.id}/Playing/Stop", null)
        delay(800); loadSessions()
    }

    /** Sends what plays here to [s] (same song, same second) and pauses it here. */
    fun sendTo(s: Session) = scope.launch {
        val p = app.player
        val q = withContext(Dispatchers.Main) { p.queueTracks() }
        val ids = q.mapNotNull { app.library.jellyfinIdOf(it) }
        val cur = withContext(Dispatchers.Main) { p.current }?.let { app.library.jellyfinIdOf(it) } ?: return@launch
        val pos = withContext(Dispatchers.Main) { p.positionMs }
        post("/Sessions/${s.id}/Playing?PlayCommand=PlayNow&ItemIds=${ids.joinToString(",")}&StartIndex=${ids.indexOf(cur).coerceAtLeast(0)}&StartPositionTicks=${pos * 10_000}", null)
        withContext(Dispatchers.Main) { p.pause() }
        delay(1200); loadSessions()
    }

    /** A Jellyfin song this library hasn't listed: streamed by id with whatever Jellyfin says it is. */
    fun streamTrack(id: String): Track? = try {
        val o = JSONObject(get("/Users/${prefs.accountUserId}/Items/$id"))
        Track(
            path = "${prefs.accountServer.trimEnd('/')}/Audio/$id/stream?static=true", title = o.optString("Name"),
            artist = o.optJSONArray("Artists")?.optString(0) ?: o.optString("AlbumArtist"), album = o.optString("Album"), albumArtist = o.optString("AlbumArtist"),
            genre = "", trackNo = o.optInt("IndexNumber"), discNo = o.optInt("ParentIndexNumber"), durationMs = o.optLong("RunTimeTicks") / 10_000, year = o.optInt("ProductionYear"),
            isMusic = true, artKey = o.optString("AlbumId").takeIf { it.isNotBlank() }?.let { "jf$it" } ?: "jf$id", mtime = 0, size = 0, source = TrackSource.JELLYFIN,
        )
    } catch (_: Exception) { null }

    // ---- the live connection -----------------------------------------------------------------------------------

    private fun openSocket() {
        val base = prefs.accountServer.trimEnd('/').replaceFirst("http", "ws")
        val req = Request.Builder().url("$base/socket?api_key=${prefs.accountToken}&deviceId=${prefs.deviceId}").build()
        ws = http.newWebSocket(req, object : WebSocketListener() {
            override fun onOpen(webSocket: WebSocket, response: Response) { main.post { connected = true; reportedItem = null; report(false) }; loadSessions() }
            override fun onMessage(webSocket: WebSocket, text: String) {
                val m = try { JSONObject(text) } catch (_: Exception) { return }
                val type = m.optString("MessageType"); val data = m.optJSONObject("Data")
                when (type) {
                    "ForceKeepAlive", "KeepAlive" -> webSocket.send("""{"MessageType":"KeepAlive"}""")
                    "Playstate" -> main.post { onPlaystate(data) }
                    "Play" -> onPlay(data)
                    "Sessions" -> loadSessions()
                }
                for (l in listeners) try { l(type, data) } catch (_: Exception) {}
            }
            override fun onClosed(webSocket: WebSocket, code: Int, reason: String) = dropped()
            override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) = dropped()
        })
    }

    private fun dropped() {
        ws = null; main.post { connected = false }
        // reconnect after a network change or server restart
        if (wantSocket) scope.launch { delay(5_000); if (wantSocket && ws == null && available) openSocket() }
    }

    private fun onPlaystate(d: JSONObject?) {
        val p = app.player
        when (d?.optString("Command")) {
            "PlayPause" -> p.toggle()
            "Pause" -> p.pause()
            "Unpause" -> p.resume()
            "NextTrack" -> p.next()
            "PreviousTrack" -> p.prev()
            "Stop" -> p.pause()
            "Seek" -> p.seekTo(d.optLong("SeekPositionTicks") / 10_000)
        }
    }

    private fun onPlay(d: JSONObject?) = scope.launch {
        val ids = d?.optJSONArray("ItemIds")?.let { a -> (0 until a.length()).map { a.getString(it) } } ?: return@launch
        val tracks = ids.mapNotNull { app.library.jellyfinTrack(it) ?: streamTrack(it) }
        if (tracks.isEmpty()) return@launch
        withContext(Dispatchers.Main) {
            val p = app.player
            when (d.optString("PlayCommand")) {
                "PlayNext" -> tracks.reversed().forEach { p.addNext(it) }
                "PlayLast" -> tracks.forEach { p.addToQueue(it) }
                else -> p.playFrom(tracks, d.optInt("StartIndex").coerceIn(0, tracks.lastIndex), d.optLong("StartPositionTicks") / 10_000)
            }
        }
    }

    // ---- plumbing ----------------------------------------------------------------------------------------------

    // same client and device as the sign-in, so this is one session in Jellyfin, not a second
    private fun auth() = "MediaBrowser Client=\"${AccountSync.CLIENT}\", Device=\"${android.os.Build.MODEL.replace("\"", "")}\", DeviceId=\"${prefs.deviceId}\", Version=\"${AccountSync.VERSION}\", Token=\"${prefs.accountToken}\""

    private fun get(path: String): String =
        http.newCall(Request.Builder().url(prefs.accountServer.trimEnd('/') + path).header("Authorization", auth()).build()).execute().use { r ->
            if (!r.isSuccessful) throw java.io.IOException("HTTP ${r.code}"); r.body?.string().orEmpty()
        }

    fun post(path: String, body: JSONObject?) = scope.launch {
        try {
            http.newCall(Request.Builder().url(prefs.accountServer.trimEnd('/') + path).header("Authorization", auth())
                .post((body?.toString() ?: "").toRequestBody("application/json".toMediaType())).build()).execute().close()
        } catch (_: Exception) {}
    }
}
