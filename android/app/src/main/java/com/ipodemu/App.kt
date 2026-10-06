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
    lateinit var userData: com.ipodemu.library.UserData; private set
    lateinit var account: com.ipodemu.library.AccountSync; private set
    lateinit var connect: com.ipodemu.library.JellyfinConnect; private set
    lateinit var jam: com.ipodemu.library.Jam; private set
    lateinit var lyrics: com.ipodemu.library.LyricsProvider; private set
    lateinit var autoplay: com.ipodemu.library.Autoplay; private set
    private val bg = kotlinx.coroutines.CoroutineScope(kotlinx.coroutines.SupervisorJob() + kotlinx.coroutines.Dispatchers.IO)

    override fun onCreate() {
        super.onCreate()
        prefs = Prefs(this)
        File(filesDir, "art").deleteRecursively()   // the old cover folder (covers changed in Jellyfin showed stale): covers are fetched again, into a new one
        art = ArtCache(File(filesDir, "art2"))
        library = Library(this, art)
        player = PlayerController(this, prefs)
        ui = UiState(prefs)
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
        player.onTrackStarted = { t -> userData.recordPlay(t.path); bg.launch { lyrics.get(t) } }
        account = com.ipodemu.library.AccountSync(this)
        connect = com.ipodemu.library.JellyfinConnect(this)
        connect.attach(); connect.refresh()
        jam = com.ipodemu.library.Jam(this)
        userData.onChanged = { account.schedulePush() }
        library.onServicesChanged = { account.schedulePush() }
        autoplay = com.ipodemu.library.Autoplay(this)
        player.onQueueLow = { autoplay.queueLow() }
        library.onDownloaded = { userData.recordDownload(it); autoplay.downloaded(it) }
        // pick up changes made on other devices (playlists, newly added services) every launch
        account.sync()
    }

    companion object {
        fun of(ctx: Context) = ctx.applicationContext as App
    }
}
