using Android.Runtime;
using FLACie.Core;
using FLACie.Server;
using Microsoft.JSInterop;

namespace FLACie.Hybrid.Platforms.Android;

/// <summary>
/// The phone's player: <see cref="PlayerState"/> chooses the songs, Media3's ExoPlayer (in the app's media service, Platforms/Android/Java) plays
/// them straight from Jellyfin. Music goes on in the background and with the screen off, with the media notification, lock-screen and headset
/// controls. About thirty times a second this reads the player back (position, playing, a song ended, next or previous pressed outside the app)
/// and hands the page a frame of the spectrum computed from the decoded sound, in place of the page's own analyser (flacie.vizFrame).
/// </summary>
public sealed class NativeOutput : IPlayerOutput, IDisposable
{
    readonly HybridAccount account;
    readonly JellyfinClient jf;
    readonly IJSRuntime js;
    readonly Timer timer;
    PlayerState? owner;
    double volume = 1, level = 1, fade = 1;
    bool lastPlaying; int lastEnded = -1; long lastTimeTick; bool busy; bool ready;
    long frameAt;

    public NativeOutput(HybridAccount account, JellyfinClient jf, IJSRuntime js)
    {
        this.account = account; this.jf = jf; this.js = js;
        Jni.Start();
        timer = new Timer(_ => Tick(), null, 500, 33);
    }

    public Task LoadAsync(DotNetObjectReference<PlayerState> player, Track track, string url, string title, string artist, string album, string? art, bool autoplay, double durationSecs, double startAt, string? nextUrl)
    {
        owner = player.Value;
        if (account.Session is not { Jellyfin: { } a }) return Task.CompletedTask;
        var (stream, auth) = StreamFor(a, track);
        Jni.Load(stream, auth, title, artist, album, ArtFor(a, track) ?? "", autoplay, (long)(startAt * 1000));
        ready = false;
        return Task.CompletedTask;
    }

    /// <summary>The song's address on Jellyfin and the header that signs it: the file as it is, or an MP3 Jellyfin makes when the phone can't play the file.</summary>
    (string Url, string Auth) StreamFor(JellyfinAccount a, Track t)
    {
        var id = t.JellyfinId ?? "";
        // ExoPlayer plays Apple Lossless (which browsers don't); what neither can play, Jellyfin converts
        var ext = MediaInfo.FromExtension(t.FilePath ?? "");
        var transcode = MediaInfo.NotPlayableInBrowsers.Contains(ext) && ext is not ("alac" or "m4a" or "mp4");
        using var req = jf.Authorized(a, transcode ? $"{a.Server}/Audio/{id}/stream.mp3?AudioCodec=mp3&AudioBitRate=320000&MaxAudioChannels=2" : jf.StreamUrl(a, id));
        var auth = req.Headers.TryGetValues("Authorization", out var v) ? string.Join(",", v) : "";
        return (req.RequestUri!.ToString(), auth);
    }

    /// <summary>The cover for the notification and the lock screen (the token goes in the address: Media3 fetches it itself).</summary>
    string? ArtFor(JellyfinAccount a, Track t)
    {
        if (t.ArtKey is not { } key) return null;
        key = CoverOverrides.Map(key) ?? key;
        if (key.StartsWith("jf", StringComparison.Ordinal))
        {
            var id = key[2..]; string? tag = null;
            if (id.IndexOf('~') is var tx and > 0) { tag = id[(tx + 1)..]; id = id[..tx]; }
            using var req = jf.Authorized(a, jf.ImageUrl(a, id, 512, tag));
            return req.RequestUri + "&api_key=" + Uri.EscapeDataString(a.Token);
        }
        return Art.ExternalUrl(key);
    }

    public Task ToggleAsync() { Jni.Call("toggle"); return Task.CompletedTask; }
    public Task PlayAsync() { Jni.Call("play"); return Task.CompletedTask; }
    public Task PauseAsync() { Jni.Call("pause"); return Task.CompletedTask; }
    public Task SeekAsync(double seconds) { Jni.Seek((long)(seconds * 1000)); return Task.CompletedTask; }
    public Task<bool> ReadyAsync() => Task.FromResult(ready);
    public Task<double> VolumeAsync(double? v)
    {
        if (v is { } x) { volume = Math.Clamp(x, 0, 1); Preferences.Default.Set("flacie.volume", volume); SendVolume(); }
        else volume = Preferences.Default.Get("flacie.volume", 1.0);
        return Task.FromResult(volume);
    }
    public Task RateAsync(double speed) { Jni.Speed((float)speed); return Task.CompletedTask; }
    public Task LoudAsync(double factor) { level = factor; SendVolume(); return Task.CompletedTask; }
    public Task CrossfadeAsync(int seconds, bool fadeIn, bool fadeOut) => Task.CompletedTask;   // songs follow each other straight on here
    public Task FadeAsync(double gain) { fade = gain; SendVolume(); return Task.CompletedTask; }
    public Task StopAsync() { Jni.Call("stop"); return Task.CompletedTask; }
    // no DJ here: the phone app has no server to ask (the Android app has its own DJ)
    public Task DjPreloadAsync(string url) => Task.CompletedTask;
    public Task DjPlayAsync(string url) => Task.CompletedTask;
    public Task DjSayAsync(string text) => Task.CompletedTask;
    public Task DjStopAsync() => Task.CompletedTask;
    void SendVolume() => Jni.Volume((float)volume, (float)level, (float)fade);

    void Tick()
    {
        if (busy) return;
        busy = true;
        try
        {
            var p = Jni.Poll();
            if (p is null) return;
            var f = p.Split('|');
            if (f.Length < 7) return;
            bool playing = f[0] == "1", buffering = f[1] == "1";
            double pos = long.Parse(f[2]) / 1000.0, dur = long.Parse(f[3]) / 1000.0;
            var ended = int.Parse(f[4]);
            var cmds = f[5].Length > 0 ? f[5].Split(',') : [];
            if (!buffering && dur > 0) ready = true;
            var now = Environment.TickCount64;
            var o = owner;
            if (o is not null)
            {
                var stateChanged = playing != lastPlaying; lastPlaying = playing;
                var endedNow = lastEnded >= 0 && ended != lastEnded; lastEnded = ended;
                var timeDue = playing && now - lastTimeTick >= 250;
                if (timeDue) lastTimeTick = now;
                if (stateChanged || endedNow || timeDue || cmds.Length > 0)
                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        try
                        {
                            if (timeDue || stateChanged) o.OnTime(pos, dur);
                            if (stateChanged) o.OnState(playing);
                            if (endedNow) await o.OnEnded();
                            foreach (var c in cmds) await (c == "next" ? o.OnNext() : c == "prev" ? o.OnPrev() : Task.CompletedTask);
                        }
                        catch (Exception e) { global::Android.Util.Log.Warn("FLACie", "player: " + e.Message); }
                    });
            }
            else lastEnded = ended;
            // the visualisers: only while the app is on screen
            if (MainActivity.Visible && (playing || now - frameAt < 1500))
            {
                if (playing) frameAt = now;
                var bins = Jni.Frame();
                if (bins is not null)
                {
                    var rate = Jni.Rate();
                    MainThread.BeginInvokeOnMainThread(() => { _ = js.InvokeVoidAsync("flacie.nativeFrame", bins, rate, playing, pos, dur).AsTask().ContinueWith(_ => { }); });
                }
            }
        }
        catch (Exception e) { global::Android.Util.Log.Warn("FLACie", "poll: " + e.Message); }
        finally { busy = false; }
    }

    public void Dispose() => timer.Dispose();

    /// <summary>The Java side (dev.ashley.flacie.player.Bridge and Spectrum), called by JNI: plain static methods, so no binding is needed.</summary>
    static class Jni
    {
        static IntPtr bridge, spectrum;
        static readonly Dictionary<string, IntPtr> ids = [];

        public static void Start()
        {
            if (bridge != IntPtr.Zero) return;
            // (JNIEnv.FindClass hands back a global reference, kept for the app's life)
            bridge = JNIEnv.FindClass("dev/ashley/flacie/player/Bridge");
            spectrum = JNIEnv.FindClass("dev/ashley/flacie/player/Spectrum");
            CallStatic(bridge, "start", "(Landroid/content/Context;)V", new JValue(global::Android.App.Application.Context));
        }

        static IntPtr Id(IntPtr cls, string name, string sig)
        {
            lock (ids)
            {
                var k = name + sig;
                if (!ids.TryGetValue(k, out var id)) ids[k] = id = JNIEnv.GetStaticMethodID(cls, name, sig);
                return id;
            }
        }

        static void CallStatic(IntPtr cls, string name, string sig, params JValue[] args) => JNIEnv.CallStaticVoidMethod(cls, Id(cls, name, sig), args);

        public static void Call(string name) => CallStatic(bridge, name, "()V");
        public static void Seek(long ms) => CallStatic(bridge, "seek", "(J)V", new JValue(ms));
        public static void Speed(float s) => CallStatic(bridge, "speed", "(F)V", new JValue(s));
        public static void Volume(float v, float l, float f) => CallStatic(bridge, "volume", "(FFF)V", new JValue(v), new JValue(l), new JValue(f));

        public static void Load(string url, string auth, string title, string artist, string album, string art, bool play, long startMs)
        {
            using var a = new Java.Lang.String(url); using var b = new Java.Lang.String(auth); using var c = new Java.Lang.String(title ?? "");
            using var d = new Java.Lang.String(artist ?? ""); using var e = new Java.Lang.String(album ?? ""); using var f = new Java.Lang.String(art);
            CallStatic(bridge, "load", "(Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;ZJ)V",
                new JValue(a), new JValue(b), new JValue(c), new JValue(d), new JValue(e), new JValue(f), new JValue(play), new JValue(startMs));
        }

        public static string? Poll()
        {
            if (bridge == IntPtr.Zero) return null;
            var r = JNIEnv.CallStaticObjectMethod(bridge, Id(bridge, "poll", "()Ljava/lang/String;"));
            return JNIEnv.GetString(r, JniHandleOwnership.TransferLocalRef);
        }

        public static byte[]? Frame()
        {
            var r = JNIEnv.CallStaticObjectMethod(spectrum, Id(spectrum, "frame", "()[B"));
            if (r == IntPtr.Zero) return null;
            try { return (byte[]?)JNIEnv.GetArray(r, JniHandleOwnership.DoNotTransfer, typeof(byte)); }
            finally { JNIEnv.DeleteLocalRef(r); }
        }

        public static int Rate() => JNIEnv.CallStaticIntMethod(spectrum, Id(spectrum, "rate", "()I"));
    }
}
