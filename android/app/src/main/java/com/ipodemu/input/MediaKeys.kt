package com.ipodemu.input

import android.view.KeyEvent

/**
 * What the dedicated PLAY and PAUSE media keys (a headset, a Bluetooth button, a keyboard) do while the app is in front. The decision is the
 * player's toggle, but only when it changes anything: PLAY on a song that already plays must not pause it, PAUSE on a paused one must not
 * start it (PLAY_PAUSE always toggles). No Android classes beyond the key constants, so it is unit-tested on the JVM.
 *
 * Why the app handles these itself: after another app took the audio focus the system may route a bare PLAY key to that app's session or
 * receiver instead of ours, so while FLACie is in front it does not wait for the media session to receive it. With the app in the
 * background the routing is Android's and cannot be changed from the app (see docs/test-run/I-android-features-notes.md).
 */
object MediaKeys {
    /** True when the key should toggle the player now ([wantsToPlay]: playing, or about to). */
    fun shouldToggle(keyCode: Int, wantsToPlay: Boolean): Boolean = when (keyCode) {
        KeyEvent.KEYCODE_MEDIA_PLAY_PAUSE, KeyEvent.KEYCODE_HEADSETHOOK -> true
        KeyEvent.KEYCODE_MEDIA_PLAY -> !wantsToPlay
        KeyEvent.KEYCODE_MEDIA_PAUSE -> wantsToPlay
        else -> false
    }

    /** Keys this object decides. */
    fun handles(keyCode: Int): Boolean = keyCode == KeyEvent.KEYCODE_MEDIA_PLAY || keyCode == KeyEvent.KEYCODE_MEDIA_PAUSE
}
