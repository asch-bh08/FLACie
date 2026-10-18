package dev.ashley.flacie.player;

import android.content.ComponentName;
import android.content.Context;
import android.net.Uri;
import android.os.Handler;
import android.os.Looper;
import androidx.media3.common.MediaItem;
import androidx.media3.common.MediaMetadata;
import androidx.media3.common.PlaybackException;
import androidx.media3.common.PlaybackParameters;
import androidx.media3.common.Player;
import androidx.media3.datasource.DefaultHttpDataSource;
import androidx.media3.session.MediaController;
import androidx.media3.session.SessionToken;
import com.google.common.util.concurrent.ListenableFuture;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * What .NET calls (by JNI, all static and with plain types): load a song, play, pause, seek, volume, speed, stop; and what it reads back about
 * thirty times a second: {@link #poll()} (playing, position, length, songs ended, buttons pressed on the notification or a headset) and
 * {@link Spectrum#frame()} for the visualisers. Calls may come from any thread; the player is only touched on the main thread.
 */
public final class Bridge {
    private Bridge() { }

    static final Handler MAIN = new Handler(Looper.getMainLooper());
    private static final DefaultHttpDataSource.Factory HTTP = new DefaultHttpDataSource.Factory()
            .setUserAgent("FLACie Android").setAllowCrossProtocolRedirects(true).setConnectTimeoutMs(15000).setReadTimeoutMs(30000);
    private static ListenableFuture<MediaController> controller;
    private static Runnable pending;   // what was asked for before the service was up
    private static float volume = 1f, level = 1f, fade = 1f;

    // what poll() reports; written on the main thread
    private static volatile boolean playing, buffering;
    private static volatile long positionMs, durationMs;
    private static volatile int ended;
    private static volatile String error = "";
    private static final List<String> commands = new ArrayList<>();

    static DefaultHttpDataSource.Factory http() { return HTTP; }

    static final Player.Listener LISTENER = new Player.Listener() {
        @Override public void onIsPlayingChanged(boolean isPlaying) { refresh(); }
        @Override public void onPlayWhenReadyChanged(boolean playWhenReady, int reason) { refresh(); }
        @Override public void onPlaybackStateChanged(int state) {
            if (state == Player.STATE_ENDED) ended++;
            refresh();
        }
        @Override public void onPlayerError(PlaybackException e) { error = e.getErrorCodeName() + ": " + e.getMessage(); playing = false; }
    };

    /** Starts the media service (by connecting to its session) when it is not running. */
    public static void start(Context context) {
        final Context app = context.getApplicationContext();
        MAIN.post(new Runnable() { @Override public void run() {
            if (controller != null) return;
            controller = new MediaController.Builder(app, new SessionToken(app, new ComponentName(app, PlaybackService.class))).buildAsync();
            MAIN.postDelayed(TICK, 100);
        } });
    }

    static void attached() {
        if (pending != null) { Runnable r = pending; pending = null; r.run(); }
    }

    private static final Runnable TICK = new Runnable() {
        @Override public void run() { refresh(); MAIN.postDelayed(this, 100); }
    };

    private static void refresh() {
        PlaybackService s = PlaybackService.instance;
        if (s == null || s.player == null) return;
        Player p = s.player;
        int state = p.getPlaybackState();
        playing = p.getPlayWhenReady() && state != Player.STATE_ENDED && state != Player.STATE_IDLE;
        buffering = state == Player.STATE_BUFFERING;
        positionMs = Math.max(0, p.getCurrentPosition());
        long d = p.getDuration();
        durationMs = d > 0 ? d : 0;
        Spectrum.playing = playing;
    }

    private static void run(Runnable r) {
        MAIN.post(new Runnable() { @Override public void run() {
            if (PlaybackService.instance == null || PlaybackService.instance.player == null) {
                // keep the newest of each: a load replaces an earlier load, anything else follows it
                Runnable before = pending;
                pending = before == null ? r : new Runnable() { @Override public void run() { before.run(); r.run(); } };
            }
            else r.run();
        } });
    }

    private static Player player() { return PlaybackService.instance.player; }

    /** Opens a song. auth is the Authorization header for the server ("" for none); art is the cover's address for the notification and the lock screen. */
    public static void load(String url, String auth, String title, String artist, String album, String art, boolean play, long startMs) {
        run(new Runnable() { @Override public void run() {
            Map<String, String> h = new HashMap<>();
            if (auth != null && auth.length() > 0) h.put("Authorization", auth);
            HTTP.setDefaultRequestProperties(h);
            MediaMetadata.Builder md = new MediaMetadata.Builder().setTitle(title).setArtist(artist).setAlbumTitle(album);
            if (art != null && art.length() > 0) md.setArtworkUri(Uri.parse(art));
            MediaItem item = new MediaItem.Builder().setUri(url).setMediaMetadata(md.build()).build();
            Player p = player();
            error = "";
            Spectrum.reset();
            p.setMediaItem(item, Math.max(0, startMs));
            p.prepare();
            p.setPlayWhenReady(play);
            refresh();
        } });
    }

    public static void play() { run(new Runnable() { @Override public void run() { Player p = player(); if (p.getPlaybackState() == Player.STATE_IDLE) p.prepare(); if (p.getPlaybackState() == Player.STATE_ENDED) p.seekTo(0); p.play(); refresh(); } }); }
    public static void pause() { run(new Runnable() { @Override public void run() { player().pause(); refresh(); } }); }
    public static void toggle() { run(new Runnable() { @Override public void run() { Player p = player(); if (p.getPlayWhenReady()) p.pause(); else { if (p.getPlaybackState() == Player.STATE_IDLE) p.prepare(); p.play(); } refresh(); } }); }
    public static void seek(long ms) { run(new Runnable() { @Override public void run() { player().seekTo(Math.max(0, ms)); refresh(); } }); }
    public static void stop() { run(new Runnable() { @Override public void run() { Player p = player(); p.stop(); p.clearMediaItems(); refresh(); } }); }
    public static void speed(float s) { run(new Runnable() { @Override public void run() { player().setPlaybackParameters(new PlaybackParameters(s)); } }); }

    /** The listener's volume, level volume and the sleep fade multiply into the player's own volume. */
    public static void volume(float v, float levelGain, float fadeGain) {
        volume = v; level = levelGain; fade = fadeGain;
        run(new Runnable() { @Override public void run() { player().setVolume(Math.max(0f, Math.min(1f, volume * level * fade))); } });
    }

    static void command(String c) { synchronized (commands) { commands.add(c); } }

    /** "playing|buffering|positionMs|durationMs|ended|commands|error": commands are comma-separated (next, prev) and are handed over once. */
    public static String poll() {
        String cmds;
        synchronized (commands) { cmds = String.join(",", commands); commands.clear(); }
        return (playing ? "1" : "0") + "|" + (buffering ? "1" : "0") + "|" + positionMs + "|" + durationMs + "|" + ended + "|" + cmds + "|" + error.replace('|', '/');
    }
}
