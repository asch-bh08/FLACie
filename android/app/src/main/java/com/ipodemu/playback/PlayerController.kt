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
private class MultiServerDataSource(private val prefs: Prefs) : DataSource {
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
        when {
            jfUrl.isNotBlank() && uriStr.startsWith(jfUrl) -> http.setRequestProperty("X-Emby-Token", prefs.jellyfinApiKey)
            plexUrl.isNotBlank() && uriStr.startsWith(plexUrl) -> http.setRequestProperty("X-Plex-Token", prefs.plexToken)
            moverUrl.isNotBlank() && uriStr.startsWith(moverUrl) -> http.setRequestProperty("X-Api-Key", prefs.fileMoverApiKey)
        }
        return http.open(dataSpec)
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
private class MultiServerDataSourceFactory(private val prefs: Prefs) : DataSource.Factory {
    override fun createDataSource(): DataSource = MultiServerDataSource(prefs)
}

class PlayerController(private val ctx: Context, private val prefs: Prefs) {
    // DefaultDataSource routes file:/content:/asset: URIs to Android's own local-file readers and only
    // hands http(s)/smb requests to our factory below -- otherwise every local track tries to open through
    // an HTTP-only data source and fails silently. Streamed songs also go through the on-disk cache (StreamSupport).
    @OptIn(UnstableApi::class)
    private val upstream = DefaultDataSource.Factory(ctx, MultiServerDataSourceFactory(prefs))
    @OptIn(UnstableApi::class)
    private val cacheSource = CacheDataSource.Factory().setCache(StreamSupport.cache(ctx)).setUpstreamDataSourceFactory(upstream)
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
    @OptIn(UnstableApi::class)
    private val prefetcher = Prefetcher(ctx) { cacheSource.createDataSource() as CacheDataSource }
    private val netWatch = NetworkWatch(ctx) { Handler(Looper.getMainLooper()).post { nudge() } }

    var onChange: (() -> Unit)? = null
    private val observers = java.util.concurrent.CopyOnWriteArrayList<() -> Unit>()
    /** Extra listeners (Compose UI); returns a function that removes the listener. */
    fun observe(fn: () -> Unit): () -> Unit { observers.add(fn); return { observers.remove(fn) } }
    private fun fire() { onChange?.invoke(); for (o in observers) o() }
    /** Invoked when a new track starts (for recently played). */
    var onTrackStarted: ((Track) -> Unit)? = null
    var queue: List<Track> = emptyList(); private set

    private val handler = Handler(Looper.getMainLooper())
    private var equalizer: Equalizer? = null

    /** Minutes until playback pauses; 0 = off. */
    var sleepMinutes = 0; private set
    private val sleepRunnable = Runnable { exo.pause(); sleepMinutes = 0; fire() }

    val current: Track? get() = exo.currentMediaItem?.localConfiguration?.tag as? Track
    val isPlaying: Boolean get() = exo.isPlaying
    /** Playing, or about to (loading after a seek, reconnecting): what the Play/Pause button shows and toggles. Showing Play
     * while it was only loading made a tap pause the reload instead of resuming. */
    val wantsToPlay: Boolean get() = exo.playWhenReady && exo.playerError == null && exo.playbackState != Player.STATE_IDLE && exo.playbackState != Player.STATE_ENDED
    val hasQueue: Boolean get() = exo.mediaItemCount > 0
    val positionMs: Long get() = exo.currentPosition.coerceAtLeast(0)
    val durationMs: Long get() = current?.durationMs?.takeIf { it > 0 } ?: exo.duration.takeIf { it > 0 } ?: 0L
    val queueIndex: Int get() = exo.currentMediaItemIndex

    var volume: Float
        get() = prefs.volume.coerceAtMost(prefs.volumeLimit / 100f)
        set(v) { prefs.volume = v.coerceIn(0f, prefs.volumeLimit / 100f); exo.volume = prefs.volume; fire() }

    init {
        exo.volume = volume
        applyModes()
        exo.addListener(object : Player.Listener {
            override fun onEvents(player: Player, events: Player.Events) { fire() }
            override fun onMediaItemTransition(mediaItem: MediaItem?, reason: Int) {
                // a metadata-only swap (refreshArtwork) also reports a transition; that's not a new play
                (mediaItem?.localConfiguration?.tag as? Track)?.let { if (it !== lastStarted || reason != Player.MEDIA_ITEM_TRANSITION_REASON_PLAYLIST_CHANGED) onTrackStarted?.invoke(it); lastStarted = it }
                refreshArtwork()
                checkQueueLow()
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
            }
            override fun onAudioSessionIdChanged(audioSessionId: Int) { applyEq() }
            override fun onPlayerError(error: androidx.media3.common.PlaybackException) {
                android.util.Log.w("FLACie", "playback error at ${exo.currentPosition}ms: ${error.errorCodeName}", error)
                val key = exo.currentMediaItemIndex to (current?.path ?: "")
                retries = if (key == retryKey) retries + 1 else 1
                retryKey = key
                // a dropped connection (screen lock, a tunnel, a dead spot) is retried after a short, growing wait, from where it stopped
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
            app.art.prefetch(t.artKey) {
                val n = (0 until exo.mediaItemCount).firstOrNull { exo.getMediaItemAt(it).localConfiguration?.tag === t } ?: return@prefetch
                if (exo.getMediaItemAt(n).mediaMetadata.artworkUri == null) exo.replaceMediaItem(n, item(t))
            }
        }
    }

    fun applyModes() {
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
        exo.addMediaItem(exo.currentMediaItemIndex + 1, item(t)); queue = queueTracks(); fire()
    }

    /** Autoplay's songs go on the end of the queue. */
    fun appendAutoplay(list: List<Track>) {
        if (list.isEmpty() || !hasQueue) return
        exo.addMediaItems(list.map(::item)); queue = queueTracks(); fire()
    }
    /** Autoplay fills the queue when at most one more song follows the current one. */
    fun checkQueueLow() { if (prefs.repeat == 0 && exo.mediaItemCount > 0 && exo.mediaItemCount - exo.currentMediaItemIndex <= 2) onQueueLow?.invoke() }
    /** Called as a song starts when at most one more is queued after it (Autoplay fills the queue). */
    var onQueueLow: (() -> Unit)? = null

    fun addToQueue(t: Track) {
        if (interceptor?.enqueue(t, false) == true) return
        if (!hasQueue) { play(listOf(t), 0, shuffle = false); return }
        exo.addMediaItem(item(t)); queue = queueTracks(); fire()
    }

    fun removeFromQueue(index: Int) {
        if (index !in 0 until exo.mediaItemCount) return
        exo.removeMediaItem(index); queue = queueTracks(); fire()
    }

    fun clearQueue() { exo.stop(); exo.clearMediaItems(); queue = emptyList(); fire() }

    fun skipTo(index: Int) { if (interceptor?.skipTo(index) == true) return; exo.seekTo(index, 0L); exo.play() }

    fun applyVolumeLimit() { exo.volume = volume }

    fun setSleepTimer(minutes: Int) {
        handler.removeCallbacks(sleepRunnable)
        sleepMinutes = minutes
        if (minutes > 0) handler.postDelayed(sleepRunnable, minutes * 60_000L)
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

    fun play(list: List<Track>, index: Int, shuffle: Boolean? = null) {
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

    fun toggle() { if (interceptor?.toggle() == true) return; if (wantsToPlay) exo.pause() else if (hasQueue) { ensurePrepared(); exo.play() } }
    fun pause() { exo.pause() }
    fun resume() { if (hasQueue) { ensurePrepared(); exo.play() } }
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
    fun next() { if (interceptor?.next() == true) return; if (exo.hasNextMediaItem()) exo.seekToNextMediaItem() else if (hasQueue) exo.seekTo(0, 0L) }
    fun prev() {
        if (interceptor?.prev() == true) return
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
        return Uri.parse("${m.groupValues[1]}/Audio/${m.groupValues[2]}/universal?UserId=$uid&DeviceId=${prefs.deviceId}&MaxStreamingBitrate=192000" +
            "&Container=aac&TranscodingContainer=ts&TranscodingProtocol=hls&AudioCodec=aac&MaxAudioChannels=2&StartTimeTicks=0")
    }

    private fun needsTranscode(t: Track) = t.filePath.substringAfterLast('.', "").lowercase() in setOf("wma", "asf", "ape", "wv", "tta")

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
        val art = t.artKey?.let { k -> app.art.file(k).takeIf { it.exists() } }
        val md = MediaMetadata.Builder().setTitle(t.title).setArtist(t.artist.ifEmpty { null }).setAlbumTitle(t.album.ifEmpty { null })
        if (art != null) md.setArtworkUri(Uri.fromFile(art))
        val uri = if (t.path.startsWith("content:") || t.path.startsWith("file:") || t.path.startsWith("http:") ||
            t.path.startsWith("https:") || t.path.startsWith("smb:")) Uri.parse(t.path) else Uri.fromFile(File(t.path))
        // formats the phone cannot decode (WMA, APE ...) are converted by Jellyfin on the way, like the data saver does
        val hls = if (dataSaver || needsTranscode(t)) hlsUri(t) else null
        return MediaItem.Builder().setMediaId(t.path).setUri(hls ?: uri).setTag(t).also { if (hls != null) it.setMimeType(androidx.media3.common.MimeTypes.APPLICATION_M3U8) }
            .setMediaMetadata(md.build()).build()
    }

    companion object {
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
