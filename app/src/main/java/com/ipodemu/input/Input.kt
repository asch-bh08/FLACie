package com.ipodemu.input

import android.view.KeyEvent

/** Device-independent commands. Touch wheel and hardware buttons both produce these. */
sealed interface Input {
    data class Scroll(val n: Int) : Input
    data class Page(val dir: Int) : Input
    data class Seek(val dir: Int) : Input
    object Select : Input
    object Back : Input
    object BackHold : Input
    object PlayPause : Input
    object Next : Input
    object Prev : Input
    object NowPlaying : Input
    object ToggleShuffle : Input
}

/** Maps RG Rotate D-pad / face / shoulder buttons onto [Input]. Left/right tap-vs-hold is resolved by the caller. */
object KeyMapper {
    /** Repeat-throttled vertical scroll step for held D-pad up/down; null = skip this repeat. */
    fun scrollStep(repeatCount: Int): Int? = when {
        repeatCount == 0 -> 1
        repeatCount < 12 -> if (repeatCount % 4 == 0) 1 else null
        else -> if (repeatCount % 2 == 0) 2 + (repeatCount - 12) / 20 else null
    }

    /** Non-directional buttons. Returns null for keys we do not own (volume etc.). */
    fun map(keyCode: Int, swapFace: Boolean): Input? {
        val a = if (swapFace) KeyEvent.KEYCODE_BUTTON_B else KeyEvent.KEYCODE_BUTTON_A
        val b = if (swapFace) KeyEvent.KEYCODE_BUTTON_A else KeyEvent.KEYCODE_BUTTON_B
        val x = if (swapFace) KeyEvent.KEYCODE_BUTTON_Y else KeyEvent.KEYCODE_BUTTON_X
        val y = if (swapFace) KeyEvent.KEYCODE_BUTTON_X else KeyEvent.KEYCODE_BUTTON_Y
        return when (keyCode) {
            a, KeyEvent.KEYCODE_DPAD_CENTER, KeyEvent.KEYCODE_ENTER -> Input.Select
            b, KeyEvent.KEYCODE_BACK, KeyEvent.KEYCODE_ESCAPE, KeyEvent.KEYCODE_BUTTON_SELECT -> Input.Back
            x, KeyEvent.KEYCODE_BUTTON_START, KeyEvent.KEYCODE_MEDIA_PLAY_PAUSE, KeyEvent.KEYCODE_SPACE -> Input.PlayPause
            y -> Input.NowPlaying
            KeyEvent.KEYCODE_BUTTON_L1 -> Input.Page(-1)
            KeyEvent.KEYCODE_BUTTON_R1 -> Input.Page(1)
            KeyEvent.KEYCODE_BUTTON_L2, KeyEvent.KEYCODE_MEDIA_PREVIOUS -> Input.Prev
            KeyEvent.KEYCODE_BUTTON_R2, KeyEvent.KEYCODE_MEDIA_NEXT -> Input.Next
            else -> null
        }
    }
}

/** On-screen touch keys beside the click wheel. */
enum class Key { SHUFFLE }
