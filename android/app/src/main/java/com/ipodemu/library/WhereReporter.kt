package com.ipodemu.library

import com.ipodemu.App
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/**
 * Tells FLACie Web where this phone is (the song, how far in, the queue and what it came from), so Devices can offer "Phone, 1 hour ago" on any device
 * (owner, 25 Nov). A new song or pause/play is sent at once, else once a minute while playing; the server keeps a day of them. It sits beside the Jellyfin
 * "now playing" report (JellyfinConnect) and uses the same device id, so the server can tell which Connect device a state belongs to. Fire and forget.
 */
class WhereReporter(private val app: App, scope: CoroutineScope) {
    private var lastKey: String? = null
    private var lastPaused: Boolean? = null
    private var lastAt = 0L

    init {
        scope.launch {
            while (true) {
                delay(5_000)
                try { tick() } catch (_: Exception) { }
            }
        }
    }

    private suspend fun tick() {
        val web = FlacieWebClient(app.prefs)
        if (!web.available) return
        val body = withContext(Dispatchers.Main) {
            val p = app.player
            val t = p.current ?: return@withContext null
            val paused = !p.wantsToPlay
            val key = app.library.jellyfinIdOf(t) ?: t.path
            val now = System.currentTimeMillis()
            if (!WhereYouWere.shouldSend(lastKey, lastPaused, lastAt, key, paused, now)) return@withContext null
            lastKey = key; lastPaused = paused; lastAt = now
            // the queue as ids (a window around the song, as the server keeps at most 200)
            // only the window that is sent (the whole queue, 10,000 songs for a shuffled library, made the app not respond: emulator smoke, 26 Nov)
            val idx = p.queueIndex.coerceAtLeast(0)
            val from = (idx - 20).coerceAtLeast(0)
            val window = p.queueWindow(from, 200)
            WhereYouWere.report(app.prefs.deviceId, android.os.Build.MODEL, t, app.library.jellyfinIdOf(t), p.positionMs, paused,
                window.map { app.library.jellyfinIdOf(it) ?: it.path }, idx - from, p.contextName)
        } ?: return
        withContext(Dispatchers.IO) { try { web.reportWhere(body) } catch (_: Exception) { lastKey = null } }   // failed: try again next time
    }
}
