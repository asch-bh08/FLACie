package com.ipodemu.playback

/**
 * "Talk to the DJ" from the car's screen (owner, 30 Oct: the DJ button in the car didn't make it clear you could talk to it). A press of the mic button on
 * the car pauses the music and listens through the phone's own recogniser; the words go to the DJ ([ask]), which answers in its voice through the car. The
 * turns are the phone mic button's ([DjConversation]): a request that plays or pauses something ends it and leaves the music as chosen; any other control
 * ends it and the paused song carries on; after an answer it listens again once the DJ has finished talking, for about four seconds (eight after a question
 * back), and nothing said ends it quietly. "Thanks" is answered "Anytime." and a second press ends it. The phone has to have been given the microphone once on its own screen (the car can't show Android's question): until then it says so.
 * Pure (the mic, the music and the DJ come in), unit-tested; the service hands it the real ones and redraws the car's button on [onChange].
 */
class CarDjMic(
    private val ears: Ears, private val music: Music, private val ask: (String, (DjAskAnswer) -> Unit) -> Unit, private val djTalking: () -> Boolean,
    private val later: (Long, () -> Unit) -> Unit, private val say: (String) -> Unit, private val micAllowed: () -> Boolean,
) {
    /** The recogniser: [listen] hears one request (null: nothing said, or it failed); false when there is none on this phone. [waitMs]: how long to wait for
     *  the person to begin (0 on the first press, where the recogniser's own wait is used); nobody speaks in that time and it reports null. */
    interface Ears { fun listen(waitMs: Long, onHeard: (String?) -> Unit): Boolean; fun stop() }
    interface Music { val playing: Boolean; fun pause(); fun resume() }
    enum class State { IDLE, LISTENING, THINKING }

    var state = State.IDLE; private set
    /** From a press until the conversation ends (lit on the car's button between turns too). */
    var conversing = false; private set
    var onChange: () -> Unit = {}
    private var pausedByMic = false
    private var turn = 0

    /** The car's mic button: starts a conversation, or ends the one going on. */
    fun press() {
        when {
            state == State.THINKING -> {}
            conversing -> { ears.stop(); end(DjTurn.END) }
            !micAllowed() -> say("Open FLACie on your phone and press its mic once to let it hear you. Then you can talk to me from here.")
            else -> {
                conversing = true; turn = 0
                pausedByMic = music.playing
                if (pausedByMic) music.pause()
                listen(0L)
            }
        }
    }

    private fun listen(waitMs: Long) {
        state = State.LISTENING; onChange()
        if (!ears.listen(waitMs, ::heard)) { say("Sorry, this phone can't listen right now."); end(DjTurn.END) }
    }

    private fun heard(text: String?) {
        if (!conversing) return
        if (text.isNullOrBlank()) { end(DjTurn.END); return }
        // "thanks", "bye": the DJ says "Anytime." (the server's fixed reply, made ahead in the voice) and the music comes back up
        val goodbye = DjConversation.isGoodbye(text)
        state = State.THINKING; onChange()
        ask(text) { a ->
            if (!conversing) return@ask
            state = State.IDLE
            val how = if (goodbye) DjTurn.END else DjConversation.after(a.doThis, a.asked, ++turn, a.intent)
            if (how != DjTurn.LISTEN_AGAIN) { end(how); return@ask }
            onChange()
            // the next turn once the DJ has finished talking (the mic would hear it otherwise), waiting a short while for a follow-up, longer after a question
            later(250L) { listenWhenQuiet(DjConversation.listenWindowMs(a.asked)) }
        }
    }

    private fun listenWhenQuiet(waitMs: Long) {
        if (!conversing) return
        if (djTalking()) later(250L) { listenWhenQuiet(waitMs) } else later(300L) { if (conversing && state == State.IDLE) listen(waitMs) }
    }

    private fun end(how: DjTurn) {
        if (!conversing) return
        conversing = false; state = State.IDLE
        if (DjConversation.resumeAtEnd(pausedByMic, how) && !music.playing) music.resume()
        pausedByMic = false
        onChange()
    }
}
