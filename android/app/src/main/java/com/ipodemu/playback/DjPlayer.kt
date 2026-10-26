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
 * server could not make one and Backup voice is on. It never takes audio focus (taking it would make the music player pause), it is never in the queue
 * (so play counting, history, the media session and the car's screen never see it), and the music is ducked through [onLevel] while it is heard.
 * The clip of the next song's cue is loaded as soon as the cue arrives ([ready]), so it starts the moment the song does ([say]). Main thread only.
 */
@OptIn(UnstableApi::class)
class DjPlayer(private val ctx: Context, private val source: DataSource.Factory, private val prefs: Prefs, private val onLevel: (Float) -> Unit) {
    private val handler = Handler(Looper.getMainLooper())
    private var exo: ExoPlayer? = null
    /** The clip loaded in [exo], ready to play; [wanted] = the clip of the cue for the next song (loaded once nothing is being said). */
    private var loaded: String? = null
    private var wanted: String? = null
    /** A line was started and has not ended or been stopped; [heard] = it is really sounding (the music is ducked from then). */
    var speaking = false; private set
    private var heard = false
    private val ramp = DuckRamp()
    private val tick = object : Runnable {
        override fun run() {
            val now = SystemClock.uptimeMillis()
            onLevel(ramp.level(now))
            if (!ramp.settled(now)) handler.postDelayed(this, TICK_MS)
        }
    }
    /** A clip still loading (slow server) or a voice that never starts: the DJ stays quiet rather than talk over the middle of the song. */
    private val giveUp = Runnable { if (speaking && !heard) stop() }

    private val listener = object : Player.Listener {
        override fun onIsPlayingChanged(isPlaying: Boolean) { if (isPlaying && speaking && !heard) { heard = true; duck() } }
        override fun onPlaybackStateChanged(state: Int) { if (state == Player.STATE_ENDED && speaking) stop() }
        override fun onPlayerError(error: PlaybackException) {
            android.util.Log.w("FLACie", "DJ clip failed: ${error.errorCodeName}")
            loaded = null
            if (speaking) stop()
        }
    }

    private fun player(): ExoPlayer = exo ?: ExoPlayer.Builder(ctx)
        // the same sources as the music: a FLACie Web address gets the account's token as a header (never in the URL)
        .setMediaSourceFactory(DefaultMediaSourceFactory(source))
        // false = no audio focus at all: requesting it would make the music player, which holds it, pause
        .setAudioAttributes(AudioAttributes.Builder().setUsage(C.USAGE_MEDIA).setContentType(C.AUDIO_CONTENT_TYPE_SPEECH).build(), false)
        .build().also { it.addListener(listener); exo = it }

    /** The server gives clips as "/api/dj/clip/<id>.mp3", relative to FLACie Web. */
    private fun url(clip: String): String = if (clip.startsWith("http://") || clip.startsWith("https://")) clip else prefs.flacieWebUrl.trimEnd('/') + "/" + clip.trimStart('/')

    /** The cue for the next song arrived: its clip is loaded now (or the phone's voice woken up for a [backup] line). Null clip = no clip to hold. */
    fun ready(clip: String?, backup: Boolean) {
        wanted = clip?.let(::url)
        if (clip == null && backup) voice()
        if (!speaking) load(wanted)
    }

    private fun load(u: String?) {
        if (u == loaded) return
        loaded = u
        val p = if (u == null) exo ?: return else player()
        p.playWhenReady = false
        if (u == null) { p.stop(); p.clearMediaItems() } else { p.setMediaItem(MediaItem.fromUri(u)); p.prepare() }
    }

    /** The song the cue was for just started by itself: says the line over its start. Quiet when the clip is not there in time or no voice is ready. */
    fun say(cue: DjCue) {
        if (speaking) stop()
        val clip = cue.clip?.let(::url)
        wanted = null
        if (clip != null) {
            load(clip)
            speaking = true; heard = false
            player().playWhenReady = true
        } else if (cue.backup && cue.text.isNotBlank()) {
            val t = tts?.takeIf { ttsReady } ?: return
            speaking = true; heard = false
            utter++
            if (t.speak(cue.text, TextToSpeech.QUEUE_FLUSH, null, "dj$utter") != TextToSpeech.SUCCESS) { speaking = false; return }
        } else return
        handler.postDelayed(giveUp, START_WITHIN_MS)
    }

    /** Pause, a seek, a skip, the sleep timer, the app swiped away: the line stops at once and the music comes back up. The next song's cue is kept. */
    fun stop() {
        handler.removeCallbacks(giveUp)
        if (speaking) {
            speaking = false; heard = false
            exo?.let { it.playWhenReady = false; it.stop(); it.clearMediaItems() }; loaded = null
            try { tts?.stop() } catch (_: Exception) {}
            unduck()
        }
        load(wanted)
    }

    /** A new queue, Clear, or the DJ switched off: nothing is said and nothing is held. */
    fun clear() { wanted = null; stop(); load(null) }

    /** The service ends: everything is let go (made again the next time the DJ has something to say). */
    fun release() {
        clear()
        exo?.release(); exo = null; loaded = null
        try { tts?.shutdown() } catch (_: Exception) {}
        tts = null; ttsReady = false
        handler.removeCallbacks(tick); ramp.reset(); onLevel(1f)
    }

    private fun duck() { ramp.duck(SystemClock.uptimeMillis()); kick() }
    private fun unduck() { ramp.release(SystemClock.uptimeMillis()); kick() }
    private fun kick() { handler.removeCallbacks(tick); handler.post(tick) }

    // ---- the phone's own voice (Backup voice) ---------------------------------------------------------------------------------------
    private var tts: TextToSpeech? = null
    private var ttsReady = false
    private var utter = 0

    /** Started when a backup cue arrives (the engine takes a moment), so it is ready by the time the song changes. */
    private fun voice() {
        if (tts != null) return
        tts = TextToSpeech(ctx.applicationContext) { status -> handler.post { setUp(status) } }
    }

    private fun setUp(status: Int) {
        val t = tts ?: return
        if (status != TextToSpeech.SUCCESS) { ttsReady = false; return }
        try {
            // a British voice when the phone has one (the DJ is meant to sound like UK radio), else the phone's own language
            if (t.isLanguageAvailable(Locale.UK) >= TextToSpeech.LANG_AVAILABLE) t.language = Locale.UK
            t.setAudioAttributes(android.media.AudioAttributes.Builder().setUsage(android.media.AudioAttributes.USAGE_MEDIA)
                .setContentType(android.media.AudioAttributes.CONTENT_TYPE_SPEECH).build())
            t.setOnUtteranceProgressListener(object : UtteranceProgressListener() {
                override fun onStart(id: String?) { handler.post { if (speaking && !heard && id == "dj$utter") { heard = true; duck() } } }
                override fun onDone(id: String?) { handler.post { if (speaking && id == "dj$utter") stop() } }
                @Deprecated("Deprecated in Java")
                override fun onError(id: String?) { handler.post { if (speaking && id == "dj$utter") stop() } }
                override fun onError(id: String?, errorCode: Int) { handler.post { if (speaking && id == "dj$utter") stop() } }
            })
            ttsReady = true
        } catch (_: Exception) { ttsReady = false }
    }

    companion object {
        /** The line must be sounding this soon after the song starts, or it is dropped. */
        const val START_WITHIN_MS = 1_500L
        private const val TICK_MS = 30L
    }
}
