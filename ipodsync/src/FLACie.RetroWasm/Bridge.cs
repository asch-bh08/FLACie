using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using FLACie.Core;
using FLACie.Server;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace FLACie.RetroWasm;

/// <summary>Calls into the Android app: window.flacieAndroid.call (bridge.js) over the app's FLACieAndroid interface, which answers at once.</summary>
public static class AppBridge
{
    public static string Call(IJSRuntime js, string name, string arg = "")
    {
        try { return ((IJSInProcessRuntime)js).Invoke<string>("flacieAndroid.call", name, arg) ?? ""; } catch (Exception) { return ""; }
    }
    public static string Version(IServiceProvider sp) => Call(sp.GetRequiredService<IJSRuntime>(), "version");
}

/// <summary>A song of the app's library, as the app sends it (short names: the whole library comes in one go).</summary>
public sealed record AppTrack(string p, string t, string a, string al, string aa, string g, int n, int d, long ms, int y, string? art, long add, long sz, string f);

/// <summary>The one person using the phone: a session whose library is the app's (every source it has: this phone, Jellyfin, Plex, the NAS).</summary>
public sealed class AppSession(IJSRuntime js)
{
    public UserSession Session { get; } = new("local", null, null) { Id = "android" };

    public void Load()
    {
        var json = AppBridge.Call(js, "library");
        if (json.Length == 0) return;
        var list = JsonSerializer.Deserialize<List<AppTrack>>(json) ?? [];
        Session.AddDownloaded(list.Select(ToTrack));
    }

    public static Track ToTrack(AppTrack x) =>
        new(x.p, x.t, x.a, x.al, x.aa, x.n, x.d, x.ms, x.y, x.art, x.add, TrackSource.Local, x.f.Length > 0 ? x.f : x.p, null, x.g, x.sz);
}

public sealed class AppSessions(AppSession s) : ISessionStore
{
    public UserSession For(ClaimsPrincipal p) => s.Session;
    public Live? LiveFor(UserSession s) => null;
}

/// <summary>Always signed in: whoever holds the phone (the app has its own accounts).</summary>
public sealed class AppAuth : AuthenticationStateProvider
{
    static readonly AuthenticationState State = new(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "android"), new Claim("kind", "local")], "flacie")));
    public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(State);
}

/// <summary>The app runs its own autoplay: nothing is suggested from here.</summary>
public sealed class NoAutoplay : IAutoplayPlanner
{
    public Task<List<CatalogSong>> RelatedAsync(Track seed, CancellationToken ct = default) => Task.FromResult(new List<CatalogSong>());
    public IReadOnlyList<CatalogSong> Fetching(UserSession s, IEnumerable<CatalogSong> suggested) => [];
    public void Prefetch(UserSession s, IEnumerable<CatalogSong> wanted, int max, Action onEach) { }
}

/// <summary>What a file is: as the app's player read it (codec, rate, depth) when it has, else from its name.</summary>
public sealed class AppTrackInfo(IJSRuntime js) : ITrackInfo
{
    readonly Dictionary<string, MediaInfo> cache = [];
    public bool IsCached(Track t) => cache.ContainsKey(t.Path);
    public Task<MediaInfo> GetAsync(UserSession s, Track t, bool retry = true, CancellationToken ct = default)
    {
        if (cache.TryGetValue(t.Path, out var have)) return Task.FromResult(have);
        var ext = MediaInfo.FromExtension(t.FilePath ?? t.Path);
        var info = new MediaInfo(ext, ext, 0, 0, 0, 0, t.Size, t.DurationMs, t.FilePath ?? "", "FLACie");
        try
        {
            var json = AppBridge.Call(js, "facts", t.Path);
            if (json.Length > 0)
            {
                using var d = JsonDocument.Parse(json);
                var r = d.RootElement;
                string S(string k) => r.TryGetProperty(k, out var v) ? v.GetString() ?? "" : "";
                int I(string k) => r.TryGetProperty(k, out var v) ? v.GetInt32() : 0;
                var codec = S("codec");
                info = info with { Codec = codec.Length > 0 ? codec : info.Codec, BitrateKbps = I("kbps"), SampleRateHz = I("rate"), BitDepth = I("bits"), Channels = I("ch") };
            }
        }
        catch (Exception) { }
        if (info.Codec.Length == 0) info = info with { Codec = ext };
        if (info.SampleRateHz > 0) cache[t.Path] = info;
        return Task.FromResult(info with { Origin = "library" });
    }
}

/// <summary>
/// The app's player plays the songs. <see cref="PlayerState"/> still chooses them (the iPods' menus, the desk, the scenes) and each choice goes to the
/// app, which keeps the queue. The app is the one that knows: on opening, Retro takes the app's queue and song, and four times a second it reads back
/// what the app plays (a song ended and the next began, a pause from the notification or headphones, a skip on the lock screen).
/// </summary>
public sealed class AppOutput(IJSRuntime js) : IPlayerOutput
{
    string Call(string name, string arg = "") => AppBridge.Call(js, name, arg);

    PlayerState? player;
    UserSession? session;
    bool adopted;

    public void Start(IServiceProvider sp)
    {
        player = sp.GetRequiredService<PlayerState>();
        session = sp.GetRequiredService<AppSession>().Session;
        _ = Loop();
    }

    async Task Loop()
    {
        await Task.Delay(200);
        while (true)
        {
            try { await Sync(); } catch (Exception e) { Console.WriteLine("FLACie retro sync: " + e.Message); }
            await Task.Delay(250);
        }
    }

    sealed record AppState(string? p, bool pl, long pos, long dur, string[]? q, int i);

    async Task Sync()
    {
        if (player is null || session is null) return;
        var json = Call("state", adopted ? "" : "queue");
        if (json.Length == 0) return;
        var s = JsonSerializer.Deserialize<AppState>(json);
        if (s is null) return;
        if (!adopted)
        {
            adopted = true;
            // the song the app plays (or left paused) and its queue: Retro carries on from there
            if (s.q is { Length: > 0 } q)
            {
                var tracks = q.Select(session.FindByPath).ToList();
                var idx = Math.Clamp(s.i, 0, q.Length - 1);
                var list = tracks.OfType<Track>().ToList();
                var at = tracks.Take(idx).Count(t => t is not null);
                if (list.Count > 0) await player.PlayFromAsync(list, Math.Clamp(at, 0, list.Count - 1), s.pos, !s.pl);
            }
        }
        else if (s.p is { } path && player.Current?.Path != path)
        {
            // the app moved on (a song ended, or Next on the notification): follow it
            var idx = player.Queue.FindIndex(t => t.Path == path);
            if (idx >= 0) await player.SkipTo(idx);
            else { adopted = false; return; }
        }
        if (player.Current is null) return;
        player.OnTime(s.pos / 1000.0, s.dur / 1000.0);
        if (player.Playing != s.pl) player.OnState(s.pl);
    }

    public Task LoadAsync(DotNetObjectReference<PlayerState> p, Track track, string url, string title, string artist, string album, string? art, bool autoplay, double durationSecs, double startAt, string? nextUrl)
    {
        var pl = p.Value;
        // the queue in the order it will play, and the chosen song's place in it
        var seq = pl.PlayOrder.Count == pl.Queue.Count ? pl.PlayOrder.Select(i => pl.Queue[i]).ToList() : pl.Queue;
        var at = seq.FindIndex(t => t.Path == track.Path);
        Call("load", JsonSerializer.Serialize(new { q = seq.Select(t => t.Path), i = Math.Max(0, at), play = autoplay, ms = (long)(startAt * 1000) }));
        return Task.CompletedTask;
    }

    public Task ToggleAsync() { Call("toggle"); return Task.CompletedTask; }
    public Task PlayAsync() { Call("play"); return Task.CompletedTask; }
    public Task PauseAsync() { Call("pause"); return Task.CompletedTask; }
    public Task SeekAsync(double seconds) { Call("seek", ((long)(seconds * 1000)).ToString(CultureInfo.InvariantCulture)); return Task.CompletedTask; }
    public Task<bool> ReadyAsync() => Task.FromResult(Call("ready") == "1");
    public Task<double> VolumeAsync(double? volume)
    {
        var r = Call("volume", volume is { } v ? Math.Clamp(v, 0, 1).ToString(CultureInfo.InvariantCulture) : "");
        return Task.FromResult(double.TryParse(r, CultureInfo.InvariantCulture, out var x) ? x : 1);
    }
    public Task RateAsync(double speed) { Call("speed", speed.ToString(CultureInfo.InvariantCulture)); return Task.CompletedTask; }
    // the app evens out volume, fades between songs and runs its sleep timer itself (its own settings)
    public Task LoudAsync(double factor) => Task.CompletedTask;
    public Task CrossfadeAsync(int seconds, bool fadeIn, bool fadeOut) => Task.CompletedTask;
    public Task FadeAsync(double gain) => Task.CompletedTask;
    public Task StopAsync() { Call("pause"); return Task.CompletedTask; }
}
