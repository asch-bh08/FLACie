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
import androidx.media3.exoplayer.ExoPlayer
import com.ipodemu.App
import com.ipodemu.Prefs
import com.ipodemu.library.Track
import java.io.File
import kotlin.math.abs
import kotlin.math.ln

class PlayerController(private val ctx: Context, private val prefs: Prefs) {
    val exo: ExoPlayer = ExoPlayer.Builder(ctx)
        .setAudioAttributes(
            AudioAttributes.Builder().setUsage(C.USAGE_MEDIA).setContentType(C.AUDIO_CONTENT_TYPE_MUSIC).build(), true
        )
        .setHandleAudioBecomingNoisy(true)
        .build()

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
                (mediaItem?.localConfiguration?.tag as? Track)?.let { onTrackStarted?.invoke(it) }
            }
            override fun onAudioSessionIdChanged(audioSessionId: Int) { applyEq() }
        })
        applyEq()
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

    fun addNext(t: Track) {
        if (!hasQueue) { play(listOf(t), 0, shuffle = false); return }
        exo.addMediaItem(exo.currentMediaItemIndex + 1, item(t)); queue = queueTracks(); fire()
    }

    fun addToQueue(t: Track) {
        if (!hasQueue) { play(listOf(t), 0, shuffle = false); return }
        exo.addMediaItem(item(t)); queue = queueTracks(); fire()
    }

    fun removeFromQueue(index: Int) {
        if (index !in 0 until exo.mediaItemCount) return
        exo.removeMediaItem(index); queue = queueTracks(); fire()
    }

    fun clearQueue() { exo.stop(); exo.clearMediaItems(); queue = emptyList(); fire() }

    fun skipTo(index: Int) { exo.seekTo(index, 0L); exo.play() }

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
        if (list.isEmpty()) return
        queue = list
        if (shuffle != null) { prefs.shuffle = shuffle; applyModes() }
        exo.setMediaItems(list.map(::item), index.coerceIn(0, list.lastIndex), 0L)
        exo.prepare()
        exo.play()
        ctx.startService(Intent(ctx, PlaybackService::class.java))
    }

    /** Shuffle Songs: random start, shuffle mode forced on. */
    fun shuffleAll(list: List<Track>) = play(list, if (list.isEmpty()) 0 else list.indices.random(), true)

    fun toggle() { if (exo.isPlaying) exo.pause() else if (hasQueue) exo.play() }
    fun next() { if (exo.hasNextMediaItem()) exo.seekToNextMediaItem() else if (hasQueue) exo.seekTo(0, 0L) }
    fun prev() {
        if (exo.currentPosition > 3000 || !exo.hasPreviousMediaItem()) exo.seekTo(0) else exo.seekToPreviousMediaItem()
    }
    fun seekBy(ms: Long) = exo.seekTo((exo.currentPosition + ms).coerceIn(0, (durationMs - 500).coerceAtLeast(0)))
    fun seekTo(ms: Long) = exo.seekTo(ms.coerceIn(0, durationMs.coerceAtLeast(0)))

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

    private fun item(t: Track): MediaItem {
        val app = App.of(ctx)
        // artwork by file URI (decoded off the main thread by Media3), not embedded bytes: reading and attaching ~600 covers made
        // every Play tap slow and made each track change hitch on the main thread
        val art = t.artKey?.let { k -> app.art.file(k).takeIf { it.exists() } }
        val md = MediaMetadata.Builder().setTitle(t.title).setArtist(t.artist.ifEmpty { null }).setAlbumTitle(t.album.ifEmpty { null })
        if (art != null) md.setArtworkUri(Uri.fromFile(art))
        val uri = if (t.path.startsWith("content:") || t.path.startsWith("file:") || t.path.startsWith("http:") || t.path.startsWith("https:"))
            Uri.parse(t.path) else Uri.fromFile(File(t.path))
        return MediaItem.Builder().setMediaId(t.path).setUri(uri).setTag(t)
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
