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
    lateinit var account: com.ipodemu.library.AccountSync; private set
    lateinit var connect: com.ipodemu.library.JellyfinConnect; private set
    lateinit var jam: com.ipodemu.library.Jam; private set
    lateinit var lyrics: com.ipodemu.library.LyricsProvider; private set
    lateinit var autoplay: com.ipodemu.library.Autoplay; private set
    lateinit var plays: com.ipodemu.library.PlaysReporter; private set
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
        Boot.span("restoreQueue") { player.restoreQueue(); player.restoreSleepTimer() }
        userData = com.ipodemu.library.UserData(this)
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
        player.onTrackStarted = { t -> userData.recordPlay(t.path); warmAroundCurrent(); bg.launch { lyrics.get(t) }; plays.started(t, bg); ui.permissions?.playbackStarted() }
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
        library.onDownloaded = { userData.recordDownload(it); autoplay.downloaded(it) }
        // downloads finished within the last hour (before a restart) keep their "Recently downloaded" tag
        library.tags.seed(userData.downloads.map { Triple(it.title, it.artist, it.time) }, System.currentTimeMillis())
        // pick up changes made on other devices (playlists, newly added services) every launch
        account.sync(background = true)
        Boot.mark("Application.onCreate end")
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
