package com.ipodemu.library.mb

import android.content.Context
import android.os.Handler
import android.os.Looper
import com.ipodemu.Prefs
import com.ipodemu.library.ArtCache
import com.ipodemu.library.Track
import com.ipodemu.playback.SpeedPolicy
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import java.io.File
import java.net.HttpURLConnection
import java.net.URL

/**
 * The MusicBrainz match and the Cover Art Archive cover for a song, for the song menu: asked only when a person chooses it, one request at a time and at least a second
 * apart, answers kept on disk, and a chosen cover kept as a pin beside the covers (the original stays; the song files and Jellyfin are never touched).
 */
class MbService(private val ctx: Context, private val prefs: Prefs, private val art: ArtCache) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val main = Handler(Looper.getMainLooper())
    private val gate = MbGate()
    private val http = UrlConnectionHttp()
    private val version = try { ctx.packageManager.getPackageInfo(ctx.packageName, 0).versionName ?: "0" } catch (_: Exception) { "0" }
    private fun ua() = MbClient.userAgentFor(version, prefs.mbContact)
    private val client by lazy { MbClient(http, ua(), File(ctx.cacheDir, "mb"), gate) }
    private val caa by lazy { CaaClient(http, ua(), gate) }
    val overlay = MbOverlay(File(ctx.filesDir, "mb-overlay.json"))
    val pins = CoverPins(File(ctx.filesDir, "art-pins")).also { art.pins = it }

    fun keyOf(t: Track) = SpeedPolicy.keyOf(t.path)

    /** Runs in the background; [done] is called on the main thread with the ranked candidates, or null when MusicBrainz did not answer. */
    fun search(t: Track, done: (List<Pair<MbCandidate, Double>>?) -> Unit) {
        scope.launch {
            val r = try { MbMatching.rank(t, client.search(t)) } catch (_: Exception) { null }
            main.post { done(r) }
        }
    }

    /** Fetches the front picture of the chosen release and pins it as the cover of [t]; [done] on the main thread: 1 = replaced, 0 = no picture, -1 = no answer. */
    fun useFrontCover(t: Track, c: MbChoice, done: (Int) -> Unit) {
        val key = t.artKey
        scope.launch {
            val code = try {
                val img = caa.list(c.releaseMbid, c.releaseGroupMbid).firstOrNull()
                val bytes = img?.let { fetchBytes(it.full) ?: fetchBytes(it.thumb) }
                if (key != null && bytes != null && pins.set(key, bytes)) { art.pinChanged(key); 1 } else 0
            } catch (_: Exception) { -1 }
            main.post { done(code) }
        }
    }

    fun originalCover(t: Track) { t.artKey?.let { pins.clear(it); art.pinChanged(it) } }

    /** One picture's bytes (https only, a sensible size, redirects followed), or null. */
    private suspend fun fetchBytes(url: String): ByteArray? = gate.run {
        try {
            val u = if (url.startsWith("http://", true)) "https://" + url.substring(7) else url
            if (!u.startsWith("https://")) return@run null
            val c = URL(u).openConnection() as HttpURLConnection
            c.connectTimeout = 10_000; c.readTimeout = 20_000; c.instanceFollowRedirects = true
            c.setRequestProperty("User-Agent", ua())
            try {
                if (c.responseCode !in 200..299 || c.contentLengthLong > CaaClient.MAX_BYTES) null
                else c.inputStream.use { s ->
                    val out = java.io.ByteArrayOutputStream(); val buf = ByteArray(64 * 1024); var total = 0L
                    while (true) { val n = s.read(buf); if (n < 0) break; total += n; if (total > CaaClient.MAX_BYTES) return@run null; out.write(buf, 0, n) }
                    out.toByteArray().takeIf { CaaClient.sniff(it) != null }
                }
            } finally { c.disconnect() }
        } catch (_: Exception) { null }
    }
}
