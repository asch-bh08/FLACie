package com.ipodemu

import android.content.Context

class Prefs(ctx: Context) {
    private val sp = ctx.getSharedPreferences("ipod", Context.MODE_PRIVATE)

    /** Which iPod (see theme/Models.kt), its colour, and whether to draw the physical body ("device") or the modern flat layout. */
    var model: String
        get() = sp.getString("model", "ipod4") ?: "ipod4"
        set(v) = sp.edit().putString("model", v).putInt("color", 0).apply()
    var colorway: Int
        get() = sp.getInt("color", 0)
        set(v) = sp.edit().putInt("color", v).apply()
    /**
     * How the app is shown: 0 = Player (modern full-screen music player, the default), 1 = Player inside a physical
     * iPod body, 2 = click-wheel iPod (flat layout), 3 = click-wheel iPod inside its body.
     */
    var viewMode: Int
        get() = sp.getInt("viewmode", 0)
        set(v) = sp.edit().putInt("viewmode", v).apply()
    /** Wheel themes: 0 = flat faceplate, 1 = physical body. Derived from [viewMode]. */
    var look: Int
        get() = if (viewMode == 3) 1 else 0
        set(v) { viewMode = if (v == 1) 3 else 2 }
    /** Tint the player with colours pulled from the current album art. */
    /** 0 = dark, 1 = light, 2 = follow the system (default). */
    var appearance: Int
        get() = sp.getInt("appearance", 2)
        set(v) = sp.edit().putInt("appearance", v).apply()
    /** Back swipe: 0 = anywhere (iPhone style), 1 = left edge only, 2 = off. */
    var swipeBack: Int
        get() = sp.getInt("swipeBack", 1)
        set(v) = sp.edit().putInt("swipeBack", v).apply()
    /** Track-row swipe actions: 0 = off, 1 = play next, 2 = favorite, 3 = add to queue. */
    /** One-time: phones that still had the old touchy defaults (back swipe from anywhere, left-swipe = favourite) get the safe ones. */
    fun applySafeTouchDefaults() {
        if (sp.getBoolean("safeTouchV2", false)) return
        if (sp.getInt("swipeBack", 1) == 0) sp.edit().putInt("swipeBack", 1).apply()
        if (sp.getInt("swipeRowL", 0) == 2) sp.edit().putInt("swipeRowL", 0).apply()
        // the app now uses the web's pink by default: an old green pick (the previous look) goes back to Default once; any other colour stays
        if (sp.getInt("accentcolor", 0) == 0xFF3DDC84.toInt()) sp.edit().putInt("accentcolor", 0).apply()
        sp.edit().putBoolean("safeTouchV2", true).apply()
    }
    var swipeRowRight: Int
        get() = sp.getInt("swipeRowR", 0)
        set(v) = sp.edit().putInt("swipeRowR", v).apply()
    var swipeRowLeft: Int
        get() = sp.getInt("swipeRowL", 0)
        set(v) = sp.edit().putInt("swipeRowL", v).apply()
    var dynamicColor: Boolean
        get() = sp.getBoolean("dynamic", true)
        set(v) = sp.edit().putBoolean("dynamic", v).apply()
    /** 0 = very slow .. 4 = very fast (degrees of finger travel per list row). */
    var wheelSensitivity: Int
        get() = sp.getInt("sens2", 2)
        set(v) = sp.edit().putInt("sens2", v).apply()
    /** Speed-up for fast spins: 0 = off, 1 = low, 2 = normal, 3 = high. */
    var wheelAccel: Int
        get() = sp.getInt("accel", 1)
        set(v) = sp.edit().putInt("accel", v).apply()
    /** 0 = quiet, 1 = medium, 2 = loud. */
    var clickVolume: Int
        get() = sp.getInt("clickvol", 1)
        set(v) = sp.edit().putInt("clickvol", v).apply()
    var clickSound: Boolean
        get() = sp.getBoolean("click", true)
        set(v) = sp.edit().putBoolean("click", v).apply()
    var haptics: Boolean
        get() = sp.getBoolean("haptic", false)
        set(v) = sp.edit().putBoolean("haptic", v).apply()
    var shuffle: Boolean
        get() = sp.getBoolean("shuffle", false)
        set(v) = sp.edit().putBoolean("shuffle", v).apply()
    /** 0 = off, 1 = all, 2 = one */
    var repeat: Int
        get() = sp.getInt("repeat", 0)
        set(v) = sp.edit().putInt("repeat", v).apply()
    var volume: Float
        get() = sp.getFloat("vol", 0.6f)
        set(v) = sp.edit().putFloat("vol", v).apply()
    /** Nintendo layout: physical right button reports BUTTON_A but is labelled B. */
    var swapFaceButtons: Boolean
        get() = sp.getBoolean("swapface", false)
        set(v) = sp.edit().putBoolean("swapface", v).apply()
    /** 0 = auto (fullscreen when the controller is deployed), 1 = always wheel layout, 2 = always fullscreen. */
    var layoutMode: Int
        get() = sp.getInt("layout", 0)
        set(v) = sp.edit().putInt("layout", v).apply()
    var eq: String
        get() = sp.getString("eq", "Off") ?: "Off"
        set(v) = sp.edit().putString("eq", v).apply()
    /** Seconds until the screen blanks; 0 = always on. */
    var backlightSec: Int
        get() = sp.getInt("backlight", 0)
        set(v) = sp.edit().putInt("backlight", v).apply()
    /** Window brightness 0.05..1, or -1 to follow the system. */
    var brightness: Float
        get() = sp.getFloat("brightness", -1f)
        set(v) = sp.edit().putFloat("brightness", v).apply()
    /** Max playback volume, percent. */
    var volumeLimit: Int
        get() = sp.getInt("vollimit", 100)
        set(v) = sp.edit().putInt("vollimit", v).apply()
    var timeInTitle: Boolean
        get() = sp.getBoolean("clock", false)
        set(v) = sp.edit().putBoolean("clock", v).apply()
    var hiddenMenu: Set<String>
        get() = sp.getStringSet("hidden", emptySet()) ?: emptySet()
        set(v) = sp.edit().putStringSet("hidden", v).apply()
    /** Last-used ipodsync host, e.g. "my-pc:5070" (LAN) or a Tailscale name. */
    var syncHost: String
        get() = sp.getString("synchost", "") ?: ""
        set(v) = sp.edit().putString("synchost", v).apply()
    /** Jellyfin server this app talks to directly (no PC/ipodsync in the loop), e.g.
     * "http://your-server:8096" or a Tailscale URL. */
    var jellyfinUrl: String
        get() = sp.getString("jfurl", "") ?: ""
        set(v) = sp.edit().putString("jfurl", v).apply()
    var jellyfinApiKey: String
        get() = sp.getString("jfkey", "") ?: ""
        set(v) = sp.edit().putString("jfkey", v).apply()

    /** Plex server this app talks to directly, e.g. "http://your-server:32400" or a Tailscale URL. */
    var plexUrl: String
        get() = sp.getString("plexurl", "") ?: ""
        set(v) = sp.edit().putString("plexurl", v).apply()
    var plexToken: String
        get() = sp.getString("plextoken", "") ?: ""
        set(v) = sp.edit().putString("plextoken", v).apply()

    /** NAS share this app browses directly over SMB, e.g. host "my-pc", share "Music". */
    var nasHost: String
        get() = sp.getString("nashost", "") ?: ""
        set(v) = sp.edit().putString("nashost", v).apply()
    var nasShare: String
        get() = sp.getString("nasshare", "") ?: ""
        set(v) = sp.edit().putString("nasshare", v).apply()
    /** Subfolder within the share to scan, e.g. "Music/Flac"; blank scans the whole share. */
    var nasFolder: String
        get() = sp.getString("nasfolder", "") ?: ""
        set(v) = sp.edit().putString("nasfolder", v).apply()
    var nasUsername: String
        get() = sp.getString("nasuser", "") ?: ""
        set(v) = sp.edit().putString("nasuser", v).apply()
    var nasPassword: String
        get() = sp.getString("naspass", "") ?: ""
        set(v) = sp.edit().putString("naspass", v).apply()
    /** NT domain/workgroup for the share's account; most home NAS boxes leave this blank. */
    var nasDomain: String
        get() = sp.getString("nasdomain", "") ?: ""
        set(v) = sp.edit().putString("nasdomain", v).apply()

    /** Lidarr server this app talks to directly to request missing tracks, e.g. a Tailscale URL. */
    var lidarrUrl: String
        get() = sp.getString("lidarrurl", "") ?: ""
        set(v) = sp.edit().putString("lidarrurl", v).apply()
    var lidarrApiKey: String
        get() = sp.getString("lidarrkey", "") ?: ""
        set(v) = sp.edit().putString("lidarrkey", v).apply()

    /** Optional slskd (Soulseek daemon) this app races against Lidarr for a faster download when a
     * peer already has the file; blank means the app skips straight to the Lidarr-only path. */
    var slskdUrl: String
        get() = sp.getString("slskdurl", "") ?: ""
        set(v) = sp.edit().putString("slskdurl", v).apply()
    var slskdApiKey: String
        get() = sp.getString("slskdkey", "") ?: ""
        set(v) = sp.edit().putString("slskdkey", v).apply()
    /** Where slskd writes finished downloads, as a path relative to the shared root both NAS (SMB)
     * and the file-mover service (HTTP) use -- a completed Soulseek download gets moved from here
     * straight into the music folder Jellyfin scans. */
    var slskdDownloadPath: String
        get() = sp.getString("slskdpath", "/downloads/slskd-inbox") ?: "/downloads/slskd-inbox"
        set(v) = sp.edit().putString("slskdpath", v).apply()

    /** Optional small HTTP service on the homelab that moves a file within its shared root -- the
     * fallback for filing a Soulseek download when a direct SMB move (NasSmb) isn't reachable, e.g.
     * away from home over the Tailscale Funnel, which can't carry raw SMB. Blank skips this fallback. */
    var fileMoverUrl: String
        get() = sp.getString("filemoverurl", "") ?: ""
        set(v) = sp.edit().putString("filemoverurl", v).apply()
    var fileMoverApiKey: String
        get() = sp.getString("filemoverkey", "") ?: ""
        set(v) = sp.edit().putString("filemoverkey", v).apply()
    /** Free Jamendo client id for the open download sources (blank = Jamendo is skipped). */
    var jamendoClientId: String
        get() = sp.getString("jamendoclientid", "") ?: ""
        set(v) = sp.edit().putString("jamendoclientid", v).apply()
    /** The open download sources (Internet Archive, Audius, Jamendo): each on or off, and the order they are preferred in (comma separated ids). */
    var openArchive: Boolean
        get() = sp.getBoolean("openarchive", true)
        set(v) = sp.edit().putBoolean("openarchive", v).apply()
    var openAudius: Boolean
        get() = sp.getBoolean("openaudius", true)
        set(v) = sp.edit().putBoolean("openaudius", v).apply()
    var openJamendo: Boolean
        get() = sp.getBoolean("openjamendo", true)
        set(v) = sp.edit().putBoolean("openjamendo", v).apply()
    /** The last-resort yt-dlp (YouTube) source: off until the user switches it on. */
    /** Look and feel: an accent colour over the theme's own (0 = the theme's), tighter list rows, and the small source badges next to songs. */
    var accentColor: Int
        get() = sp.getInt("accentcolor", 0)
        set(v) = sp.edit().putInt("accentcolor", v).apply()
    /** Animations on (the default); off = calm: no pops, slides or springs. */
    var motion: Boolean
        get() = sp.getBoolean("motion", true)
        set(v) = sp.edit().putBoolean("motion", v).apply()
    var compactLists: Boolean
        get() = sp.getBoolean("compactlists", false)
        set(v) = sp.edit().putBoolean("compactlists", v).apply()
    var showBadges: Boolean
        get() = sp.getBoolean("showbadges", true)
        set(v) = sp.edit().putBoolean("showbadges", v).apply()
    var openYtdl: Boolean
        get() = sp.getBoolean("openytdl", false)
        set(v) = sp.edit().putBoolean("openytdl", v).apply()
    var openOrder: String
        get() = sp.getString("openorder", "archive,audius,jamendo") ?: "archive,audius,jamendo"
        set(v) = sp.edit().putString("openorder", v).apply()
    val openSources: com.ipodemu.library.OpenSourceSettings
        get() = com.ipodemu.library.OpenSourceSettings(openArchive, openAudius, openJamendo, jamendoClientId.trim(), openOrder.split(',').map { it.trim() }.filter { it.isNotEmpty() }, openYtdl)


    /** Look & feel: 0 = Modern (the default: a flat, YT Music/Spotify-style player), 1 = iPod (the glossy iPod-OS
     * skins and click-wheel modes, configured on the Appearance screen). */
    var uiTheme: Int
        get() = sp.getInt("uitheme", 0)
        set(v) = sp.edit().putInt("uitheme", v).apply()

    /** Account (see library/AccountSync.kt): a Jellyfin user whose server keeps this app's saved profile --
     * every service connection plus playlists/favourites -- so a fresh install restores it all by signing in. */
    var accountServer: String
        get() = sp.getString("acctserver", "") ?: ""
        set(v) = sp.edit().putString("acctserver", v).apply()
    var accountUserId: String
        get() = sp.getString("acctuid", "") ?: ""
        set(v) = sp.edit().putString("acctuid", v).apply()
    var accountUserName: String
        get() = sp.getString("acctname", "") ?: ""
        set(v) = sp.edit().putString("acctname", v).apply()
    var accountToken: String
        get() = sp.getString("accttoken", "") ?: ""
        set(v) = sp.edit().putString("accttoken", v).apply()
    /** Epoch ms of the last successful profile upload/download, for Settings. */
    var accountSyncedAt: Long
        get() = sp.getLong("acctsynced", 0L)
        set(v) = sp.edit().putLong("acctsynced", v).apply()
    /** Playlists that existed only on this device before Playlists started listing the account's own (hidden from it). */
    var hiddenPlaylists: Set<String>
        get() = sp.getStringSet("hiddenlists", null) ?: emptySet()
        set(v) = sp.edit().putStringSet("hiddenlists", v).apply()
    var playlistsMigrated: Boolean
        get() = sp.getBoolean("listsmigrated", false)
        set(v) = sp.edit().putBoolean("listsmigrated", v).apply()
    /** How this install got in: "account" (signed in with Jellyfin or a NAS, or set up before the login screen existed),
     * "guest" (this device's files only, nothing synced), or "" (not yet: the login screen shows). */
    var loginMode: String
        get() = sp.getString("loginMode", "") ?: ""
        set(v) = sp.edit().putString("loginMode", v).apply()
    /** Stable per-install id Jellyfin needs for its session/device bookkeeping. */
    val deviceId: String
        get() = sp.getString("deviceid", null) ?: java.util.UUID.randomUUID().toString().also { sp.edit().putString("deviceid", it).apply() }
    /** A Jellyfin user session (signed in with Jellyfin, or restored from a NAS profile). */
    val hasJellyfinAccount: Boolean get() = accountServer.isNotBlank() && accountToken.isNotBlank() && accountUserId.isNotBlank()
    /** A NAS that can hold the profile: a share and a login. */
    val hasNasAccount: Boolean get() = nasHost.isNotBlank() && nasShare.isNotBlank()
    /** Which account the user signed in with: "jellyfin" or "nas". The profile is kept in both whenever both exist. */
    var accountKind: String
        get() = sp.getString("accountKind", "jellyfin") ?: "jellyfin"
        set(v) = sp.edit().putString("accountKind", v).apply()
    val signedIn: Boolean get() = if (accountKind == "nas") hasNasAccount else hasJellyfinAccount

    /** When the queue is nearly over, carry on with different songs that go with what played (Autoplay). */
    var autoplay: Boolean
        get() = sp.getBoolean("autoplay", true)
        set(v) = sp.edit().putBoolean("autoplay", v).apply()
    /** Autoplay may download the next few songs the library lacks (the server does the downloading; the phone only asks). */
    var autoplayFetch: Boolean
        get() = sp.getBoolean("autoplayFetch", true)
        set(v) = sp.edit().putBoolean("autoplayFetch", v).apply()
    /** Streaming quality: 0 = automatic (full quality, a lower bitrate only when the connection can't keep up), 1 = always full, 2 = always data saver. */
    var streamQuality: Int
        get() = sp.getInt("streamQuality", 0)
        set(v) = sp.edit().putInt("streamQuality", v).apply()
    /** The address of FLACie Web (it takes playlist files and does the downloading); shared through the account profile. */
    var flacieWebUrl: String
        get() = sp.getString("flacieWeb", "") ?: ""
        set(v) = sp.edit().putString("flacieWeb", v).apply()

    /** Reset Settings: everything except the chosen iPod, colour and look. */
    fun reset() {
        val keep = listOf("acctserver", "acctuid", "acctname", "accttoken", "deviceid", "uitheme").associateWith { sp.all[it] }
        val m = model; val c = colorway; val vm = viewMode
        val e = sp.edit().clear().putString("model", m).putInt("color", c).putInt("viewmode", vm)
        keep.forEach { (k, v) -> when (v) { is String -> e.putString(k, v); is Int -> e.putInt(k, v) } }
        e.apply()
    }
}
