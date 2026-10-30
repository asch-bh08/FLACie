package com.ipodemu.playback

/**
 * "Talk to the DJ" from the car's screen (owner, 30 Oct: the DJ button in the car didn't make it clear you could talk to it). A press of the mic button on
 * the car pauses the music and listens through the phone's own recogniser; the words go to the DJ ([ask]), which answers in its voice through the car. The
 * turns are the phone mic button's ([DjConversation]): a request that plays or pauses something ends it and leaves the music as chosen; an answer ends it
 * and the paused song carries on; when the DJ asks something back it listens again once it has finished talking. Nothing said, "thanks" or a second press
 * ends it quietly. The phone has to have been given the microphone once on its own screen (the car can't show Android's question): until then it says so.
 * Pure (the mic, the music and the DJ come in), unit-tested; the service hands it the real ones and redraws the car's button on [onChange].
 */
class CarDjMic(
    private val ears: Ears, private val music: Music, private val ask: (String, (DjAskAnswer) -> Unit) -> Unit, private val djTalking: () -> Boolean,
    private val later: (Long, () -> Unit) -> Unit, private val say: (String) -> Unit, private val micAllowed: () -> Boolean,
) {
    /** The recogniser: [listen] hears one request (null: nothing said, or it failed); false when there is none on this phone. */
    interface Ears { fun listen(onHeard: (String?) -> Unit): Boolean; fun stop() }
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
                listen()
            }
        }
    }

    private fun listen() {
        state = State.LISTENING; onChange()
        if (!ears.listen(::heard)) { say("Sorry, this phone can't listen right now."); end(DjTurn.END) }
    }

    private fun heard(text: String?) {
        if (!conversing) return
        if (text.isNullOrBlank() || DjConversation.isGoodbye(text)) { end(DjTurn.END); return }
        state = State.THINKING; onChange()
        ask(text) { a ->
            if (!conversing) return@ask
            state = State.IDLE
            val how = DjConversation.after(a.doThis, a.asked, ++turn)
            if (how != DjTurn.LISTEN_AGAIN) { end(how); return@ask }
            onChange()
            // the next turn once the DJ has finished asking (the mic would hear it otherwise)
            later(250L, ::listenWhenQuiet)
        }
    }

    private fun listenWhenQuiet() {
        if (!conversing) return
        if (djTalking()) later(250L, ::listenWhenQuiet) else later(300L) { if (conversing && state == State.IDLE) listen() }
    }

    private fun end(how: DjTurn) {
        if (!conversing) return
        conversing = false; state = State.IDLE
        if (DjConversation.resumeAtEnd(pausedByMic, how) && !music.playing) music.resume()
        pausedByMic = false
        onChange()
    }
}
