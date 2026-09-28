package com.ipodemu.player

import android.view.KeyEvent
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.ui.draw.drawBehind
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.focus.FocusDirection
import androidx.compose.ui.viewinterop.AndroidView
import com.ipodemu.App
import com.ipodemu.input.Zone
import com.ipodemu.theme.Themes
import com.ipodemu.ui.IpodView
import com.ipodemu.ui.MainActivity

/** Gamepad / keyboard shortcuts for the Player UI (the RG Rotate's face buttons and shoulders). */
object PlayerKeys {
    /** Returns true/false when the key was handled here, or null to let normal dispatch continue. */
    fun translate(activity: MainActivity, e: KeyEvent): Boolean? {
        val app = App.of(activity)
        val swap = app.prefs.swapFaceButtons
        val a = if (swap) KeyEvent.KEYCODE_BUTTON_B else KeyEvent.KEYCODE_BUTTON_A
        val b = if (swap) KeyEvent.KEYCODE_BUTTON_A else KeyEvent.KEYCODE_BUTTON_B
        val x = if (swap) KeyEvent.KEYCODE_BUTTON_Y else KeyEvent.KEYCODE_BUTTON_X
        val y = if (swap) KeyEvent.KEYCODE_BUTTON_X else KeyEvent.KEYCODE_BUTTON_Y
        val down = e.action == KeyEvent.ACTION_DOWN
        return when (e.keyCode) {
            a -> activity.window.decorView.dispatchKeyEvent(KeyEvent(e.downTime, e.eventTime, e.action, KeyEvent.KEYCODE_DPAD_CENTER, e.repeatCount))
            b, KeyEvent.KEYCODE_ESCAPE -> { if (e.action == KeyEvent.ACTION_UP) activity.onBackPressedDispatcher.onBackPressed(); true }
            x, KeyEvent.KEYCODE_BUTTON_START, KeyEvent.KEYCODE_MEDIA_PLAY_PAUSE -> { if (down && e.repeatCount == 0) app.player.toggle(); true }
            y -> { if (down && e.repeatCount == 0) app.ui.requestNowPlaying(); true }
            KeyEvent.KEYCODE_BUTTON_L1, KeyEvent.KEYCODE_BUTTON_L2, KeyEvent.KEYCODE_MEDIA_PREVIOUS -> { if (down && e.repeatCount == 0) app.player.prev(); true }
            KeyEvent.KEYCODE_BUTTON_R1, KeyEvent.KEYCODE_BUTTON_R2, KeyEvent.KEYCODE_MEDIA_NEXT -> { if (down && e.repeatCount == 0) app.player.next(); true }
            else -> null
        }
    }
}

@Composable
fun AppRoot(activity: MainActivity) {
    val app = App.of(activity)
    val ui = app.ui
    ui.rev
    val nav = remember { PlayerNav() }
    val model = Themes.model(ui.model)
    val wheel = ui.wheelActive
    CompositionLocalProvider(LocalApp provides app) {
        // the picker is an overlay, so opening/closing it never tears down the Player or the wheel view underneath
        Box(Modifier.fillMaxSize()) {
            if (wheel) {
                // rebuild the wheel view when the mode, iPod or colour changes so its theme is re-read from prefs
                androidx.compose.runtime.key(ui.viewMode, ui.model, ui.colorway) { AndroidView(
                    modifier = Modifier.fillMaxSize().safeArea(),
                    factory = { ctx -> IpodView(ctx).also { activity.ipodView = it; it.requestFocus() } },
                    onRelease = { it.release(); if (activity.ipodView === it) activity.ipodView = null },
                ) }
            } else PlayerRoot(activity, nav)
            // overlays sit outside PlayerRoot, so give them the active theme's widget style too
            val oStyle = remember(model, ui.colorway, ui.ipodTheme) { IpodStyle(model, model.colors[ui.colorway.coerceIn(0, model.colors.lastIndex)], !ui.ipodTheme) }
            val overlay: @Composable (@Composable () -> Unit) -> Unit = {
                CompositionLocalProvider(LocalStyle provides oStyle, LocalScheme provides if (ui.ipodTheme) LocalScheme.current else remember { buildModernScheme(null) }) {
                    Box(Modifier.fillMaxSize().background(androidx.compose.ui.graphics.Color(0xFF07080B)).safeArea()) { it() }
                }
            }
            if (ui.pickerOpen) overlay { PickerScreen() }
            if (ui.syncSetupOpen) overlay { SyncSetupScreen() }
            if (ui.jellyfinSetupOpen) overlay { JellyfinSetupScreen() }
            if (ui.plexSetupOpen) overlay { PlexSetupScreen() }
            if (ui.nasSetupOpen) overlay { NasSetupScreen() }
            if (ui.lidarrSetupOpen) overlay { LidarrSetupScreen() }
            if (ui.accountOpen) overlay { AccountScreen() }
        }
    }
}

@OptIn(androidx.compose.ui.ExperimentalComposeUiApi::class)
@Composable
private fun PlayerRoot(activity: MainActivity, nav: PlayerNav) {
    val app = LocalApp.current
    val ui = app.ui
    ui.rev
    val model = Themes.model(ui.model)
    val cw = model.colors[ui.colorway.coerceIn(0, model.colors.lastIndex)]
    val modern = !ui.ipodTheme
    val style = remember(model, cw, modern) { IpodStyle(model, cw, modern) }
    val mode = if (modern) 0 else ui.viewMode
    val snap = rememberSnap(app.player, app.prefs)

    val artKey = nav.overrideArt ?: snap.track?.artKey
    var artColors by remember { mutableStateOf<ArtColors?>(null) }
    LaunchedEffect(artKey, ui.dynamicColor) {
        if (!ui.dynamicColor || artKey == null) artColors = null
        else ArtPalette.of(app.art, artKey)?.let { artColors = it }
    }
    val dark = when (app.prefs.appearance) { 0 -> true; 1 -> false; else -> isSystemInDarkTheme() }
    val scheme = animatedScheme(if (modern) buildModernScheme(if (ui.dynamicColor) artColors else null) else buildScheme(style, artColors, if (style.mono) false else dark, ui.dynamicColor))

    CompositionLocalProvider(LocalStyle provides style, LocalScheme provides scheme, LocalHardware provides (mode == 1)) {
        // (movableContentOf here left a frozen, unresponsive copy of the screen after switching modes, so the UI is simply rebuilt)
        val content: @Composable (@Composable () -> Unit) -> Unit = { it() }
        val body: @Composable () -> Unit = {
            Box(Modifier.fillMaxSize().drawBehind { drawRect(Brush.verticalGradient(listOf(scheme.top, scheme.bottom), endY = if (modern) size.height * 0.55f else Float.POSITIVE_INFINITY)) }.let { if (mode == 1) it else it.safeArea() }) {
                PlayerHost(nav)
                BackHandler(enabled = !ui.pickerOpen && nav.stack.size == 1 && nav.top == Screen.Home && !nav.nowPlaying && nav.sheet == null && nav.nameDialog == null) { activity.moveTaskToBack(true) }
            }
        }
        if (mode == 1) {
            // straight into Compose's focus owner: view-level dispatch is dropped while the window is in touch mode
            val composeView = androidx.compose.ui.platform.LocalView.current
            val focusManager = androidx.compose.ui.platform.LocalFocusManager.current
            // keep the highlighted row when the finger lands on the wheel (touching would otherwise drop focus)
            androidx.compose.runtime.SideEffect { composeView.isFocusableInTouchMode = true }
            var lastStep by androidx.compose.runtime.remember { androidx.compose.runtime.mutableLongStateOf(0L) }
            val inputMode = androidx.compose.ui.platform.LocalInputModeManager.current
            // clickable rows are only focusable in keyboard mode; the wheel acts like a keyboard/D-pad
            fun keyboardMode() {
                if (inputMode.inputMode != androidx.compose.ui.input.InputMode.Keyboard) inputMode.requestInputMode(androidx.compose.ui.input.InputMode.Keyboard)
                WheelFocus.restore()
            }
            fun key(code: Int) {
                keyboardMode()
                val t = android.os.SystemClock.uptimeMillis()
                composeView.dispatchKeyEvent(KeyEvent(t, t, KeyEvent.ACTION_DOWN, code, 0))
                composeView.dispatchKeyEvent(KeyEvent(t, t, KeyEvent.ACTION_UP, code, 0))
            }
            DeviceFrame(model, cw, onWheel = { z ->
                when (z) {
                    Zone.MENU -> if (nav.sheet != null) nav.sheet = null else if (nav.nowPlaying) nav.nowPlaying = false else if (!nav.pop()) nav.home()
                    Zone.PLAY_PAUSE -> app.player.toggle()
                    Zone.NEXT -> app.player.next()
                    Zone.PREV -> app.player.prev()
                    Zone.CENTER -> if (nav.nowPlaying) app.player.toggle() else key(KeyEvent.KEYCODE_DPAD_CENTER)
                }
            }, onStep = { d ->
                if (nav.nowPlaying) app.player.seekBy(d * 5000L)
                else {
                    val now = android.os.SystemClock.uptimeMillis()
                    if (now - lastStep > 250) keyboardMode()
                    lastStep = now
                    if (!focusManager.moveFocus(if (d > 0) FocusDirection.Down else FocusDirection.Up)) focusManager.moveFocus(if (d > 0) FocusDirection.Next else FocusDirection.Previous)
                }
            }, content = { content(body) })
        } else content(body)
    }
}
