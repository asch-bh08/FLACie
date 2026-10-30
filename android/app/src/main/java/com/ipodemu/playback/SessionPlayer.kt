package com.ipodemu.playback

import android.os.Looper
import androidx.media3.common.C
import androidx.media3.common.MediaItem
import androidx.media3.common.PlaybackParameters
import androidx.media3.common.Player
import androidx.media3.common.SimpleBasePlayer
import androidx.media3.common.util.UnstableApi
import com.google.common.collect.ImmutableList
import com.google.common.util.concurrent.Futures
import com.google.common.util.concurrent.ListenableFuture
import com.ipodemu.Prefs
import com.ipodemu.library.Track

/**
 * What the system media session (Bluetooth head units, Android Auto, the lock screen and the quick-settings card) sees of the player: a mirror of [PlayerController] that
 * shows a WINDOW of the queue ([QueueWindow]: the song that plays, 20 before and 80 after, in play order) instead of all of it, and sends every command (play, pause, seek,
 * next, previous, a pick from "Up next", repeat, shuffle, speed, "play this" from a browse list) to the controller, so skip bursts, history and the rest behave as in the app.
 * With 10,000 songs in the queue, handing all of them to a Bluetooth head unit is what once ran the phone out of memory; 101 is plenty and the cover addresses point at the
 * app's content provider so other apps can read them.
 */
@UnstableApi
class SessionPlayer(private val c: PlayerController, private val prefs: Prefs) : SimpleBasePlayer(Looper.getMainLooper()) {
    /** Queue indices of the items last shown, in order (what a controller's item index means). */
    private var shown: List<Int> = emptyList()

    init { c.observe { invalidateState() } }

    override fun getState(): State {
        val exo = c.exo
        val count = exo.mediaItemCount
        val cur = c.queueIndex
        val tl = exo.currentTimeline
        val shuffle = exo.shuffleModeEnabled
        shown = if (count == 0 || cur !in 0 until count) emptyList() else QueueWindow.indices(cur, count,
            next = { i -> tl.getNextWindowIndex(i, Player.REPEAT_MODE_OFF, shuffle).let { if (it == C.INDEX_UNSET) QueueWindow.NONE else it } },
            previous = { i -> tl.getPreviousWindowIndex(i, Player.REPEAT_MODE_OFF, shuffle).let { if (it == C.INDEX_UNSET) QueueWindow.NONE else it } })
        val items = shown.map { real ->
            val mi = exo.getMediaItemAt(real)
            val t = mi.localConfiguration?.tag as? Track
            // covers as addresses other apps can read; the song's own length (the stream's is not known until it is open)
            val md = mi.mediaMetadata.buildUpon().also { b -> ArtProvider.uri(t?.artKey)?.let { b.setArtworkUri(it) } }.build()
            MediaItemData.Builder(real.toString() + "|" + (t?.path ?: mi.mediaId)).setMediaItem(mi.buildUpon().setMediaMetadata(md).build()).setMediaMetadata(md)
                .setDurationUs(((t?.durationMs ?: 0L).takeIf { it > 0 } ?: 0L) * 1000L).setIsSeekable(true).build()
        }
        val playing = exo.isPlaying
        val pos = c.positionMs
        val b = State.Builder()
            .setAvailableCommands(COMMANDS)
            .setPlayWhenReady(exo.playWhenReady, Player.PLAY_WHEN_READY_CHANGE_REASON_USER_REQUEST)
            .setPlaybackState(if (items.isEmpty()) Player.STATE_IDLE else exo.playbackState)
            .setPlayerError(exo.playerError)
            .setRepeatMode(RepeatCycle.toPlayer(prefs.repeat))
            .setShuffleModeEnabled(prefs.shuffle)
            .setPlaybackParameters(PlaybackParameters(c.speed, 1f))
            .setPlaylist(ImmutableList.copyOf(items))
        if (items.isNotEmpty()) {
            b.setCurrentMediaItemIndex(QueueWindow.positionOf(shown, cur).coerceAtLeast(0))
            b.setContentPositionMs(if (playing) PositionSupplier.getExtrapolating(pos, c.speed) else PositionSupplier.getConstant(pos))
            b.setTotalBufferedDurationMs(PositionSupplier.getConstant((exo.totalBufferedDuration).coerceAtLeast(0L)))
        }
        return b.build()
    }

    private val done: ListenableFuture<*> get() = Futures.immediateVoidFuture()

    override fun handleSetPlayWhenReady(playWhenReady: Boolean): ListenableFuture<*> {
        if (playWhenReady != c.wantsToPlay) c.toggle()
        return done
    }

    override fun handlePrepare(): ListenableFuture<*> = done
    override fun handleStop(): ListenableFuture<*> { c.exo.pause(); return done }

    override fun handleSeek(mediaItemIndex: Int, positionMs: Long, seekCommand: Int): ListenableFuture<*> {
        when (seekCommand) {
            Player.COMMAND_SEEK_TO_NEXT, Player.COMMAND_SEEK_TO_NEXT_MEDIA_ITEM -> c.next()
            Player.COMMAND_SEEK_TO_PREVIOUS, Player.COMMAND_SEEK_TO_PREVIOUS_MEDIA_ITEM -> c.prev()
            else -> {
                val real = shown.getOrNull(mediaItemIndex)
                val at = if (positionMs == C.TIME_UNSET) 0L else positionMs.coerceAtLeast(0L)
                if (real == null) return done
                if (real == c.queueIndex) c.seekTo(at) else { c.skipTo(real); if (at > 0L) c.seekTo(at) }
            }
        }
        return done
    }

    override fun handleSetRepeatMode(repeatMode: Int): ListenableFuture<*> {
        prefs.repeat = RepeatCycle.fromPlayer(repeatMode)
        c.applyModes(); return done
    }

    override fun handleSetShuffleModeEnabled(shuffleModeEnabled: Boolean): ListenableFuture<*> { prefs.shuffle = shuffleModeEnabled; c.applyModes(); return done }
    override fun handleSetPlaybackParameters(playbackParameters: PlaybackParameters): ListenableFuture<*> { c.setSpeed(playbackParameters.speed); return done }

    /** "Play this" from a browse list or by voice: the controller queues the list it came from. */
    override fun handleSetMediaItems(mediaItems: List<MediaItem>, startIndex: Int, startPositionMs: Long): ListenableFuture<*> { c.playBrowse(mediaItems); return done }

    companion object {
        private val COMMANDS: Player.Commands = Player.Commands.Builder().addAll(
            Player.COMMAND_PLAY_PAUSE, Player.COMMAND_PREPARE, Player.COMMAND_STOP, Player.COMMAND_SEEK_TO_DEFAULT_POSITION, Player.COMMAND_SEEK_IN_CURRENT_MEDIA_ITEM,
            Player.COMMAND_SEEK_TO_PREVIOUS, Player.COMMAND_SEEK_TO_PREVIOUS_MEDIA_ITEM, Player.COMMAND_SEEK_TO_NEXT, Player.COMMAND_SEEK_TO_NEXT_MEDIA_ITEM, Player.COMMAND_SEEK_TO_MEDIA_ITEM,
            Player.COMMAND_SET_REPEAT_MODE, Player.COMMAND_SET_SHUFFLE_MODE, Player.COMMAND_SET_SPEED_AND_PITCH, Player.COMMAND_GET_CURRENT_MEDIA_ITEM, Player.COMMAND_GET_TIMELINE,
            Player.COMMAND_GET_METADATA, Player.COMMAND_SET_MEDIA_ITEM, Player.COMMAND_CHANGE_MEDIA_ITEMS,
        ).build()
    }
}
