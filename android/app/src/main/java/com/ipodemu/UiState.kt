package com.ipodemu

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue

/** Observable UI preferences for the Compose player; every change is written through to [Prefs]. */
class UiState(private val prefs: Prefs) {
    /** Bumped whenever anything that affects styling changes (model, colour, view...). */
    var rev by mutableIntStateOf(0)
        private set

    /** 0 = Modern (default), 1 = iPod; see [Prefs.uiTheme]. */
    var uiTheme by mutableIntStateOf(prefs.uiTheme)
        private set
    val ipodTheme: Boolean get() = uiTheme == 1
    /** 2 = Sync mode: browse/edit a real iPod through ipodsync (player/SyncModeUi.kt). */
    val syncMode: Boolean get() = uiTheme == 2
    /** Account sign-in overlay (Settings > Account, or first-run). */
    var accountOpen by mutableStateOf(false)
    var devicesOpen by mutableStateOf(false)
    var jamOpen by mutableStateOf(false)
    /** Search text, kept while switching tabs. */
    var searchQuery by mutableStateOf("")
    var viewMode by mutableIntStateOf(0)
        private set
    var model by mutableStateOf(prefs.model)
        private set
    var colorway by mutableIntStateOf(prefs.colorway)
        private set
    var dynamicColor by mutableStateOf(prefs.dynamicColor)
        private set
    /** True for administrators of the FLACie server (or on a phone with no server, for the signed-in user): the Dashboard and the Hi-Res download are theirs. */
    var isAdmin by mutableStateOf(false)
    var pickerOpen by mutableStateOf(false)
    /** Sync mode setup overlay (Settings > Sync), same pattern as [pickerOpen]. */
    var syncSetupOpen by mutableStateOf(false)
    /** Jellyfin/Plex/NAS direct-connect setup overlays (Settings > Jellyfin/Plex/NAS), same pattern as [syncSetupOpen]. */
    var jellyfinSetupOpen by mutableStateOf(false)
    var plexSetupOpen by mutableStateOf(false)
    var nasSetupOpen by mutableStateOf(false)
    var lidarrSetupOpen by mutableStateOf(false)
    var downloadsOpen by mutableStateOf(false)
    /** Bumped by the L1/R1 shoulder buttons while the picker is open (-1 / +1 via [pickerStepDir]) to cycle the carousel. */
    var pickerStep by mutableStateOf(0)
    var pickerStepDir = 0
    fun stepPicker(d: Int) { pickerStepDir = d; pickerStep++ }
    /** Incremented to ask the player UI to show its full Now Playing screen. */
    var nowPlayingRequest by mutableIntStateOf(0)
        private set
    fun requestNowPlaying() { nowPlayingRequest++ }

    /** True when the click-wheel view (IpodView) is what is on screen: a wheel mode with an iPod that has a wheel. */
    val wheelActive: Boolean get() = ipodTheme && viewMode >= 2 && !com.ipodemu.theme.Themes.model(model).touch

    /** Only combinations that exist: touch iPods have no wheel views; a wheel iPod in a body shows the wheel OS, not the Player. */
    private fun fixedViewMode(v: Int, m: com.ipodemu.theme.Model): Int = when {
        m.touch -> when (v) { 2 -> 0; 3 -> 1; else -> v }
        else -> if (v == 1) 3 else v
    }
    private fun normalise() {
        val v = fixedViewMode(prefs.viewMode, com.ipodemu.theme.Themes.model(prefs.model))
        if (v != prefs.viewMode) prefs.viewMode = v
    }

    fun changeViewMode(v: Int) { prefs.viewMode = fixedViewMode(v, com.ipodemu.theme.Themes.model(prefs.model)); viewMode = prefs.viewMode; rev++ }
    fun changeModel(id: String) {
        prefs.model = id; model = id; colorway = 0
        normalise(); viewMode = prefs.viewMode
        rev++
    }
    fun changeTheme(t: Int) { prefs.uiTheme = t; uiTheme = t; rev++ }
    fun changeColorway(i: Int) { prefs.colorway = i; colorway = i; rev++ }
    fun changeDynamic(on: Boolean) { prefs.dynamicColor = on; dynamicColor = on; rev++ }
    /** Called after code outside Compose (the wheel UI's own settings) changed prefs. */
    var loginMode by mutableStateOf(prefs.loginMode); private set
    fun setLogin(mode: String) { prefs.loginMode = mode; loginMode = mode }
    val guest: Boolean get() = loginMode == "guest"

    fun refreshFromPrefs() { com.ipodemu.player.Tweaks.load(prefs); normalise(); uiTheme = prefs.uiTheme; viewMode = prefs.viewMode; model = prefs.model; colorway = prefs.colorway; dynamicColor = prefs.dynamicColor; rev++ }

    init { refreshFromPrefs() }   // last: all state fields must exist first
}
