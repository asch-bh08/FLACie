package com.ipodemu.library.offline

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Context
import android.content.Intent
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.os.Build
import android.os.IBinder
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.setValue
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat
import com.ipodemu.Prefs
import com.ipodemu.R
import com.ipodemu.library.JellyfinAuth
import com.ipodemu.library.JellyfinNet
import com.ipodemu.library.Priority
import com.ipodemu.library.Track
import com.ipodemu.library.TrackSource
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import java.io.File
import java.io.FileOutputStream
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL

/**
 * Songs kept on the phone for offline listening. One song at a time is downloaded in the background (resumable: the bytes so far stay in a .part file and the
 * next try asks the server to continue from there; the finished file is renamed into place, so a half file is never played). The server is asked through the
 * Jellyfin gate at background priority, so playback and the screen always go first. Only on Wi-Fi unless the person allows mobile data.
 * The rules (which answers can be appended, names, the plan for favourites) are in OfflineModel.kt and unit-tested; this class is the Android side.
 */
class OfflineManager(private val ctx: Context, private val prefs: Prefs) {
    private val dir: File = (ctx.getExternalFilesDir(null)?.let { File(it, "offline") } ?: File(ctx.filesDir, "offline")).also { it.mkdirs() }
    private val indexFile = File(ctx.filesDir, "offline.json")
    private val lock = Any()
    private var index: OfflineIndex = OfflineIndex.decode(try { if (indexFile.exists()) indexFile.readText(Charsets.UTF_8) else null } catch (_: Exception) { null })
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private var worker: Job? = null
    @Volatile private var currentKey: String? = null
    @Volatile private var cancelCurrent = false

    /** Changes on every state change; Compose reads it to redraw the lists. */
    var rev by mutableIntStateOf(0); private set

    /** A short line for the notification and the settings row. */
    @Volatile var statusLine: String = ""; private set

    private val cm = ctx.getSystemService(Context.CONNECTIVITY_SERVICE) as ConnectivityManager

    init {
        // a crash can leave files no entry owns, and entries marked "downloading": clean up once, in the background
        scope.launch {
            try {
                val files = dir.list()?.toList() ?: emptyList()
                for (f in OfflineMath.orphans(files, snapshotIndex())) File(dir, f).delete()
            } catch (_: Exception) {}
        }
        try {
            cm.registerDefaultNetworkCallback(object : ConnectivityManager.NetworkCallback() { override fun onAvailable(network: Network) { kick() } })
        } catch (_: Exception) {}
        if (index.pendingCount > 0) kick()
    }

    private fun snapshotIndex(): OfflineIndex = synchronized(lock) { OfflineIndex.decode(index.encode()) }

    // ---- what the rest of the app asks -----------------------------------------------------------------------------------------------

    val entries: List<OfflineEntry> get() { rev; return synchronized(lock) { index.entries } }
    fun entry(path: String): OfflineEntry? { rev; return synchronized(lock) { index[OfflineMath.keyOf(path)] } }
    val doneCount: Int get() { rev; return synchronized(lock) { index.doneCount } }
    val pendingCount: Int get() { rev; return synchronized(lock) { index.pendingCount } }
    val failedCount: Int get() { rev; return synchronized(lock) { index.failedCount } }
    val usedBytes: Long get() { rev; return synchronized(lock) { index.doneBytes } }
    val freeBytes: Long get() = try { dir.usableSpace } catch (_: Exception) { 0L }

    /** The finished file of a song, or null when it is not (yet) offline. */
    fun localFile(t: Track): File? {
        val e = synchronized(lock) { index[OfflineMath.keyOf(t.path)] } ?: return null
        if (e.state != OfflineState.DONE) return null
        return fileFor(e).takeIf { it.exists() && it.length() > 0 }
    }

    fun isDownloaded(path: String): Boolean = synchronized(lock) { index[OfflineMath.keyOf(path)]?.state == OfflineState.DONE }

    private fun fileFor(e: OfflineEntry) = File(dir, OfflineMath.fileName(e.key, e.ext.ifEmpty { "bin" }))
    private fun partFor(e: OfflineEntry) = File(dir, OfflineMath.fileName(e.key, "part"))

    /** Queues songs for download (Jellyfin songs only; others are already on the phone or not downloadable). */
    fun enqueue(tracks: List<Track>, reason: OfflineReason): Int {
        var n = 0
        synchronized(lock) {
            for (t in tracks) { if (t.source != TrackSource.JELLYFIN) continue; if (!index.has(OfflineMath.keyOf(t.path))) n++; index.add(t, reason, System.currentTimeMillis()) }
            save()
        }
        changed(); kick()
        return n
    }

    fun remove(path: String) {
        val key = OfflineMath.keyOf(path)
        if (currentKey == key) cancelCurrent = true
        synchronized(lock) { index.remove(key)?.let { fileFor(it).delete(); partFor(it).delete() }; save() }
        changed()
    }

    fun removeAll() {
        cancelCurrent = true
        synchronized(lock) { for (e in index.entries) { fileFor(e).delete(); partFor(e).delete() }; index.clear(); save() }
        changed()
    }

    /** Lets failed songs try again. */
    fun retryFailed() {
        synchronized(lock) { for (e in index.entries) if (e.state == OfflineState.FAILED) { e.state = OfflineState.QUEUED; e.error = null }; save() }
        changed(); kick()
    }

    /** Applies the auto-download setting: favourites are added up to the limit; downloaded favourites that are no longer favourites go. */
    fun syncFavorites(favorites: List<Track>) {
        if (!prefs.offlineAutoFavorites) return
        val plan = synchronized(lock) { AutoDownloadPlan.plan(favorites, index, prefs.offlineLimitGb * 1_000_000_000L) }
        for (k in plan.removeKeys) synchronized(lock) { index[k] }?.let { remove(it.path) }
        if (plan.add.isNotEmpty()) enqueue(plan.add, OfflineReason.FAVORITE)
    }

    // ---- albums and playlists ---------------------------------------------------------------------------------------------------------

    /** The playlists kept offline (the id of a hand-made one, or "folder:" and the name of a server one); follows every change. */
    val keptPlaylists: Set<String> get() { rev; return KeptLists.decode(prefs.offlineKeptLists) }
    fun isKept(listId: String): Boolean = listId in keptPlaylists

    /** Marks a playlist as kept offline (or not). Switching it on queues its songs now; later additions follow through [syncPlaylists]. */
    fun setKept(listId: String, on: Boolean) {
        prefs.offlineKeptLists = KeptLists.encode(if (on) keptPlaylists + listId else keptPlaylists - listId)
        changed()
    }

    /** How many of these songs are on the phone, for the Download button of an album or a playlist. */
    fun keepStatus(tracks: List<Track>): KeepStatus { rev; return synchronized(lock) { KeepStatus.of(tracks) { index[OfflineMath.keyOf(it)] } } }

    /** Queues the songs of an album, a playlist or a pick that are not offline yet. Returns how many were queued. */
    fun keep(tracks: List<Track>, reason: OfflineReason): Int {
        val add = synchronized(lock) { KeepPlan.toEnqueue(tracks) { index[OfflineMath.keyOf(it)] } }
        return if (add.isEmpty()) 0 else enqueue(add, reason)
    }

    /** Lets go of the downloads of these songs (any reason: the person asked). */
    fun forget(tracks: List<Track>) { for (t in tracks) if (isDownloaded(t.path) || entry(t.path) != null) remove(t.path) }

    /** Brings the downloads in line with the kept playlists: new songs download, songs that left every kept playlist go (a favourite kept offline carries on as one). */
    fun syncPlaylists(lists: List<List<Track>>, favoriteKeys: Set<String>) {
        val plan = synchronized(lock) { PlaylistKeepPlan.plan(lists, favoriteKeys, index) }
        for (k in plan.removeKeys) synchronized(lock) { index[k] }?.let { remove(it.path) }
        if (plan.demoteKeys.isNotEmpty()) { synchronized(lock) { for (k in plan.demoteKeys) index[k]?.reason = OfflineReason.FAVORITE; save() }; changed() }
        if (plan.add.isNotEmpty()) enqueue(plan.add, OfflineReason.PLAYLIST)
    }

    // ---- the worker -------------------------------------------------------------------------------------------------------------------

    private fun save() {
        try {
            val tmp = File(indexFile.path + ".tmp"); tmp.writeText(index.encode(), Charsets.UTF_8)
            if (!tmp.renameTo(indexFile)) { indexFile.delete(); tmp.renameTo(indexFile) }
        } catch (_: Exception) {}
    }

    private fun changed() { rev++ }

    fun networkOk(): Boolean {
        val n = cm.activeNetwork ?: return false
        val caps = cm.getNetworkCapabilities(n) ?: return false
        val connected = caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
        return OfflineMath.networkAllowed(prefs.offlineWifiOnly, connected, cm.isActiveNetworkMetered)
    }

    /** Why downloading waits now ("Paused", "Waiting for Wi-Fi", ...), or null when it may go on. */
    fun waitReason(): String? {
        val caps = cm.activeNetwork?.let { cm.getNetworkCapabilities(it) }
        val connected = caps?.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET) == true
        return OfflineMath.waitReason(prefs.offlinePaused, prefs.offlineWifiOnly, connected, cm.isActiveNetworkMetered)
    }

    /** Starts the worker if there is something to do and it is not already running. */
    fun kick() {
        synchronized(lock) { if (worker?.isActive == true || index.pendingCount == 0) return; worker = scope.launch { runWorker() } }
    }

    private suspend fun runWorker() {
        startService()
        try {
            var idleChecks = 0
            while (scope.isActive) {
                val e = synchronized(lock) { index.next() } ?: break
                val why = waitReason()
                if (why != null) {
                    statusLine = why
                    // paused: stop now (Resume kicks it again); waiting for a network: look again every 5 s for up to an hour
                    if (prefs.offlinePaused) break
                    notify(statusLine); delay(5_000); if (++idleChecks > 720) break   // an hour of waiting: the next network change or app start kicks it again
                    continue
                }
                idleChecks = 0
                try { downloadOne(e) }
                catch (ex: CancellationException) { throw ex }
                catch (ex: UserCancelled) { synchronized(lock) { if (index[e.key] != null && e.state == OfflineState.DOWNLOADING) { e.state = OfflineState.QUEUED; save() } }; changed(); continue }
                catch (ex: Exception) {
                    // a dropped connection or a server hiccup: the .part file stays, the song goes to the back of the line after a pause; three misses in a row = failed
                    val tries = failures.merge(e.key, 1, Int::plus) ?: 1
                    synchronized(lock) {
                        if (index[e.key] != null) {
                            if (tries >= 3) { e.state = OfflineState.FAILED; e.error = ex.message?.take(120) ?: ex.javaClass.simpleName; failures.remove(e.key) } else e.state = OfflineState.QUEUED
                            save()
                        }
                    }
                    changed(); delay(if (tries >= 3) 500L else 4_000L * tries)
                }
            }
        } finally {
            statusLine = ""
            stopService()
            changed()
        }
    }

    private val failures = HashMap<String, Int>()
    /** The person removed the song that was downloading. */
    private class UserCancelled : Exception()

    private fun authFor(url: String): Map<String, String> {
        val jf = prefs.jellyfinUrl.trimEnd('/'); val acct = prefs.accountServer.trimEnd('/')
        return when {
            jf.isNotBlank() && url.startsWith(jf) -> mapOf("Authorization" to JellyfinAuth.header(prefs.jellyfinApiKey))
            acct.isNotBlank() && url.startsWith(acct) -> mapOf("Authorization" to JellyfinAuth.header(prefs.accountToken))
            else -> emptyMap()
        }
    }

    private fun downloadOne(e: OfflineEntry) {
        currentKey = e.key; cancelCurrent = false
        try {
            synchronized(lock) { e.state = OfflineState.DOWNLOADING; save() }
            changed()
            val part = partFor(e)
            val have = if (part.exists()) part.length() else 0L
            val conn = URL(e.path).openConnection() as HttpURLConnection
            conn.connectTimeout = 10_000; conn.readTimeout = 30_000; conn.instanceFollowRedirects = true
            for ((k, v) in authFor(e.path)) conn.setRequestProperty(k, v)
            OfflineMath.rangeHeader(have)?.let { conn.setRequestProperty("Range", it) }
            // a slot at the one gate while the connection is made (background priority: playback and the screen go first, and it holds back while the server is slow)
            val code = JellyfinNet.gate.withPermitBlocking(Priority.BACKGROUND, "download") { conn.connect(); conn.responseCode.also { c -> if (c >= 400 && c != 416) throw IOException("http $c") } }
            val range = conn.getHeaderField("Content-Range"); val len = conn.contentLengthLong
            when (val d = OfflineMath.decide(have, code, range, len)) {
                is ResumeDecision.Fail -> throw IOException("http ${d.code}")
                ResumeDecision.Complete -> { finish(e, part, have, conn); return }
                ResumeDecision.Restart -> { part.delete(); stream(e, conn, part, append = false, startAt = 0L, total = OfflineMath.totalSize(code, 0, range, len)) }
                is ResumeDecision.Append -> stream(e, conn, part, append = true, startAt = d.from, total = OfflineMath.totalSize(code, d.from, range, len))
            }
        } finally { currentKey = null; cancelCurrent = false }
    }

    private fun stream(e: OfflineEntry, conn: HttpURLConnection, part: File, append: Boolean, startAt: Long, total: Long) {
        if (e.ext.isEmpty()) e.ext = OfflineMath.extensionFor(conn.contentType, conn.getHeaderField("Content-Disposition"))
        synchronized(lock) { e.total = total; e.bytes = startAt }
        var written = startAt; var lastTick = 0L
        conn.inputStream.use { input ->
            FileOutputStream(part, append).use { out ->
                val buf = ByteArray(64 * 1024)
                while (true) {
                    if (!scope.isActive) throw CancellationException("stopped")
                    if (cancelCurrent) throw UserCancelled()
                    val n = input.read(buf); if (n < 0) break
                    out.write(buf, 0, n); written += n
                    val now = System.currentTimeMillis()
                    if (now - lastTick > 400) {
                        lastTick = now; e.bytes = written; changed(); progressNotification(e)
                        if (!networkOk()) throw IOException("network changed")   // e.g. Wi-Fi lost while on Wi-Fi-only: the part stays for later
                    }
                }
            }
        }
        finish(e, part, written, conn)
    }

    private fun finish(e: OfflineEntry, part: File, bytes: Long, conn: HttpURLConnection) {
        if (e.ext.isEmpty()) e.ext = OfflineMath.extensionFor(conn.contentType, conn.getHeaderField("Content-Disposition"))
        val expected = if (e.total > 0) e.total else bytes
        if (part.length() != expected) throw IOException("incomplete (${part.length()} of $expected bytes)")
        val dest = fileFor(e)
        if (!part.renameTo(dest)) { dest.delete(); if (!part.renameTo(dest)) throw IOException("could not save the file") }
        synchronized(lock) {
            if (index[e.key] == null) { dest.delete(); return }   // removed while it downloaded
            e.state = OfflineState.DONE; e.bytes = dest.length(); e.total = dest.length(); e.error = null; save()
        }
        failures.remove(e.key)
        changed()
    }

    // ---- the notification (a foreground service keeps the process alive while the queue is being worked) ------------------------------

    private fun channel() {
        if (Build.VERSION.SDK_INT >= 26) {
            val nm = ctx.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
            if (nm.getNotificationChannel(CHANNEL) == null) nm.createNotificationChannel(NotificationChannel(CHANNEL, "Downloads", NotificationManager.IMPORTANCE_LOW))
        }
    }

    fun notification(text: String): Notification {
        channel()
        return NotificationCompat.Builder(ctx, CHANNEL).setSmallIcon(R.drawable.ic_stat_flacie).setContentTitle("FLACie downloads").setContentText(text)
            .setOngoing(true).setOnlyAlertOnce(true).setPriority(NotificationCompat.PRIORITY_LOW).build()
    }

    private var lastNote = 0L
    private fun progressNotification(e: OfflineEntry) {
        val (done, pending) = synchronized(lock) { index.doneCount to index.pendingCount }
        val pct = (e.progress * 100).toInt()
        statusLine = "Downloading ${done + 1} of ${done + pending}: ${e.title} ($pct %)"
        val now = System.currentTimeMillis(); if (now - lastNote < 1000) return; lastNote = now
        notify(statusLine)
    }

    private fun notify(text: String) {
        try { (ctx.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager).notify(NOTE_ID, notification(text)) } catch (_: Exception) {}
    }

    private fun startService() { try { ContextCompat.startForegroundService(ctx, Intent(ctx, DownloadService::class.java)) } catch (_: Exception) {} }
    private fun stopService() { try { ctx.stopService(Intent(ctx, DownloadService::class.java)) } catch (_: Exception) {} }

    companion object {
        const val CHANNEL = "downloads"
        const val NOTE_ID = 4711
    }
}

/** Holds the process in the foreground (type dataSync) while the download queue is worked; the notification is the manager's. */
class DownloadService : Service() {
    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val m = com.ipodemu.App.of(this).offline
        val n = m.notification(m.statusLine.ifEmpty { "Starting" })
        try {
            if (Build.VERSION.SDK_INT >= 29) startForeground(OfflineManager.NOTE_ID, n, android.content.pm.ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC) else startForeground(OfflineManager.NOTE_ID, n)
        } catch (_: Exception) { stopSelf() }
        return START_NOT_STICKY
    }
}
