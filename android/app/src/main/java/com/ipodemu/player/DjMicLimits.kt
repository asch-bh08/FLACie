package com.ipodemu.player

/** What the mic button is doing: nothing, listening (from a press until the person stops talking), or waiting for the DJ's answer. */
enum class MicState { IDLE, LISTENING, THINKING }

/** What a press of the mic does: starts a conversation, stops the listening, ends the conversation between turns, or nothing while it thinks. */
enum class MicPress { START, STOP, END, WAIT }

object DjMicLimits {
    /** The longest one turn listens: a recogniser left open while music plays hears words with no pause (owner, 9 Nov: stuck on "listening"). */
    const val LISTEN_MAX_MS = 15_000L
    /** The longest it waits for the DJ's answer before letting go, so the mic takes a press again. */
    const val THINK_MAX_MS = 30_000L
}

/**
 * The mic's turns and their limits, without Android (DjMicButton keeps one): listening ends when the music starts again during a follow-up turn
 * (on the first the press itself pauses the song, and that pause is reported a moment later), and after [DjMicLimits.LISTEN_MAX_MS]; thinking
 * ends after [DjMicLimits.THINK_MAX_MS]. Times are in ms on any clock.
 */
class MicGate {
    var state = MicState.IDLE; private set
    var conversing = false; private set
    var turn = 0; private set
    private var since = 0L
    private var musicWas = false

    fun press(now: Long): MicPress = when (state) {
        MicState.LISTENING -> { state = MicState.IDLE; conversing = false; MicPress.STOP }
        MicState.THINKING -> if (now - since >= DjMicLimits.THINK_MAX_MS) { state = MicState.IDLE; conversing = true; turn = 0; MicPress.START } else MicPress.WAIT
        MicState.IDLE -> if (conversing) { conversing = false; MicPress.END } else { conversing = true; turn = 0; MicPress.START }
    }

    fun listening(now: Long, playing: Boolean = false) { state = MicState.LISTENING; since = now; musicWas = playing }
    fun thinking(now: Long) { state = MicState.THINKING; since = now }
    fun answered(now: Long, listenAgain: Boolean) { turn++; state = MicState.IDLE; since = now; if (!listenAgain) conversing = false }
    /** Silence, an error or a press ended it. */
    fun ended() { state = MicState.IDLE; conversing = false }

    /** Called often while it listens or thinks: true when it has just given up (the caller lets the recogniser go and ends the conversation). */
    fun tick(now: Long, playing: Boolean): Boolean {
        val started = playing && !musicWas
        musicWas = playing
        val over = when (state) {
            MicState.LISTENING -> (turn > 0 && started) || now - since >= DjMicLimits.LISTEN_MAX_MS
            MicState.THINKING -> now - since >= DjMicLimits.THINK_MAX_MS
            MicState.IDLE -> false
        }
        if (over) ended()
        return over
    }
}
