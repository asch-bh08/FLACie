package dev.ashley.flacie.player;

import android.app.PendingIntent;
import android.content.Intent;
import androidx.annotation.Nullable;
import androidx.media3.common.AudioAttributes;
import androidx.media3.common.C;
import androidx.media3.common.ForwardingPlayer;
import androidx.media3.common.Player;
import androidx.media3.exoplayer.DefaultRenderersFactory;
import androidx.media3.exoplayer.ExoPlayer;
import androidx.media3.common.audio.AudioProcessor;
import androidx.media3.exoplayer.audio.AudioSink;
import androidx.media3.exoplayer.audio.DefaultAudioSink;
import androidx.media3.exoplayer.audio.TeeAudioProcessor;
import androidx.media3.exoplayer.source.DefaultMediaSourceFactory;
import androidx.media3.session.MediaSession;
import androidx.media3.session.MediaSessionService;

/**
 * FLACie's player on Android: Media3's ExoPlayer in a MediaSessionService, so the music goes on with the screen off or the app in the background,
 * with a media notification, lock-screen controls and headset buttons. The app's page chooses what plays (the queue lives in PlayerState, in .NET);
 * it reaches this player through {@link Bridge}. A TeeAudioProcessor hands the decoded sound to {@link Spectrum} for the visualisers.
 */
public final class PlaybackService extends MediaSessionService {
    static volatile PlaybackService instance;
    ExoPlayer player;
    MediaSession session;

    @Override
    public void onCreate() {
        super.onCreate();
        DefaultRenderersFactory renderers = new DefaultRenderersFactory(this) {
            @Override
            protected AudioSink buildAudioSink(android.content.Context context, boolean enableFloatOutput, boolean enableAudioTrackPlaybackParams) {
                return new DefaultAudioSink.Builder(context)
                        .setAudioProcessors(new AudioProcessor[] { new TeeAudioProcessor(Spectrum.SINK) })
                        .setEnableAudioTrackPlaybackParams(enableAudioTrackPlaybackParams)
                        .build();
            }
        };
        player = new ExoPlayer.Builder(this, renderers)
                .setMediaSourceFactory(new DefaultMediaSourceFactory(Bridge.http()))
                .setAudioAttributes(new AudioAttributes.Builder().setUsage(C.USAGE_MEDIA).setContentType(C.AUDIO_CONTENT_TYPE_MUSIC).build(), true)
                .setHandleAudioBecomingNoisy(true)
                .setWakeMode(C.WAKE_MODE_NETWORK)
                .build();
        player.addListener(Bridge.LISTENER);
        Intent launch = getPackageManager().getLaunchIntentForPackage(getPackageName());
        MediaSession.Builder b = new MediaSession.Builder(this, new Controls(player));
        if (launch != null) b.setSessionActivity(PendingIntent.getActivity(this, 0, launch, PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT));
        session = b.build();
        instance = this;
        Bridge.attached();
    }

    @Nullable
    @Override
    public MediaSession onGetSession(MediaSession.ControllerInfo controllerInfo) { return session; }

    @Override
    public void onTaskRemoved(@Nullable Intent rootIntent) {
        // swiped away from the recent apps: keep playing if music is on, otherwise go
        if (player == null || !player.getPlayWhenReady() || player.getMediaItemCount() == 0) stopSelf();
    }

    @Override
    public void onDestroy() {
        instance = null;
        if (session != null) { session.release(); session = null; }
        if (player != null) { player.removeListener(Bridge.LISTENER); player.release(); player = null; }
        super.onDestroy();
    }

    /** The player as the notification, the lock screen and headsets see it: next and previous are always offered, and go to the app's queue. */
    static final class Controls extends ForwardingPlayer {
        Controls(Player p) { super(p); }
        static final Player.Commands EXTRA = new Player.Commands.Builder()
                .addAll(Player.COMMAND_SEEK_TO_NEXT, Player.COMMAND_SEEK_TO_PREVIOUS, Player.COMMAND_SEEK_TO_NEXT_MEDIA_ITEM, Player.COMMAND_SEEK_TO_PREVIOUS_MEDIA_ITEM).build();
        @Override public Player.Commands getAvailableCommands() { return super.getAvailableCommands().buildUpon().addAll(EXTRA).build(); }
        @Override public boolean isCommandAvailable(int command) { return EXTRA.contains(command) || super.isCommandAvailable(command); }
        @Override public void seekToNext() { Bridge.command("next"); }
        @Override public void seekToNextMediaItem() { Bridge.command("next"); }
        @Override public void seekToPrevious() { Bridge.command("prev"); }
        @Override public void seekToPreviousMediaItem() { Bridge.command("prev"); }
    }
}
