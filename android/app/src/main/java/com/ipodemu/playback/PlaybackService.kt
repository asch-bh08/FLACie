package com.ipodemu.playback

import android.app.PendingIntent
import android.content.Intent
import android.os.Bundle
import androidx.media3.common.util.UnstableApi
import androidx.media3.session.CommandButton
import androidx.media3.session.DefaultMediaNotificationProvider
import androidx.media3.session.MediaSession
import androidx.media3.session.MediaSessionService
import androidx.media3.session.SessionCommand
import androidx.media3.session.SessionResult
import com.google.common.collect.ImmutableList
import com.google.common.util.concurrent.Futures
import com.google.common.util.concurrent.ListenableFuture
import com.ipodemu.App
import com.ipodemu.R
import com.ipodemu.ui.MainActivity

/**
 * Makes the app a proper Android media player: a MediaSession (lock screen, quick-settings media card, Bluetooth and
 * headset buttons, Android Auto metadata) plus a playback notification, kept alive as a foreground service while
 * music plays. Tapping the notification reopens the iPod UI. Two custom buttons ride along: favourite (the heart follows the song) and speed (taps cycle 1x, 1.25x ... 3x, 1x).
 */
@UnstableApi
class PlaybackService : MediaSessionService() {
    private var session: MediaSession? = null
    private var unobserve: (() -> Unit)? = null
    private var lastLayoutKey: String? = null

    private val favCommand = SessionCommand(CMD_FAVORITE, Bundle.EMPTY)
    private val speedCommand = SessionCommand(CMD_SPEED, Bundle.EMPTY)

    override fun onCreate() {
        super.onCreate()
        val open = PendingIntent.getActivity(
            this, 0, Intent(this, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        val app = App.of(this)
        session = MediaSession.Builder(this, app.player.sessionPlayer).setSessionActivity(open).setCallback(object : MediaSession.Callback {
            override fun onConnect(session: MediaSession, controller: MediaSession.ControllerInfo): MediaSession.ConnectionResult {
                val commands = MediaSession.ConnectionResult.DEFAULT_SESSION_COMMANDS.buildUpon().add(favCommand).add(speedCommand).build()
                return MediaSession.ConnectionResult.AcceptedResultBuilder(session).setAvailableSessionCommands(commands).setCustomLayout(layout()).build()
            }

            override fun onCustomCommand(session: MediaSession, controller: MediaSession.ControllerInfo, customCommand: SessionCommand, args: Bundle): ListenableFuture<SessionResult> {
                // the player's own thread is the main thread, where this runs
                when (customCommand.customAction) {
                    CMD_FAVORITE -> app.player.current?.let { app.userData.toggleFavorite(it.path) }
                    CMD_SPEED -> app.player.setSpeed(SpeedPolicy.cycle(app.player.speed))
                }
                refreshLayout(force = true)
                return Futures.immediateFuture(SessionResult(SessionResult.RESULT_SUCCESS))
            }
        }).build()
        // Register the session so the notification manager tracks the player even with no external controller bound.
        addSession(session!!)
        setMediaNotificationProvider(DefaultMediaNotificationProvider(this).apply { setSmallIcon(R.drawable.ic_stat_flacie) })
        // the heart and the speed label follow the song and the speed
        unobserve = app.player.observe { refreshLayout(force = false) }
        refreshLayout(force = true)
    }

    private fun layout(): ImmutableList<CommandButton> {
        val app = App.of(this)
        val fav = isFavorite(app)
        val speed = app.player.speed
        return ImmutableList.of(
            CommandButton.Builder().setDisplayName(if (fav) "Remove from favourites" else "Favourite").setIconResId(if (fav) R.drawable.ic_fav_on else R.drawable.ic_fav)
                .setSessionCommand(favCommand).build(),
            CommandButton.Builder().setDisplayName("Speed " + SpeedPolicy.label(speed)).setIconResId(R.drawable.ic_speed).setSessionCommand(speedCommand).build(),
        )
    }

    /** The service can start (to hold the restored queue) before the favourites are loaded: until then, not a favourite. */
    private fun isFavorite(app: App): Boolean = try { app.player.current?.let { app.userData.isFavorite(it.path) } == true } catch (_: UninitializedPropertyAccessException) { false }

    /** Sets the custom buttons again when what they show (the heart, the speed) has changed. */
    private fun refreshLayout(force: Boolean) {
        val s = session ?: return
        val app = App.of(this)
        val key = (app.player.current?.path ?: "") + "|" + isFavorite(app) + "|" + app.player.speed
        if (!force && key == lastLayoutKey) return
        lastLayoutKey = key
        s.setCustomLayout(layout())
    }

    override fun onGetSession(controllerInfo: MediaSession.ControllerInfo): MediaSession? = session

    /** Swiping the app away from recents stops the music, clears the notification and ends the service. */
    override fun onTaskRemoved(rootIntent: Intent?) {
        App.of(this).player.persistNow()   // the swipe-away stops the music, but the queue comes back (paused) next time
        val p = App.of(this).player.exo
        p.pause()
        p.stop()
        p.clearMediaItems()
        stopSelf()
    }

    override fun onDestroy() {
        unobserve?.invoke(); unobserve = null
        session?.release()
        session = null
        super.onDestroy()
    }

    companion object {
        const val CMD_FAVORITE = "com.ipodemu.FAVORITE"
        const val CMD_SPEED = "com.ipodemu.SPEED"
    }
}
