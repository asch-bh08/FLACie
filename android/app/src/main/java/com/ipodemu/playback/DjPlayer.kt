package com.ipodemu.playback

import android.content.Context
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.speech.tts.TextToSpeech
import android.speech.tts.UtteranceProgressListener
import androidx.annotation.OptIn
import androidx.media3.common.AudioAttributes
import androidx.media3.common.C
import androidx.media3.common.MediaItem
import androidx.media3.common.PlaybackException
import androidx.media3.common.Player
import androidx.media3.common.util.UnstableApi
import androidx.media3.datasource.DataSource
import androidx.media3.exoplayer.ExoPlayer
import androidx.media3.exoplayer.source.DefaultMediaSourceFactory
import com.ipodemu.Prefs
import java.util.Locale

/**
 * The DJ's voice: a second small ExoPlayer that plays the server's clip over the start of a song, or the phone's own voice ([TextToSpeech]) when the
 * person chose "Device voice" (the server then sends no clip; it never mixes the two). It never takes audio focus (taking it would make the music
 * player pause), it is never in the queue (so play counting, history, the media session and the car's screen never see it), and the music is ducked
 * through [onLevel] while it is heard. The clip of the next song's cue is loaded as soon as the cue arrives ([ready]), so it starts the moment the song
 * does ([say]). The times a line may take are [DjLine]'s; a watchdog holds it to them, so the music is never left ducked. Main thread only.
 * A clip is timed to the song's intro when the server knows it ([DjTiming]): started later so it ends just before the singing, or, when it is longer than
 * the intro, the song is held back under it (silent through [onLevel], then from its start through [onRestart]) for at most 1.5 s.
 */
@OptIn(UnstableApi::class)
class DjPlayer(private val ctx: Context, private val source: DataSource.Factory, private val prefs: Prefs, private val onLevel: (Float) -> Unit,
               private val onRestart: () -> Unit = {}, private val settings: () -> DjSettings = { DjSettings() },
               /** The DJ's words to show now (Captions), or null: Now Playing and the mini bar draw them. */
               private val onCaption: (String?) -> Unit = {}) {
    private val handler = Handler(Looper.getMainLooper())
    private var exo: ExoPlayer? = null
    /** The clip loaded in [exo], ready to play; [wanted] = the clip of the cue for the next song (loaded once nothing is being said). */
    private var loaded: String? = null
    private var wanted: String? = null
    /** The cue for the next song may be said with the phone's voice ([DjCue.backup]). */
    private var wantedBackup = false
    /** The last clip that failed to load (the server could not make it): its line goes to the phone's voice when the cue allows it. */
    private var failed: String? = null
    /** The line started and not yet ended or stopped. */
    private var line: DjLine? = null
    val speaking: Boolean get() = line != null
    private val ramp = DuckRamp()
    /** The song is held back under a line longer than its intro: silent until [letGo]. */
    private var holding = false
    private val tick = object : Runnable {
        override fun run() {
            val now = SystemClock.uptimeMillis()
            onLevel(if (holding) 0f else ramp.level(now))
            if (!holding && !ramp.settled(now)) handler.postDelayed(this, TICK_MS)
        }
    }
    /** The hold is over: the song starts again from its beginning, quietly under the end of the line. */
    private val letGo = Runnable { if (holding) { holding = false; onRestart(); kick() } }
    /** The clip starts (at once, or later in the song): after the cue sound when that is on. */
    private val startClip = Runnable { if (line != null) { if (cueOn && swoosh()) handler.postDelayed(playClip, DjCaptions.CUE_LEAD_MS.toLong()) else playClip.run() } }
    private val playClip = Runnable { if (line != null) player().playWhenReady = true }
    /** The person's "Cue sound" and "Captions", read for every line. */
    private var cueOn = true
    private var captionsOn = true
    /** The phrases still to show for the line being said. */
    private val captionSteps = ArrayList<Runnable>()
    /** A line not sounding in time (a slow clip, a voice that never starts) or never ending (a clip that stalls, a voice that never says it is done). */
    private val watchdog = Runnable { val l = line; if (l != null) { if (SystemClock.uptimeMillis() >= l.stopAt) stop() else watch() } }
    private fun watch() { handler.removeCallbacks(watchdog); line?.let { handler.postAtTime(watchdog, it.stopAt) } }

    private val listener = object : Player.Listener {
        override fun onIsPlayingChanged(isPlaying: Boolean) { val l = line; if (isPlaying && l != null && !l.phoneVoice && !l.heard) heard(l) }
        override fun onPlaybackStateChanged(state: Int) { if (state == Player.STATE_ENDED && line?.phoneVoice == false) stop() }
        override fun onPlayerError(error: PlaybackException) {
            android.util.Log.w("FLACie", "DJ clip failed: ${error.errorCodeName}")
            failed = loaded; loaded = null
            val l = line
            when {
                // loading ahead failed: the phone's voice is woken up now (the engine takes a moment), to say the line when its song comes
                l == null -> if (wantedBackup && failed != null && failed == wanted) voice()
                l.phoneVoice -> {}
                // the clip failed before it was heard: the phone's voice says it instead, while the song is still young
                l.clipFailed(SystemClock.uptimeMillis()) -> { quietClip(); speak(l); watch() }
                else -> stop()
            }
        }
    }

    private fun player(): ExoPlayer = exo ?: ExoPlayer.Builder(ctx)
        // the same sources as the music: a FLACie Web address gets the account's token as a header (never in the URL)
        .setMediaSourceFactory(DefaultMediaSourceFactory(source))
        // false = no audio focus at all: requesting it would make the music player, which holds it, pause
        .setAudioAttributes(AudioAttributes.Builder().setUsage(C.USAGE_MEDIA).setContentType(C.AUDIO_CONTENT_TYPE_SPEECH).build(), false)
        .build().also { it.addListener(listener); it.volume = voiceLevel; exo = it; voiceFx(it.audioSessionId) }

    // ---- a light voice chain so the DJ sits on top of the mix: an equaliser (rumble cut, a little presence near 3 kHz) and a 12 dB loudness lift on the
    // clip player's own audio session (the music is untouched). Phones without these effects just play the clip as it is. ----
    private var eq: android.media.audiofx.Equalizer? = null
    private var lift: android.media.audiofx.LoudnessEnhancer? = null
    private fun voiceFx(session: Int) {
        if (session == C.AUDIO_SESSION_ID_UNSET || session == 0) return
        try {
            eq = android.media.audiofx.Equalizer(0, session).also { e ->
                val bands = e.numberOfBands.toInt()
                val range = e.bandLevelRange
                val levels = DjCaptions.voiceEq(IntArray(bands) { e.getCenterFreq(it.toShort()) }, range[0], range[1])
                for (b in 0 until bands) e.setBandLevel(b.toShort(), levels[b])
                e.enabled = true
            }
        } catch (_: Exception) { eq = null }
        try { lift = android.media.audiofx.LoudnessEnhancer(session).also { it.setTargetGain(LIFT_MB); it.enabled = true } } catch (_: Exception) { lift = null }
    }

    /** The cue sound (made here, [DjCaptions.swoosh]) on a short static track; false when it can't play, so the voice goes at once. */
    private fun swoosh(): Boolean = try {
        val pcm = DjCaptions.swoosh(SWOOSH_RATE)
        val track = android.media.AudioTrack.Builder()
            .setAudioAttributes(android.media.AudioAttributes.Builder().setUsage(android.media.AudioAttributes.USAGE_MEDIA).setContentType(android.media.AudioAttributes.CONTENT_TYPE_SONIFICATION).build())
            .setAudioFormat(android.media.AudioFormat.Builder().setSampleRate(SWOOSH_RATE).setEncoding(android.media.AudioFormat.ENCODING_PCM_16BIT).setChannelMask(android.media.AudioFormat.CHANNEL_OUT_MONO).build())
            .setTransferMode(android.media.AudioTrack.MODE_STATIC).setBufferSizeInBytes(pcm.size * 2).build()
        track.write(pcm, 0, pcm.size)
        track.setVolume(voiceLevel)
        track.play()
        handler.postDelayed({ try { track.release() } catch (_: Exception) {} }, 800)
        true
    } catch (_: Exception) { false }

    /** Shows the line's words in time with it (from when it is heard), phrase by phrase. */
    private fun captions(l: DjLine) {
        clearCaptions(false)
        if (!captionsOn) return
        val words = l.cue.caption.ifBlank { l.cue.text }
        val len = exo?.duration?.takeIf { !l.phoneVoice && it != C.TIME_UNSET && it > 0 }?.toInt() ?: DjTiming.estimateMs(l.cue.text)
        val plan = DjCaptions.plan(words, len)
        for (p in plan) {
            val show = Runnable { if (line === l) onCaption(p.text) }
            captionSteps.add(show); handler.postDelayed(show, p.atMs.toLong())
        }
        plan.lastOrNull()?.let { last -> val hide = Runnable { onCaption(null) }; captionSteps.add(hide); handler.postDelayed(hide, last.untilMs.toLong()) }
    }

    private fun clearCaptions(hide: Boolean) {
        for (r in captionSteps) handler.removeCallbacks(r)
        captionSteps.clear()
        if (hide) onCaption(null)
    }

    /** The server gives clips as "/api/dj/clip/<id>.mp3", relative to FLACie Web. */
    private fun url(clip: String): String = if (clip.startsWith("http://") || clip.startsWith("https://")) clip else prefs.flacieWebUrl.trimEnd('/') + "/" + clip.trimStart('/')

    /** The cue for the next song arrived: its clip is loaded now (or the phone's voice woken up for a [backup] line). Null clip = no clip to hold. */
    fun ready(clip: String?, backup: Boolean) {
        wanted = clip?.let(::url); wantedBackup = backup
        if (backup && (clip == null || wanted == failed)) voice()
        if (!speaking) load(wanted)
    }

    private fun load(u: String?) {
        if (u == loaded) return
        loaded = u
        // a clip that failed is tried again; it counts as failed again only if it fails again
        if (u != null && u == failed) failed = null
        val p = if (u == null) exo ?: return else player()
        p.playWhenReady = false
        if (u == null) { p.stop(); p.clearMediaItems() } else { p.setMediaItem(MediaItem.fromUri(u)); p.prepare() }
    }

    /** The clip player goes quiet and lets its clip go. */
    private fun quietClip() { exo?.let { it.playWhenReady = false; it.stop(); it.clearMediaItems() }; loaded = null }

    /** The song the cue was for just started by itself ([songAtMs] into it): says the line over its start, timed to its intro ([DjTiming]) when the cue
     *  knows it. Quiet when the clip is not there in time or no voice is ready. The phone's own voice is never timed (how long it takes is not known). */
    fun say(cue: DjCue, songAtMs: Long = 0L) {
        if (speaking) stop()
        // the person's "DJ volume" and "Music under the DJ", read for every line
        val st = settings()
        voiceLevel = st.voiceLevel; ramp.low = st.duckLevel; cueOn = st.cue; captionsOn = st.captions
        exo?.volume = voiceLevel
        val clip = cue.clip?.let(::url)
        wanted = null; wantedBackup = false
        val now = SystemClock.uptimeMillis()
        val failedAhead = clip != null && clip == failed
        // the clip's own length once loaded ahead, else a guess from its words
        val known = exo?.duration?.takeIf { clip != null && clip == loaded && it != C.TIME_UNSET && it > 0 }?.toInt()
        val plan = if (clip == null || failedAhead || cue.introMs == null) DjTimingPlan.NOW else DjTiming.plan(cue.introMs, known ?: DjTiming.estimateMs(cue.text))
        val wait = (plan.clipAtMs - songAtMs).coerceAtLeast(0L)
        // a clip due later counts its times from when it is due
        val l = DjLine.start(cue, if (plan.clipAtMs > 0) now + wait else now, failedAhead) ?: return
        line = l
        if (l.phoneVoice) speak(l)
        else {
            load(clip)
            if (plan.clipAtMs > 0) handler.postDelayed(startClip, wait) else startClip.run()
            if (plan.songDelayMs > 0) { holding = true; kick(); handler.postDelayed(letGo, plan.songDelayMs.toLong()) }
        }
        watch()
    }

    /** Pause, a seek, a skip, the sleep timer, the app swiped away: the line stops at once and the music comes back up. The next song's cue is kept. */
    fun stop() {
        handler.removeCallbacks(watchdog); handler.removeCallbacks(startClip); handler.removeCallbacks(playClip); handler.removeCallbacks(letGo)
        clearCaptions(true)
        // a song held back goes on from where it is (a skip, a pause or a seek: it is not started again)
        if (holding) { holding = false; kick() }
        val was = line
        if (was != null) {
            line = null; waitingForVoice = false
            quietClip()
            try { tts?.stop() } catch (_: Exception) {}
        }
        // never left ducked, whatever ended the line
        if (was != null || ramp.ducking) unduck()
        load(wanted)
    }

    /** A new queue, Clear, or the DJ switched off: nothing is said and nothing is held. */
    fun clear() { wanted = null; wantedBackup = false; stop(); load(null) }

    /** The service ends: everything is let go (made again the next time the DJ has something to say). */
    fun release() {
        clear()
        try { eq?.release() } catch (_: Exception) {}
        try { lift?.release() } catch (_: Exception) {}
        eq = null; lift = null
        exo?.release(); exo = null; loaded = null
        try { tts?.shutdown() } catch (_: Exception) {}
        tts = null; ttsReady = false
        handler.removeCallbacks(tick); ramp.reset(); onLevel(1f)
    }

    /** The line is sounding: the music ducks, and the line now has until its end to finish. */
    private fun heard(l: DjLine) { l.heard(SystemClock.uptimeMillis()); duck(); watch(); captions(l) }

    private fun duck() { ramp.duck(SystemClock.uptimeMillis()); kick() }
    private fun unduck() { ramp.release(SystemClock.uptimeMillis()); kick() }
    private fun kick() { handler.removeCallbacks(tick); handler.post(tick) }

    // ---- the phone's own voice (Device voice, when the person chose it) ---------------------------------------------------------------------------------------
    private var tts: TextToSpeech? = null
    /** The clip's and the phone voice's volume (0..1): the person's "DJ volume". */
    private var voiceLevel = DjSettings.DEFAULT_VOLUME / 100f
    private var ttsReady = false
    private var utter = 0
    /** A line waits for the engine to start (it was not woken up in time): said as soon as it is ready, if still in time. */
    private var waitingForVoice = false

    /** Started when a backup cue arrives (the engine takes a moment), so it is ready by the time the song changes. */
    private fun voice() {
        if (tts != null) return
        tts = TextToSpeech(ctx.applicationContext) { status -> handler.post { setUp(status) } }
    }

    /** Says [l] with the phone's voice, now or once the engine is ready. */
    private fun speak(l: DjLine) {
        utter++
        voice()
        if (ttsReady) speakNow(l) else waitingForVoice = true
    }

    private fun speakNow(l: DjLine) {
        val t = tts ?: return
        val params = android.os.Bundle().apply { putFloat(TextToSpeech.Engine.KEY_PARAM_VOLUME, voiceLevel) }
        if (t.speak(l.cue.text, TextToSpeech.QUEUE_FLUSH, params, "dj$utter") != TextToSpeech.SUCCESS) stop()
    }

    private fun setUp(status: Int) {
        val t = tts ?: return
        if (status != TextToSpeech.SUCCESS) { ttsReady = false; if (waitingForVoice) stop(); return }
        try {
            // a British voice when the phone has one (the DJ is meant to sound like UK radio), else the phone's own language
            if (t.isLanguageAvailable(Locale.UK) >= TextToSpeech.LANG_AVAILABLE) t.setLanguage(Locale.UK)
            t.setAudioAttributes(android.media.AudioAttributes.Builder().setUsage(android.media.AudioAttributes.USAGE_MEDIA)
                .setContentType(android.media.AudioAttributes.CONTENT_TYPE_SPEECH).build())
            t.setOnUtteranceProgressListener(object : UtteranceProgressListener() {
                override fun onStart(id: String?) { handler.post { val l = line; if (l != null && l.phoneVoice && !l.heard && id == "dj$utter") heard(l) } }
                override fun onDone(id: String?) { handler.post { if (line?.phoneVoice == true && id == "dj$utter") stop() } }
                @Deprecated("Deprecated in Java")
                override fun onError(id: String?) { handler.post { if (line?.phoneVoice == true && id == "dj$utter") stop() } }
                override fun onError(id: String?, errorCode: Int) { handler.post { if (line?.phoneVoice == true && id == "dj$utter") stop() } }
            })
            ttsReady = true
        } catch (_: Exception) { ttsReady = false }
        if (waitingForVoice) {
            waitingForVoice = false
            val l = line
            if (ttsReady && l != null && l.phoneVoice && !l.heard) speakNow(l) else if (l != null) stop()
        }
    }

    companion object {
        private const val TICK_MS = 30L
        private const val SWOOSH_RATE = 44_100
        /** The clip's loudness lift, millibels: 12 dB. Aura's clips come out near -27 LUFS and mastered pop near -8, so even over music turned down to
         *  15 % the voice was lost (owner, 27 Oct); the enhancer limits the lifted peaks itself. The web lifts the same (flacie.js DJ_LIFT_DB). */
        private const val LIFT_MB = 1200
    }
}
