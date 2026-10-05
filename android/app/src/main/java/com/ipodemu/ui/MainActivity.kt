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
import com.ipodemu.player.AppRoot
import com.ipodemu.player.PlayerKeys

class MainActivity : ComponentActivity() {
    /** The click-wheel view, when that mode is showing (it needs raw key/hat events). */
    var ipodView: IpodView? = null

    private val app get() = App.of(this)
    private val wheelMode get() = app.ui.wheelActive && !app.ui.pickerOpen && !app.ui.syncSetupOpen &&
        !app.ui.jellyfinSetupOpen && !app.ui.plexSetupOpen && !app.ui.nasSetupOpen && !app.ui.lidarrSetupOpen && !app.ui.downloadsOpen && !app.ui.accountOpen && !app.ui.devicesOpen && !app.ui.jamOpen && app.ui.loginMode.isNotEmpty()   // these overlays take keys themselves

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        WindowCompat.setDecorFitsSystemWindows(window, false)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        ensurePermission()
        app.library.start()
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
    }

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

    private fun ensurePermission() {
        val perm = if (Build.VERSION.SDK_INT >= 33) Manifest.permission.READ_MEDIA_AUDIO else Manifest.permission.READ_EXTERNAL_STORAGE
        val wanted = ArrayList<String>()
        if (checkSelfPermission(perm) != PackageManager.PERMISSION_GRANTED) wanted += perm
        // Android 13+ needs this for the playback notification / lock-screen controls to show.
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) wanted += Manifest.permission.POST_NOTIFICATIONS
        if (wanted.isNotEmpty()) requestPermissions(wanted.toTypedArray(), 1)
    }

    override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<String>, grantResults: IntArray) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        val i = permissions.indexOfFirst { it == Manifest.permission.READ_EXTERNAL_STORAGE || it == Manifest.permission.READ_MEDIA_AUDIO }
        if (i >= 0 && grantResults.getOrNull(i) == PackageManager.PERMISSION_GRANTED) app.library.rescan()
    }

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
        ipodView?.release()
        ipodView = null
        super.onDestroy()
    }
}
