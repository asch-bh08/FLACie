package com.ipodemu.player

import android.Manifest
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import android.speech.RecognitionListener
import android.speech.RecognizerIntent
import android.speech.SpeechRecognizer
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.core.content.ContextCompat
import com.ipodemu.playback.DjAskDo
import com.ipodemu.playback.DjConversation
import com.ipodemu.playback.DjTurn
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import androidx.compose.runtime.rememberCoroutineScope

/**
 * "Talk to the DJ" on the phone: a glass mic button. Nothing listens until it is pressed; the platform's recogniser (on the device when it can) turns the
 * speech into words, and only the words go to FLACie Web. The first press asks for the microphone, saying why first. With [onSearch] (the search screen)
 * words that are not a request fill the search instead. Not there without FLACie Web or a recogniser.
 */
@Composable
fun DjMicButton(size: Dp = 44.dp, tint: Color = Color.White, glass: Color = Color(0x47000000), lit: Color = LocalScheme.current.accent, onSearch: ((String) -> Unit)? = null) {
    val app = LocalApp.current
    val ctx = LocalContext.current
    if (!app.player.djAvailable || !SpeechRecognizer.isRecognitionAvailable(ctx)) return
    var state by remember { mutableStateOf(MicState.IDLE) }
    var explaining by remember { mutableStateOf(false) }
    val rec = remember { mutableStateOf<SpeechRecognizer?>(null) }
    val handler = remember { android.os.Handler(android.os.Looper.getMainLooper()) }
    val timing = remember { com.ipodemu.playback.MicTiming() }
    // a recogniser is kept made while this button is on screen, so listening starts at once
    DisposableEffect(Unit) { MicSpeech.arm(ctx); onDispose { MicSpeech.disarm() } }
    val giveUp = remember { mutableStateOf<Runnable?>(null) }
    fun stop() { giveUp.value?.let { handler.removeCallbacks(it) }; giveUp.value = null; rec.value?.let { try { it.stopListening(); it.destroy() } catch (_: Exception) { }; MicSpeech.recycle(ctx) }; rec.value = null }
    DisposableEffect(Unit) { onDispose { stop(); if (state == MicState.LISTENING) DjStatus.now.done() } }
    // the DJ bar above the mini player follows the mic: listening, then (set in heard) what it heard and thinking, then nothing (its words are the caption)
    LaunchedEffect(state) { when (state) { MicState.LISTENING -> DjStatus.now.listening(); MicState.IDLE -> DjStatus.now.done(); MicState.THINKING -> Unit } }

    // the conversation (DjConversation): on from a press until it ends; whether the mic paused the song (it carries on at the end); turns so far
    var conversing by remember { mutableStateOf(false) }
    var pausedByMic by remember { mutableStateOf(false) }
    var turn by remember { mutableStateOf(0) }
    val scope = rememberCoroutineScope()
    // the limits (DjMicLimits): owner, 9 Nov: after an answer the music came back and the mic stayed on "listening", taking no press
    val gate = remember { MicGate() }
    fun clock() = android.os.SystemClock.elapsedRealtime()
    fun end(how: DjTurn) {
        gate.ended()
        if (!conversing) return
        conversing = false
        if (DjConversation.resumeAtEnd(pausedByMic, how) && !app.player.isPlaying) app.player.resume()
        pausedByMic = false
    }
    lateinit var listenRef: (Long) -> Unit

    fun heard(text: String) {
        // "thanks", "bye": in a conversation the DJ says "Anytime." (a fixed reply, made ahead in its voice) and the music comes back up. First thing in
        // the search screen's mic it is nothing to answer, so it just ends
        val goodbye = DjConversation.isGoodbye(text)
        if (goodbye && turn == 0 && onSearch != null) { state = MicState.IDLE; end(DjTurn.END); return }
        DjStatus.now.heard(text)
        state = MicState.THINKING; gate.thinking(clock())
        app.player.askDj(text, search = onSearch != null) { a ->
            state = MicState.IDLE
            if (a.doThis == DjAskDo.SEARCH) { end(DjTurn.END); onSearch?.invoke(text); return@askDj }
            // the answer is always shown; it is also said in the DJ's voice when it could be
            if (a.say.isNotBlank()) app.ui.say(a.say)
            val how = if (goodbye) DjTurn.END else DjConversation.after(a.mainDo, a.asked, ++turn, a.intent)
            gate.answered(clock(), how == DjTurn.LISTEN_AGAIN && conversing)
            if (how != DjTurn.LISTEN_AGAIN || !conversing) { end(how); return@askDj }
            // the next turn once the DJ has finished saying the answer (the mic would hear it otherwise); a press meanwhile ends it. It waits a short
            // while for a follow-up (longer after a question back) and ends quietly if nobody speaks
            scope.launch {
                for (i in 0 until 100) { if (!conversing || !app.player.djTalking) break; delay(250) }
                if (conversing) { delay(300); if (conversing) listenRef(DjConversation.listenWindowMs(a.asked)) }
            }
        }
    }

    /** One turn of listening. [waitMs]: how long to wait for the person to begin (0 on the first press, where the recogniser's own wait is used). */
    fun listen(waitMs: Long) {
        stop()
        if (waitMs > 0) timing.press()
        val r = MicSpeech.create(ctx) ?: run { app.ui.say("Speech recognition isn't available here"); return }
        rec.value = r
        r.setRecognitionListener(object : RecognitionListener {
            override fun onResults(results: Bundle?) {
                // a recogniser already let go (a newer turn, a limit, a press) says nothing more
                if (rec.value !== r) return
                val text = results?.getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION)?.firstOrNull()?.trim().orEmpty()
                stop()
                if (text.isEmpty()) { state = MicState.IDLE; end(DjTurn.END) } else heard(text)
            }
            // nothing said (no match, silence) ends the conversation quietly; anything else says so
            override fun onError(error: Int) {
                if (rec.value !== r) return
                // the on-device recogniser failed at once: the same turn again with the online one
                if (!MicSpeech.onDeviceBroken && MicSpeech.retryOnline(error)) { MicSpeech.useOnline(ctx); listen(waitMs); return }
                stop(); state = MicState.IDLE; end(DjTurn.END)
                if (error != SpeechRecognizer.ERROR_NO_MATCH && error != SpeechRecognizer.ERROR_SPEECH_TIMEOUT) app.ui.say("Sorry, I didn't catch that.")
            }
            override fun onReadyForSpeech(params: Bundle?) { timing.listening(); MicSpeech.beep() }
            override fun onBeginningOfSpeech() { giveUp.value?.let { handler.removeCallbacks(it) }; giveUp.value = null }
            override fun onRmsChanged(rmsdB: Float) { DjStatus.now.level(rmsdB) }
            override fun onBufferReceived(buffer: ByteArray?) {}
            override fun onEndOfSpeech() {}
            override fun onPartialResults(partialResults: Bundle?) {}
            override fun onEvent(eventType: Int, params: Bundle?) {}
        })
        state = MicState.LISTENING; gate.listening(clock(), app.player.isPlaying)
        r.startListening(MicSpeech.intent(ctx))
        timing.recogniserReady()
        // nobody began speaking in the follow-up window: let go quietly
        giveUp.value?.let { handler.removeCallbacks(it) }; giveUp.value = null
        if (waitMs > 0) Runnable { if (rec.value === r) { stop(); state = MicState.IDLE; end(DjTurn.END) } }.also { giveUp.value = it; handler.postDelayed(it, waitMs) }
    }
    listenRef = ::listen
    // while it listens or thinks: lets go when the music starts again in a follow-up turn, or a limit passes
    LaunchedEffect(state) {
        if (state == MicState.IDLE) return@LaunchedEffect
        while (true) {
            delay(500)
            if (gate.tick(clock(), app.player.isPlaying)) {
                val music = app.player.isPlaying
                stop(); state = MicState.IDLE; end(if (music) DjTurn.END_MUSIC_CHOSEN else DjTurn.END)
                break
            }
        }
    }

    // a conversation starts: the music pauses so the mic doesn't hear the song, then it listens
    fun begin() {
        gate.ended(); gate.press(clock())
        conversing = true; turn = 0
        pausedByMic = app.player.isPlaying
        if (pausedByMic) app.player.pause()
        listen(0L)
    }

    val ask = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { ok -> if (ok) begin() else app.ui.say("FLACie needs the microphone to hear you") }
    fun press() {
        if (state == MicState.IDLE && !conversing) timing.press()
        when (state) {
            MicState.LISTENING -> { stop(); state = MicState.IDLE; end(DjTurn.END) }
            MicState.THINKING -> {}
            // between turns (the DJ is answering): a press ends the conversation
            MicState.IDLE -> if (conversing) end(DjTurn.END) else when {
                ContextCompat.checkSelfPermission(ctx, Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED -> begin()
                // the first time, why it wants the microphone, before Android asks. A dialog in the middle of the screen, not the message bar: on the
                // unfolded Fold the bar's words and its Continue were not seen together (the owner saw only Continue and had to press twice)
                !app.prefs.micAsked -> explaining = true
                else -> ask.launch(Manifest.permission.RECORD_AUDIO)
            }
        }
    }

    if (explaining) MicWhyDialog(
        onContinue = { explaining = false; app.prefs.micAsked = true; ask.launch(Manifest.permission.RECORD_AUDIO) },
        onDismiss = { explaining = false },
    )

    val pulse = rememberInfiniteTransition(label = "mic").animateFloat(1f, 1.12f, infiniteRepeatable(tween(600), RepeatMode.Reverse), label = "mic-pulse")
    val on = state == MicState.LISTENING
    // lit (without the pulse) between turns of a conversation
    val lit2 = on || conversing
    Box(
        Modifier.size(size).scale(if (on) pulse.value else 1f).clip(CircleShape).background(if (lit2) lit.copy(alpha = .35f) else glass)
            .then(if (lit2) Modifier.border(2.dp, lit, CircleShape) else Modifier).focusRing(CircleShape)
            .semantics {
                contentDescription = when (state) { MicState.LISTENING -> "Listening. Press to stop"; MicState.THINKING -> "Thinking"; else -> if (conversing) "Talking with the DJ. Press to stop" else "Talk to the DJ" }
                role = Role.Button; stateDescription = state.name.lowercase()
            }
            .clickable(onClick = ::press),
        contentAlignment = Alignment.Center,
    ) { GlyphIcon(Glyph.MIC, Modifier.size(size * .5f), if (state == MicState.THINKING) tint.copy(alpha = .5f) else tint) }
}

/** Why FLACie wants the microphone, asked once before Android's own question: stays until Continue or Not now. */
@Composable
private fun MicWhyDialog(onContinue: () -> Unit, onDismiss: () -> Unit) {
    val sc = LocalScheme.current
    androidx.compose.ui.window.Dialog(onDismissRequest = onDismiss) {
        androidx.compose.foundation.layout.Column(
            Modifier.widthIn(max = 420.dp).clip(androidx.compose.foundation.shape.RoundedCornerShape(20.dp)).background(sc.top.mix(Color.Black, .4f)).padding(20.dp),
            verticalArrangement = androidx.compose.foundation.layout.Arrangement.spacedBy(14.dp),
        ) {
            Txt("Talk to the DJ", size = 20f, weight = androidx.compose.ui.text.font.FontWeight.Bold)
            Txt("FLACie listens only while the mic is lit, to hear what you ask. Your phone's speech recogniser turns it into words, and only the words go to FLACie. Some phones do that on their own servers.", size = 15f)
            androidx.compose.foundation.layout.Row(horizontalArrangement = androidx.compose.foundation.layout.Arrangement.spacedBy(10.dp), modifier = Modifier.align(Alignment.End)) {
                GlossPill("Not now", onDismiss)
                GlossPill("Continue", onContinue, primary = true)
            }
        }
    }
}

/**
 * The platform's recogniser, on the device when it has one (Android 12 and later), so the sound need not leave the phone. Making one takes a second or
 * two (it binds the speech service), so while the DJ screen or the car is up one is kept made ([arm]) and handed over by [create]; a new one is made
 * ready again after each turn ([recycle]). All on the main thread.
 */
object MicSpeech {
    private var warm: SpeechRecognizer? = null
    private var armed = 0

    /** The on-device recogniser failed at once this session: the online one is used from then on. */
    @Volatile var onDeviceBroken = false

    private fun build(ctx: Context): SpeechRecognizer? = try {
        if (!onDeviceBroken && Build.VERSION.SDK_INT >= Build.VERSION_CODES.S && SpeechRecognizer.isOnDeviceRecognitionAvailable(ctx)) SpeechRecognizer.createOnDeviceSpeechRecognizer(ctx)
        else SpeechRecognizer.createSpeechRecognizer(ctx)
    } catch (_: Exception) { null }

    /** Errors after which the turn is tried again with the online recogniser instead of giving up: the on-device one has no language pack, is busy or
     *  lost its service (11 server disconnected, 12/13 language not supported/unavailable, 14 can't check support). Not: nothing said, no
     *  microphone permission, no audio, no network (the online one needs it too). */
    fun retryOnline(error: Int): Boolean = error in setOf(SpeechRecognizer.ERROR_SERVER, SpeechRecognizer.ERROR_CLIENT, SpeechRecognizer.ERROR_RECOGNIZER_BUSY, 11, 12, 13, 14)

    /** The online recogniser from now on; the warm one (on-device) is let go. */
    fun useOnline(ctx: Context) {
        onDeviceBroken = true
        warm?.let { try { it.destroy() } catch (_: Exception) { } }; warm = null
        if (armed > 0) warm = build(ctx.applicationContext)
    }

    /** The DJ screen or the car session is up: keep a recogniser made. Each [arm] is matched by a [disarm]. */
    fun arm(ctx: Context) { armed++; if (warm == null) warm = build(ctx.applicationContext) }

    fun disarm() {
        if (armed > 0) armed--
        if (armed == 0) { warm?.let { try { it.destroy() } catch (_: Exception) { } }; warm = null }
    }

    /** A turn is over: make the next recogniser ready (when armed). */
    fun recycle(ctx: Context) { if (armed > 0 && warm == null) warm = build(ctx.applicationContext) }

    /** The pre-warmed recogniser if there is one, else a new one. */
    fun create(ctx: Context): SpeechRecognizer? = warm?.also { warm = null } ?: build(ctx)

    /** A short beep: the recogniser is really listening now. */
    fun beep() {
        try {
            val tone = android.media.ToneGenerator(android.media.AudioManager.STREAM_NOTIFICATION, 60)
            tone.startTone(android.media.ToneGenerator.TONE_PROP_BEEP, 80)
            android.os.Handler(android.os.Looper.getMainLooper()).postDelayed({ try { tone.release() } catch (_: Exception) { } }, 300)
        } catch (_: Exception) { }
    }

    fun intent(ctx: Context): Intent = Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH)
        .putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM)
        .putExtra(RecognizerIntent.EXTRA_MAX_RESULTS, 1)
        .putExtra(RecognizerIntent.EXTRA_PREFER_OFFLINE, true)
        .putExtra(RecognizerIntent.EXTRA_CALLING_PACKAGE, ctx.packageName)
        // about 1.2 s of quiet after the person stops talking is the end of their turn (the recogniser's own default is shorter and cuts people off mid-thought)
        .putExtra(RecognizerIntent.EXTRA_SPEECH_INPUT_COMPLETE_SILENCE_LENGTH_MILLIS, DjConversation.END_OF_SPEECH_MS.toLong())
        .putExtra(RecognizerIntent.EXTRA_SPEECH_INPUT_POSSIBLY_COMPLETE_SILENCE_LENGTH_MILLIS, DjConversation.END_OF_SPEECH_MS.toLong())
}
