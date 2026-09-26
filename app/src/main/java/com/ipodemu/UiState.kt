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

    var viewMode by mutableIntStateOf(prefs.viewMode)
        private set
    var model by mutableStateOf(prefs.model)
        private set
    var colorway by mutableIntStateOf(prefs.colorway)
        private set
    var dynamicColor by mutableStateOf(prefs.dynamicColor)
        private set
    var pickerOpen by mutableStateOf(false)
    /** Bumped by the L1/R1 shoulder buttons while the picker is open (-1 / +1 via [pickerStepDir]) to cycle the carousel. */
    var pickerStep by mutableStateOf(0)
    var pickerStepDir = 0
    fun stepPicker(d: Int) { pickerStepDir = d; pickerStep++ }
    /** Incremented to ask the player UI to show its full Now Playing screen. */
    var nowPlayingRequest by mutableIntStateOf(0)
        private set
    fun requestNowPlaying() { nowPlayingRequest++ }

    fun changeViewMode(v: Int) { prefs.viewMode = v; viewMode = v; rev++ }
    fun changeModel(id: String) {
        prefs.model = id; model = id; colorway = 0
        if (com.ipodemu.theme.Themes.model(id).touch) {   // no wheel on touch iPods: fullscreen wheel -> modern player, wheel body -> touch body
            val v = when (prefs.viewMode) { 2 -> 0; 3 -> 1; else -> prefs.viewMode }
            prefs.viewMode = v; viewMode = v
        }
        rev++
    }
    fun changeColorway(i: Int) { prefs.colorway = i; colorway = i; rev++ }
    fun changeDynamic(on: Boolean) { prefs.dynamicColor = on; dynamicColor = on; rev++ }
    /** Called after code outside Compose (the wheel UI's own settings) changed prefs. */
    fun refreshFromPrefs() { viewMode = prefs.viewMode; model = prefs.model; colorway = prefs.colorway; dynamicColor = prefs.dynamicColor; rev++ }
}
