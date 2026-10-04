using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace FLACie.Core;

/// <summary>Another session of this user (or this one, <see cref="IsSelf"/>) and what it plays.</summary>
public sealed record ConnectSession(string Id, string Device, string Client, string User, bool IsSelf, bool Controllable,
    string? ItemId, string Title, string Artist, long PositionMs, long DurationMs, bool Paused, IReadOnlyList<string> Queue, string? ArtKey = null);

/// <summary>
/// Connect, like Spotify Connect, on Jellyfin's own session API (the same calls as the Android app): this player reports
/// what it plays, accepts remote commands over Jellyfin's WebSocket, and can list, control, take over from and send to the
/// user's other sessions. Only songs Jellyfin has can travel; Jams (SyncPlay) listen on the same socket via <see cref="Message"/>.
/// </summary>
public sealed class Connect : IDisposable
{
    readonly JellyfinClient jf;
    readonly UserSession user;
    JellyfinAccount Account => user.Jellyfin!;
    CancellationTokenSource? cts;
    ClientWebSocket? ws;
    Timer? ticker;
    IPlayerHost? host;
    string? reportedItem;
    bool? lastPaused;
    long lastProgressAt, lastPosMs;

    public bool Connected { get; private set; }
    public IReadOnlyList<ConnectSession> Sessions { get; private set; } = [];
    /// <summary>When <see cref="Sessions"/> was read, so a playing session's position can be carried forward between reads.</summary>
    public DateTime SessionsAsOf { get; private set; } = DateTime.UtcNow;
    public event Action? Changed;
    /// <summary>Every WebSocket message (MessageType, Data); Jams read SyncPlay from here.</summary>
    public event Action<string, JsonNode?>? Message;

    public Connect(JellyfinClient jf, UserSession user) { this.jf = jf; this.user = user; }

    public bool Available => user.Jellyfin is not null;
    public JellyfinClient Client => jf;
    public UserSession User => user;

    /// <summary>The player this account's remote commands drive (the tab that last played something).</summary>
    public IPlayerHost? Host
    {
        get => host;
        set
        {
            if (ReferenceEquals(host, value)) return;
            if (host is not null) host.Changed -= OnHostChanged;
            host = value;
            if (host is not null) host.Changed += OnHostChanged;
            OnHostChanged();
        }
    }

    public void Start()
    {
        if (!Available || cts is not null) return;
        cts = new CancellationTokenSource();
        var ct = cts.Token;
        _ = Task.Run(() => Loop(ct));
        ticker = new Timer(_ => Report(true), null, 10_000, 10_000);
    }

    public void Stop() { cts?.Cancel(); cts = null; ticker?.Dispose(); ticker = null; try { ws?.Abort(); } catch { } SetConnected(false); Sessions = []; reportedItem = null; }
    public void Dispose() => Stop();

    void SetConnected(bool c) { if (Connected == c) return; Connected = c; Changed?.Invoke(); }

    // ---- the live connection ----

    async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var sock = new ClientWebSocket();
                ws = sock;
                await sock.ConnectAsync(jf.SocketUri(Account), ct);
                await PostSafe("/Sessions/Capabilities/Full", new JsonObject
                {
                    ["PlayableMediaTypes"] = new JsonArray("Audio"),
                    ["SupportedCommands"] = new JsonArray("SetVolume", "Mute", "Unmute", "DisplayMessage", "PlayState", "Play", "PlayNext", "PlayMediaSource"),
                    ["SupportsMediaControl"] = true, ["SupportsPersistentIdentifier"] = true,
                });
                reportedItem = null; SetConnected(true); Report(false); _ = LoadSessionsAsync();
                var buf = new byte[16 * 1024];
                while (sock.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var sb = new StringBuilder();
                    WebSocketReceiveResult r;
                    do { r = await sock.ReceiveAsync(buf, ct); if (r.MessageType == WebSocketMessageType.Close) break; sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count)); } while (!r.EndOfMessage);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    var text = sb.ToString();
                    _ = Task.Run(() => OnMessage(text, sock));
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
            SetConnected(false); ws = null;
            try { await Task.Delay(5000, ct); } catch (OperationCanceledException) { }
        }
    }

    async Task OnMessage(string text, ClientWebSocket sock)
    {
        JsonNode? m; try { m = JsonNode.Parse(text); } catch { return; }
        var type = m?["MessageType"]?.GetValue<string>() ?? ""; var data = m?["Data"];
        try
        {
            switch (type)
            {
                case "ForceKeepAlive": case "KeepAlive":
                    try { await sock.SendAsync(Encoding.UTF8.GetBytes("""{"MessageType":"KeepAlive"}"""), WebSocketMessageType.Text, true, CancellationToken.None); } catch { }
                    break;
                case "Playstate": await OnPlaystate(data); break;
                case "Play": await OnPlay(data); break;
                case "Sessions": _ = LoadSessionsAsync(); break;
            }
            Message?.Invoke(type, data);
        }
        catch (Exception) { }
    }

    async Task OnPlaystate(JsonNode? d)
    {
        if (host is not { } p) return;
        switch (d?["Command"]?.GetValue<string>())
        {
            case "PlayPause": await p.ToggleAsync(); break;
            case "Pause": case "Stop": await p.PauseAsync(); break;
            case "Unpause": await p.ResumeAsync(); break;
            case "NextTrack": await p.NextAsync(); break;
            case "PreviousTrack": await p.PrevAsync(); break;
            case "Seek": await p.SeekRawAsync((d["SeekPositionTicks"]?.GetValue<long>() ?? 0) / 10_000); break;
        }
    }

    async Task OnPlay(JsonNode? d)
    {
        if (host is not { } p || d?["ItemIds"] is not JsonArray arr) return;
        var tracks = (await ResolveManyAsync(arr.Where(i => i is not null).Select(i => i!.GetValue<string>()))).Select(x => x.Track).ToList();
        if (tracks.Count == 0) return;
        switch (d["PlayCommand"]?.GetValue<string>())
        {
            case "PlayNext": foreach (var t in Enumerable.Reverse(tracks)) p.AddNext(t); break;
            case "PlayLast": foreach (var t in tracks) p.AddLast(t); break;
            default: await p.PlayFromAsync(tracks, Math.Clamp(d["StartIndex"]?.GetValue<int>() ?? 0, 0, tracks.Count - 1), (d["StartPositionTicks"]?.GetValue<long>() ?? 0) / 10_000, false); break;
        }
    }

    /// <summary>Resolves many songs at once (a dozen at a time) and keeps their order; ones that can't be found are left out.</summary>
    public async Task<List<(string Id, Track Track)>> ResolveManyAsync(IEnumerable<string> ids)
    {
        using var gate = new SemaphoreSlim(12);
        var all = await Task.WhenAll(ids.Select(async id =>
        {
            await gate.WaitAsync();
            try { return (Id: id, Track: await ResolveAsync(id)); } finally { gate.Release(); }
        }));
        return all.Where(x => x.Track is not null).Select(x => (x.Id, x.Track!)).ToList();
    }

    /// <summary>A Jellyfin song from this library, or fetched by id and remembered so it can be streamed.</summary>
    public async Task<Track?> ResolveAsync(string id)
    {
        if (user.Library.ByJellyfinId(id) is { } t) return t;
        var path = Account.Server + "/Audio/" + id + "/stream?static=true";
        if (user.Extra.TryGetValue(path, out var known)) return known;
        var fetched = await jf.TrackAsync(Account, id);
        if (fetched is not null) user.Extra[fetched.Path] = fetched;
        return fetched;
    }

    // ---- reporting what this player plays ----

    void OnHostChanged() => Report(false);

    void Report(bool tick)
    {
        var p = host;
        if (!Connected) return;
        var id = p?.Current?.JellyfinId;
        var paused = p is null || !p.Playing;
        var now = Environment.TickCount64;
        JsonObject? body = null; string? path = null;
        lock (this)
        {
            if (id is null) { if (reportedItem is { } old) { reportedItem = null; path = "/Sessions/Playing/Stopped"; body = new JsonObject { ["ItemId"] = old }; } }
            else if (id != reportedItem) { reportedItem = id; lastPaused = paused; lastProgressAt = now; lastPosMs = p!.PositionMs; path = "/Sessions/Playing"; body = PlayState(p!, id, paused); }
            else if (paused != lastPaused || (tick && !paused && now - lastProgressAt >= 9_000) || Math.Abs(p!.PositionMs - (lastPosMs + (lastPaused == true ? 0 : now - lastProgressAt))) > 3_000)
            {
                lastPaused = paused; lastProgressAt = now; lastPosMs = p!.PositionMs; path = "/Sessions/Playing/Progress";
                body = PlayState(p!, id, paused); body["EventName"] = tick ? "TimeUpdate" : paused ? "Pause" : "Unpause";
            }
        }
        if (path is not null) _ = PostSafe(path, body);
    }

    static JsonObject PlayState(IPlayerHost p, string itemId, bool paused)
    {
        // the queue travels as Jellyfin ids, so another device can take over the whole thing
        // a window around the song playing: a 5,000-song shuffle must not be cut at its first 200 songs and lose the current one
        var all = p.Queue.Select(t => t.JellyfinId).OfType<string>().ToList();
        var q = all.Skip(Math.Max(0, all.IndexOf(itemId) - 20)).Take(200).ToList();
        var items = new JsonArray(); for (var i = 0; i < q.Count; i++) items.Add(new JsonObject { ["Id"] = q[i], ["PlaylistItemId"] = "q" + i });
        return new JsonObject
        {
            ["ItemId"] = itemId, ["PositionTicks"] = p.PositionMs * 10_000, ["IsPaused"] = paused, ["CanSeek"] = true, ["PlayMethod"] = "DirectPlay",
            ["RepeatMode"] = p.Repeat switch { 1 => "RepeatAll", 2 => "RepeatOne", _ => "RepeatNone" },
            ["NowPlayingQueue"] = items, ["PlaylistItemId"] = "q" + Math.Max(0, q.IndexOf(itemId)),
        };
    }

    // ---- the user's sessions ----

    public async Task LoadSessionsAsync()
    {
        if (!Available) return;
        JsonArray? arr;
        try { arr = await jf.GetAsync(Account, $"/Sessions?ControllableByUserId={Account.UserId}&ActiveWithinSeconds=600") as JsonArray; } catch { return; }
        if (arr is null) return;
        if (Environment.GetEnvironmentVariable("FLACIE_DEBUG") == "1") Console.WriteLine("SESSIONS " + arr.ToJsonString());
        var list = new List<ConnectSession>();
        foreach (var o in arr.OfType<JsonObject>())
        {
            if (!(string.Equals(o["UserId"]?.GetValue<string>(), Account.UserId, StringComparison.OrdinalIgnoreCase) || o["SupportsRemoteControl"]?.GetValue<bool>() == true)) continue;
            var np = o["NowPlayingItem"]; var ps = o["PlayState"];
            var q = (o["NowPlayingQueue"] as JsonArray)?.Select(x => x?["Id"]?.GetValue<string>() ?? "").ToList() ?? [];
            list.Add(new ConnectSession(
                o["Id"]?.GetValue<string>() ?? "", o["DeviceName"]?.GetValue<string>() ?? "", o["Client"]?.GetValue<string>() ?? "", o["UserName"]?.GetValue<string>() ?? "",
                o["DeviceId"]?.GetValue<string>() == jf.DeviceId, o["SupportsRemoteControl"]?.GetValue<bool>() == true,
                np?["Id"]?.GetValue<string>(), np?["Name"]?.GetValue<string>() ?? "",
                (np?["Artists"] as JsonArray)?.FirstOrDefault()?.GetValue<string>() ?? np?["AlbumArtist"]?.GetValue<string>() ?? "",
                (ps?["PositionTicks"]?.GetValue<long>() ?? 0) / 10_000, (np?["RunTimeTicks"]?.GetValue<long>() ?? 0) / 10_000, ps?["IsPaused"]?.GetValue<bool>() ?? true, q,
                np?["ImageTags"]?["Primary"] is not null ? "jf" + np["Id"]?.GetValue<string>() : np?["AlbumPrimaryImageTag"] is not null ? "jf" + np["AlbumId"]?.GetValue<string>() : null));
        }
        SessionsAsOf = DateTime.UtcNow;
        Sessions = list.OrderBy(s => !s.IsSelf).ThenBy(s => s.ItemId is null).ThenBy(s => s.Device).ToList();
        Changed?.Invoke();
    }

    /// <summary>"PlayPause", "Pause", "Unpause", "NextTrack", "PreviousTrack", "Stop".</summary>
    public async Task CommandAsync(ConnectSession s, string cmd) { await PostSafe($"/Sessions/{s.Id}/Playing/{cmd}", null); await Task.Delay(600); await LoadSessionsAsync(); }

    public Task SeekAsync(ConnectSession s, long ms) => PostSafe($"/Sessions/{s.Id}/Playing/Seek?SeekPositionTicks={ms * 10_000}", null);

    /// <summary>Continues <paramref name="s"/>'s queue here from the same second, and stops it there.</summary>
    public async Task TakeOverAsync(ConnectSession s)
    {
        if (host is not { } p) return;
        var ids = s.Queue.Count > 0 ? s.Queue.ToList() : s.ItemId is { } one ? [one] : [];
        if (s.ItemId is { } now && !ids.Contains(now)) ids.Insert(0, now);
        var resolved = await ResolveManyAsync(ids); var tracks = resolved.Select(x => x.Track).ToList(); var kept = resolved.Select(x => x.Id).ToList();
        if (tracks.Count == 0) return;
        var idx = Math.Clamp(kept.IndexOf(s.ItemId ?? ""), 0, tracks.Count - 1);
        await p.PlayFromAsync(tracks, idx, s.PositionMs, false);
        await PostSafe($"/Sessions/{s.Id}/Playing/Stop", null);
        await Task.Delay(800); await LoadSessionsAsync();
    }

    /// <summary>Sends what plays here to <paramref name="s"/> (same song, same second) and pauses it here.</summary>
    public async Task SendToAsync(ConnectSession s)
    {
        if (host is not { } p || p.Current?.JellyfinId is not { } cur) return;
        var every = p.Queue.Select(t => t.JellyfinId).OfType<string>().ToList();
        // the ids travel in the URL: a window around the current song, not a whole shuffled library
        var ids = every.Skip(Math.Max(0, every.IndexOf(cur) - 5)).Take(100).ToList();
        await PostSafe($"/Sessions/{s.Id}/Playing?PlayCommand=PlayNow&ItemIds={string.Join(",", ids)}&StartIndex={Math.Max(0, ids.IndexOf(cur))}&StartPositionTicks={p.PositionMs * 10_000}", null);
        await p.PauseAsync();
        await Task.Delay(1200); await LoadSessionsAsync();
    }

    public async Task PostSafe(string path, JsonNode? body) { try { await jf.PostAsync(Account, path, body); } catch { } }
}
