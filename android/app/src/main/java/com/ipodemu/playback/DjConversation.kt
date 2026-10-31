package com.ipodemu.playback

/** What happens after one turn of "talk to the DJ": listen again, or end the conversation (and whether the paused song carries on). */
enum class DjTurn { LISTEN_AGAIN, END, END_MUSIC_CHOSEN }

/**
 * Conversation mode for "talk to the DJ": a press of the mic pauses the music (the mic heard the song otherwise) and listens, and from there it takes turns
 * like a person would (owner, 31 Oct). A plain control the DJ carried out (skip, pause, volume, shuffle, play something) ends it at once: nothing more is wanted.
 * A normal answer ("It's Firestarter.") leaves a short window, about four seconds, in which the mic listens again for a follow-up, and ends quietly if nobody
 * speaks. When the DJ asks something back the window is longer, about eight seconds, as the person has to think. "Thanks", "bye", "that's all" or "never mind"
 * end it with a quick "Anytime." and the music comes back up. About 1.2 seconds of quiet after speech ends the person's turn. An answer that starts music
 * (play, next, resume) or pauses it leaves the music as chosen; otherwise the song paused at the start carries on. Pure; the server has the same rules
 * (FLACie.Core DjConversation.cs), keep the two the same.
 */
object DjConversation {
    /** Turns at most, so a mic left lit in a noisy room stops by itself. */
    const val MAX_TURNS = 8

    /** After a normal answer: how long the mic waits for the person to start speaking again before it lets go quietly. */
    const val FOLLOW_UP_MS = 4000L
    /** After the DJ asked something back: longer, as the person has to think about it. */
    const val ANSWER_WINDOW_MS = 8000L
    /** Quiet this long after the person's speech means they have finished their turn (the recogniser's complete-silence length). */
    const val END_OF_SPEECH_MS = 1200
    /** What the DJ says to "thanks" and its kind: a fixed line the server has made ahead in the voice, so it is heard at once. */
    const val ANYTIME = "Anytime."

    private val goodbye = Regex(
        "^(ok(ay)?[, ]+)?(that'?s (all|it|everything)|thanks?( you)?( dj)?|thank you( very much| so much)?|cheers|no,? thanks?( you)?|bye( bye)?|good ?bye|see (you|ya)|never ?mind|nothing( else)?|i'?m (good|done|fine)|all good|stop listening|that will be all|we'?re done|done)[.!]*$",
        RegexOption.IGNORE_CASE,
    )

    /** The person ended the conversation ("that's all", "thanks", "bye", "never mind"...): nothing is asked of the DJ. */
    fun isGoodbye(text: String?): Boolean = text != null && goodbye.matches(text.trim().replace('’', '\''))

    /** How long the mic waits for the person to begin speaking on the next turn: longer when the DJ [asked] something back. */
    fun listenWindowMs(asked: Boolean): Long = if (asked) ANSWER_WINDOW_MS else FOLLOW_UP_MS

    /**
     * After the DJ answered [a] on turn [turn] (1 = the first). [intent] is the server's name for what it understood ("Clarify", "Goodbye", "ThumbsDown"...).
     * A control carried out ends it; a goodbye ends it; anything else (an answer, a question back, a line that didn't catch it) listens again until the last
     * turn, for [listenWindowMs]. [asked] is kept for callers that know the question back from the answer's flag.
     */
    @Suppress("UNUSED_PARAMETER")
    fun after(a: DjAskDo, asked: Boolean, turn: Int, intent: String = ""): DjTurn = when (a) {
        DjAskDo.PLAY, DjAskDo.NEXT, DjAskDo.RESUME, DjAskDo.PAUSE -> DjTurn.END_MUSIC_CHOSEN
        DjAskDo.NONE -> if (intent != "Goodbye" && intent != "ThumbsDown" && turn < MAX_TURNS) DjTurn.LISTEN_AGAIN else DjTurn.END
        else -> DjTurn.END
    }

    /** At the end: the song paused by the mic carries on, unless the conversation chose what the music does. */
    fun resumeAtEnd(pausedByMic: Boolean, how: DjTurn): Boolean = pausedByMic && how != DjTurn.END_MUSIC_CHOSEN
}
