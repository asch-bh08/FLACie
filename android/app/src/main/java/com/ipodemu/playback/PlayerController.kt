package com.ipodemu.playback

import android.content.Context
import android.content.Intent
import android.media.audiofx.Equalizer
import android.media.MediaMetadataRetriever
import android.net.Uri
import android.provider.OpenableColumns
import android.os.Handler
import android.os.Looper
import androidx.media3.common.AudioAttributes
import androidx.media3.common.C
import androidx.media3.common.MediaItem
import androidx.media3.common.MediaMetadata
import androidx.media3.common.Player
import androidx.media3.common.util.UnstableApi
import androidx.media3.datasource.DataSource
import androidx.media3.datasource.DataSpec
import androidx.media3.datasource.DefaultDataSource
import androidx.media3.datasource.DefaultHttpDataSource
import androidx.media3.datasource.TransferListener
import androidx.media3.datasource.cache.CacheDataSource
import androidx.media3.exoplayer.ExoPlayer
import androidx.media3.exoplayer.source.DefaultMediaSourceFactory
import com.ipodemu.App
import com.ipodemu.Prefs
import com.ipodemu.library.AudioFacts
import com.ipodemu.library.NasSmb
import com.ipodemu.library.Track
import java.io.File
import kotlin.math.abs
import kotlin.math.ln

/** Handles the two kinds of playback DefaultDataSource can't do on its own: an smb:// NAS file
 * (opened via jcifs-ng, no HTTP involved) or an http(s) request to the configured Jellyfin/Plex
 * server, which needs its auth token attached as a real header -- never in the URL itself. A plain
 * HTML &lt;audio src&gt; has no way to set a header, which is why ipodsync's own web player puts
 * keys in the query string instead; ExoPlayer has no such limitation, so there's no reason to
 * accept that exposure here. DefaultDataSource (see below) only ever hands this factory http(s)
 * and smb URIs -- file:/content:/asset: local tracks are handled by Android's own readers first. */
@OptIn(UnstableApi::class)
private class MultiServerDataSource(private val prefs: Prefs, private val priority: com.ipodemu.library.Priority) : DataSource {
    // generous timeouts: a seek over Tailscale can take a few seconds to answer, and the default gave up mid-seek
    private val http = DefaultHttpDataSource.Factory().setConnectTimeoutMs(15_000).setReadTimeoutMs(20_000)
        .setAllowCrossProtocolRedirects(true).createDataSource()
    private var smbStream: jcifs.smb.SmbRandomAccessFile? = null
    private var smbUri: Uri? = null
    private var usingSmb = false

    override fun open(dataSpec: DataSpec): Long {
        usingSmb = dataSpec.uri.scheme == "smb"
        if (usingSmb) {
            val nasCtx = NasSmb.context(prefs.nasUsername, prefs.nasPassword, prefs.nasDomain)
            val file = jcifs.smb.SmbFile(dataSpec.uri.toString(), nasCtx)
            // random access: a seek (FLAC seeking reopens several times) jumps straight to the byte instead of reading
            // and discarding everything before it over the network, which stalled and then broke playback
            val stream = jcifs.smb.SmbRandomAccessFile(file, "r")
            if (dataSpec.position > 0) stream.seek(dataSpec.position)
            smbStream = stream
            smbUri = dataSpec.uri
            val remaining = file.length() - dataSpec.position
            return if (dataSpec.length != C.LENGTH_UNSET.toLong()) dataSpec.length else remaining
        }
        val uriStr = dataSpec.uri.toString()
        val jfUrl = prefs.jellyfinUrl.trimEnd('/')
        val plexUrl = prefs.plexUrl.trimEnd('/')
        val moverUrl = prefs.fileMoverUrl.trimEnd('/')
        http.clearAllRequestProperties()
        // every open of a Jellyfin stream takes a slot at the one gate for as long as the connection is being made (not for the whole song), so a skip burst
        // or a prefetch can never flood the server; the playing song goes first, a prefetch waits behind everything and while the server is slow
        val acct = prefs.accountServer.trimEnd('/')
        val jellyfin = (jfUrl.isNotBlank() && uriStr.startsWith(jfUrl)) || (acct.isNotBlank() && uriStr.startsWith(acct))
        when {
            jfUrl.isNotBlank() && uriStr.startsWith(jfUrl) -> http.setRequestProperty("Authorization", com.ipodemu.library.JellyfinAuth.header(prefs.jellyfinApiKey))
            plexUrl.isNotBlank() && uriStr.startsWith(plexUrl) -> http.setRequestProperty("X-Plex-Token", prefs.plexToken)
            moverUrl.isNotBlank() && uriStr.startsWith(moverUrl) -> http.setRequestProperty("X-Api-Key", prefs.fileMoverApiKey)
            prefs.flacieWebUrl.isNotBlank() && uriStr.startsWith(prefs.flacieWebUrl.trimEnd('/') + "/api/") -> {
                http.setRequestProperty("X-Emby-Token", prefs.accountToken); http.setRequestProperty("X-Jellyfin-Server", prefs.accountServer.trimEnd('/'))
            }
        }
        if (!jellyfin) return http.open(dataSpec)
        return com.ipodemu.library.JellyfinNet.gate.withPermitBlocking(priority, "stream", { e ->
            (e as? androidx.media3.datasource.HttpDataSource.InvalidResponseCodeException)?.responseCode ?: 0
        }) { http.open(dataSpec) }
    }

    override fun read(buffer: ByteArray, offset: Int, length: Int): Int =
        if (usingSmb) (smbStream?.read(buffer, offset, length) ?: -1) else http.read(buffer, offset, length)

    override fun addTransferListener(transferListener: TransferListener) { if (!usingSmb) http.addTransferListener(transferListener) }
    override fun getUri() = if (usingSmb) smbUri else http.uri
    override fun getResponseHeaders(): Map<String, List<String>> = if (usingSmb) emptyMap() else http.responseHeaders
    override fun close() {
        if (usingSmb) { try { smbStream?.close() } catch (_: Exception) {}; smbStream = null } else http.close()
    }
}

@OptIn(UnstableApi::class)
private class MultiServerDataSourceFactory(private val prefs: Prefs, private val priority: com.ipodemu.library.Priority = com.ipodemu.library.Priority.PLAYING) : DataSource.Factory {
    override fun createDataSource(): DataSource = MultiServerDataSource(prefs, priority)
}

class PlayerController(private val ctx: Context, private val prefs: Prefs) {
    // DefaultDataSource routes file:/content:/asset: URIs to Android's own local-file readers and only
    // hands http(s)/smb requests to our factory below -- otherwise every local track tries to open through
    // an HTTP-only data source and fails silently. Streamed songs also go through the on-disk cache (StreamSupport).
    @OptIn(UnstableApi::class)
    private val upstream = DefaultDataSource.Factory(ctx, MultiServerDataSourceFactory(prefs))
    @OptIn(UnstableApi::class)
    // the cache is written in 512 KB pieces (not the default 5 MB), so the seek bar's frame-size shape (WaveAnalyzer) can follow the download closely
    private val cacheSource = CacheDataSource.Factory().setCache(StreamSupport.cache(ctx)).setUpstreamDataSourceFactory(upstream)
        .setCacheWriteDataSinkFactory(androidx.media3.datasource.cache.CacheDataSink.Factory().setCache(StreamSupport.cache(ctx)).setFragmentSize(512L * 1024))
        .setFlags(CacheDataSource.FLAG_IGNORE_CACHE_ON_ERROR)

    /** The prefetcher's own source: the same cache, but its Jellyfin opens wait at Background priority (behind the playing song and the visible rows). */
    @OptIn(UnstableApi::class)
    private val prefetchSource = CacheDataSource.Factory().setCache(StreamSupport.cache(ctx))
        .setCacheWriteDataSinkFactory(androidx.media3.datasource.cache.CacheDataSink.Factory().setCache(StreamSupport.cache(ctx)).setFragmentSize(512L * 1024))
        .setUpstreamDataSourceFactory(DefaultDataSource.Factory(ctx, MultiServerDataSourceFactory(prefs, com.ipodemu.library.Priority.BACKGROUND)))
        .setFlags(CacheDataSource.FLAG_IGNORE_CACHE_ON_ERROR)

    @OptIn(UnstableApi::class)
    val live = LiveAudio()
    @OptIn(UnstableApi::class)
    val exo: ExoPlayer = ExoPlayer.Builder(ctx)
        // the sound passes through a tap that only reads it, for the live graphs on the Info tab
        .setRenderersFactory(object : androidx.media3.exoplayer.DefaultRenderersFactory(ctx) {
            override fun buildAudioSink(context: Context, enableFloatOutput: Boolean, enableAudioTrackPlaybackParams: Boolean): androidx.media3.exoplayer.audio.AudioSink =
                androidx.media3.exoplayer.audio.DefaultAudioSink.Builder(context).setEnableFloatOutput(enableFloatOutput).setEnableAudioTrackPlaybackParams(enableAudioTrackPlaybackParams)
                    .setAudioProcessorChain(androidx.media3.exoplayer.audio.DefaultAudioSink.DefaultAudioProcessorChain(androidx.media3.exoplayer.audio.TeeAudioProcessor(live))).build()
        })
        .setMediaSourceFactory(
            DefaultMediaSourceFactory(DataSource.Factory { RoutingDataSource(cacheSource.createDataSource(), upstream.createDataSource()) })
                // a failed piece is tried six times with growing waits before the player gives up, instead of twice
                .setLoadErrorHandlingPolicy(androidx.media3.exoplayer.upstream.DefaultLoadErrorHandlingPolicy(6)),
        )
        .setLoadControl(StreamSupport.loadControl())
        .setAudioAttributes(
            AudioAttributes.Builder().setUsage(C.USAGE_MEDIA).setContentType(C.AUDIO_CONTENT_TYPE_MUSIC).build(), true
        )
        .setHandleAudioBecomingNoisy(true)
        // holds the CPU and the Wi-Fi radio awake while playing: with the screen off Android otherwise slows or parks the network, which
        // is what starved the stream around locking and unlocking the phone
        .setWakeMode(C.WAKE_MODE_NETWORK)
        .build()
    /** What the media session (media buttons, notification, Bluetooth) controls: Next and Previous go through the skip-burst handling too. */
    /** Resolving a browse id (a car or a voice request asks to play an album, a playlist, a song of a list) reads the library: off the main thread. */
    private val browseExec = java.util.concurrent.Executors.newSingleThreadExecutor { r -> Thread(r, "browse-play").also { it.isDaemon = true } }
    private fun isBrowseId(id: String) = BrowseTree.isPlayId(id)
    /** A controller (Android Auto, a head unit, a voice command) asked to play browse items: the songs of the list they come from are queued here with the normal player, so the queue, history and everything else behave as if it was played in the app. */
    internal fun playBrowse(items: List<MediaItem>): Boolean {
        val id = items.firstOrNull()?.mediaId ?: return false
        if (!isBrowseId(id)) return false
        browseExec.execute {
            val r = try { BrowseTree.resolve(id, AppBrowseSource(App.of(ctx))) } catch (_: Exception) { null }
            if (r != null) handler.post { play(r.tracks, r.index) }
        }
        return true
    }
    /** What the system media session (media buttons, notification, Bluetooth, Android Auto) controls: a window of the queue, commands sent here (see [SessionPlayer]). */
    @OptIn(UnstableApi::class)
    val sessionPlayer: Player by lazy { SessionPlayer(this, prefs) }
    @OptIn(UnstableApi::class)
    private val prefetcher = Prefetcher(ctx) { prefetchSource.createDataSource() as CacheDataSource }
    private val netWatch = NetworkWatch(ctx) { Handler(Looper.getMainLooper()).post { nudge() } }

    var onChange: (() -> Unit)? = null
    private val observers = java.util.concurrent.CopyOnWriteArrayList<() -> Unit>()
    /** Extra listeners (Compose UI); returns a function that removes the listener. */
    fun observe(fn: () -> Unit): () -> Unit { observers.add(fn); return { observers.remove(fn) } }
    private fun fire() { onChange?.invoke(); for (o in observers) o() }
    /** Invoked when a new track starts (for recently played). */
    var onTrackStarted: ((Track) -> Unit)? = null
    var queue: List<Track> = emptyList(); private set
    /** Counts every change of the queue's songs or order (also a reorder that leaves the count and the current song alone), so the UI can refresh its list. */
    var queueRev = 0; private set

    private val handler = Handler(Looper.getMainLooper())
    private var equalizer: Equalizer? = null

    /** Minutes until playback pauses; 0 = off. */
    var sleepMinutes = 0; private set
    /** When the sleep timer fires (uptime clock), 0 = off; [sleepRemainingMs] is what the UI counts down. */
    var sleepEndsAt = 0L; private set
    /** What the sleep timer waits for (see [SleepPlan]); MINUTES uses [sleepEndsAt]. */
    var sleepMode = SleepMode.OFF; private set
    private var sleepArmedPath: String? = null
    private val sleepRunnable = Runnable { sleepTick() }
    /** Time left until the timer fires (real time, so a faster speed shortens what is left of a song); null when off or not known (a song of unknown length). */
    val sleepRemainingOrNull: Long? get() = when (sleepMode) {
        SleepMode.OFF -> null
        SleepMode.MINUTES -> (sleepEndsAt - android.os.SystemClock.elapsedRealtime()).coerceAtLeast(0L)
        SleepMode.END_OF_TRACK -> SleepPlan.trackRemaining(exo.currentPosition, durationMs)?.let { (it / speed).toLong() }
        SleepMode.END_OF_ALBUM -> SleepPlan.albumRemaining(queue, queueIndex, exo.currentPosition)?.let { (it / speed).toLong() }
    }
    val sleepRemainingMs: Long get() = sleepRemainingOrNull ?: 0L
    private fun sleepFired() {
        exo.pause(); sleepMode = SleepMode.OFF; sleepMinutes = 0; sleepEndsAt = 0L; sleepArmedPath = null; prefs.sleepEndsAtWall = 0L
        fade = 1f; applyOutputVolume(); stopShake(); handler.removeCallbacks(sleepRunnable); fire()
    }
    private fun sleepTick() {
        if (sleepMode == SleepMode.OFF) return
        val left = sleepRemainingOrNull
        // "end of this song": a different song has started (a skip, or the end came before the tick): the moment has passed
        if (sleepMode == SleepMode.END_OF_TRACK && sleepArmedPath != null && current?.path != sleepArmedPath) { sleepFired(); return }
        if (left != null && left <= (if (sleepMode == SleepMode.MINUTES) 0L else 250L)) { sleepFired(); return }
        val g = if (left == null) 1f else SleepPlan.fadeGain(left)
        if (g != fade) { fade = g; applyOutputVolume() }
        if (SleepPlan.canExtend(sleepMode, left)) startShake() else stopShake()
        handler.postDelayed(sleepRunnable, if (left != null && left <= SleepPlan.FADE_MS + 1_000L) 250L else 1_000L)
    }
    private fun armSleep(mode: SleepMode) {
        handler.removeCallbacks(sleepRunnable)
        sleepMode = mode; sleepArmedPath = if (mode == SleepMode.END_OF_TRACK) current?.path else null
        if (mode != SleepMode.MINUTES) { sleepMinutes = 0; sleepEndsAt = 0L; prefs.sleepEndsAtWall = 0L }
        if (mode == SleepMode.OFF) { fade = 1f; applyOutputVolume(); stopShake() } else handler.post(sleepRunnable)
        fire()
    }
    /** "End of this song" / "End of this album": playback pauses (after a fade-out) when it ends. */
    fun setSleepEndOfTrack() = armSleep(SleepMode.END_OF_TRACK)
    fun setSleepEndOfAlbum() = armSleep(SleepMode.END_OF_ALBUM)
    /** Adds [SleepPlan.EXTEND_MIN] minutes to what is left (a song-or-album timer becomes a minutes timer). */
    fun extendSleep() {
        if (sleepMode == SleepMode.OFF) return
        val left = sleepRemainingOrNull ?: 0L
        val now = android.os.SystemClock.elapsedRealtime()
        sleepEndsAt = SleepPlan.extend(now + left, now); sleepMinutes = ((sleepEndsAt - now + 59_999L) / 60_000L).toInt()
        prefs.sleepEndsAtWall = System.currentTimeMillis() + (sleepEndsAt - now)
        fade = 1f; applyOutputVolume()
        handler.removeCallbacks(sleepRunnable); sleepMode = SleepMode.MINUTES; sleepArmedPath = null; handler.post(sleepRunnable); fire()
    }

    // shaking the phone in the last stretch of the timer extends it
    private val shake = ShakeDetector {
        handler.post { extendSleep(); android.widget.Toast.makeText(ctx, "Sleep timer +" + SleepPlan.EXTEND_MIN + " min", android.widget.Toast.LENGTH_SHORT).show() }
    }
    private var shaking = false
    private val shakeListener = object : android.hardware.SensorEventListener {
        override fun onSensorChanged(e: android.hardware.SensorEvent) { shake.onSample(e.values[0], e.values[1], e.values[2], android.os.SystemClock.elapsedRealtime()) }
        override fun onAccuracyChanged(s: android.hardware.Sensor?, a: Int) {}
    }
    private fun startShake() {
        if (shaking) return
        try {
            val sm = ctx.getSystemService(Context.SENSOR_SERVICE) as android.hardware.SensorManager
            sm.getDefaultSensor(android.hardware.Sensor.TYPE_ACCELEROMETER)?.let { shaking = sm.registerListener(shakeListener, it, android.hardware.SensorManager.SENSOR_DELAY_UI) }
        } catch (_: Exception) {}
    }
    private fun stopShake() {
        if (!shaking) return
        shaking = false
        try { (ctx.getSystemService(Context.SENSOR_SERVICE) as android.hardware.SensorManager).unregisterListener(shakeListener) } catch (_: Exception) {}
    }

    val current: Track? get() = (if (burstTarget in 0 until exo.mediaItemCount) exo.getMediaItemAt(burstTarget) else exo.currentMediaItem)?.localConfiguration?.tag as? Track
    val isPlaying: Boolean get() = exo.isPlaying
    /** Playing, or about to (loading after a seek, reconnecting): what the Play/Pause button shows and toggles. Showing Play
     * while it was only loading made a tap pause the reload instead of resuming. */
    val wantsToPlay: Boolean get() = exo.playWhenReady && exo.playerError == null && exo.playbackState != Player.STATE_IDLE && exo.playbackState != Player.STATE_ENDED
    val hasQueue: Boolean get() = exo.mediaItemCount > 0
    val positionMs: Long get() = if (burstTarget >= 0) 0L else exo.currentPosition.coerceAtLeast(0)
    val durationMs: Long get() = current?.durationMs?.takeIf { it > 0 } ?: exo.duration.takeIf { it > 0 } ?: 0L
    val queueIndex: Int get() = if (burstTarget >= 0) burstTarget else exo.currentMediaItemIndex

    var volume: Float
        get() = prefs.volume.coerceAtMost(prefs.volumeLimit / 100f)
        set(v) { prefs.volume = v.coerceIn(0f, prefs.volumeLimit / 100f); applyOutputVolume(); fire() }

    /** The sleep timer's fade-out scales the volume; 1 = untouched. */
    private var fade = 1f
    /** The levelling factor of the song that plays (see [Loudness]); 1 when the setting is off or the server gave no gain. */
    private var loud = 1f
    private fun applyOutputVolume() { exo.volume = Loudness.output(volume, fade * xfade, loud) }
    /** The fade between songs (see [FadeBetween]): 1 in the middle of a song. */
    private var xfade = 1f
    private val xfadeTick = object : Runnable {
        override fun run() {
            val secs = prefs.fadeSecs
            if (secs <= 0 || !exo.isPlaying) { if (xfade != 1f) { xfade = 1f; applyOutputVolume() }; return }
            val i = exo.currentMediaItemIndex; val q = queue
            // the neighbours in PLAY order (a shuffled queue is not played in list order)
            val flags = FadeBetween.flags(exo.previousMediaItemIndex.takeIf { it >= 0 }?.let { q.getOrNull(it) }, q.getOrNull(i), exo.nextMediaItemIndex.takeIf { it >= 0 }?.let { q.getOrNull(it) })
            val g = FadeBetween.gain(exo.currentPosition, durationMs, secs * 1000L, flags)
            if (Math.abs(g - xfade) > 0.01f || (g == 1f && xfade != 1f)) { xfade = g; applyOutputVolume() }
            handler.postDelayed(this, 150L)
        }
    }
    /** The setting was changed, or playback started: runs (or stops) the fade ticker. */
    fun applyFade() { handler.removeCallbacks(xfadeTick); if (prefs.fadeSecs > 0 && exo.isPlaying) handler.post(xfadeTick) else if (xfade != 1f) { xfade = 1f; applyOutputVolume() } }
    private fun levelFor(t: Track?): Float = Loudness.factor(t?.let { com.ipodemu.library.AudioFacts.of(it)?.gainDb } ?: Float.NaN, prefs.levelVolume)
    /** The setting was switched: the song that plays follows at once. */
    fun applyLeveling() { loud = levelFor(current); applyOutputVolume() }

    init {
        applyOutputVolume()
        applyModes()
        exo.addListener(object : Player.Listener {
            override fun onEvents(player: Player, events: Player.Events) { fire() }
            override fun onIsPlayingChanged(isPlaying: Boolean) {
                handler.removeCallbacks(persistTick)
                if (isPlaying) handler.postDelayed(persistTick, 5_000L) else persistNow()
                applyFade()
            }
            override fun onTimelineChanged(timeline: androidx.media3.common.Timeline, reason: Int) { handler.removeCallbacks(persistSoon); handler.postDelayed(persistSoon, 1_500L) }
            override fun onMediaItemTransition(mediaItem: MediaItem?, reason: Int) {
                // a metadata-only swap (refreshArtwork) also reports a transition; that's not a new play
                (mediaItem?.localConfiguration?.tag as? Track)?.let { if (!quietTransition && (it !== lastStarted || reason != Player.MEDIA_ITEM_TRANSITION_REASON_PLAYLIST_CHANGED)) onTrackStarted?.invoke(it); lastStarted = it }
                refreshArtwork()
                checkQueueLow()
                (mediaItem?.localConfiguration?.tag as? Track)?.let { startedItem(it) }
                if (reason == Player.MEDIA_ITEM_TRANSITION_REASON_AUTO && (sleepMode == SleepMode.END_OF_TRACK || sleepMode == SleepMode.END_OF_ALBUM)) sleepFired()
                adaptUpcoming(); schedulePrefetch()
                // a song ending on its own: a Jam moves the whole group on instead (see Jam)
                if (reason == Player.MEDIA_ITEM_TRANSITION_REASON_AUTO) onAutoAdvance?.invoke()
            }
            override fun onPlaybackStateChanged(state: Int) {
                // READY -> BUFFERING while playing is a stall (the stream ran dry); a seek or a track change is not
                if (state == Player.STATE_BUFFERING && lastState == Player.STATE_READY && exo.playWhenReady && !seeking && exo.currentPosition > 1000) registerStall()
                if (state == Player.STATE_READY) seeking = false
                lastState = state
            }
            override fun onPositionDiscontinuity(oldPosition: Player.PositionInfo, newPosition: Player.PositionInfo, reason: Int) {
                if (reason == Player.DISCONTINUITY_REASON_SEEK || reason == Player.DISCONTINUITY_REASON_SEEK_ADJUSTMENT) seeking = true
                if (reason == Player.DISCONTINUITY_REASON_AUTO_TRANSITION || reason == Player.DISCONTINUITY_REASON_SKIP) rememberPlace(oldPosition.mediaItem?.localConfiguration?.tag as? Track, oldPosition.positionMs, finished = reason == Player.DISCONTINUITY_REASON_AUTO_TRANSITION)
            }
            override fun onAudioSessionIdChanged(audioSessionId: Int) { applyEq() }
            override fun onPlayerError(error: androidx.media3.common.PlaybackException) {
                android.util.Log.w("FLACie", "playback error at ${exo.currentPosition}ms: ${error.errorCodeName}", error)
                // a song this phone cannot decode (no decoder for the format) is played through the server's conversion instead of going silent
                val cur0 = current
                if (cur0 != null && cur0.path !in forceHls && hlsUri(cur0) != null && !isHls(exo.currentMediaItem ?: return) && error.errorCode in setOf(
                        androidx.media3.common.PlaybackException.ERROR_CODE_DECODER_INIT_FAILED, androidx.media3.common.PlaybackException.ERROR_CODE_DECODING_FAILED,
                        androidx.media3.common.PlaybackException.ERROR_CODE_DECODING_FORMAT_UNSUPPORTED, androidx.media3.common.PlaybackException.ERROR_CODE_DECODING_FORMAT_EXCEEDS_CAPABILITIES,
                        androidx.media3.common.PlaybackException.ERROR_CODE_AUDIO_TRACK_INIT_FAILED)) {
                    forceHls.add(cur0.path)
                    val idx = exo.currentMediaItemIndex; val at = exo.currentPosition
                    exo.replaceMediaItem(idx, item(cur0)); exo.seekTo(idx, at); exo.prepare(); exo.play()
                    fire(); return
                }
                val key = exo.currentMediaItemIndex to (current?.path ?: "")
                retries = if (key == retryKey) retries + 1 else 1
                retryKey = key
                // a dropped connection (screen lock, a tunnel, a dead spot) is retried after a short, growing wait, from where it stopped
                if (retries == 6) android.widget.Toast.makeText(ctx, "Couldn't play \"${current?.title ?: "this song"}\" (${error.errorCodeName})", android.widget.Toast.LENGTH_LONG).show()
                if (retries <= 5) { val at = exo.currentPosition; val idx = exo.currentMediaItemIndex; handler.postDelayed({ if (exo.currentMediaItemIndex == idx) { exo.seekTo(idx, at); exo.prepare(); exo.play() } }, 600L * retries * retries) }
                fire()
            }
        })
        applyEq()
    }

    /** Covers that weren't downloaded yet when the queue was built (Jellyfin/iTunes keys) left the lock screen and
     * notification on no art or the previous song's: fetch the current and next covers, then swap the metadata in. */
    private fun refreshArtwork() {
        val app = App.of(ctx)
        val idx = exo.currentMediaItemIndex
        if (idx < 0 || idx >= exo.mediaItemCount) return
        for (i in idx until minOf(idx + 3, exo.mediaItemCount)) {
            val item = exo.getMediaItemAt(i)
            val t = item.localConfiguration?.tag as? Track ?: continue
            if (item.mediaMetadata.artworkUri != null || t.artKey == null) continue
            // the song that plays asks first; the next two are background work
            app.art.prefetch(t.artKey, {
                val n = (0 until exo.mediaItemCount).firstOrNull { exo.getMediaItemAt(it).localConfiguration?.tag === t } ?: return@prefetch
                if (exo.getMediaItemAt(n).mediaMetadata.artworkUri == null) exo.replaceMediaItem(n, item(t))
            }, if (i == idx) com.ipodemu.library.Priority.PLAYING else com.ipodemu.library.Priority.BACKGROUND)
        }
    }

    // ---- the queue survives the app being killed ----------------------------------------------------------------------------------
    private val queueFile = File(ctx.filesDir, "queue.json")
    private val writer = java.util.concurrent.Executors.newSingleThreadExecutor { r -> Thread(r, "queue-save").also { it.isDaemon = true } }
    private val persistSoon = Runnable { persistNow() }
    private val persistTick = object : Runnable {
        override fun run() { persistNow(); if (exo.isPlaying) handler.postDelayed(this, 5_000L) }
    }
    /** Writes the queue, the current song and the position (in the background). Never writes an empty queue: a cleared player keeps the last state. */
    fun persistNow() {
        if (!hasQueue || burstTarget >= 0) return
        val tracks = queueTracks(); if (tracks.isEmpty()) return
        val idx = exo.currentMediaItemIndex.coerceIn(0, tracks.lastIndex); val pos = exo.currentPosition
        rememberPlace(tracks[idx], pos)
        // a shuffled queue keeps its play order (a reordered Up next must come back as it was left)
        val order = if (exo.shuffleModeEnabled && tracks.size == exo.mediaItemCount) playOrderNow() else null
        writer.execute {
            try {
                val tmp = File(queueFile.path + ".tmp")
                tmp.writeText(QueueStore.encode(tracks, idx, pos, order), Charsets.UTF_8)
                if (!tmp.renameTo(queueFile)) { queueFile.delete(); tmp.renameTo(queueFile) }
            } catch (_: Exception) {}
        }
    }
    /** At start-up: the queue, song and position of last time, paused (nothing plays by itself). Songs whose file is gone are dropped. */
    fun restoreQueue() {
        if (hasQueue) return
        try {
            if (!queueFile.exists()) return
            val s = QueueStore.decode(queueFile.readText(Charsets.UTF_8)) { QueueStore.stillThere(it) { p -> File(p).exists() } } ?: return
            queue = s.tracks
            lastStarted = s.tracks[s.index]   // a restore is not a new play (no history entry, no plays report)
            exo.setMediaItems(s.tracks.map(::item), s.index, s.positionMs)
            s.shuffleOrder?.let { setShuffleOrderQuiet(it) }
            exo.playWhenReady = false
            ensureService()   // a paused session, so the media buttons and the notification reach the restored queue
        } catch (_: Exception) {}
    }

    fun applyModes() {
        // turning shuffle on mid-song: a fresh order with the song playing first, so every other song is still to come
        // (the player's old shuffle order could have put it near the end, leaving only a few songs before playback stopped)
        if (prefs.shuffle && !exo.shuffleModeEnabled && exo.mediaItemCount > 1) setShuffleOrderQuiet(ShuffleStart.order(exo.currentMediaItemIndex, exo.mediaItemCount))
        exo.shuffleModeEnabled = prefs.shuffle
        exo.repeatMode = when (prefs.repeat) {
            1 -> Player.REPEAT_MODE_ALL
            2 -> Player.REPEAT_MODE_ONE
            else -> Player.REPEAT_MODE_OFF
        }
    }

    /** Decoded audio format of the current track (null until playback starts). */
    @androidx.annotation.OptIn(androidx.media3.common.util.UnstableApi::class)
    fun audioFormat(): androidx.media3.common.Format? = exo.audioFormat

    /** The next [n] tracks in actual play order (respects shuffle), as (queue index, track). */
    fun upNext(n: Int): List<Pair<Int, Track>> {
        val out = ArrayList<Pair<Int, Track>>()
        if (!hasQueue) return out
        val tl = exo.currentTimeline
        val repeat = if (exo.repeatMode == Player.REPEAT_MODE_ONE) Player.REPEAT_MODE_OFF else exo.repeatMode
        var i = exo.currentMediaItemIndex
        repeat(n) {
            i = tl.getNextWindowIndex(i, repeat, exo.shuffleModeEnabled)
            if (i == C.INDEX_UNSET || i == exo.currentMediaItemIndex) return out
            (exo.getMediaItemAt(i).localConfiguration?.tag as? Track)?.let { out.add(i to it) }
        }
        return out
    }

    /** Tracks currently queued, in queue order (not shuffle order). */
    fun queueTracks(): List<Track> = List(exo.mediaItemCount) { exo.getMediaItemAt(it).localConfiguration?.tag as? Track }.filterNotNull()

    /** While in a Jam, the transport and queue actions go to the group (which then drives this player) instead. */
    interface Interceptor {
        fun toggle(): Boolean; fun seek(ms: Long): Boolean; fun next(): Boolean; fun prev(): Boolean
        fun play(list: List<Track>, index: Int): Boolean; fun enqueue(t: Track, next: Boolean): Boolean; fun skipTo(index: Int): Boolean
    }
    @Volatile var interceptor: Interceptor? = null
    /** Called when a song ends and the next starts by itself. */
    var onAutoAdvance: (() -> Unit)? = null
    /** Loaded and able to play at once (a Jam waits for this before telling the group it is ready). */
    val buffered: Boolean get() = exo.playbackState == Player.STATE_READY

    fun addNext(t: Track) {
        if (interceptor?.enqueue(t, true) == true) return
        if (!hasQueue) { play(listOf(t), 0, shuffle = false); return }
        exo.addMediaItem(exo.currentMediaItemIndex + 1, item(t)); queueChanged()
    }

    /** Autoplay's songs go on the end of the queue. */
    fun appendAutoplay(list: List<Track>) {
        if (list.isEmpty() || !hasQueue) return
        exo.addMediaItems(list.map(::item)); queueChanged()
    }
    /** Autoplay fills the queue when at most one more song follows the current one. */
    fun checkQueueLow() { if (prefs.repeat == 0 && exo.mediaItemCount > 0 && exo.mediaItemCount - exo.currentMediaItemIndex <= 2) onQueueLow?.invoke() }
    /** Called as a song starts when at most one more is queued after it (Autoplay fills the queue). */
    var onQueueLow: (() -> Unit)? = null

    fun addToQueue(t: Track) {
        if (interceptor?.enqueue(t, false) == true) return
        if (!hasQueue) { play(listOf(t), 0, shuffle = false); return }
        exo.addMediaItem(item(t)); queueChanged()
    }

    private fun queueChanged() { queue = queueTracks(); queueRev++; fire() }
    /** After a user edit of the queue: refresh the UI, the preloaded songs (the next ones may be different now) and the file on disk. */
    private fun edited() { queueChanged(); adaptUpcoming(); schedulePrefetch(); persistNow() }

    // ---- editing the queue (Up next) ---------------------------------------------------------------------------------------------
    // The arithmetic is [QueueEdit] (unit-tested); this applies it to the player. The playing song keeps playing through every edit: ExoPlayer
    // tracks the current item across move/remove, and removing the current song first jumps to the one that plays next (see [QueueEdit.removePlan]).

    /** A song taken out of the queue, with what Undo needs to put it back. */
    class QueueRemoval(val track: Track, val index: Int, val after: Track?)

    /** Queue edits are for the local player; in a Jam the group owns the queue. */
    val canEditQueue: Boolean get() = interceptor == null && hasQueue

    /** True while a queue edit moves the player to another song itself, so the history and the plays report do not count it as heard. */
    private var quietTransition = false

    /** A pending skip burst is settled first, so the edit works on the song that is really playing. */
    private fun settleBurst() { if (burstTarget >= 0) { handler.removeCallbacks(commitBurst); commitBurst.run() } }

    private fun trackAt(i: Int): Track? = if (i in 0 until exo.mediaItemCount) exo.getMediaItemAt(i).localConfiguration?.tag as? Track else null

    /** The queue indices of all songs in the order they play (the queue order, or the shuffle order with shuffle on). */
    fun playOrderNow(): IntArray {
        val n = exo.mediaItemCount
        if (!exo.shuffleModeEnabled) return IntArray(n) { it }
        val tl = exo.currentTimeline
        val out = IntArray(n); var i = tl.getFirstWindowIndex(true); var k = 0
        while (i != C.INDEX_UNSET && k < n) { out[k++] = i; i = tl.getNextWindowIndex(i, Player.REPEAT_MODE_OFF, true) }
        return if (k == n) out else IntArray(n) { it }
    }

    @OptIn(UnstableApi::class)
    private fun setShuffleOrderQuiet(order: IntArray) {
        if (!QueueEdit.isPermutation(order, exo.mediaItemCount)) return
        exo.setShuffleOrder(androidx.media3.exoplayer.source.ShuffleOrder.DefaultShuffleOrder(order, System.nanoTime()))
    }

    private val repeatAll: Boolean get() = exo.repeatMode == Player.REPEAT_MODE_ALL

    /**
     * Moves the upcoming song with queue index [index] one place up or down in Up next, or to the top ("Play next"). Works in the shuffle order
     * with shuffle on, in the queue order otherwise. Returns false when nothing changed (not upcoming, already at that end, a Jam).
     */
    fun moveUpNext(index: Int, step: QueueEdit.Step): Boolean {
        if (!canEditQueue) return false
        settleBurst()
        if (index !in 0 until exo.mediaItemCount) return false
        val cur = exo.currentMediaItemIndex
        val order = playOrderNow()
        if (exo.shuffleModeEnabled) {
            val next = QueueEdit.reorder(order, cur, repeatAll, index, step) ?: return false
            setShuffleOrderQuiet(next)
        } else {
            val moves = QueueEdit.linearMoves(cur, QueueEdit.upcoming(order, cur, repeatAll), index, step)
            if (moves.isEmpty()) return false
            for ((from, to) in moves) exo.moveMediaItem(from, to)
        }
        edited()
        return true
    }

    /** Moves the song at queue index [from] to [to] in the queue order (the Up Next screen lists that order). The current song keeps playing. */
    fun moveInQueue(from: Int, to: Int): Boolean {
        if (!canEditQueue) return false
        settleBurst()
        val n = exo.mediaItemCount
        if (from !in 0 until n || to !in 0 until n || from == to) return false
        exo.moveMediaItem(from, to)
        edited()
        return true
    }

    /** "Play next" in the queue order: just after the current song. */
    fun playNextInQueue(index: Int): Boolean {
        val target = QueueEdit.playNextTarget(exo.currentMediaItemIndex, index) ?: return false
        return moveInQueue(index, target)
    }

    /**
     * Takes the song at queue index [index] out. Returns what [undoRemoval] needs, or null when nothing was removed. Removing the playing song
     * starts the one that plays next (it stays paused if playback was paused); with none after it, the one before it becomes current, paused.
     */
    fun removeAt(index: Int): QueueRemoval? {
        if (!canEditQueue) return null
        settleBurst()
        val track = trackAt(index) ?: return null
        val order = playOrderNow()
        val plan = QueueEdit.removePlan(order, exo.currentMediaItemIndex, index, repeatAll) ?: return null
        val at = order.indexOf(index)
        val removal = QueueRemoval(track, index, if (at > 0) trackAt(order[at - 1]) else null)
        if (plan.emptiesQueue) { clearQueue(); return removal }
        if (plan.jumpTo >= 0) {
            // a jump to the song before it (the end of the queue) is not a play of that song
            quietTransition = plan.pauseAfterJump
            try {
                exo.seekTo(plan.jumpTo, 0L)
                if (plan.pauseAfterJump) exo.pause()
            } finally { quietTransition = false }
        }
        exo.removeMediaItem(index)
        edited()
        return removal
    }

    /** Puts a removed song back where it was (in the shuffle order: right after the song that played before it). The current song is not changed. */
    fun undoRemoval(r: QueueRemoval) {
        if (interceptor != null) return
        if (!hasQueue) { playFrom(listOf(r.track), 0, 0L, paused = true); return }
        settleBurst()
        val at = r.index.coerceIn(0, exo.mediaItemCount)
        exo.addMediaItem(at, item(r.track))
        if (exo.shuffleModeEnabled) {
            val after = r.after?.let { a -> (0 until exo.mediaItemCount).firstOrNull { it != at && trackAt(it) == a } } ?: -1
            setShuffleOrderQuiet(QueueEdit.placeAfter(playOrderNow(), at, after))
        }
        edited()
    }

    /** Kept for callers that remove without Undo. */
    fun removeFromQueue(index: Int) { removeAt(index) }

    fun clearQueue() { handler.removeCallbacks(commitBurst); burst.reset(); exo.stop(); exo.clearMediaItems(); queue = emptyList(); try { queueFile.delete() } catch (_: Exception) {}; fire() }

    fun skipTo(index: Int) { if (interceptor?.skipTo(index) == true) return; exo.seekTo(index, 0L); exo.play() }

    fun applyVolumeLimit() { applyOutputVolume() }

    // ---- speed and long items ------------------------------------------------------------------------------------------------------
    // 0.5x to 3x with the pitch kept (pitch 1). The speed is remembered per item (spoken word and anything you changed), the speed last used on spoken
    // word is the default for the next audiobook or podcast, and a long item (20 minutes or more) remembers where you stopped. Rules: [SpeedPolicy].
    private val speedMem = ItemMemory.decode(prefs.speedMemory, 400)
    private val placeMem = ItemMemory.decode(prefs.placeMemory, 100)
    var speed = 1f; private set

    private fun applySpeed(s: Float) {
        speed = SpeedPolicy.clamp(s)
        exo.playbackParameters = androidx.media3.common.PlaybackParameters(speed, 1f)
    }

    /** Sets the speed and remembers it for this item (and as the spoken-word default when the item is spoken). */
    fun setSpeed(s: Float) {
        applySpeed(s)
        current?.let { t ->
            val spoken = SpeedPolicy.isSpoken(t.genre)
            speedMem.set(SpeedPolicy.keyOf(t.path), if (spoken || speed != 1f) speed.toDouble() else null)
            prefs.speedMemory = speedMem.encode()
            if (spoken) prefs.spokenSpeed = speed
        }
        fire()
    }
    fun stepSpeed(up: Boolean) = setSpeed(SpeedPolicy.step(speed, up))

    /** Skip back 10 s / forward 30 s inside the item (the long-item buttons). */
    fun skipBy(ms: Long) { seekTo((exo.currentPosition + ms).coerceIn(0L, (durationMs - 1_000L).coerceAtLeast(0L))) }

    /** A new song starts: its remembered speed (else the spoken-word default, else 1x) and, for a long item, where it was left. */
    private fun startedItem(t: Track) {
        loud = levelFor(t); applyOutputVolume()
        val saved = speedMem.get(SpeedPolicy.keyOf(t.path))?.toFloat()
        applySpeed(saved ?: if (SpeedPolicy.isSpoken(t.genre)) prefs.spokenSpeed else 1f)
        if (!quietTransition && exo.currentPosition < 2_000L) {
            val at = SpeedPolicy.resumeAt(placeMem.get(SpeedPolicy.keyOf(t.path))?.toLong(), t.durationMs.takeIf { it > 0 } ?: exo.duration.coerceAtLeast(0L))
            if (at > 0L) exo.seekTo(exo.currentMediaItemIndex, at)
        }
    }

    /** Stores (or clears) the place of a long item; called with the current item while playing and with the old item when a song changes. */
    private fun rememberPlace(t: Track?, positionMs: Long, finished: Boolean = false) {
        if (t == null) return
        val dur = t.durationMs
        if (!SpeedPolicy.isLong(dur)) return
        val key = SpeedPolicy.keyOf(t.path)
        if (finished) placeMem.set(key, null) else if (SpeedPolicy.shouldSavePlace(positionMs, dur)) placeMem.set(key, positionMs.toDouble()) else if (positionMs >= dur - SpeedPolicy.FINISHED_MS) placeMem.set(key, null) else return
        prefs.placeMemory = placeMem.encode()
    }

    fun setSleepTimer(minutes: Int) {
        handler.removeCallbacks(sleepRunnable)
        sleepMinutes = minutes
        if (minutes > 0) {
            sleepEndsAt = android.os.SystemClock.elapsedRealtime() + minutes * 60_000L
            prefs.sleepEndsAtWall = System.currentTimeMillis() + minutes * 60_000L
            armSleep(SleepMode.MINUTES)
        } else { sleepEndsAt = 0L; prefs.sleepEndsAtWall = 0L; armSleep(SleepMode.OFF) }
    }
    /** After a kill and restart: a sleep timer that was still running carries on for what was left of it (it never fires late while playing nothing). */
    fun restoreSleepTimer() {
        val left = prefs.sleepEndsAtWall - System.currentTimeMillis()
        if (prefs.sleepEndsAtWall == 0L) return
        if (left <= 0L) { prefs.sleepEndsAtWall = 0L; return }
        sleepMinutes = ((left + 59_999L) / 60_000L).toInt()
        sleepEndsAt = android.os.SystemClock.elapsedRealtime() + left
        armSleep(SleepMode.MINUTES)
    }

    // ---- equalizer ---------------------------------------------------------

    /** Applies prefs.eq to the output. Presets are gain curves (dB) at reference frequencies, interpolated per hardware band. */
    fun applyEq() {
        try {
            equalizer?.release(); equalizer = null
            val curve = EQ_PRESETS[prefs.eq] ?: return
            val eq = Equalizer(0, exo.audioSessionId)
            val range = eq.bandLevelRange
            for (b in 0 until eq.numberOfBands) {
                val hz = eq.getCenterFreq(b.toShort()) / 1000f
                val db = interp(curve, hz)
                eq.setBandLevel(b.toShort(), (db * 100).toInt().coerceIn(range[0].toInt(), range[1].toInt()).toShort())
            }
            eq.enabled = true
            equalizer = eq
        } catch (_: Exception) { equalizer = null }
    }

    private fun interp(g: FloatArray, hz: Float): Float {
        val x = ln(hz.coerceIn(REF_HZ.first(), REF_HZ.last()))
        for (i in 0 until REF_HZ.size - 1) {
            val a = ln(REF_HZ[i]); val b = ln(REF_HZ[i + 1])
            if (x <= b) return g[i] + (g[i + 1] - g[i]) * ((x - a) / (b - a))
        }
        return g.last()
    }

    // ---- transport ---------------------------------------------------------

    /** What the queue came from ("Bad Romance mix", a playlist name...), shown as "Playing from" in the Up next sheet. */
    var contextName: String? = null

    fun play(list: List<Track>, index: Int, shuffle: Boolean? = null, context: String? = null) {
        contextName = context
        if (interceptor?.play(list, index) == true) return
        if (list.isEmpty()) return
        queue = list
        if (shuffle != null) { prefs.shuffle = shuffle; applyModes() }
        exo.setMediaItems(list.map(::item), index.coerceIn(0, list.lastIndex), 0L)
        exo.prepare()
        exo.play()
        ctx.startService(Intent(ctx, PlaybackService::class.java))
        handler.postDelayed({ checkQueueLow() }, 1500)
    }

    /** Shuffle Songs: random start, shuffle mode forced on. */
    fun shuffleAll(list: List<Track>) = play(list, if (list.isEmpty()) 0 else list.indices.random(), true)

    /** The media session and notification live in the service: a queue restored after a kill has none until something starts it. */
    private fun ensureService() { try { ctx.startService(Intent(ctx, PlaybackService::class.java)) } catch (_: Exception) {} }
    fun toggle() { if (interceptor?.toggle() == true) return; if (wantsToPlay) exo.pause() else if (hasQueue) { ensureService(); ensurePrepared(); exo.play() } }
    fun pause() { exo.pause() }
    fun resume() { if (hasQueue) { ensureService(); ensurePrepared(); exo.play() } }
    /** Starts [list] at [index] from [positionMs] (taking over playback from another device). */
    fun playFrom(list: List<Track>, index: Int, positionMs: Long, paused: Boolean = false) {
        if (list.isEmpty()) return
        queue = list
        exo.setMediaItems(list.map(::item), index.coerceIn(0, list.lastIndex), positionMs.coerceAtLeast(0))
        exo.prepare(); if (paused) exo.pause() else exo.play()
        ctx.startService(Intent(ctx, PlaybackService::class.java))
    }
    /** After an error the player sits idle and play() alone does nothing; this was why Play stopped working after a bad seek. */
    private fun ensurePrepared() { if (exo.playbackState == Player.STATE_IDLE || exo.playerError != null) { retries = 0; exo.prepare() } }
    private var retries = 0
    private var lastStarted: Track? = null
    private var retryKey: Pair<Int, String>? = null
    // ---- skip bursts ------------------------------------------------------------------------------------------------------------
    // Presses of Next/Previous that follow each other within BURST_MS are not each sent to the player (every one made it open a stream that the
    // next press abandoned: 45 presses took 25 s to settle). The first press acts at once; the rest only move a target, shown in the UI
    // straight away, and the player jumps there once, SETTLE_MS after the last press. The decision itself is [SkipBurst] (unit-tested).
    private val burst = SkipBurst(BURST_MS)
    private val burstTarget: Int get() = burst.target
    private val commitBurst = Runnable {
        val t = burst.settle()
        if (t in 0 until exo.mediaItemCount) exo.seekTo(t, 0L)
        fire()
    }
    private fun stepIndex(from: Int, forward: Boolean): Int {
        val tl = exo.currentTimeline
        if (tl.isEmpty) return -1
        val repeat = if (exo.repeatMode == Player.REPEAT_MODE_ONE) Player.REPEAT_MODE_OFF else exo.repeatMode
        val i = if (forward) tl.getNextWindowIndex(from, repeat, exo.shuffleModeEnabled) else tl.getPreviousWindowIndex(from, repeat, exo.shuffleModeEnabled)
        return if (i == C.INDEX_UNSET) (if (forward) 0 else from) else i
    }
    private fun skip(forward: Boolean): Boolean {
        val r = burst.press(android.os.SystemClock.uptimeMillis(), exo.currentMediaItemIndex) { from -> stepIndex(from, forward) }
        if (r == SkipBurst.Press.ACT_NOW) return false
        if (r == SkipBurst.Press.MOVED) { handler.removeCallbacks(commitBurst); handler.postDelayed(commitBurst, SETTLE_MS); fire() }
        return true
    }
    fun next() {
        if (interceptor?.next() == true) return
        if (skip(true)) return
        if (exo.hasNextMediaItem()) exo.seekToNextMediaItem() else if (hasQueue) exo.seekTo(0, 0L)
    }
    fun prev() {
        if (interceptor?.prev() == true) return
        if (skip(false)) return
        if (exo.currentPosition > 3000 || !exo.hasPreviousMediaItem()) exo.seekTo(0) else exo.seekToPreviousMediaItem()
    }
    fun seekBy(ms: Long) { if (interceptor?.seek((exo.currentPosition + ms).coerceAtLeast(0)) == true) return; ensurePrepared(); exo.seekTo((exo.currentPosition + ms).coerceIn(0, (durationMs - 500).coerceAtLeast(0))) }
    fun seekTo(ms: Long) { if (interceptor?.seek(ms) == true) return; rawSeek(ms) }
    /** Seeks without going through a Jam (the Jam itself uses this). */
    fun rawSeek(ms: Long) { ensurePrepared(); exo.seekTo(ms.coerceIn(0, (durationMs - 250).coerceAtLeast(0))) }

    /** Plays a single audio file opened from another app (file manager, browser, chat...). */
    fun playUri(uri: Uri) {
        val t = trackFromUri(uri) ?: return
        play(listOf(t), 0, shuffle = false)
    }

    private fun trackFromUri(uri: Uri): Track? {
        val mmr = MediaMetadataRetriever()
        return try {
            mmr.setDataSource(ctx, uri)
            fun tag(k: Int) = mmr.extractMetadata(k)?.trim()?.takeIf { it.isNotEmpty() }
            val name = try {
                ctx.contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)?.use { if (it.moveToFirst()) it.getString(0) else null }
            } catch (_: Exception) { null } ?: uri.lastPathSegment ?: "Audio"
            val size = if (uri.scheme == "file") (uri.path?.let { File(it).length() } ?: 0L) else try {
                ctx.contentResolver.query(uri, arrayOf(OpenableColumns.SIZE), null, null, null)?.use { if (it.moveToFirst()) it.getLong(0) else 0L } ?: 0L
            } catch (_: Exception) { 0L }
            val key = "u" + Integer.toHexString(uri.toString().hashCode())
            val art = App.of(ctx).art
            if (!art.has(key)) mmr.embeddedPicture?.let { try { art.save(key, it) } catch (_: Exception) {} }
            Track(
                path = uri.toString(), title = tag(MediaMetadataRetriever.METADATA_KEY_TITLE) ?: name.substringBeforeLast('.'),
                artist = tag(MediaMetadataRetriever.METADATA_KEY_ARTIST) ?: "", album = tag(MediaMetadataRetriever.METADATA_KEY_ALBUM) ?: "",
                albumArtist = "", genre = "", trackNo = 0, discNo = 0,
                durationMs = tag(MediaMetadataRetriever.METADATA_KEY_DURATION)?.toLongOrNull() ?: 0L, year = 0,
                isMusic = true, artKey = key.takeIf { art.has(it) }, mtime = System.currentTimeMillis(), size = size,
            )
        } catch (_: Exception) { null } finally { try { mmr.release() } catch (_: Exception) {} }
    }

    // ---- weak connections -------------------------------------------------------------------------------------------------------------
    // Songs from Jellyfin normally stream as the original file. When the stream keeps running dry (two stalls within two minutes) the
    // current song and the next two switch to a lower-bitrate transcode (HLS, so seeking still works) for ten minutes, then go back to the
    // original. Settings > Streaming quality can pin it either way. Nothing is transcoded while the connection copes.

    private var lastState = Player.STATE_IDLE
    private var seeking = false
    private val stalls = ArrayDeque<Long>()
    @Volatile private var poorUntil = 0L
    private val dataSaver: Boolean get() = when (prefs.streamQuality) { 1 -> false; 2 -> true; else -> System.currentTimeMillis() < poorUntil }
    /** True while the lower-bitrate stream is the one in use (for the UI). */
    val usingDataSaver: Boolean get() = dataSaver && current?.let { hlsUri(it) } != null

    private fun registerStall() {
        val now = System.currentTimeMillis()
        stalls.addLast(now)
        while (stalls.isNotEmpty() && now - stalls.first() > 120_000) stalls.removeFirst()
        android.util.Log.w("FLACie", "stream stall #${stalls.size} at ${exo.currentPosition}ms on ${current?.title}")
        if (prefs.streamQuality == 0 && stalls.size >= 2 && poorUntil < now) {
            poorUntil = now + 10 * 60_000
            android.util.Log.w("FLACie", "weak connection: switching to the lower bitrate for 10 minutes")
            downgradeCurrent()
        }
    }

    private val jfStreamRe = Regex("^(https?://[^/]+(?:/[^?]*?)?)/Audio/([0-9a-fA-F]{32})/stream")
    /** The lower-bitrate version of a Jellyfin song, or null for anything else (local files, the NAS, downloads). */
    private fun hlsUri(t: Track): Uri? {
        val m = jfStreamRe.find(t.path) ?: return null
        val uid = prefs.accountUserId.ifBlank { return null }
        return Uri.parse("${m.groupValues[1]}/Audio/${m.groupValues[2]}/universal?UserId=$uid&DeviceId=${prefs.deviceId}&MaxStreamingBitrate=320000" +
            "&Container=aac&TranscodingContainer=ts&TranscodingProtocol=hls&AudioCodec=aac&MaxAudioChannels=2&StartTimeTicks=0")
    }

    /**
     * Apple Lossless on a phone that has no decoder for it: FLACie Web decodes the song itself (its own ALAC decoder, no ffmpeg and no Jellyfin conversion) and sends
     * WAV, which plays and seeks anywhere. Only when FLACie Web is set up and this is a Jellyfin song; null otherwise (the Jellyfin conversion is the fallback).
     */
    private fun ownAlacUri(t: Track): Uri? {
        val web = prefs.flacieWebUrl.trimEnd('/'); if (web.isBlank() || !prefs.hasJellyfinAccount) return null
        if (t.filePath.substringAfterLast('.', "").lowercase() !in setOf("m4a", "alac", "mp4")) return null
        val id = jfStreamRe.find(t.path)?.groupValues?.get(2) ?: return null
        val codec = AudioFacts.of(t)?.codec
        // developer switch: an empty file named force-own-alac in the app's external files folder sends every .m4a through the server's decoder, to test it on a phone that has an ALAC decoder
        val forceTest = java.io.File(ctx.getExternalFilesDir(null), "force-own-alac").exists() && (codec.isNullOrBlank() || codec.equals("alac", true))
        val alac = (codec.equals("alac", true) && !hasAlacDecoder) || forceTest
        val forcedUnknown = t.path in forceHls && codec.isNullOrBlank()
        return if (alac || forcedUnknown) Uri.parse("$web/api/alac?id=$id") else null
    }

    private fun needsTranscode(t: Track) = t.filePath.substringAfterLast('.', "").lowercase() in setOf("wma", "asf", "ape", "wv", "tta") || t.path in forceHls ||
        (AudioFacts.of(t)?.codec.equals("alac", true) && !hasAlacDecoder)   // Apple lossless: only where the phone has no decoder for it (many have none and stay silent)

    /** Songs that failed to decode here and are played through the server's conversion from now on. */
    private val forceHls = HashSet<String>()
    private val hasAlacDecoder: Boolean by lazy {
        try { android.media.MediaCodecList(android.media.MediaCodecList.REGULAR_CODECS).codecInfos.any { !it.isEncoder && it.supportedTypes.any { m -> m.equals("audio/alac", true) } } } catch (_: Exception) { true }
    }

    private fun isHls(i: MediaItem) = i.localConfiguration?.mimeType == androidx.media3.common.MimeTypes.APPLICATION_M3U8

    private fun downgradeCurrent() {
        val idx = exo.currentMediaItemIndex
        val t = current ?: return
        if (hlsUri(t) == null || isHls(exo.currentMediaItem ?: return)) return
        val at = exo.currentPosition
        exo.replaceMediaItem(idx, item(t)); exo.seekTo(idx, at)
        adaptUpcoming()
    }

    /** The next two songs follow whatever is in force now (lower bitrate while the connection is poor, the original again once it's fine). */
    private fun adaptUpcoming() {
        val idx = exo.currentMediaItemIndex
        for (i in idx + 1..minOf(idx + 2, exo.mediaItemCount - 1)) {
            val mi = exo.getMediaItemAt(i)
            val t = mi.localConfiguration?.tag as? Track ?: continue
            val want = dataSaver && hlsUri(t) != null
            if (want != isHls(mi)) exo.replaceMediaItem(i, item(t))
        }
    }

    /** Fetches the next song (two on Wi-Fi with plenty of bandwidth) completely into the cache while the current one plays. */
    private fun schedulePrefetch() {
        val bw = androidx.media3.exoplayer.upstream.DefaultBandwidthMeter.getSingletonInstance(ctx).bitrateEstimate
        val ups = upNext(2).map { it.second }.filter { t -> val u = Uri.parse(t.path); StreamSupport.cacheable(u) }
        fun need(t: Track) = if (t.size > 0 && t.durationMs > 0) t.size * 8000 / t.durationMs else 1_400_000L
        val n = when {
            dataSaver -> 0
            ups.isNotEmpty() && !netWatch.metered && bw >= 3 * need(ups[0]) -> 2
            ups.isNotEmpty() && bw >= 3 * need(ups[0]) / 2 -> 1
            else -> 0
        }
        prefetcher.fetch(ups.take(n).map { Uri.parse(it.path) })
    }

    /** The connection came back: an interrupted stream is restarted at once instead of after its timeout. */
    private fun nudge() {
        if (exo.playbackState == Player.STATE_IDLE && exo.playerError != null && exo.playWhenReady) exo.prepare()
    }

    private fun item(t: Track): MediaItem {
        val app = App.of(ctx)
        // artwork by file URI (decoded off the main thread by Media3), not embedded bytes: reading and attaching ~600 covers made
        // every Play tap slow and made each track change hitch on the main thread
        val art = app.art.bestFile(t.artKey)
        val md = MediaMetadata.Builder().setTitle(t.title).setArtist(t.artist.ifEmpty { null }).setAlbumTitle(t.album.ifEmpty { null })
        if (art != null) md.setArtworkUri(Uri.fromFile(art))
        val uri = if (t.path.startsWith("content:") || t.path.startsWith("file:") || t.path.startsWith("http:") ||
            t.path.startsWith("https:") || t.path.startsWith("smb:")) Uri.parse(t.path) else Uri.fromFile(File(t.path))
        // formats the phone cannot decode (WMA, APE ...) are converted by Jellyfin on the way, like the data saver does
        val own = ownAlacUri(t)
        // a song kept offline plays from its file (unless this phone needs the server to convert it): no network, no data used, no waiting
        val local = if (own == null && !needsTranscode(t)) app.offline.localFile(t) else null
        val hls = if (own == null && local == null && (dataSaver || needsTranscode(t))) hlsUri(t) else null
        return MediaItem.Builder().setMediaId(t.path).setUri(own ?: (local?.let { Uri.fromFile(it) }) ?: hls ?: uri).setTag(t).also { if (hls != null) it.setMimeType(androidx.media3.common.MimeTypes.APPLICATION_M3U8) }
            .setMediaMetadata(md.build()).build()
    }

    companion object {
        const val BURST_MS = 400L
        const val SETTLE_MS = 250L
        private val REF_HZ = floatArrayOf(60f, 230f, 910f, 3600f, 14000f)
        /** dB gains at 60 / 230 / 910 / 3.6k / 14k Hz. "Off" is absent (no effect attached). */
        val EQ_PRESETS: Map<String, FloatArray> = linkedMapOf(
            "Off" to null, "Acoustic" to floatArrayOf(4f, 3f, 1f, 3f, 3f), "Bass Booster" to floatArrayOf(6f, 4f, 0f, 0f, 0f),
            "Bass Reducer" to floatArrayOf(-6f, -4f, 0f, 0f, 0f), "Classical" to floatArrayOf(4f, 3f, -1f, 3f, 4f),
            "Dance" to floatArrayOf(6f, 4f, 0f, 3f, 4f), "Deep" to floatArrayOf(5f, 3f, 1f, -2f, -4f),
            "Electronic" to floatArrayOf(5f, 3f, -1f, 3f, 5f), "Flat" to floatArrayOf(0f, 0f, 0f, 0f, 0f),
            "Hip-Hop" to floatArrayOf(6f, 4f, -1f, 1f, 3f), "Jazz" to floatArrayOf(3f, 2f, -2f, 2f, 4f),
            "Latin" to floatArrayOf(3f, 1f, -1f, 2f, 4f), "Loudness" to floatArrayOf(6f, 3f, -1f, 3f, 5f),
            "Lounge" to floatArrayOf(-3f, -1f, 2f, 1f, -2f), "Piano" to floatArrayOf(2f, 1f, 0f, 3f, 3f),
            "Pop" to floatArrayOf(-1f, 2f, 4f, 2f, -1f), "R&B" to floatArrayOf(5f, 4f, 1f, -1f, 2f),
            "Rock" to floatArrayOf(5f, 3f, -1f, 3f, 5f), "Small Speakers" to floatArrayOf(5f, 3f, 1f, 0f, -1f),
            "Spoken Word" to floatArrayOf(-4f, 0f, 3f, 4f, 2f), "Treble Booster" to floatArrayOf(0f, 0f, 0f, 4f, 6f),
            "Treble Reducer" to floatArrayOf(0f, 0f, 0f, -4f, -6f), "Vocal Booster" to floatArrayOf(-2f, -1f, 3f, 3f, 0f),
        ).filterValues { it != null }.mapValues { it.value!! }
        val EQ_NAMES: List<String> = listOf("Off") + EQ_PRESETS.keys.sorted()
    }
}

/** The shuffle order used when shuffle is turned on mid-song: the song playing first, the rest in a random order. */
object ShuffleStart {
    fun order(current: Int, count: Int, random: kotlin.random.Random = kotlin.random.Random.Default): IntArray =
        intArrayOf(current.coerceIn(0, count - 1)) + (0 until count).filter { it != current.coerceIn(0, count - 1) }.shuffled(random).toIntArray()
}
