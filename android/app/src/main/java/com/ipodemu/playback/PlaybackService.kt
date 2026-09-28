package com.ipodemu.playback

import android.app.PendingIntent
import android.content.Intent
import androidx.media3.common.util.UnstableApi
import androidx.media3.session.DefaultMediaNotificationProvider
import androidx.media3.session.MediaSession
import androidx.media3.session.MediaSessionService
import com.ipodemu.App
import com.ipodemu.R
import com.ipodemu.ui.MainActivity

/**
 * Makes the app a proper Android media player: a MediaSession (lock screen, quick-settings media card, Bluetooth and
 * headset buttons, Android Auto metadata) plus a playback notification, kept alive as a foreground service while
 * music plays. Tapping the notification reopens the iPod UI.
 */
@UnstableApi
class PlaybackService : MediaSessionService() {
    private var session: MediaSession? = null

    override fun onCreate() {
        super.onCreate()
        val open = PendingIntent.getActivity(
            this, 0, Intent(this, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        session = MediaSession.Builder(this, App.of(this).player.exo).setSessionActivity(open).build()
        // Register the session so the notification manager tracks the player even with no external controller bound.
        addSession(session!!)
        setMediaNotificationProvider(DefaultMediaNotificationProvider(this).apply { setSmallIcon(R.drawable.ic_stat_flacie) })
    }

    override fun onGetSession(controllerInfo: MediaSession.ControllerInfo): MediaSession? = session

    /** Swiping the app away keeps music going; if nothing is playing there is no reason to stay alive. */
    override fun onTaskRemoved(rootIntent: Intent?) {
        val p = App.of(this).player.exo
        if (!p.playWhenReady || p.mediaItemCount == 0) stopSelf()
    }

    override fun onDestroy() {
        session?.release()
        session = null
        super.onDestroy()
    }
}
