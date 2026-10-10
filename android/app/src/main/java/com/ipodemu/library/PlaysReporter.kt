package com.ipodemu.library

import com.ipodemu.Prefs
import com.ipodemu.playback.PlayerController
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL

/**
 * Tells FLACie Web about songs that do not go through Jellyfin (the NAS, the file mover's direct copy), so the server can send its "music played"
 * notification: Jellyfin already announces plays that go through it, and the server cannot see these ones. A start, then a heartbeat about every ten seconds
 * while the song plays, then a stop. Fire and forget: nothing here can slow down or stop playback, and failures are silent.
 */
class PlaysReporter(private val prefs: Prefs, private val player: PlayerController, scope: CoroutineScope) {
    private var reported: String? = null      // the path of the song the server was told about
    private var lastSent = false              // whether the last thing said was "playing"

    private fun via(t: Track?): String? = when (t?.source) { TrackSource.NAS -> "NAS"; TrackSource.CLOUD -> "direct"; TrackSource.JELLYFIN -> "Jellyfin"; else -> null }
    private val usable: Boolean get() = prefs.flacieWebUrl.isNotBlank() && prefs.hasJellyfinAccount

    init {
        scope.launch {
            while (true) {
                delay(10_000)
                try { beat() } catch (_: Exception) { }
            }
        }
    }

    /** A song started playing (any source; only NAS and file-mover songs are reported). */
    fun started(t: Track, scope: CoroutineScope) {
        val v = via(t) ?: return
        if (!usable) return
        reported = t.path; lastSent = true
        scope.launch { send("start", t, v) }
    }

    private suspend fun beat() {
        if (!usable) return
        val (t, playing) = withContext(Dispatchers.Main) { player.current to player.isPlaying }
        val v = via(t)
        if (t == null || v == null) { lastSent = false; return }
        when {
            playing && reported != t.path -> { reported = t.path; lastSent = true; send("start", t, v) }
            playing -> { lastSent = true; send("progress", t, v) }
            lastSent -> { lastSent = false; send("stop", t, v) }
        }
    }

    private suspend fun send(event: String, t: Track, via: String) = withContext(Dispatchers.IO) {
        try {
            val c = URL(prefs.flacieWebUrl.trimEnd('/') + "/api/played").openConnection() as HttpURLConnection
            c.requestMethod = "POST"; c.connectTimeout = 5_000; c.readTimeout = 5_000; c.doOutput = true
            c.setRequestProperty("X-Emby-Token", prefs.accountToken)
            c.setRequestProperty("X-Jellyfin-Server", prefs.accountServer.trimEnd('/'))
            c.setRequestProperty("Content-Type", "application/json")
            val body = JSONObject().put("event", event).put("key", t.path).put("title", t.title).put("artist", t.artist).put("via", via)
                .put("device", android.os.Build.MODEL).toString().toByteArray()
            c.setFixedLengthStreamingMode(body.size)
            c.outputStream.use { it.write(body) }
            c.responseCode
            c.disconnect()
        } catch (_: Exception) { }
    }
}
