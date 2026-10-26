using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FLACie.Core;

namespace FLACie.Server.Dj;

/// <summary>Where a person's DJ is when it goes through the family's FLACie Web: its address, whether it answered last time (null: not asked yet), why not
/// ("couldn't be reached", ...), and whether it can make clips.</summary>
public sealed record DjHome(string Url, bool? Reachable, string Problem, bool VoiceReady);

/// <summary>
/// The DJ of the Windows app. Its server is a copy on this PC with its own data folder, so on its own it would keep each person's DJ settings (and need a Cloudflare
/// setup) apart from the family's always-on FLACie Web, which the phone and the car use. When this is that desktop copy and the account's profile names the home
/// server (<c>services.flacieweb.url</c>, written by the phone and by FLACie Web itself), the DJ for that person goes through it the way the phone does: the
/// settings and "what before the next song" over its /api/dj doors with the person's own Jellyfin sign-in (X-Emby-Token and X-Jellyfin-Server), and each clip
/// through <c>/api/dj/clip/r/&lt;id&gt;.mp3</c> here, which fetches it from there, so the page needs no sign-in of its own.
/// A page is never kept waiting for the network: what the home server said last is kept in this PC's own settings (a restart starts from it) and read again in the
/// background, at most every <see cref="RefreshEvery"/>. While the home server can't be reached, this PC's own DJ (<see cref="DjService"/>) stands in, settings
/// and all. The token only ever goes into those two headers: it is never logged and never stored here.
/// </summary>
public sealed partial class HomeDj : IDjService
{
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(30);
    static readonly TimeSpan SettingsWait = TimeSpan.FromSeconds(6), NextWait = TimeSpan.FromSeconds(10), ClipWait = TimeSpan.FromSeconds(15);
    const int MaxAnswer = 64 * 1024, MaxClip = 8 * 1024 * 1024, KeptClips = 16;

    readonly DjService local; readonly IHttpClientFactory hf; readonly ILogger<HomeDj> log; readonly Func<DateTime> clock;
    readonly ConcurrentDictionary<string, Home> homes = new();
    readonly ConcurrentDictionary<string, (byte[] Mp3, DateTime At)> clips = new();
    readonly SingleFlight<byte[]?> clipFlights = new();

    /// <summary>[desktop]: this server is the Windows app's copy (FLACIE_PARENT_PID set, <see cref="ChartsService.InDesktopShell"/>); anywhere else this is just
    /// this server's own DJ.</summary>
    public HomeDj(DjService local, IHttpClientFactory hf, ILogger<HomeDj> log, bool desktop, Func<DateTime>? clock = null)
    {
        this.local = local; this.hf = hf; this.log = log; Desktop = desktop; this.clock = clock ?? (() => DateTime.UtcNow);
    }

    public bool Desktop { get; }

    /// <summary>Raised with the person's session id when what their home server says, or whether it can be reached, has changed (the settings section draws again).</summary>
    public event Action<string>? Changed;

    sealed class Home(string url)
    {
        public readonly string Url = url;
        public readonly SemaphoreSlim Writes = new(1, 1);
        public DateTime Checked = DateTime.MinValue;   // when it last answered or failed
        public bool? Reachable;                        // null: not asked yet
        public string Problem = "";
        public bool VoiceReady;
        public int Reading;                            // 1 while a read of the settings is on its way
        public long Rev;                               // one more for every change saved here, so a read that started before it never undoes it
        public int Pending;                            // changes waiting to be sent: only the last one's answer is kept (the ones before it lack the later change)
    }

    HttpClient Http => hf.CreateClient("dj-home");

    /// <summary>
    /// The family's FLACie Web for this person: the profile's <c>services.flacieweb.url</c>, when this is the desktop copy, the person has a Jellyfin sign-in to show
    /// it, and the address is another server (not a loopback one, which would be this PC). Null: this PC's own DJ.
    /// </summary>
    public string? HomeUrl(UserSession s) => Desktop && s.Jellyfin is { Token.Length: > 0, Server.Length: > 0 } ? HomeOf(s.Profile) : null;

    /// <summary>The <c>services.flacieweb.url</c> of a profile as a base address (no trailing slash), or null when there is none or it is no use from here.</summary>
    public static string? HomeOf(JsonObject profile)
    {
        string? url;
        try { url = profile["services"]?["flacieweb"]?["url"] is JsonValue v && v.TryGetValue<string>(out var x) ? x.Trim() : null; }
        catch (Exception) { return null; }   // (the profile is being changed by another page this very moment)
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https") || u.UserInfo.Length > 0) return null;
        if (u.IsLoopback || u.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)) return null;
        return u.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    Home Entry(UserSession s, string url) => homes.AddOrUpdate(s.Id, _ => new Home(url), (_, h) => h.Url == url ? h : new Home(url));

    /// <summary>The home server this person's DJ goes through and how it is going; null when it is this PC's own DJ. Asks it again in the background when due.</summary>
    public DjHome? Where(UserSession s)
    {
        if (HomeUrl(s) is not { } url) return null;
        var h = Entry(s, url);
        Refresh(s, h);
        lock (h) return new DjHome(h.Url, h.Reachable, h.Problem, h.VoiceReady);
    }

    bool Away(Home h) { lock (h) return h.Reachable == false && clock() - h.Checked < RefreshEvery; }

    /// <summary>Whether clips can be made for this person: the home server's answer while it is in use, else this PC's own Cloudflare setup.</summary>
    public bool VoiceReadyFor(UserSession s) => Where(s) is { Reachable: true } h ? h.VoiceReady : local.VoiceReady;

    /// <summary>The home server's answer when one is in use (one person on their own PC, as a rule), else this PC's own Cloudflare setup.</summary>
    public bool VoiceReady
    {
        get
        {
            var up = homes.Values.Where(h => { lock (h) return h.Reachable == true; }).ToList();
            return up.Count > 0 ? up.Any(h => { lock (h) return h.VoiceReady; }) : local.VoiceReady;
        }
    }

    /// <summary>This person's settings, never waiting for the network: the home server's as last read (kept here), or this PC's own while it can't be reached.</summary>
    public DjSettings Settings(UserSession s)
    {
        if (HomeUrl(s) is { } url) Refresh(s, Entry(s, url));
        return local.Settings(s);
    }

    /// <summary>Kept here at once (the page shows it now, and it is what this PC goes by if the home server is away), then sent to the home server in the background,
    /// as only what changed, so a switch flipped on the phone meanwhile is not undone.</summary>
    public void Save(UserSession s, DjSettings settings)
    {
        var was = local.Settings(s);
        local.Save(s, settings);
        if (HomeUrl(s) is not { } url) return;
        var h = Entry(s, url);
        var patch = DjJson.Patch(was, local.Settings(s));
        if (patch.Count == 0) return;
        Interlocked.Increment(ref h.Rev);
        if (Away(h)) return;   // known to be away: the change stays on this PC
        Interlocked.Increment(ref h.Pending);
        _ = Task.Run(() => WriteAsync(s, h, patch));
    }

    public async Task<DjCue?> PrepareAsync(UserSession s, DjRequest req, CancellationToken ct = default)
    {
        if (HomeUrl(s) is not { } url) return await local.PrepareAsync(s, req, ct);
        var h = Entry(s, url);
        if (Away(h)) return await local.PrepareAsync(s, req, ct);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct); cts.CancelAfter(NextWait);
            using var ask = Ask(HttpMethod.Post, h.Url + "/api/dj/next", s, DjJson.RequestBody(req));
            using var res = await Http.SendAsync(ask, cts.Token);
            if (await AnswerAsync(res, cts.Token) is not { } o || o["speak"] is not JsonValue) { Down(s, h, Why(res)); return await local.PrepareAsync(s, req, ct); }
            Up(s, h, null);
            if (DjJson.ReadCue(o) is not { } cue) return null;
            // the home server's clip, fetched through this server (a clip address of another shape is not followed: the device's voice says the line, if allowed)
            var clip = cue.ClipUrl is { } c && ClipPath().Match(c) is { Success: true } m ? RemoteClipUrl(m.Groups[1].Value) : null;
            if (clip is null && !cue.Backup) return null;
            return cue with { ClipUrl = clip };
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            Down(s, h, Unreached);
            return await local.PrepareAsync(s, req, ct);
        }
    }

    [GeneratedRegex(@"^/api/dj/clip/([0-9a-f]{32})\.mp3$")] private static partial Regex ClipPath();

    /// <summary>The address on this server of the home server's clip [id].</summary>
    public static string RemoteClipUrl(string id) => $"/api/dj/clip/r/{id}.mp3";

    /// <summary>The home server's clip [id] for this person (it waits there up to 10 s while the clip is being made); null when there is none, or no home server.
    /// The last few are kept in memory, so the page's fetch ahead and its play (and a range request) ask the home server once.</summary>
    public async Task<byte[]?> ClipAsync(UserSession s, string id, CancellationToken ct = default)
    {
        if (!DjClips.IsId(id) || HomeUrl(s) is not { } url) return null;
        if (clips.TryGetValue(id, out var kept)) return kept.Mp3;
        var mp3 = await clipFlights.RunAsync(url + "|" + id, async fct =>
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(fct); cts.CancelAfter(ClipWait);
                using var ask = Ask(HttpMethod.Get, url + "/api/dj/clip/" + id + ".mp3", s);
                using var res = await Http.SendAsync(ask, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                if (!res.IsSuccessStatusCode || res.Content.Headers.ContentLength is > MaxClip) return null;
                return await ReadAtMost(res.Content, MaxClip, cts.Token) is { Length: > 0 } b ? b : null;
            }
            catch (Exception) { return null; }
        }, ct);
        if (mp3 is null) return null;
        clips[id] = (mp3, clock());
        if (clips.Count > KeptClips)
            foreach (var old in clips.OrderBy(x => x.Value.At).Take(clips.Count - KeptClips).Select(x => x.Key).ToList()) clips.TryRemove(old, out _);
        return mp3;
    }

    // ---- talking to the home server ----

    /// <summary>A request with the person's own Jellyfin sign-in, as the phone sends it (FLACie Web checks the token with that Jellyfin).</summary>
    static HttpRequestMessage Ask(HttpMethod method, string url, UserSession s, JsonNode? body = null)
    {
        var a = s.Jellyfin!;
        var r = new HttpRequestMessage(method, url);
        r.Headers.TryAddWithoutValidation("X-Emby-Token", a.Token);
        r.Headers.TryAddWithoutValidation("X-Jellyfin-Server", a.Server.TrimEnd('/'));
        if (body is not null) r.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        return r;
    }

    /// <summary>A successful answer's JSON object; null for anything else.</summary>
    static async Task<JsonObject?> AnswerAsync(HttpResponseMessage res, CancellationToken ct)
    {
        if (!res.IsSuccessStatusCode || res.Content.Headers.ContentLength is > MaxAnswer) return null;
        try { return await ReadAtMost(res.Content, MaxAnswer, ct) is { } b && JsonNode.Parse(b) is JsonObject o ? o : null; }
        catch (System.Text.Json.JsonException) { return null; }
    }

    static async Task<byte[]?> ReadAtMost(HttpContent content, int max, CancellationToken ct)
    {
        await using var s = await content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buf = new byte[16384]; int n;
        while ((n = await s.ReadAsync(buf, ct)) > 0) { ms.Write(buf, 0, n); if (ms.Length > max) return null; }
        return ms.ToArray();
    }

    /// <summary>Why the home server's answer is no use, as the settings section says it after "FLACie Web at &lt;address&gt;".</summary>
    static string Why(HttpResponseMessage res) => res.StatusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "didn't accept your sign-in",
        HttpStatusCode.NotFound => "has no DJ yet (it needs updating)",
        _ when res.IsSuccessStatusCode => "gave an answer this app couldn't read",
        _ => Unreached,
    };

    const string Unreached = "couldn't be reached";

    /// <summary>Reads the person's settings in the background when due (at most one read at a time, and not again within <see cref="RefreshEvery"/>).</summary>
    void Refresh(UserSession s, Home h)
    {
        lock (h) if (clock() - h.Checked < RefreshEvery) return;
        if (Interlocked.CompareExchange(ref h.Reading, 1, 0) != 0) return;
        _ = Task.Run(async () =>
        {
            try { await ReadAsync(s, h); }
            finally { Volatile.Write(ref h.Reading, 0); }
        });
    }

    /// <summary>Reads the person's settings from the home server now and keeps them here. False when it couldn't (this PC's own settings then stand).</summary>
    public async Task<bool> ReadNowAsync(UserSession s, CancellationToken ct = default) => HomeUrl(s) is { } url && await ReadAsync(s, Entry(s, url), ct);

    async Task<bool> ReadAsync(UserSession s, Home h, CancellationToken ct = default)
    {
        var rev = Interlocked.Read(ref h.Rev);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct); cts.CancelAfter(SettingsWait);
            using var ask = Ask(HttpMethod.Get, h.Url + "/api/dj/settings", s);
            using var res = await Http.SendAsync(ask, cts.Token);
            if (await AnswerAsync(res, cts.Token) is not { } o || DjJson.ReadSettings(o, out var voice) is not { } st) { Down(s, h, Why(res)); return false; }
            // a change saved here since the read began (or still being sent) is on its way there: its own answer is kept instead
            if (Interlocked.Read(ref h.Rev) == rev && Volatile.Read(ref h.Pending) == 0) Keep(s, st);
            Up(s, h, voice);
            return true;
        }
        catch (Exception) when (!ct.IsCancellationRequested) { Down(s, h, Unreached); return false; }
    }

    async Task WriteAsync(UserSession s, Home h, JsonObject patch)
    {
        await h.Writes.WaitAsync();
        try
        {
            using var cts = new CancellationTokenSource(SettingsWait);
            using var ask = Ask(HttpMethod.Post, h.Url + "/api/dj/settings", s, patch);
            using var res = await Http.SendAsync(ask, cts.Token);
            if (await AnswerAsync(res, cts.Token) is not { } o || DjJson.ReadSettings(o, out var voice) is not { } st) { Down(s, h, Why(res)); return; }
            if (Volatile.Read(ref h.Pending) == 1) Keep(s, st);   // (with a later change still to send, its answer is kept instead)
            Up(s, h, voice);
        }
        catch (Exception) { Down(s, h, Unreached); }
        finally { Interlocked.Decrement(ref h.Pending); h.Writes.Release(); }
    }

    /// <summary>The home server's settings, kept as this PC's own (so a restart, or the home server going away, starts from them).</summary>
    void Keep(UserSession s, DjSettings st)
    {
        if (DjJson.Patch(local.Settings(s), st).Count == 0) return;
        local.Save(s, st);
        Changed?.Invoke(s.Id);
    }

    void Up(UserSession s, Home h, bool? voiceReady) => Mark(s, h, true, "", voiceReady);
    void Down(UserSession s, Home h, string problem) => Mark(s, h, false, problem, null);

    void Mark(UserSession s, Home h, bool reachable, string problem, bool? voiceReady)
    {
        bool changed;
        lock (h)
        {
            h.Checked = clock();
            changed = h.Reachable != reachable || h.Problem != problem || (voiceReady is { } v && v != h.VoiceReady);
            h.Reachable = reachable; h.Problem = problem;
            if (voiceReady is { } v2) h.VoiceReady = v2;
        }
        if (!changed) return;
        if (reachable) log.LogInformation("DJ: using FLACie Web at {Url}", h.Url);
        else log.LogWarning("DJ: FLACie Web at {Url} {Problem}; this PC's own DJ stands in", h.Url, problem);
        Changed?.Invoke(s.Id);
    }
}
