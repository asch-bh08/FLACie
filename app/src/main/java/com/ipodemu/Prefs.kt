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

    /** Reset Settings: everything except the chosen iPod, colour and look. */
    fun reset() {
        val m = model; val c = colorway; val vm = viewMode
        sp.edit().clear().putString("model", m).putInt("color", c).putInt("viewmode", vm).apply()
    }
}
