using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FLACie.Core;

namespace FLACie.Server;

/// <summary>
/// Where a device was at one moment: the song, how far in, the queue it was playing and what that queue came from. Kept so the person can go back to it
/// later ("Phone, 1 hour ago: Song X at 2:13 in Road trip") and carry on from there on another device (owner, 25 Nov).
/// </summary>
/// <param name="At">When, in milliseconds since 1970 (the server's clock, stamped when the state arrived).</param>
/// <param name="DeviceId">"web:Edge on Windows" for a browser, the Jellyfin device id for the phone (so it matches the phone's Connect session).</param>
/// <param name="Queue">The queue around the song: Jellyfin ids, or a library path for a song Jellyfin does not have. At most <see cref="WhereStore.MaxQueue"/>.</param>
/// <param name="Index">Where the song is in <paramref name="Queue"/>.</param>
/// <param name="Context">What the queue came from (a playlist's or album's name), when known.</param>
public sealed record WhereState(
    string Id, long At, string DeviceId, string Device, string Kind,
    string Title, string Artist, string? Album, string? ArtKey, string? ItemId, string? Path,
    long PositionMs, long DurationMs, bool Paused, IReadOnlyList<string> Queue, int Index, string? Context)
{
    /// <summary>Which song this is, whatever the device calls it: two states with the same key are the same song.</summary>
    public string SongKey => ItemId ?? Path ?? (Title + "\u0001" + Artist);
}

/// <summary>What a device sends: what it is playing now (POST /api/where).</summary>
public sealed record WhereReport(
    string? DeviceId, string? Device, string? Kind, string? Title, string? Artist, string? Album, string? ArtKey, string? ItemId, string? Path,
    long PositionMs, long DurationMs, bool Paused, List<string>? Queue, int Index, string? Context);

/// <summary>
/// Each account's last day of play states, per device (data/where/&lt;account&gt;.json). A state is kept when the song changes, when it is paused or
/// played again, and about once a minute while it plays. Once a device has moved on from a song only where it left that song is kept (and, for a long
/// song or mix, one every ten minutes), so a day of listening is a few hundred states, not thousands. Older than a day is dropped, and at most
/// <see cref="MaxStates"/> are kept per account.
/// </summary>
public sealed class WhereStore(DataPaths paths)
{
    public const int MaxStates = 1000, MaxQueue = 200;
    public static readonly long KeepMs = (long)TimeSpan.FromHours(24).TotalMilliseconds;
    /// <summary>A state while the same song plays on: once a minute.</summary>
    public static readonly long SampleMs = 60_000;
    /// <summary>Inside a song the device has moved on from, states this far apart are still kept (a long mix keeps more than its last minute).</summary>
    public static readonly long SpacingMs = 10 * 60_000;

    sealed class Account { public readonly object Gate = new(); public List<WhereState>? States; }
    readonly ConcurrentDictionary<string, Account> accounts = new();
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    string FileOf(string account) => System.IO.Path.Combine(Directory.CreateDirectory(System.IO.Path.Combine(paths.Root, "where")).FullName, Safe(account) + ".json");
    // an account id is a hex hash already; anything else is hashed so it can never name a file outside the folder
    static string Safe(string account) => account.All(char.IsAsciiLetterOrDigit) && account.Length <= 80 ? account : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account)));

    /// <summary>Keeps <paramref name="s"/> when it says something new (see the class summary); true when it was kept.</summary>
    public bool Record(string account, WhereState s, long? nowMs = null)
    {
        if (string.IsNullOrWhiteSpace(s.DeviceId) || (string.IsNullOrWhiteSpace(s.Title) && s.ItemId is null && s.Path is null)) return false;
        var now = nowMs ?? s.At;
        var a = accounts.GetOrAdd(account, _ => new Account());
        lock (a.Gate)
        {
            var list = Load(a, account);
            var last = list.LastOrDefault(x => x.DeviceId == s.DeviceId);
            if (last is not null)
            {
                if (s.At < last.At) return false;   // arrived late: a newer one is already kept
                var same = last.SongKey == s.SongKey && last.Paused == s.Paused;
                // the same song, still paused: nothing new. Still playing: once a minute.
                if (same && (s.Paused || s.At - last.At < SampleMs)) return false;
            }
            list.Add(s);
            a.States = Compact(list, now);
            Save(account, a.States);
            return true;
        }
    }

    /// <summary>Every kept state of the account, oldest first (states older than a day are left out).</summary>
    public IReadOnlyList<WhereState> All(string account, long nowMs)
    {
        var a = accounts.GetOrAdd(account, _ => new Account());
        lock (a.Gate) return Load(a, account).Where(x => x.At >= nowMs - KeepMs).ToList();
    }

    public WhereState? Find(string account, string id, long nowMs) => All(account, nowMs).FirstOrDefault(x => x.Id == id);

    /// <summary>The account's devices, the one seen last first, each with its states newest first: what the "Where you were" picker shows.</summary>
    public IReadOnlyList<WhereDevice> Devices(string account, long nowMs) =>
        All(account, nowMs).GroupBy(x => x.DeviceId)
            .Select(g => { var newest = g.OrderByDescending(x => x.At).ToList(); return new WhereDevice(g.Key, newest[0].Device, newest[0].Kind, newest[0].At, newest); })
            .OrderByDescending(d => d.LastSeen).ToList();

    /// <summary>Drops states older than a day; thins the songs each device has moved on from (where it left the song, plus one every
    /// <see cref="SpacingMs"/> before that); keeps the newest <see cref="MaxStates"/>.</summary>
    public static List<WhereState> Compact(List<WhereState> list, long nowMs)
    {
        var fresh = list.Where(x => x.At >= nowMs - KeepMs).OrderBy(x => x.At).ToList();
        var keep = new List<WhereState>(fresh.Count);
        foreach (var dev in fresh.GroupBy(x => x.DeviceId))
        {
            var states = dev.ToList();
            // runs of the same song, one after another
            var runs = new List<List<WhereState>>();
            foreach (var x in states) { if (runs.Count > 0 && runs[^1][0].SongKey == x.SongKey) runs[^1].Add(x); else runs.Add([x]); }
            for (var r = 0; r < runs.Count; r++)
            {
                var run = runs[r];
                if (r == runs.Count - 1) { keep.AddRange(run); continue; }   // the song the device is on (or ended on): every state, it may still move
                var kept = new List<WhereState> { run[^1] };
                for (var i = run.Count - 2; i >= 0; i--) if (kept[^1].At - run[i].At >= SpacingMs) kept.Add(run[i]);
                keep.AddRange(kept);
            }
        }
        keep = keep.OrderBy(x => x.At).ToList();
        return keep.Count > MaxStates ? keep.Skip(keep.Count - MaxStates).ToList() : keep;
    }

    // ---- the file: queues are written once each and named by a short hash, as one queue is shared by most states of a song run ----

    sealed record FileState(string Id, long At, string DeviceId, string Device, string Kind, string Title, string Artist, string? Album, string? ArtKey,
        string? ItemId, string? Path, long PositionMs, long DurationMs, bool Paused, string Q, int Index, string? Context);
    sealed record FileModel(Dictionary<string, List<string>> Queues, List<FileState> States);

    static string QueueKey(IReadOnlyList<string> q) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", q))))[..16];

    List<WhereState> Load(Account a, string account)
    {
        if (a.States is not null) return a.States;
        var list = new List<WhereState>();
        try
        {
            var f = FileOf(account);
            if (File.Exists(f) && JsonSerializer.Deserialize<FileModel>(File.ReadAllText(f), Json) is { } m)
                foreach (var x in m.States ?? [])
                    list.Add(new WhereState(x.Id, x.At, x.DeviceId, x.Device, x.Kind, x.Title, x.Artist, x.Album, x.ArtKey, x.ItemId, x.Path, x.PositionMs, x.DurationMs, x.Paused,
                        m.Queues?.GetValueOrDefault(x.Q ?? "") ?? [], x.Index, x.Context));
        }
        catch (Exception) { }   // a damaged file starts again empty: this is a convenience, not a record anyone relies on
        return a.States = list.OrderBy(x => x.At).ToList();
    }

    void Save(string account, List<WhereState> list)
    {
        var queues = new Dictionary<string, List<string>>();
        var states = new List<FileState>(list.Count);
        foreach (var x in list)
        {
            var k = QueueKey(x.Queue);
            queues.TryAdd(k, x.Queue.ToList());
            states.Add(new FileState(x.Id, x.At, x.DeviceId, x.Device, x.Kind, x.Title, x.Artist, x.Album, x.ArtKey, x.ItemId, x.Path, x.PositionMs, x.DurationMs, x.Paused, k, x.Index, x.Context));
        }
        var f = FileOf(account);
        try { File.WriteAllText(f + ".tmp", JsonSerializer.Serialize(new FileModel(queues, states), Json)); File.Move(f + ".tmp", f, overwrite: true); }
        catch (Exception) { }
    }

    /// <summary>A report from a device, cut to the limits and stamped with the server's time. Null when it says nothing is playing.</summary>
    public static WhereState? From(WhereReport r, long nowMs)
    {
        static string Cut(string? x, int n) => (x ?? "").Trim() is var y && y.Length > n ? y[..n] : y;
        static string? CutOrNull(string? x, int n) => string.IsNullOrWhiteSpace(x) ? null : Cut(x, n);
        var deviceId = Cut(r.DeviceId, 80);
        var title = Cut(r.Title, 200);
        if (deviceId.Length == 0 || (title.Length == 0 && string.IsNullOrWhiteSpace(r.ItemId) && string.IsNullOrWhiteSpace(r.Path))) return null;
        var device = Cut(r.Device, 60) is { Length: > 0 } d ? d : "Device";
        var queue = (r.Queue ?? []).Where(q => !string.IsNullOrWhiteSpace(q) && q.Length <= 600).ToList();
        var index = Math.Clamp(r.Index, 0, Math.Max(0, queue.Count - 1));
        // a long queue keeps a window around the song (a little before, the rest after), so the song itself is always in it
        if (queue.Count > MaxQueue) { var from = Math.Clamp(index - 20, 0, queue.Count - MaxQueue); queue = queue.Skip(from).Take(MaxQueue).ToList(); index -= from; }
        return new WhereState(Guid.NewGuid().ToString("N")[..12], nowMs, deviceId, device, CutOrNull(r.Kind, 20) ?? DeviceLabels.Kind(device),
            title, Cut(r.Artist, 200), CutOrNull(r.Album, 200), CutOrNull(r.ArtKey, 200), CutOrNull(r.ItemId, 64), CutOrNull(r.Path, 600),
            Math.Max(0, r.PositionMs), Math.Max(0, r.DurationMs), r.Paused, queue, index, CutOrNull(r.Context, 120));
    }
}

/// <summary>A device in the "Where you were" picker: its states, newest first.</summary>
public sealed record WhereDevice(string DeviceId, string Device, string Kind, long LastSeen, IReadOnlyList<WhereState> States);

/// <summary>Words for devices and times, the same as the phone's (DeviceLabels.kt): what kind a device is, "1 h ago", "at 2:13 in Road trip".</summary>
public static class DeviceLabels
{
    /// <summary>Jellyfin only gives a device's name (the app is the same for every FLACie player), so the kind is guessed from it.</summary>
    public static string Kind(string name)
    {
        var n = (name ?? "").ToLowerInvariant();
        if (n.Contains("flacie for windows") || n.Contains("desktop") || n.Contains("windows pc") || n.Contains("laptop")) return "Computer";
        if (n.Contains("flacie web") || n.Contains("browser") || n.Contains("chrome") || n.Contains("firefox") || n.Contains("edge") || n.Contains("safari")) return "Browser";
        if (n.StartsWith("sm-") || n.StartsWith("sdk_") || n.Contains("pixel") || n.Contains("phone") || n.Contains("galaxy") || n.Contains("iphone")) return "Phone";
        if (n.Contains("echo") || n.Contains("alexa")) return "Speaker";
        return "Player";
    }

    /// <summary>The icon for a kind (the web's icon names).</summary>
    public static string Icon(string kind) => kind switch { "Browser" => "globe", _ => "devices" };

    /// <summary>"just now", "5 min ago", "1 h ago", "yesterday".</summary>
    public static string Ago(long atMs, long nowMs)
    {
        var mins = Math.Max(0, (nowMs - atMs) / 60_000);
        if (mins < 1) return "just now";
        if (mins < 60) return $"{mins} min ago";
        var hours = mins / 60;
        return hours < 24 ? $"{hours} h ago" : "yesterday";
    }

    /// <summary>"2:13", "1:02:05".</summary>
    public static string Clock(long ms)
    {
        var s = Math.Max(0, ms / 1000);
        return s >= 3600 ? $"{s / 3600}:{s % 3600 / 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
    }

    /// <summary>"at 2:13 in Road trip", or "at 2:13" when the queue came from nowhere in particular.</summary>
    public static string Where(long positionMs, string? context) => "at " + Clock(positionMs) + (string.IsNullOrWhiteSpace(context) ? "" : " in " + context);
}

/// <summary>The /api/where calls, written as plain functions so the tests call them as the endpoints do.</summary>
public static class WhereApi
{
    public static IResult Report(WhereStore store, UserSession? s, WhereReport? body, long nowMs)
    {
        if (s is null) return Results.Unauthorized();
        if (body is null || WhereStore.From(body, nowMs) is not { } state) return Results.BadRequest();
        // a phone's id never starts with "web:": those are the browsers this server records itself
        if (state.DeviceId.StartsWith("web:", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest();
        return Results.Ok(new { kept = store.Record(s.Id, state, nowMs) });
    }

    public static IResult List(WhereStore store, UserSession? s, long nowMs) => s is null ? Results.Unauthorized() : Results.Json(new
    {
        now = nowMs,
        devices = store.Devices(s.Id, nowMs).Select(d => new
        {
            deviceId = d.DeviceId, device = d.Device, kind = d.Kind, lastSeen = d.LastSeen,
            states = d.States.Select(x => new
            {
                id = x.Id, at = x.At, title = x.Title, artist = x.Artist, album = x.Album, artKey = x.ArtKey, itemId = x.ItemId, path = x.Path,
                positionMs = x.PositionMs, durationMs = x.DurationMs, paused = x.Paused, context = x.Context, queueLength = x.Queue.Count,
            }),
        }),
    });

    public static IResult One(WhereStore store, UserSession? s, string id, long nowMs)
    {
        if (s is null) return Results.Unauthorized();
        if (store.Find(s.Id, id, nowMs) is not { } x) return Results.NotFound();
        return Results.Json(new
        {
            id = x.Id, at = x.At, deviceId = x.DeviceId, device = x.Device, kind = x.Kind, title = x.Title, artist = x.Artist, album = x.Album, artKey = x.ArtKey,
            itemId = x.ItemId, path = x.Path, positionMs = x.PositionMs, durationMs = x.DurationMs, paused = x.Paused, context = x.Context, queue = x.Queue, index = x.Index,
        });
    }

    public static void MapWhere(this WebApplication app)
    {
        static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // the phone says where it is (song changes, pause and play, and about once a minute while playing); browsers are recorded by WhereSampler
        app.MapPost("/api/where", async (HttpContext ctx, WhereReport body, SessionStore store, JellyfinClient jf, WhereStore where) =>
            Report(where, await ApiEndpoints.CallerAsync(ctx, store, jf), body, Now())).DisableAntiforgery();
        app.MapGet("/api/where", async (HttpContext ctx, SessionStore store, JellyfinClient jf, WhereStore where) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            return List(where, await ApiEndpoints.CallerAsync(ctx, store, jf), Now());
        });
        app.MapGet("/api/where/{id}", async (HttpContext ctx, string id, SessionStore store, JellyfinClient jf, WhereStore where) =>
            One(where, await ApiEndpoints.CallerAsync(ctx, store, jf), id, Now()));
    }
}

/// <summary>Records where each open browser is, every 15 seconds (the store keeps only what is new: see <see cref="WhereStore"/>).</summary>
public sealed class WhereSampler(ClientRegistry clients, WhereStore store) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try { while (await timer.WaitForNextTickAsync(stop)) { try { Sample(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()); } catch (Exception) { } } }
        catch (OperationCanceledException) { }
    }

    /// <summary>One browser's id in the store: its label ("Edge on Windows"), so a reload or a second tab is still the same device.</summary>
    public static string DeviceIdOf(WebClient c) => "web:" + c.Label;

    public void Sample(long nowMs)
    {
        // two tabs of one browser are one device: the one playing speaks for it
        foreach (var g in clients.Open.Where(c => c.Player.Current is not null).GroupBy(c => (c.Session.Id, DeviceIdOf(c))))
        {
            var c = g.FirstOrDefault(x => x.Player.Playing) ?? g.First();
            if (Of(c.Player, DeviceIdOf(c), c.Label, nowMs) is { } s) store.Record(c.Session.Id, s, nowMs);
        }
    }

    /// <summary>What a web player is doing, as a state (null when nothing is loaded).</summary>
    public static WhereState? Of(PlayerState p, string deviceId, string device, long nowMs)
    {
        if (p.Current is not { } t) return null;
        // the queue in the order it plays (a shuffled queue too), so carrying on plays the same songs next
        var order = p.PlayOrder.Count == p.Queue.Count ? p.PlayOrder.ToList() : Enumerable.Range(0, p.Queue.Count).ToList();
        var ids = order.Select(i => p.Queue[i].JellyfinId ?? p.Queue[i].Path).ToList();
        var at = Math.Max(0, order.IndexOf(p.Index));
        return WhereStore.From(new WhereReport(deviceId, device, DeviceLabels.Kind(device), t.Title, t.Artist, t.Album, t.ArtKey, t.JellyfinId, t.JellyfinId is null ? t.Path : null,
            p.PositionMs, t.DurationMs > 0 ? t.DurationMs : (long)(p.Duration * 1000), !p.Playing, ids, at, p.Context), nowMs);
    }
}
