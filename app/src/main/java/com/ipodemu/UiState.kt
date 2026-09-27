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

    var viewMode by mutableIntStateOf(0)
        private set
    var model by mutableStateOf(prefs.model)
        private set
    var colorway by mutableIntStateOf(prefs.colorway)
        private set
    var dynamicColor by mutableStateOf(prefs.dynamicColor)
        private set
    var pickerOpen by mutableStateOf(false)
    /** Sync mode setup overlay (Settings > Sync), same pattern as [pickerOpen]. */
    var syncSetupOpen by mutableStateOf(false)
    /** Bumped by the L1/R1 shoulder buttons while the picker is open (-1 / +1 via [pickerStepDir]) to cycle the carousel. */
    var pickerStep by mutableStateOf(0)
    var pickerStepDir = 0
    fun stepPicker(d: Int) { pickerStepDir = d; pickerStep++ }
    /** Incremented to ask the player UI to show its full Now Playing screen. */
    var nowPlayingRequest by mutableIntStateOf(0)
        private set
    fun requestNowPlaying() { nowPlayingRequest++ }

    /** True when the click-wheel view (IpodView) is what is on screen: a wheel mode with an iPod that has a wheel. */
    val wheelActive: Boolean get() = viewMode >= 2 && !com.ipodemu.theme.Themes.model(model).touch

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
    fun changeColorway(i: Int) { prefs.colorway = i; colorway = i; rev++ }
    fun changeDynamic(on: Boolean) { prefs.dynamicColor = on; dynamicColor = on; rev++ }
    /** Called after code outside Compose (the wheel UI's own settings) changed prefs. */
    fun refreshFromPrefs() { normalise(); viewMode = prefs.viewMode; model = prefs.model; colorway = prefs.colorway; dynamicColor = prefs.dynamicColor; rev++ }

    init { refreshFromPrefs() }   // last: all state fields must exist first
}
