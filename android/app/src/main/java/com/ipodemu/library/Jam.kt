package com.ipodemu.library

import android.os.Handler
import android.os.Looper
import android.widget.Toast
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import com.ipodemu.App
import com.ipodemu.playback.PlayerController
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONArray
import org.json.JSONObject
import java.time.Instant
import java.util.concurrent.TimeUnit

/**
 * Jams: listening together on Jellyfin's SyncPlay. Anyone signed in to the same Jellyfin server can start a Jam or join
 * one; everyone hears the same song at the same moment, and anyone can play, pause, seek, skip or add to the queue.
 * While in a Jam, this device's player hands those actions to the group (see [PlayerController.Interceptor]) and only
 * plays what the group says, when the group says. Only Jellyfin songs can be shared; local files can't be heard elsewhere.
 * Jellyfin users need SyncPlay allowed (Dashboard > Users > the user > SyncPlay access).
 */
class Jam(private val app: App) {
    class Group(val id: String, val name: String, val people: List<String>)
    private class Entry(val itemId: String, val playlistItemId: String)

    var groups by mutableStateOf<List<Group>>(emptyList()); private set
    var groupId by mutableStateOf<String?>(null); private set
    var groupName by mutableStateOf(""); private set
    var people by mutableStateOf<List<String>>(emptyList()); private set
    var status by mutableStateOf<String?>(null); private set
    val inJam: Boolean get() = groupId != null

    private val prefs get() = app.prefs
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val main = Handler(Looper.getMainLooper())
    private val http = OkHttpClient.Builder().connectTimeout(10, TimeUnit.SECONDS).readTimeout(15, TimeUnit.SECONDS).build()
    private var playlist: List<Entry> = emptyList()
    private var playingIndex = 0
    private var playing = false
    /** Server clock minus this device's clock, so "unpause at When" happens at the same instant everywhere. */
    @Volatile private var offsetMs = 0L
    private var clockJob: Job? = null
    private var commandJob: Job? = null

    init {
        app.connect.listeners.add { type, data -> if (type == "SyncPlayGroupUpdate") onGroupUpdate(data) else if (type == "SyncPlayCommand") onCommand(data) }
    }

    // ---- joining ----------------------------------------------------------------------------------------------

    fun loadGroups() = scope.launch {
        val arr = try { JSONArray(get("/SyncPlay/List")) } catch (e: Exception) { setStatus(problem(e)); return@launch }
        val out = (0 until arr.length()).map { arr.getJSONObject(it) }.map { o ->
            Group(o.optString("GroupId"), o.optString("GroupName"), o.optJSONArray("Participants")?.let { a -> (0 until a.length()).map { a.getString(it) } } ?: emptyList())
        }
        withContext(Dispatchers.Main) { groups = out }
    }

    /** Starts a Jam with what plays here (if it's on Jellyfin), so the others hear it as soon as they join. */
    fun start() = scope.launch {
        try { post("/SyncPlay/New", JSONObject().put("GroupName", "${prefs.accountUserName.ifBlank { "FLACie" }}'s Jam")) } catch (e: Exception) { setStatus(problem(e)); return@launch }
        delay(800)
        val (ids, idx, pos) = withContext(Dispatchers.Main) {
            val p = app.player
            val q = p.queueTracks().mapNotNull { app.library.jellyfinIdOf(it) }
            val cur = p.current?.let { app.library.jellyfinIdOf(it) }
            Triple(q, q.indexOf(cur).coerceAtLeast(0), p.positionMs)
        }
        if (ids.isNotEmpty()) try {
            post("/SyncPlay/SetNewQueue", JSONObject().put("PlayingQueue", JSONArray(ids)).put("PlayingItemPosition", idx).put("StartPositionTicks", pos * 10_000))
        } catch (_: Exception) {}
    }

    fun join(g: Group) = scope.launch { try { post("/SyncPlay/Join", JSONObject().put("GroupId", g.id)) } catch (e: Exception) { setStatus(problem(e)) } }

    fun leave() = scope.launch { try { post("/SyncPlay/Leave", null) } catch (_: Exception) {}; withContext(Dispatchers.Main) { left() } }

    private fun left() {
        groupId = null; groupName = ""; people = emptyList(); playlist = emptyList()
        app.player.interceptor = null; app.player.onAutoAdvance = null
        clockJob?.cancel(); commandJob?.cancel()
    }

    // ---- messages from the group ------------------------------------------------------------------------------

    private fun onGroupUpdate(d: JSONObject?) {
        d ?: return
        val type = d.optString("Type")
        main.post {
            when (type) {
                "GroupJoined" -> {
                    val g = d.optJSONObject("Data")
                    groupId = d.optString("GroupId"); groupName = g?.optString("GroupName").orEmpty()
                    people = g?.optJSONArray("Participants")?.let { a -> (0 until a.length()).map { a.getString(it) } } ?: emptyList()
                    app.player.interceptor = interceptor; app.player.onAutoAdvance = { autoAdvance() }
                    syncClock(); setStatus(null)
                }
                "UserJoined" -> d.optString("Data").takeIf { it.isNotBlank() }?.let { n -> people = (people + n).distinct(); toast("$n joined the Jam") }
                "UserLeft" -> d.optString("Data").takeIf { it.isNotBlank() }?.let { n -> people = people - n; toast("$n left the Jam") }
                "GroupLeft", "NotInGroup", "GroupDoesNotExist" -> left()
                "LibraryAccessDenied" -> setStatus("Someone in the Jam can't access these songs in Jellyfin.")
                "PlayQueue" -> d.optJSONObject("Data")?.let { onPlayQueue(it) }
            }
        }
    }

    /** The group's queue changed (new queue, song added, skip): load the group's song here, paused, and say when ready. */
    private fun onPlayQueue(q: JSONObject) {
        val arr = q.optJSONArray("Playlist") ?: return
        val entries = (0 until arr.length()).map { arr.getJSONObject(it) }.map { Entry(it.optString("ItemId"), it.optString("PlaylistItemId")) }
        val idx = q.optInt("PlayingItemIndex").coerceIn(0, (entries.size - 1).coerceAtLeast(0))
        val start = q.optLong("StartPositionTicks") / 10_000
        val sameSong = entries.getOrNull(idx)?.playlistItemId == playlist.getOrNull(playingIndex)?.playlistItemId && playlist.isNotEmpty()
        playlist = entries; playingIndex = idx; playing = q.optBoolean("IsPlaying")
        if (entries.isEmpty()) { app.player.pause(); return }
        scope.launch {
            val tracks = entries.map { e -> app.library.jellyfinTrack(e.itemId) ?: app.connect.streamTrack(e.itemId) }
            if (tracks.any { it == null }) { setStatus("A song in the Jam isn't on this server."); return@launch }
            withContext(Dispatchers.Main) {
                if (!sameSong || q.optString("Reason") == "NewPlaylist") app.player.playFrom(tracks.filterNotNull(), idx, start, paused = true)
            }
            reportReady(start)
        }
    }

    /** Waits until the song is buffered, then tells the group this device is ready (the group then unpauses everyone). */
    private suspend fun reportReady(posMs: Long) {
        repeat(40) { if (withContext(Dispatchers.Main) { app.player.buffered }) return@repeat; delay(250) }
        val e = playlist.getOrNull(playingIndex) ?: return
        try {
            post("/SyncPlay/Ready", JSONObject().put("When", Instant.ofEpochMilli(serverNow()).toString()).put("PositionTicks", posMs * 10_000)
                .put("IsPlaying", playing).put("PlaylistItemId", e.playlistItemId))
        } catch (_: Exception) {}
    }

    /** Play, pause, seek at the server instant [When], corrected for this device's clock. */
    private fun onCommand(d: JSONObject?) {
        d ?: return
        val whenMs = try { Instant.parse(d.optString("When")).toEpochMilli() } catch (_: Exception) { serverNow() }
        val posMs = d.optLong("PositionTicks") / 10_000
        val cmd = d.optString("Command")
        commandJob?.cancel()
        commandJob = scope.launch {
            val wait = whenMs - serverNow()
            if (wait > 0) delay(wait)
            val late = (serverNow() - whenMs).coerceAtLeast(0)
            withContext(Dispatchers.Main) {
                val p = app.player
                when (cmd) {
                    "Unpause" -> { playing = true; p.rawSeek(posMs + late); p.resume() }
                    "Pause", "Stop" -> { playing = false; p.pause(); p.rawSeek(posMs) }
                    "Seek" -> { p.pause(); p.rawSeek(posMs) }
                }
            }
            if (cmd == "Seek") reportReady(posMs)
        }
    }

    /** A song ended here: ask the group to move on. Every device asks; the group moves once (later asks name an old song). */
    private fun autoAdvance() {
        app.player.pause()
        val e = playlist.getOrNull(playingIndex) ?: return
        scope.launch { try { post("/SyncPlay/NextItem", JSONObject().put("PlaylistItemId", e.playlistItemId)) } catch (_: Exception) {} }
    }

    // ---- this device's buttons go to the group -----------------------------------------------------------------

    private val interceptor = object : PlayerController.Interceptor {
        override fun toggle(): Boolean { send(if (app.player.wantsToPlay) "/SyncPlay/Pause" else "/SyncPlay/Unpause", null); return true }
        override fun seek(ms: Long): Boolean { send("/SyncPlay/Seek", JSONObject().put("PositionTicks", ms * 10_000)); return true }
        override fun next(): Boolean { playlist.getOrNull(playingIndex)?.let { send("/SyncPlay/NextItem", JSONObject().put("PlaylistItemId", it.playlistItemId)) }; return true }
        override fun prev(): Boolean { playlist.getOrNull(playingIndex)?.let { send("/SyncPlay/PreviousItem", JSONObject().put("PlaylistItemId", it.playlistItemId)) }; return true }
        override fun skipTo(index: Int): Boolean { playlist.getOrNull(index)?.let { send("/SyncPlay/SetPlaylistItem", JSONObject().put("PlaylistItemId", it.playlistItemId)) }; return true }
        override fun play(list: List<Track>, index: Int): Boolean {
            val ids = list.map { app.library.jellyfinIdOf(it) }
            val picked = ids.getOrNull(index)
            val shared = ids.filterNotNull()
            if (picked == null || shared.isEmpty()) { toast("Only songs on Jellyfin can play in a Jam"); return true }
            send("/SyncPlay/SetNewQueue", JSONObject().put("PlayingQueue", JSONArray(shared)).put("PlayingItemPosition", shared.indexOf(picked)).put("StartPositionTicks", 0))
            return true
        }
        override fun enqueue(t: Track, next: Boolean): Boolean {
            val id = app.library.jellyfinIdOf(t) ?: run { toast("Only songs on Jellyfin can join a Jam's queue"); return true }
            send("/SyncPlay/Queue", JSONObject().put("ItemIds", JSONArray().put(id)).put("Mode", if (next) "QueueNext" else "Queue"))
            toast(if (next) "Playing next in the Jam" else "Added to the Jam")
            return true
        }
    }

    private fun send(path: String, body: JSONObject?) = scope.launch { try { post(path, body) } catch (e: Exception) { setStatus(problem(e)) } }

    // ---- clock ---------------------------------------------------------------------------------------------------

    private fun serverNow() = System.currentTimeMillis() + offsetMs

    /** NTP-style: three samples of the server's clock, keeping the one with the shortest round trip; again each minute. */
    private fun syncClock() {
        clockJob?.cancel()
        clockJob = scope.launch {
            while (true) {
                var best: Pair<Long, Long>? = null
                repeat(3) {
                    try {
                        val t0 = System.currentTimeMillis()
                        val o = JSONObject(get("/GetUtcTime"))
                        val t3 = System.currentTimeMillis()
                        val t1 = Instant.parse(o.getString("RequestReceptionTime")).toEpochMilli()
                        val t2 = Instant.parse(o.getString("ResponseTransmissionTime")).toEpochMilli()
                        val rtt = (t3 - t0) - (t2 - t1); val off = ((t1 - t0) + (t2 - t3)) / 2
                        if (best == null || rtt < best!!.first) best = rtt to off
                    } catch (_: Exception) {}
                }
                best?.let { offsetMs = it.second }
                delay(60_000)
            }
        }
    }

    // ---- plumbing ----------------------------------------------------------------------------------------------

    private fun setStatus(s: String?) = main.post { status = s }
    private fun toast(m: String) = main.post { Toast.makeText(app, m, Toast.LENGTH_SHORT).show() }
    private fun problem(e: Exception) = if (e.message?.contains("403") == true) "Your Jellyfin user isn't allowed to use SyncPlay. An admin can turn it on in Dashboard > Users." else "Jam failed: ${e.message}"

    private fun auth() = "MediaBrowser Client=\"FLACie\", Device=\"${android.os.Build.MODEL.replace("\"", "")}\", DeviceId=\"${prefs.deviceId}\", Version=\"${AccountSync.VERSION}\", Token=\"${prefs.accountToken}\""

    private fun get(path: String): String =
        http.newCall(Request.Builder().url(prefs.accountServer.trimEnd('/') + path).header("Authorization", auth()).build()).execute().use { r ->
            if (!r.isSuccessful) throw java.io.IOException("HTTP ${r.code}"); r.body?.string().orEmpty()
        }

    private fun post(path: String, body: JSONObject?) {
        http.newCall(Request.Builder().url(prefs.accountServer.trimEnd('/') + path).header("Authorization", auth())
            .post((body?.toString() ?: "").toRequestBody("application/json".toMediaType())).build()).execute().use { r ->
            if (!r.isSuccessful) throw java.io.IOException("HTTP ${r.code}")
        }
    }
}
