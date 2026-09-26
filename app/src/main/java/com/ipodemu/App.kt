package com.ipodemu

import android.app.Application
import android.content.Context
import com.ipodemu.library.ArtCache
import com.ipodemu.library.Library
import com.ipodemu.playback.PlayerController
import java.io.File

class App : Application() {
    lateinit var prefs: Prefs; private set
    lateinit var art: ArtCache; private set
    lateinit var library: Library; private set
    lateinit var player: PlayerController; private set
    lateinit var ui: UiState; private set
    lateinit var userData: com.ipodemu.library.UserData; private set

    override fun onCreate() {
        super.onCreate()
        prefs = Prefs(this)
        art = ArtCache(File(filesDir, "art"))
        library = Library(this, art)
        player = PlayerController(this, prefs)
        ui = UiState(prefs)
        userData = com.ipodemu.library.UserData(this)
        player.onTrackStarted = { userData.recordPlay(it.path) }
    }

    companion object {
        fun of(ctx: Context) = ctx.applicationContext as App
    }
}
