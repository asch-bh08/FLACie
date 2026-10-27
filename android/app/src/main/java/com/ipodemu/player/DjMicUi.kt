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

/** What the mic button is doing: nothing, listening (from a press until the person stops talking), or waiting for the DJ's answer. */
enum class MicState { IDLE, LISTENING, THINKING }

/**
 * "Talk to the DJ" on the phone: a glass mic button. Nothing listens until it is pressed; the platform's recogniser (on the device when it can) turns the
 * speech into words, and only the words go to FLACie Web. The first press asks for the microphone, saying why first. With [onSearch] (the search screen)
 * words that are not a request fill the search instead. Not there without FLACie Web or a recogniser.
 */
@Composable
fun DjMicButton(size: Dp = 44.dp, tint: Color = Color.White, glass: Color = Color(0x47000000), lit: Color = Color(0xFFE8734A), onSearch: ((String) -> Unit)? = null) {
    val app = LocalApp.current
    val ctx = LocalContext.current
    if (!app.player.djAvailable || !SpeechRecognizer.isRecognitionAvailable(ctx)) return
    var state by remember { mutableStateOf(MicState.IDLE) }
    var explaining by remember { mutableStateOf(false) }
    val rec = remember { mutableStateOf<SpeechRecognizer?>(null) }
    fun stop() { rec.value?.let { try { it.stopListening(); it.destroy() } catch (_: Exception) { } }; rec.value = null }
    DisposableEffect(Unit) { onDispose { stop() } }

    // the conversation (DjConversation): on from a press until it ends; whether the mic paused the song (it carries on at the end); turns so far
    var conversing by remember { mutableStateOf(false) }
    var pausedByMic by remember { mutableStateOf(false) }
    var turn by remember { mutableStateOf(0) }
    val scope = rememberCoroutineScope()
    fun end(how: DjTurn) {
        if (!conversing) return
        conversing = false
        if (DjConversation.resumeAtEnd(pausedByMic, how) && !app.player.isPlaying) app.player.resume()
        pausedByMic = false
    }
    lateinit var listenRef: () -> Unit

    fun heard(text: String) {
        if (DjConversation.isGoodbye(text)) { state = MicState.IDLE; end(DjTurn.END); return }
        state = MicState.THINKING
        app.player.askDj(text, search = onSearch != null) { a ->
            state = MicState.IDLE
            if (a.doThis == DjAskDo.SEARCH) { end(DjTurn.END); onSearch?.invoke(text); return@askDj }
            // the answer is always shown; it is also said in the DJ's voice when it could be
            if (a.say.isNotBlank()) app.ui.say(a.say)
            val how = DjConversation.after(a.doThis, ++turn)
            if (how != DjTurn.LISTEN_AGAIN || !conversing) { end(how); return@askDj }
            // the next turn once the DJ has finished saying the answer (the mic would hear it otherwise); a press meanwhile ends it
            scope.launch {
                for (i in 0 until 100) { if (!conversing || !app.player.djTalking) break; delay(250) }
                if (conversing) { delay(300); if (conversing) listenRef() }
            }
        }
    }

    fun listen() {
        stop()
        val r = MicSpeech.create(ctx) ?: run { app.ui.say("Speech recognition isn't available here"); return }
        rec.value = r
        r.setRecognitionListener(object : RecognitionListener {
            override fun onResults(results: Bundle?) {
                val text = results?.getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION)?.firstOrNull()?.trim().orEmpty()
                stop()
                if (text.isEmpty()) { state = MicState.IDLE; end(DjTurn.END) } else heard(text)
            }
            // nothing said (no match, silence) ends the conversation quietly; anything else says so
            override fun onError(error: Int) {
                stop(); state = MicState.IDLE; end(DjTurn.END)
                if (error != SpeechRecognizer.ERROR_NO_MATCH && error != SpeechRecognizer.ERROR_SPEECH_TIMEOUT) app.ui.say("Sorry, I didn't catch that.")
            }
            override fun onReadyForSpeech(params: Bundle?) {}
            override fun onBeginningOfSpeech() {}
            override fun onRmsChanged(rmsdB: Float) {}
            override fun onBufferReceived(buffer: ByteArray?) {}
            override fun onEndOfSpeech() {}
            override fun onPartialResults(partialResults: Bundle?) {}
            override fun onEvent(eventType: Int, params: Bundle?) {}
        })
        state = MicState.LISTENING
        r.startListening(MicSpeech.intent(ctx))
    }
    listenRef = ::listen

    // a conversation starts: the music pauses so the mic doesn't hear the song, then it listens
    fun begin() {
        conversing = true; turn = 0
        pausedByMic = app.player.isPlaying
        if (pausedByMic) app.player.pause()
        listen()
    }

    val ask = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { ok -> if (ok) begin() else app.ui.say("FLACie needs the microphone to hear you") }
    fun press() {
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
            Txt("FLACie listens only while the mic is lit, to hear what you ask. Only the words go to FLACie, never the sound.", size = 15f)
            androidx.compose.foundation.layout.Row(horizontalArrangement = androidx.compose.foundation.layout.Arrangement.spacedBy(10.dp), modifier = Modifier.align(Alignment.End)) {
                GlossPill("Not now", onDismiss)
                GlossPill("Continue", onContinue, primary = true)
            }
        }
    }
}

/** The platform's recogniser, on the device when it has one (Android 12 and later), so the sound need not leave the phone. */
object MicSpeech {
    fun create(ctx: Context): SpeechRecognizer? = try {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S && SpeechRecognizer.isOnDeviceRecognitionAvailable(ctx)) SpeechRecognizer.createOnDeviceSpeechRecognizer(ctx)
        else SpeechRecognizer.createSpeechRecognizer(ctx)
    } catch (_: Exception) { null }

    fun intent(ctx: Context): Intent = Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH)
        .putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM)
        .putExtra(RecognizerIntent.EXTRA_MAX_RESULTS, 1)
        .putExtra(RecognizerIntent.EXTRA_PREFER_OFFLINE, true)
        .putExtra(RecognizerIntent.EXTRA_CALLING_PACKAGE, ctx.packageName)
}
