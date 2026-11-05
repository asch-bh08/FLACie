package com.ipodemu.playback

/**
 * Times a press of the mic: the press, the recogniser ready, and the moment it is really listening (F14). Each mark is logged under the tag FLACieMic
 * (adb logcat -s FLACieMic), so the wait before listening can be read off a device. The aim is listening within [TARGET_MS] of the press.
 */
class MicTiming(
    private val clock: () -> Long = { android.os.SystemClock.elapsedRealtime() },
    private val out: (String) -> Unit = { try { android.util.Log.i(TAG, it) } catch (_: Throwable) { } },
) {
    private var pressAt = -1L
    private var readyAt = -1L

    /** The person pressed (or the next turn of a conversation is starting). */
    fun press() { pressAt = clock(); readyAt = -1L; out("press") }

    /** The recogniser is made (or taken from the pre-warmed one) and asked to listen. */
    fun recogniserReady() {
        if (pressAt < 0) return
        readyAt = clock(); out("recogniser ready +${readyAt - pressAt} ms")
    }

    /** The recogniser says it is listening. Returns ms since the press (-1 when there was no press). */
    fun listening(): Long {
        if (pressAt < 0) return -1L
        val now = clock(); val total = now - pressAt
        out("listening +$total ms" + (if (readyAt >= 0) " (recogniser ready to listening ${now - readyAt} ms)" else "") + (if (total <= TARGET_MS) "" else " over the $TARGET_MS ms aim"))
        pressAt = -1L; readyAt = -1L
        return total
    }

    companion object {
        const val TAG = "FLACieMic"
        const val TARGET_MS = 500L
    }
}
