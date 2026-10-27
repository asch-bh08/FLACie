package com.ipodemu.playback

/** What happens after one turn of "talk to the DJ": listen again, or end the conversation (and whether the paused song carries on). */
enum class DjTurn { LISTEN_AGAIN, END, END_MUSIC_CHOSEN }

/**
 * Conversation mode for "talk to the DJ" (owner, 27 Oct): a press of the mic pauses the music (the mic heard the song otherwise) and starts a
 * conversation; after each answer the mic listens again once the DJ has stopped talking, until "that's all" (or thanks, bye, never mind), a press,
 * or nothing said. An answer that starts music (play, next, resume) or pauses it ends it too, so the music asked for is not paused again; otherwise
 * the song paused at the start carries on at the end. Pure; the server has the same rules (FLACie.Core DjConversation.cs), keep the two the same.
 */
object DjConversation {
    /** Turns at most, so a mic left lit in a noisy room stops by itself. */
    const val MAX_TURNS = 8

    private val goodbye = Regex(
        "^(ok(ay)?[, ]+)?(that'?s (all|it|everything)|thanks?( you)?( dj)?|thank you( very much| so much)?|cheers|no,? thanks?( you)?|bye( bye)?|good ?bye|see (you|ya)|never ?mind|nothing( else)?|i'?m (good|done|fine)|all good|stop listening|that will be all|we'?re done|done)[.!]*$",
        RegexOption.IGNORE_CASE,
    )

    /** The person ended the conversation ("that's all", "thanks", "bye", "never mind"...): nothing is asked of the DJ. */
    fun isGoodbye(text: String?): Boolean = text != null && goodbye.matches(text.trim().replace('’', '\''))

    /** After the DJ answered [a] on turn [turn] (1 = the first): listen again, or end. */
    fun after(a: DjAskDo, turn: Int): DjTurn = when (a) {
        DjAskDo.PLAY, DjAskDo.NEXT, DjAskDo.RESUME, DjAskDo.PAUSE -> DjTurn.END_MUSIC_CHOSEN
        else -> if (turn >= MAX_TURNS) DjTurn.END else DjTurn.LISTEN_AGAIN
    }

    /** At the end: the song paused by the mic carries on, unless the conversation chose what the music does. */
    fun resumeAtEnd(pausedByMic: Boolean, how: DjTurn): Boolean = pausedByMic && how != DjTurn.END_MUSIC_CHOSEN
}
