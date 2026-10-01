using System.Text.Json.Nodes;

namespace FLACie.Core;

public sealed record JamGroup(string Id, string Name, IReadOnlyList<string> People);

/// <summary>
/// Jams: listening together on Jellyfin's SyncPlay, same calls as the Android app. Everyone in the Jam hears the same song
/// at the same moment and anyone can play, pause, seek, skip or add to the queue. While in a Jam the player hands its
/// buttons to the group (<see cref="IPlayInterceptor"/>) and only plays what the group says, when it says. Only Jellyfin
/// songs can be shared. Jellyfin users need SyncPlay allowed (Dashboard > Users > the user > SyncPlay access).
/// </summary>
public sealed class Jam : IPlayInterceptor
{
    sealed record Entry(string ItemId, string PlaylistItemId);

    readonly Connect connect;
    readonly object gate = new();
    List<Entry> playlist = [];
    int playingIndex;
    bool playing;
    long offsetMs; // server clock minus this machine's clock, so "unpause at When" lands at the same instant everywhere
    CancellationTokenSource? clock, command;
    IPlayerHost? joinedHost;

    public IReadOnlyList<JamGroup> Groups { get; private set; } = [];
    public string? GroupId { get; private set; }
    public string GroupName { get; private set; } = "";
    public IReadOnlyList<string> People { get; private set; } = [];
    public string? Status { get; private set; }
    public bool InJam => GroupId is not null;
    public event Action? Changed;
    /// <summary>A short message for the user ("Sam joined the Jam").</summary>
    public event Action<string>? Notice;

    public Jam(Connect connect) { this.connect = connect; connect.Message += OnMessage; }

    JellyfinAccount Account => connect.User.Jellyfin!;
    long ServerNow => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Interlocked.Read(ref offsetMs);
    void SetStatus(string? s) { Status = s; Changed?.Invoke(); }

    // ---- joining ----

    public async Task LoadGroupsAsync()
    {
        try
        {
            var arr = await connect.Client.GetAsync(Account, "/SyncPlay/List") as JsonArray ?? [];
            Groups = arr.OfType<JsonObject>().Select(o => new JamGroup(o["GroupId"]?.GetValue<string>() ?? "", o["GroupName"]?.GetValue<string>() ?? "",
                (o["Participants"] as JsonArray)?.Select(x => x?.GetValue<string>() ?? "").ToList() ?? [])).ToList();
            Changed?.Invoke();
        }
        catch (Exception e) { SetStatus(Problem(e)); }
    }

    /// <summary>Starts a Jam with what plays here (if it's on Jellyfin), so the others hear it as soon as they join.</summary>
    public async Task StartAsync()
    {
        try { await Send("/SyncPlay/New", new JsonObject { ["GroupName"] = $"{(Account.UserName.Length > 0 ? Account.UserName : "FLACie")}'s Jam" }); }
        catch (Exception e) { SetStatus(Problem(e)); return; }
        await Task.Delay(800);
        if (connect.Host is not { } p) return;
        var q = p.Queue.Select(t => t.JellyfinId).OfType<string>().ToList();
        if (q.Count == 0) return;
        var idx = Math.Max(0, q.IndexOf(p.Current?.JellyfinId ?? ""));
        try { await Send("/SyncPlay/SetNewQueue", new JsonObject { ["PlayingQueue"] = new JsonArray(q.Select(x => (JsonNode)x).ToArray()), ["PlayingItemPosition"] = idx, ["StartPositionTicks"] = p.PositionMs * 10_000 }); } catch { }
    }

    public async Task JoinAsync(JamGroup g) { try { await Send("/SyncPlay/Join", new JsonObject { ["GroupId"] = g.Id }); } catch (Exception e) { SetStatus(Problem(e)); } }

    public async Task LeaveAsync() { try { await Send("/SyncPlay/Leave", null); } catch { } Left(); }

    void Left()
    {
        GroupId = null; GroupName = ""; People = []; playlist = [];
        if (joinedHost is { } h) { h.Interceptor = null; h.OnAutoAdvance = null; joinedHost = null; }
        clock?.Cancel(); command?.Cancel();
        Changed?.Invoke();
    }

    // ---- messages from the group ----

    void OnMessage(string type, JsonNode? d)
    {
        if (type == "SyncPlayGroupUpdate") OnGroupUpdate(d);
        else if (type == "SyncPlayCommand") OnCommand(d);
    }

    void OnGroupUpdate(JsonNode? d)
    {
        if (d is null) return;
        var data = d["Data"];
        switch (d["Type"]?.GetValue<string>())
        {
            case "GroupJoined":
                GroupId = d["GroupId"]?.GetValue<string>() ?? data?["GroupId"]?.GetValue<string>(); GroupName = data?["GroupName"]?.GetValue<string>() ?? "";
                People = (data?["Participants"] as JsonArray)?.Select(x => x?.GetValue<string>() ?? "").ToList() ?? [];
                if (connect.Host is { } h) { joinedHost = h; h.Interceptor = this; h.OnAutoAdvance = AutoAdvance; }
                SyncClock(); Status = null; Changed?.Invoke();
                break;
            case "UserJoined": if (data is JsonValue j1 && j1.TryGetValue<string>(out var n1) && n1.Length > 0) { People = People.Append(n1).Distinct().ToList(); Notice?.Invoke($"{n1} joined the Jam"); Changed?.Invoke(); } break;
            case "UserLeft": if (data is JsonValue j2 && j2.TryGetValue<string>(out var n2) && n2.Length > 0) { People = People.Where(x => x != n2).ToList(); Notice?.Invoke($"{n2} left the Jam"); Changed?.Invoke(); } break;
            case "GroupLeft": case "NotInGroup": case "GroupDoesNotExist": Left(); break;
            case "LibraryAccessDenied": SetStatus("Someone in the Jam can't access these songs in Jellyfin."); break;
            case "PlayQueue": if (data is JsonObject q) _ = OnPlayQueue(q); break;
        }
    }

    /// <summary>The group's queue changed (new queue, song added, skip): load the group's song here, paused, and say when ready.</summary>
    async Task OnPlayQueue(JsonObject q)
    {
        var entries = (q["Playlist"] as JsonArray)?.OfType<JsonObject>().Select(o => new Entry(o["ItemId"]?.GetValue<string>() ?? "", o["PlaylistItemId"]?.GetValue<string>() ?? "")).ToList();
        if (entries is null) return;
        var idx = Math.Clamp(q["PlayingItemIndex"]?.GetValue<int>() ?? 0, 0, Math.Max(0, entries.Count - 1));
        var start = (q["StartPositionTicks"]?.GetValue<long>() ?? 0) / 10_000;
        bool same;
        lock (gate)
        {
            same = playlist.Count > 0 && entries.ElementAtOrDefault(idx)?.PlaylistItemId == playlist.ElementAtOrDefault(playingIndex)?.PlaylistItemId;
            playlist = entries; playingIndex = idx; playing = q["IsPlaying"]?.GetValue<bool>() ?? false;
        }
        if (connect.Host is not { } p) return;
        if (entries.Count == 0) { await p.PauseAsync(); return; }
        var tracks = new List<Track>();
        foreach (var e in entries) { var t = await connect.ResolveAsync(e.ItemId); if (t is null) { SetStatus("A song in the Jam isn't on this server."); return; } tracks.Add(t); }
        if (!same || q["Reason"]?.GetValue<string>() == "NewPlaylist") await p.PlayFromAsync(tracks, idx, start, true);
        await ReportReady(start);
    }

    /// <summary>Waits until the song is buffered, then tells the group this player is ready (the group then unpauses everyone).</summary>
    async Task ReportReady(long posMs)
    {
        if (connect.Host is not { } p) return;
        for (var i = 0; i < 40 && !await p.IsBufferedAsync(); i++) await Task.Delay(250);
        Entry? e; bool isPlaying;
        lock (gate) { e = playlist.ElementAtOrDefault(playingIndex); isPlaying = playing; }
        if (e is null) return;
        try { await Send("/SyncPlay/Ready", new JsonObject { ["When"] = DateTimeOffset.FromUnixTimeMilliseconds(ServerNow).UtcDateTime.ToString("O"), ["PositionTicks"] = posMs * 10_000, ["IsPlaying"] = isPlaying, ["PlaylistItemId"] = e.PlaylistItemId }); } catch { }
    }

    /// <summary>Play, pause, seek at the server instant <c>When</c>, corrected for this machine's clock.</summary>
    void OnCommand(JsonNode? d)
    {
        if (d is null || connect.Host is not { } p) return;
        long whenMs;
        try { whenMs = DateTimeOffset.Parse(d["When"]!.GetValue<string>()).ToUnixTimeMilliseconds(); } catch { whenMs = ServerNow; }
        var posMs = (d["PositionTicks"]?.GetValue<long>() ?? 0) / 10_000;
        var cmd = d["Command"]?.GetValue<string>();
        command?.Cancel();
        var cts = command = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                var wait = whenMs - ServerNow;
                if (wait > 0) await Task.Delay((int)Math.Min(wait, 30_000), cts.Token);
                var late = Math.Max(0, ServerNow - whenMs);
                switch (cmd)
                {
                    case "Unpause": playing = true; await p.SeekRawAsync(posMs + late); await p.ResumeAsync(); break;
                    case "Pause": case "Stop": playing = false; await p.PauseAsync(); await p.SeekRawAsync(posMs); break;
                    case "Seek": await p.PauseAsync(); await p.SeekRawAsync(posMs); await ReportReady(posMs); break;
                }
            }
            catch (OperationCanceledException) { }
        });
    }

    /// <summary>A song ended here: ask the group to move on. Every player asks; the group moves once (later asks name an old song).</summary>
    void AutoAdvance()
    {
        _ = connect.Host?.PauseAsync();
        Entry? e; lock (gate) e = playlist.ElementAtOrDefault(playingIndex);
        if (e is null) return;
        _ = SendQuiet("/SyncPlay/NextItem", new JsonObject { ["PlaylistItemId"] = e.PlaylistItemId });
    }

    // ---- this player's buttons go to the group ----

    Entry? Cur() { lock (gate) return playlist.ElementAtOrDefault(playingIndex); }

    public bool Toggle() { _ = SendQuiet(connect.Host?.Playing == true ? "/SyncPlay/Pause" : "/SyncPlay/Unpause", null); return true; }
    public bool Seek(long ms) { _ = SendQuiet("/SyncPlay/Seek", new JsonObject { ["PositionTicks"] = ms * 10_000 }); return true; }
    public bool Next() { if (Cur() is { } e) _ = SendQuiet("/SyncPlay/NextItem", new JsonObject { ["PlaylistItemId"] = e.PlaylistItemId }); return true; }
    public bool Prev() { if (Cur() is { } e) _ = SendQuiet("/SyncPlay/PreviousItem", new JsonObject { ["PlaylistItemId"] = e.PlaylistItemId }); return true; }
    public bool SkipTo(int index) { Entry? e; lock (gate) e = playlist.ElementAtOrDefault(index); if (e is not null) _ = SendQuiet("/SyncPlay/SetPlaylistItem", new JsonObject { ["PlaylistItemId"] = e.PlaylistItemId }); return true; }

    public bool Play(IReadOnlyList<Track> list, int index)
    {
        var picked = list.ElementAtOrDefault(index)?.JellyfinId;
        var shared = list.Select(t => t.JellyfinId).OfType<string>().ToList();
        if (picked is null || shared.Count == 0) { Notice?.Invoke("Only songs on Jellyfin can play in a Jam"); return true; }
        _ = SendQuiet("/SyncPlay/SetNewQueue", new JsonObject { ["PlayingQueue"] = new JsonArray(shared.Select(x => (JsonNode)x).ToArray()), ["PlayingItemPosition"] = shared.IndexOf(picked), ["StartPositionTicks"] = 0 });
        return true;
    }

    public bool Enqueue(Track t, bool next)
    {
        if (t.JellyfinId is not { } id) { Notice?.Invoke("Only songs on Jellyfin can join a Jam's queue"); return true; }
        _ = SendQuiet("/SyncPlay/Queue", new JsonObject { ["ItemIds"] = new JsonArray(id), ["Mode"] = next ? "QueueNext" : "Queue" });
        Notice?.Invoke(next ? "Playing next in the Jam" : "Added to the Jam");
        return true;
    }

    // ---- clock ----

    /// <summary>NTP-style: three samples of the server's clock, keeping the one with the shortest round trip; again each minute.</summary>
    void SyncClock()
    {
        clock?.Cancel();
        var cts = clock = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                (double rtt, long off)? best = null;
                for (var i = 0; i < 3; i++)
                {
                    try
                    {
                        var t0 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        var o = await connect.Client.GetAsync(Account, "/GetUtcTime", cts.Token);
                        var t3 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        var t1 = DateTimeOffset.Parse(o!["RequestReceptionTime"]!.GetValue<string>()).ToUnixTimeMilliseconds();
                        var t2 = DateTimeOffset.Parse(o["ResponseTransmissionTime"]!.GetValue<string>()).ToUnixTimeMilliseconds();
                        var rtt = (t3 - t0) - (t2 - t1); var off = ((t1 - t0) + (t2 - t3)) / 2;
                        if (best is null || rtt < best.Value.rtt) best = (rtt, off);
                    }
                    catch (OperationCanceledException) { return; }
                    catch { }
                }
                if (best is { } b) Interlocked.Exchange(ref offsetMs, b.off);
                try { await Task.Delay(60_000, cts.Token); } catch (OperationCanceledException) { return; }
            }
        });
    }

    // ---- plumbing ----

    Task Send(string path, JsonNode? body) => connect.Client.PostAsync(Account, path, body);
    async Task SendQuiet(string path, JsonNode? body) { try { await Send(path, body); } catch (Exception e) { SetStatus(Problem(e)); } }
    static string Problem(Exception e) => e.Message.Contains("403") ? "Your Jellyfin user isn't allowed to use SyncPlay. An admin can turn it on in Dashboard > Users." : $"Jam failed: {e.Message}";
}
