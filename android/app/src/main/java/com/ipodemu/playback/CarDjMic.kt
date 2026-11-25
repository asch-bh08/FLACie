package com.ipodemu.playback

/**
 * "Talk to the DJ" from the car's screen (owner, 30 Oct: the DJ button in the car didn't make it clear you could talk to it). A press of the mic button on
 * the car pauses the music and listens through the phone's own recogniser; the words go to the DJ ([ask]), which answers in its voice through the car. The
 * turns are the phone mic button's ([DjConversation]): a request that plays or pauses something ends it and leaves the music as chosen; any other control
 * ends it and the paused song carries on; after an answer it listens again once the DJ has finished talking, for about four seconds (eight after a question
 * back), and nothing said ends it quietly. "Thanks" is answered "Anytime." and a second press ends it. The phone has to have been given the microphone once on its own screen (the car can't show Android's question): until then it says so.
 * Each turn has hard limits ([MicTurn], owner, 25 Nov: "it listens forever, then the song starts playing again, then it hears the song"): listening stops
 * after about 8 s and what was heard is used; no answer in 12 s lets go and says so; a press while it works cancels the request. The music is held quiet
 * ([hold]) from the press to the end of the conversation, so the mic never hears it even if something starts it again, and each stage is shown on the
 * car ([show]: Listening…, Heard: "…" · Working…, then the answer).
 * Pure (the mic, the music and the DJ come in), unit-tested; the service hands it the real ones and redraws the car's button on [onChange].
 */
class CarDjMic(
    private val ears: Ears, private val music: Music, private val ask: (String, (DjAskAnswer) -> Unit) -> Unit, private val djTalking: () -> Boolean,
    private val later: (Long, () -> Unit) -> Unit, private val say: (String) -> Unit, private val micAllowed: () -> Boolean,
    /** The person moved on: the request still waiting for its answer is dropped (see [DjAskGate.cancel]). */
    private val cancelAsk: () -> Unit = {},
    /** True from the press to the end: the music is kept silent. */
    private val hold: (Boolean) -> Unit = {},
    /** The line the car shows for the turn (null: none). */
    private val show: (String?) -> Unit = {},
) {
    /** The recogniser: [listen] hears one request (null: nothing said, or it failed); false when there is none on this phone. [waitMs]: how long to wait for
     *  the person to begin (0 on the first press, where the recogniser's own wait is used); nobody speaks in that time and it reports null. [finish]: stop
     *  listening now and hand over what was heard so far. */
    interface Ears { fun listen(waitMs: Long, onHeard: (String?) -> Unit): Boolean; fun stop(); fun finish() {} }
    interface Music { val playing: Boolean; fun pause(); fun resume() }
    enum class State { IDLE, LISTENING, THINKING }

    var state = State.IDLE; private set
    /** From a press until the conversation ends (lit on the car's button between turns too). */
    var conversing = false; private set
    var onChange: () -> Unit = {}
    private var pausedByMic = false
    private var turn = 0
    /** Counts every listen and every request, so a limit set for an earlier one does nothing. */
    private var step = 0
    private var line: String? = null

    /** The car's mic button: starts a conversation, ends the one going on, or cancels the request being worked on. */
    fun press() {
        when {
            state == State.THINKING -> { cancelAsk(); end(DjTurn.END) }
            conversing -> { ears.stop(); end(DjTurn.END) }
            !micAllowed() -> say("Open FLACie on your phone and press its mic once to let it hear you. Then you can talk to me from here.")
            else -> {
                conversing = true; turn = 0
                pausedByMic = music.playing
                if (pausedByMic) music.pause()
                hold(true)
                listen(0L)
            }
        }
    }

    private fun showLine(text: String?) { line = text; show(text) }
    /** Shows [text] for a while, then clears it if nothing newer took its place. */
    private fun showForAWhile(text: String) { showLine(text); later(MicTurn.RESULT_SHOW_MS) { if (line == text && state == State.IDLE) showLine(null) } }

    private fun listen(waitMs: Long) {
        state = State.LISTENING; onChange()
        showLine(MicTurn.LISTENING)
        val mine = ++step
        if (!ears.listen(waitMs, ::heard)) { say("Sorry, this phone can't listen right now."); end(DjTurn.END); return }
        // the hard limit: stop listening and use what was heard; if the recogniser hands nothing back soon after, give up and say so
        later(MicTurn.LISTEN_MAX_MS) {
            if (step != mine || state != State.LISTENING) return@later
            ears.finish()
            later(MicTurn.RESULT_GRACE_MS) {
                if (step != mine || state != State.LISTENING) return@later
                ears.stop(); say(MicTurn.NOT_CAUGHT); end(DjTurn.END); showForAWhile(MicTurn.NOT_CAUGHT)
            }
        }
    }

    private fun heard(text: String?) {
        if (!conversing || state != State.LISTENING) return
        if (text.isNullOrBlank()) {
            val first = turn == 0
            end(DjTurn.END)
            if (first) showForAWhile(MicTurn.NOTHING_HEARD) else showLine(null)
            return
        }
        // "thanks", "bye": the DJ says "Anytime." (the server's fixed reply, made ahead in the voice) and the music comes back up
        val goodbye = DjConversation.isGoodbye(text)
        state = State.THINKING; onChange()
        showLine(MicTurn.heard(text))
        val mine = ++step
        later(MicTurn.THINK_MAX_MS) {
            if (step != mine || state != State.THINKING) return@later
            cancelAsk(); end(DjTurn.END); showForAWhile(MicTurn.NO_ANSWER)
        }
        ask(text) { a ->
            if (!conversing || step != mine || state != State.THINKING) return@ask
            state = State.IDLE
            showForAWhile(MicTurn.done(a))
            val how = if (goodbye) DjTurn.END else DjConversation.after(a.mainDo, a.asked, ++turn, a.intent)
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
        conversing = false; state = State.IDLE; step++
        if (line == MicTurn.LISTENING || line?.startsWith("Heard:") == true) showLine(null)
        hold(false)
        if (DjConversation.resumeAtEnd(pausedByMic, how) && !music.playing) music.resume()
        pausedByMic = false
        onChange()
    }
}
