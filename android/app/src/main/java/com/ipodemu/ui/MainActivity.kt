package com.ipodemu.ui

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import android.view.KeyEvent
import android.view.MotionEvent
import android.view.WindowManager
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import com.ipodemu.App
import com.ipodemu.Boot
import com.ipodemu.Need
import com.ipodemu.PermissionActions
import com.ipodemu.PermissionPlan
import com.ipodemu.player.AppRoot
import com.ipodemu.player.PlayerKeys

class MainActivity : ComponentActivity() {
    // touches and keys stamp this, so background work (the NAS tag scan) can wait for the screen to be left alone

    /** The click-wheel view, when that mode is showing (it needs raw key/hat events). */
    var ipodView: IpodView? = null

    private val app get() = App.of(this)
    private val wheelMode get() = app.ui.wheelActive && !app.ui.pickerOpen && !app.ui.syncSetupOpen &&
        !app.ui.jellyfinSetupOpen && !app.ui.plexSetupOpen && !app.ui.nasSetupOpen && !app.ui.lidarrSetupOpen && !app.ui.downloadsOpen && !app.ui.headphonesOpen && !app.ui.accountOpen && !app.ui.devicesOpen && !app.ui.jamOpen && app.ui.loginMode.isNotEmpty()   // these overlays take keys themselves

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        WindowCompat.setDecorFitsSystemWindows(window, false)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        app.ui.permissions = permissionActions   // no system prompt here: see the permissions section below
        Boot.mark("MainActivity.onCreate start")
        // test aid, like the fake extras below: `am start --ez clearcovers true` starts with an empty cover cache (what Settings > Clear cache does for covers)
        if (intent?.getBooleanExtra("clearcovers", false) == true) app.art.clearAll()
        // Debug aid: `am start --es fake 1080x2400` letterboxes the UI into that aspect ratio to test layouts.
        // `--es fakedp 360` also renders it at that width in dp (e.g. a Z Fold cover screen), and `--es fakecutout 32`
        // simulates a camera cutout that tall at the top, for checking safe-area handling on a device without one.
        val fake = intent?.getStringExtra("fake")?.split("x")?.mapNotNull { it.toFloatOrNull() }
        val fakeDp = intent?.getStringExtra("fakedp")?.toFloatOrNull()
        val fakeCutout = intent?.getStringExtra("fakecutout")?.toFloatOrNull() ?: 0f
        setContent {
            androidx.compose.runtime.CompositionLocalProvider(com.ipodemu.player.LocalDebugCutout provides fakeCutout.dp) {
                if (fake != null && fake.size == 2) {
                    Box(Modifier.fillMaxSize().background(Color(0xFF303030)), contentAlignment = Alignment.Center) {
                        androidx.compose.foundation.layout.BoxWithConstraints(Modifier.aspectRatio(fake[0] / fake[1])) {
                            val d = androidx.compose.ui.platform.LocalDensity.current
                            val density = if (fakeDp != null) androidx.compose.ui.unit.Density(constraints.maxWidth / fakeDp, d.fontScale) else d
                            androidx.compose.runtime.CompositionLocalProvider(androidx.compose.ui.platform.LocalDensity provides density) { AppRoot(this@MainActivity) }
                        }
                    }
                } else AppRoot(this@MainActivity)
            }
        }
        handleIntent(intent)
        Boot.mark("MainActivity.onCreate end")
        window.decorView.viewTreeObserver.addOnDrawListener(object : android.view.ViewTreeObserver.OnDrawListener {
            private var n = 0
            override fun onDraw() {
                if (++n <= 3) Boot.mark("frame $n drawn")
                if (n == 1) window.decorView.post { startLibrary() }   // loading the saved library competes with the first frame for the CPU: it starts once that is on screen
                if (n >= 3) window.decorView.post { window.decorView.viewTreeObserver.removeOnDrawListener(this) }
            }
        })
        window.decorView.postDelayed({ startLibrary() }, 1500)   // never later than this, even if no frame is drawn (screen off)
    }

    private var libraryStarted = false
    private fun startLibrary() { if (libraryStarted) return; libraryStarted = true; Boot.mark("library.start()"); app.library.start() }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        handleIntent(intent)
    }

    /** Audio opened from another app plays immediately and shows Now Playing. */
    private fun handleIntent(intent: Intent?) {
        val data = intent?.data ?: return
        if (intent.action != Intent.ACTION_VIEW) return
        app.player.playUri(data)
        ipodView?.openNowPlaying()
        app.ui.requestNowPlaying()
        intent.data = null
    }

    /** Window brightness from Settings (-1 = leave to the system). The click-wheel view also manages its own dimming. */
    fun applyBrightness() {
        val p = App.of(this).prefs
        val a = window.attributes
        a.screenBrightness = if (p.brightness < 0f) android.view.WindowManager.LayoutParams.BRIGHTNESS_OVERRIDE_NONE else p.brightness
        window.attributes = a
    }

    /** The click-wheel iPod view is a full-bleed device and hides the system bars; everything else keeps the status and navigation bars, so the gesture bar
     * (or the Home / Back buttons) is always there to swipe out of the app. */
    fun applySystemBars() {
        val c = WindowInsetsControllerCompat(window, window.decorView)
        if (app.ui.wheelActive || app.ui.ipodTheme) {
            c.systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            c.hide(WindowInsetsCompat.Type.systemBars())
        } else {
            c.show(WindowInsetsCompat.Type.systemBars())
            c.isAppearanceLightStatusBars = false; c.isAppearanceLightNavigationBars = false
            window.statusBarColor = android.graphics.Color.TRANSPARENT; window.navigationBarColor = android.graphics.Color.TRANSPARENT
            if (android.os.Build.VERSION.SDK_INT >= 29) window.isNavigationBarContrastEnforced = false
        }
    }

    private fun hideSystemBars() = applySystemBars()

    override fun onResume() {
        idleHandler.removeCallbacks(idleCheck); idleHandler.postDelayed(idleCheck, 1000)
        applyBrightness()
        super.onResume()
        refreshPermissions()
        ipodView?.updateHinge()
        hideSystemBars()
    }

    // onResume alone misses this: a transient system-bar reveal (edge swipe, or a status-bar tap) doesn't pause the
    // activity, and our own frequent invalidate()s (wheel animations, scrolling) reset the OS's auto-hide timer, so the
    // bar can stay stuck open indefinitely, drawn over our own header. Re-hide whenever the window regains focus.
    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus) hideSystemBars()
    }

    override fun onConfigurationChanged(newConfig: android.content.res.Configuration) {
        super.onConfigurationChanged(newConfig)
        ipodView?.updateHinge()
    }

    // ---- permissions: never asked at launch. The screens show why first (sign-in screen, the reason card), then call request() ----

    private val sdk get() = Build.VERSION.SDK_INT
    private fun grantedSet(): Set<String> = listOf(Need.MEDIA, Need.NOTIFICATIONS)
        .mapNotNull { PermissionPlan.permission(it, sdk) }.filterTo(HashSet()) { checkSelfPermission(it) == PackageManager.PERMISSION_GRANTED }
    private fun asked(need: Need) = if (need == Need.MEDIA) app.prefs.mediaAsked else app.prefs.notifAsked

    private val permissionActions = object : PermissionActions {
        override fun request(need: Need) {
            val p = PermissionPlan.permission(need, sdk) ?: return
            if (checkSelfPermission(p) == PackageManager.PERMISSION_GRANTED) { refreshPermissions(); return }
            if (need == Need.MEDIA) app.prefs.mediaAsked = true else app.prefs.notifAsked = true
            app.ui.permissionReason = null
            requestPermissions(arrayOf(p), if (need == Need.MEDIA) REQ_MEDIA else REQ_NOTIF)
            refreshPermissions()
        }
        override fun decline(need: Need) {
            if (need == Need.MEDIA) app.prefs.mediaAsked = true else app.prefs.notifAsked = true
            app.ui.permissionReason = null
            refreshPermissions()
        }
        override fun openAppSettings() {
            try { startActivity(Intent(android.provider.Settings.ACTION_APPLICATION_DETAILS_SETTINGS, android.net.Uri.fromParts("package", packageName, null))) } catch (_: Exception) {}
        }
        override fun playbackStarted() {
            if (!lifecycle.currentState.isAtLeast(androidx.lifecycle.Lifecycle.State.RESUMED) || app.ui.permissionReason != null) return
            if (PermissionPlan.askNotificationsNow(app.ui.loginMode, sdk, grantedSet(), app.prefs.notifAsked, playing = true)) app.ui.permissionReason = Need.NOTIFICATIONS
        }
    }

    /** Re-read what is granted (settings can change it behind our back) and, for someone using this device's music, raise the reason card once. */
    private fun refreshPermissions() {
        val g = grantedSet()
        app.ui.mediaAccess = PermissionPlan.access(Need.MEDIA, sdk, g, asked(Need.MEDIA))
        app.ui.notifAccess = PermissionPlan.access(Need.NOTIFICATIONS, sdk, g, asked(Need.NOTIFICATIONS))
        if (app.ui.permissionReason == null && PermissionPlan.askMediaNow(app.ui.loginMode, sdk, g, app.prefs.mediaAsked)) app.ui.permissionReason = Need.MEDIA
    }

    override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<String>, grantResults: IntArray) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (requestCode == REQ_MEDIA && grantResults.firstOrNull() == PackageManager.PERMISSION_GRANTED) app.library.rescan()
        refreshPermissions()
    }

    private companion object { const val REQ_MEDIA = 1; const val REQ_NOTIF = 2 }

    // Backlight timeout for the Player views (the click-wheel view keeps its own dimming): dim after N idle seconds, wake on input.
    private val idleHandler = android.os.Handler(android.os.Looper.getMainLooper())
    private var lastInput = android.os.SystemClock.uptimeMillis()
    private var dimmed = false
    private val idleCheck = object : Runnable {
        override fun run() {
            val s = app.prefs.backlightSec
            if (!wheelMode && s > 0 && !dimmed && android.os.SystemClock.uptimeMillis() - lastInput > s * 1000L) {
                dimmed = true
                window.attributes = window.attributes.also { it.screenBrightness = 0.01f }
            }
            idleHandler.postDelayed(this, 1000)
        }
    }
    private fun wake() {
        // every touch and key event (a long scroll sends hundreds): background work such as the NAS tag scan waits while this is recent
        com.ipodemu.library.tags.UserActivity.last = android.os.SystemClock.elapsedRealtime()
        lastInput = android.os.SystemClock.uptimeMillis()
        if (dimmed) { dimmed = false; applyBrightness() }
    }
    override fun dispatchTouchEvent(ev: MotionEvent): Boolean { wake(); return super.dispatchTouchEvent(ev) }

    override fun dispatchKeyEvent(event: KeyEvent): Boolean {
        wake()
        if (app.ui.pickerOpen && event.action == KeyEvent.ACTION_DOWN && event.repeatCount == 0) {
            when (event.keyCode) {
                KeyEvent.KEYCODE_BUTTON_L1, KeyEvent.KEYCODE_BUTTON_L2 -> { app.ui.stepPicker(-1); return true }
                KeyEvent.KEYCODE_BUTTON_R1, KeyEvent.KEYCODE_BUTTON_R2 -> { app.ui.stepPicker(1); return true }
            }
        }
        if (!wheelMode) PlayerKeys.translate(this, event)?.let { return it }
        // the click-wheel view never saw a bare PLAY or PAUSE key (PlayerKeys is not used there, KeyMapper only knows PLAY_PAUSE), so it fell through to
        // the system, which can send it to the app that last had the audio focus instead of this one
        if (wheelMode && com.ipodemu.input.MediaKeys.handles(event.keyCode)) {
            if (event.action == KeyEvent.ACTION_DOWN && event.repeatCount == 0 && com.ipodemu.input.MediaKeys.shouldToggle(event.keyCode, app.player.wantsToPlay)) app.player.toggle()
            return true
        }
        return super.dispatchKeyEvent(event)
    }

    override fun onKeyDown(keyCode: Int, event: KeyEvent): Boolean =
        (wheelMode && ipodView?.onKeyDownHw(keyCode, event) == true) || super.onKeyDown(keyCode, event)

    override fun onKeyUp(keyCode: Int, event: KeyEvent): Boolean =
        (wheelMode && ipodView?.onKeyUpHw(keyCode, event) == true) || super.onKeyUp(keyCode, event)

    override fun onGenericMotionEvent(event: MotionEvent): Boolean =
        (wheelMode && ipodView?.onHat(event) == true) || super.onGenericMotionEvent(event)

    override fun onPause() { idleHandler.removeCallbacks(idleCheck); super.onPause() }

    override fun onDestroy() {
        if (app.ui.permissions === permissionActions) app.ui.permissions = null
        ipodView?.release()
        ipodView = null
        super.onDestroy()
    }
}
