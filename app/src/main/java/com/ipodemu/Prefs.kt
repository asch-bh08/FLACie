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
        get() = sp.getInt("swipeBack", 0)
        set(v) = sp.edit().putInt("swipeBack", v).apply()
    /** Track-row swipe actions: 0 = off, 1 = play next, 2 = favorite, 3 = add to queue. */
    var swipeRowRight: Int
        get() = sp.getInt("swipeRowR", 0)
        set(v) = sp.edit().putInt("swipeRowR", v).apply()
    var swipeRowLeft: Int
        get() = sp.getInt("swipeRowL", 2)
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
    /** Last-used ipodsync host, e.g. "192.168.1.50:5070" (LAN) or a Tailscale name. */
    var syncHost: String
        get() = sp.getString("synchost", "") ?: ""
        set(v) = sp.edit().putString("synchost", v).apply()
    /** Jellyfin server this app talks to directly (no PC/ipodsync in the loop), e.g.
     * "http://192.168.1.183:8096" or a Tailscale URL. */
    var jellyfinUrl: String
        get() = sp.getString("jfurl", "") ?: ""
        set(v) = sp.edit().putString("jfurl", v).apply()
    var jellyfinApiKey: String
        get() = sp.getString("jfkey", "") ?: ""
        set(v) = sp.edit().putString("jfkey", v).apply()

    /** Plex server this app talks to directly, e.g. "http://192.168.1.183:32400" or a Tailscale URL. */
    var plexUrl: String
        get() = sp.getString("plexurl", "") ?: ""
        set(v) = sp.edit().putString("plexurl", v).apply()
    var plexToken: String
        get() = sp.getString("plextoken", "") ?: ""
        set(v) = sp.edit().putString("plextoken", v).apply()

    /** NAS share this app browses directly over SMB, e.g. host "192.168.1.50", share "Music". */
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

    /** Reset Settings: everything except the chosen iPod, colour and look. */
    fun reset() {
        val m = model; val c = colorway; val vm = viewMode
        sp.edit().clear().putString("model", m).putInt("color", c).putInt("viewmode", vm).apply()
    }
}
