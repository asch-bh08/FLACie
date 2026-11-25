package com.ipodemu.playback

/**
 * The limits and the words of one turn of "talk to the DJ", the same on the phone's mic button and the car's (owner, 25 Nov: "it listens forever ...
 * and there's no way of knowing if the DJ heard you"). Listening stops after [LISTEN_MAX_MS] whatever the recogniser does (on a weak signal the online
 * one waited for the network for half a minute and more), and what it heard by then is used; working on the answer stops after [THINK_MAX_MS] (the
 * phone answers by itself at [DjAskGate.ASK_DEADLINE_MS], so this is only a last guard). Each stage is shown: Listening…, Heard: "…" · Working…,
 * then the answer or why there is none.
 */
object MicTurn {
    /** The longest one turn listens. */
    const val LISTEN_MAX_MS = 8_000L
    /** After the listening limit, how long the recogniser is given to hand over what it heard before the turn gives up. */
    const val RESULT_GRACE_MS = 2_500L
    /** The longest the turn waits for the answer. */
    const val THINK_MAX_MS = 12_000L
    /** How long the answer (or the reason there is none) stays on show. */
    const val RESULT_SHOW_MS = 5_000L

    const val LISTENING = "Listening…"
    const val NOT_CAUGHT = "Sorry, I didn't catch that."
    const val NO_ANSWER = "Sorry, no answer in time. Try again."
    const val NOTHING_HEARD = "Didn't hear anything"

    fun heard(text: String) = "Heard: “${text.trim().take(60)}” · Working…"

    /** What the answer did, in a line: its own words, else the step it carried out. */
    fun done(a: DjAskAnswer): String = a.say.trim().takeIf { it.isNotEmpty() }?.let { if (it.length > 90) it.take(89) + "…" else it } ?: when (a.mainDo) {
        DjAskDo.PLAY, DjAskDo.RESUME -> "Playing"
        DjAskDo.UPNEXT -> "Up next"
        DjAskDo.NEXT -> "Skipped"
        DjAskDo.PAUSE -> "Paused"
        DjAskDo.VOLUMEUP -> "Louder"
        DjAskDo.VOLUMEDOWN -> "Quieter"
        DjAskDo.DJON -> "DJ on"
        DjAskDo.DJOFF -> "DJ off"
        else -> "Done"
    }
}
