package com.ipodemu

import android.app.Application
import android.content.Context
import com.ipodemu.library.ArtCache
import com.ipodemu.library.Library
import com.ipodemu.playback.PlayerController
import java.io.File
import kotlinx.coroutines.launch

class App : Application() {
    lateinit var prefs: Prefs; private set
    lateinit var art: ArtCache; private set
    lateinit var library: Library; private set
    lateinit var player: PlayerController; private set
    lateinit var ui: UiState; private set
    /** Home's shelves as the last run left them (see HomeSnapshot): what the first frame draws before the library has loaded. */
    @Volatile var homeSnapshot: com.ipodemu.library.HomeSnapshot.Data? = null; private set
    lateinit var userData: com.ipodemu.library.UserData; private set
    lateinit var offline: com.ipodemu.library.offline.OfflineManager; private set
    lateinit var mb: com.ipodemu.library.mb.MbService; private set
    lateinit var account: com.ipodemu.library.AccountSync; private set
    lateinit var connect: com.ipodemu.library.JellyfinConnect; private set
    lateinit var jam: com.ipodemu.library.Jam; private set
    lateinit var lyrics: com.ipodemu.library.LyricsProvider; private set
    lateinit var autoplay: com.ipodemu.library.Autoplay; private set
    lateinit var plays: com.ipodemu.library.PlaysReporter; private set
    /** What was heard, liked and thumbed down, waiting to reach the account's listening log (the server's recommendations learn from it). */
    // one per account, read on first use off the main thread (a phone offline for weeks has a big file)
    @Volatile private var listenQ: com.ipodemu.library.ListenQueue? = null
    private fun listens(owner: String): com.ipodemu.library.ListenQueue = synchronized(this) {
        listenQ?.takeIf { listenOwner == owner } ?: com.ipodemu.library.ListenQueue(filesDir, owner).also { listenQ = it; listenOwner = owner }
    }
    private var listenOwner = ""

    /** Sign-out or a different account: the waiting events are dropped and any send in progress stops, so none go out under the new sign-in. */
    fun forgetListens() {
        listenJob?.cancel()
        listenWriter.launch { synchronized(this@App) { listenQ = null; listenOwner = "" }; com.ipodemu.library.ListenQueue.clearAll(filesDir) }
    }
    private val bg = kotlinx.coroutines.CoroutineScope(kotlinx.coroutines.SupervisorJob() + kotlinx.coroutines.Dispatchers.IO)

    override fun onCreate() {
        super.onCreate()
        Boot.mark("Application.onCreate start")
        prefs = Prefs(this)
        // the old cover folder (covers changed in Jellyfin showed stale): covers are fetched again, into a new one; deleted off the main thread
        bg.launch { File(filesDir, "art").deleteRecursively() }
        art = ArtCache(File(filesDir, "art2"))
        // row thumbnails: a 44 dp row is 66 px at 240 dpi, so 80 px (1/4 of the 320 px file) is plenty and decodes in about half the time
        art.thumbSample = if (resources.displayMetrics.density <= 1.6f) 4 else 3
        // Jellyfin row covers are 240 px files: half size is sharp on ordinary screens, full size on dense ones
        art.jfThumbSample = if (resources.displayMetrics.density <= 2.0f) 2 else 1
        art.start()
        com.ipodemu.library.JellyfinNet.startLogging()
        library = Boot.span("Library()") { Library(this, art) }
        player = Boot.span("PlayerController()") { PlayerController(this, prefs) }
        // the first screen's songs and covers come from this small file and the cover cache, not from the whole saved library
        homeSnapshot = Boot.span("home snapshot read") { com.ipodemu.library.HomeSnapshot.read(File(filesDir, "home.json")) }
        homeSnapshot?.let { art.warm(com.ipodemu.library.HomeSnapshot.coverKeys(it)) }
        ui = UiState(prefs)
        // the player asks the offline library which songs are on the phone while it rebuilds the queue: it must exist first
        offline = com.ipodemu.library.offline.OfflineManager(this, prefs)
        mb = com.ipodemu.library.mb.MbService(this, prefs, art)
        Boot.span("restoreQueue") { player.restoreQueue(); player.restoreSleepTimer() }
        userData = com.ipodemu.library.UserData(this)
        // favourites kept offline follow the favourites list (a pause first, so a run of taps is one change)
        userData.onFavoritesChanged = { offlineSyncSoon() }
        userData.onRated = { t, stars -> rateOnJellyfin(t, stars) }
        bg.launch { kotlinx.coroutines.delay(30_000); offlineSyncSoon() }
        // one-off: on-device playlists that never reached the account stay out of Playlists; new ones always show
        if (!prefs.playlistsMigrated) { prefs.hiddenPlaylists = userData.playlists.filter { it.jfId == null && it.paths.isNotEmpty() }.mapTo(HashSet()) { it.id }; prefs.playlistsMigrated = true }
        // installs from before the login screen go straight in, as whatever they were already using
        if (prefs.loginMode.isEmpty()) prefs.loginMode = when {
            prefs.accountToken.isNotBlank() || prefs.jellyfinUrl.isNotBlank() || prefs.plexUrl.isNotBlank() -> "account"
            prefs.nasHost.isNotBlank() -> "account"
            else -> ""
        }
        ui.setLogin(prefs.loginMode)
        lyrics = com.ipodemu.library.LyricsProvider(this)
        // fetch lyrics as each song starts, so they are ready (and cached) before Now Playing asks
        player.onTrackStarted = { t -> userData.meta.putIfAbsent(t.path, t.title to t.artist); userData.recordPlay(t.path); warmAroundCurrent(); bg.launch { lyrics.get(t) }; plays.started(t, bg); ui.permissions?.playbackStarted() }
        // the seek bar's exact shape, measured from the sound as each song is heard (screen on or off)
        bg.launch { try { com.ipodemu.player.WaveAnalyzer.hear(this@App) } catch (e: kotlinx.coroutines.CancellationException) { throw e } catch (_: Exception) { } }
        account = com.ipodemu.library.AccountSync(this)
        connect = com.ipodemu.library.JellyfinConnect(this)
        connect.attach(); connect.refresh()
        // Jellyfin's live socket tells the library about changes (a delta pass instead of polling); with the socket down the library looks every 15 minutes
        library.socketUp = { connect.connected }
        connect.listeners.add { type, data -> library.onServerMessage(type, data) }
        jam = com.ipodemu.library.Jam(this)
        userData.onChanged = { account.schedulePush() }
        library.onServicesChanged = { account.schedulePush() }
        autoplay = com.ipodemu.library.Autoplay(this)
        plays = com.ipodemu.library.PlaysReporter(prefs, player, bg)
        player.onQueueLow = { autoplay.queueLow() }
        player.onPlayEnded = { t, heard -> listened(com.ipodemu.library.ListenKind.PLAY, t, heard) }
        userData.onListen = { kind, path, playlist -> library.resolve(path, userData.meta[path])?.let { listened(kind, it, context = playlist) } }
        listenWriter.launch { sendListensSoon() }
        library.onDownloaded = { userData.recordDownload(it); autoplay.downloaded(it) }
        // downloads finished within the last hour (before a restart) keep their "Recently downloaded" tag
        library.tags.seed(userData.downloads.map { Triple(it.title, it.artist, it.time) }, System.currentTimeMillis())
        // pick up changes made on other devices (playlists, newly added services) every launch
        account.sync(background = true)
        Boot.mark("Application.onCreate end")
    }

    /** A rating is also written to Jellyfin as the item's own rating (0 to 10); the newer endpoint first, the older one when the server does not have it. Best effort. */
    private fun rateOnJellyfin(t: com.ipodemu.library.Track, stars: Int) {
        val id = Regex("/Audio/([0-9a-fA-F]{32})/").find(t.path)?.groupValues?.get(1) ?: return
        val server = prefs.accountServer.ifBlank { prefs.jellyfinUrl }.trimEnd('/'); val token = prefs.accountToken.ifBlank { prefs.jellyfinApiKey }
        val userId = prefs.accountUserId
        if (server.isBlank() || token.isBlank() || userId.isBlank()) return
        bg.launch {
            val body = org.json.JSONObject().put("Rating", com.ipodemu.library.RatingStore.toJellyfin(stars)).toString().toByteArray()
            for (path in com.ipodemu.library.RatingStore.jellyfinPaths(userId, id)) {
                try {
                    val r = com.ipodemu.library.JellyfinNet.send("POST", server + path, mapOf("Authorization" to com.ipodemu.library.JellyfinAuth.header(token)), body, "application/json", com.ipodemu.library.Priority.BACKGROUND, "rating")
                    if (r.ok) return@launch
                } catch (_: Exception) { }
            }
        }
    }

    /**
     * Keeps one listening event and sends what is waiting. A Play's context is the playlist it came from, "autoplay" for Autoplay's songs, else
     * where the queue was started ("search", "home"). Nothing here can stop or slow playback: a failure only leaves the event in the file for later.
     */
    fun listened(kind: Int, t: com.ipodemu.library.Track, heard: Double = 0.0, context: String? = null) {
        if (!t.isMusic) return
        try {
            val ctx = context ?: if (kind != com.ipodemu.library.ListenKind.PLAY) null else if (autoplay.isAutoplay(t)) "autoplay"
                else player.contextName?.let { n -> userData.playlists.firstOrNull { it.name == n }?.let { it.jfId ?: it.id } } ?: player.listenFrom
            val e = com.ipodemu.library.ListenEventJson.of(kind, t, heard, ctx, surface = if (player.carControllers > 0) "car" else "phone")
            val owner = prefs.accountUserId
            if (owner.isBlank()) return
            // the file is written off the main thread, one event at a time so they stay in order
            listenWriter.launch { try { listens(owner).add(e) } catch (x: Exception) { android.util.Log.w("FLACie", "listen event not kept: ${x.message}") }; sendListensSoon() }
        } catch (e: Exception) { android.util.Log.w("FLACie", "listen event not kept: ${e.message}") }
    }
    @OptIn(kotlinx.coroutines.ExperimentalCoroutinesApi::class)
    private val listenWriter = kotlinx.coroutines.CoroutineScope(kotlinx.coroutines.SupervisorJob() + kotlinx.coroutines.Dispatchers.IO.limitedParallelism(1))

    @Volatile private var listenJob: kotlinx.coroutines.Job? = null
    /** Sends the waiting events, trying again until the server takes them (a phone offline for days sends them all once it is back). */
    fun sendListensSoon() {
        if (listenJob?.isActive == true) return
        val web = com.ipodemu.library.FlacieWebClient(prefs)
        val owner = prefs.accountUserId
        if (!web.available || owner.isBlank()) return
        val q = listens(owner)
        if (q.pending().isEmpty()) return
        listenJob = bg.launch {
            // the client reads the sign-in as it sends: if the account changed meanwhile, this account's events wait for its next sign-in
            try { com.ipodemu.library.SyncRetry.attempt(listOf(10_000L, 60_000L, 300_000L), thenEvery = 900_000L) { q.flush { if (prefs.accountUserId != owner) throw kotlinx.coroutines.CancellationException("account changed"); web.listen(com.ipodemu.library.ListenQueue.body(it)) } } }
            catch (e: kotlinx.coroutines.CancellationException) { throw e } catch (_: Exception) { }
        }
    }

    private var offlineSyncJob: kotlinx.coroutines.Job? = null
    /** Brings the offline favourites in line with the favourites list (only when the setting is on). */
    fun offlineSyncSoon() {
        if (!prefs.offlineAutoFavorites) return
        offlineSyncJob?.cancel()
        offlineSyncJob = bg.launch {
            kotlinx.coroutines.delay(2_000)
            val favs = userData.favorites.toList().mapNotNull { library.resolve(it, userData.meta[it]) }
            offline.syncFavorites(favs)
        }
    }

    /** The covers of the songs about to play (and the one just before) are made ready, so skipping shows the cover at once. */
    private fun warmAroundCurrent() {
        val q = player.queue; val i = player.queueIndex
        val keys = q.map { it.artKey }; art.warmAround(com.ipodemu.library.WarmPlan.rows(keys, i), com.ipodemu.library.WarmPlan.large(keys, i))
    }

    /** Settings > Cache: covers, the saved song list and facts, and the audio kept for replay. */
    fun cacheBytes(): Long = library.cacheBytes(com.ipodemu.playback.StreamSupport.sizeBytes(this))

    /** Settings > Clear cache (see [Library.clearCache]). */
    fun clearCaches() { library.clearCache(); com.ipodemu.playback.StreamSupport.clear(this) }

    companion object {
        fun of(ctx: Context) = ctx.applicationContext as App
    }
}
