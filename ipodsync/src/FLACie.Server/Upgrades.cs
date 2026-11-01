using FLACie.Core;

namespace FLACie.Server;

/// <summary>One song waiting for a Hi-Res copy (saved in <c>data/upgrades.json</c>): the song, the file it has now, and when it is next looked for.</summary>
public sealed class UpgradeEntry
{
    /// <summary>Whose download it was: their download services do the looking, and their log proves FLACie filed the file.</summary>
    public string UserId { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Title { get; set; } = "";
    public string Album { get; set; } = "";
    public long DurationMs { get; set; }
    /// <summary>The file it has now, relative to the file mover's root (as the download log keeps it).</summary>
    public string File { get; set; } = "";
    /// <summary>Its format as far as is known when it was added (the extension: m4a, mp3, flac ...).</summary>
    public string Format { get; set; } = "";
    /// <summary>The source that delivered the file now in the library (ytdl, soulseek, archive ...).</summary>
    public string? Source { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public int Attempts { get; set; }
    public DateTime NextTry { get; set; } = DateTime.UtcNow;
    /// <summary>A lossy copy was swapped for a lossless one after the first look found no Hi-Res: it stays on the list for Hi-Res, but that step is done.</summary>
    public bool LosslessTaken { get; set; }
    public string LastNote { get; set; } = "";

    public string Key => Matching.MatchKey(Title, Artist);
}

/// <summary>The upgrade list's rules that don't need a server.</summary>
public static class UpgradeList
{
    /// <summary>
    /// The songs due a look now, in the order they are looked for: songs in someone's queue first (they are about to be heard), then the most recently
    /// played (so a song played again moves up), then the oldest on the list. A song still waiting out its retry time is left out, queued or not.
    /// </summary>
    public static List<UpgradeEntry> Order(IEnumerable<UpgradeEntry> entries, IReadOnlySet<string> queued, IReadOnlyDictionary<string, long> lastPlayedMs, DateTime now) =>
        entries.Where(e => e.NextTry <= now)
            .OrderByDescending(e => queued.Contains(e.Key))
            .ThenByDescending(e => lastPlayedMs.TryGetValue(e.Key, out var at) ? at : long.MinValue)
            .ThenBy(e => e.AddedAt)
            .ToList();
}

/// <summary>The upgrade list as saved in the data folder (<c>upgrades.json</c>). The entries handed out are the live ones: change one, then <see cref="Save"/>.</summary>
public sealed class UpgradeStore
{
    public const string FileName = "upgrades.json";
    readonly string file; readonly object gate = new();
    readonly List<UpgradeEntry> entries;

    public UpgradeStore(DataPaths paths)
    {
        file = Path.Combine(paths.Root, FileName);
        List<UpgradeEntry>? read = null;
        try { if (System.IO.File.Exists(file)) read = System.Text.Json.JsonSerializer.Deserialize<List<UpgradeEntry>>(System.IO.File.ReadAllText(file)); } catch (Exception) { }
        entries = read ?? [];
    }

    public IReadOnlyList<UpgradeEntry> All() { lock (gate) return entries.ToList(); }

    /// <summary>Puts a song on the list, once: false when this person's song is already there (it keeps its place and its retry times).</summary>
    public bool Add(UpgradeEntry e)
    {
        lock (gate)
        {
            if (entries.Any(x => x.UserId == e.UserId && x.Key == e.Key)) return false;
            entries.Add(e);
            SaveLocked();
            return true;
        }
    }

    public void Remove(UpgradeEntry e) { lock (gate) { entries.Remove(e); SaveLocked(); } }
    public void Save() { lock (gate) SaveLocked(); }

    void SaveLocked()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!); var tmp = file + ".tmp";
            System.IO.File.WriteAllText(tmp, System.Text.Json.JsonSerializer.Serialize(entries, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            System.IO.File.Move(tmp, file, true);
        }
        catch (Exception) { }
    }
}

/// <summary>
/// The one background worker that upgrades songs on the list (spec section 2), one song at a time, while downloads are allowed and the music storage
/// has room. Each look asks Soulseek for Hi-Res copies only, with no time limit on a peer that is sending. A copy is filed next to the old file and read
/// back (ffprobe through the file mover): only when it is complete, the same length within 3 s and really Hi-Res is the old file retired (moved into
/// <c>.flacie-replaced/</c> by the file mover, nothing deleted) and Jellyfin asked to look again. A copy that fails is moved aside instead and the old
/// file stays. Nothing found: tried again after an hour, six hours, a day, then weekly. A YouTube or lossy copy with no Hi-Res anywhere on its first
/// look is swapped for a lossless copy, and stays on the list for Hi-Res.
/// <para>Only files in the download log are ever touched (FLACie filed them), and nothing runs at all while the file mover has no /retire: an older
/// file mover would leave two copies of every upgraded song in the library.</para>
/// </summary>
public sealed class UpgradeWorker(
    UpgradeStore store, DownloadLog log, Func<bool> allowed, Func<string, UserSession?> sessionOf, Func<UserSession, bool> roomFor,
    Func<UserSession, IFileMover> fileMover, Func<UserSession, UpgradeEntry, bool, CancellationToken, Task<UpgradeCopy?>> fetch, Func<UserSession, Task> rescan,
    Func<UserSession, IReadOnlySet<string>>? queuedOf = null, ILogger<UpgradeWorker>? logger = null) : BackgroundService
{
    static readonly HashSet<string> Lossy = ["m4a", "mp3", "opus", "ogg", "aac", "webm", "wma", "mp4"];

    /// <summary>A copy that can still be improved without Hi-Res: YouTube's, or any lossy file.</summary>
    static bool LossyCopy(UpgradeEntry e) => e.Source == OpenSourceSettings.YtdlId || Lossy.Contains(e.Format.ToLowerInvariant());

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        // the Windows app's copy leaves this to the always-on server, as it does the daily charts
        if (ChartsService.InDesktopShell) return;
        try { await Task.Delay(TimeSpan.FromMinutes(2), stop); } catch (OperationCanceledException) { return; }
        while (!stop.IsCancellationRequested)
        {
            var did = false;
            try
            {
                var note = await RunOnceAsync(stop);
                did = !note.StartsWith("idle", StringComparison.Ordinal);
                if (did) logger?.LogInformation("Upgrade: {Note}", note);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e) { logger?.LogWarning(e, "Upgrade pass failed"); }
            // straight on to the next song after one was worked on; otherwise a look every few minutes for anything that became due
            try { await Task.Delay(did ? TimeSpan.FromSeconds(5) : TimeSpan.FromMinutes(5), stop); } catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>One pass: the first due song (songs in a queue first, then each person's order) whose owner can be served now is worked on. Returns what
    /// happened ("idle: ..." when nothing was).</summary>
    public async Task<string> RunOnceAsync(CancellationToken ct)
    {
        if (!allowed()) return "idle: downloads are off";
        var now = DateTime.UtcNow;
        var all = store.All();
        if (all.Count == 0) return "idle: nothing on the list";
        var ordered = new List<(UpgradeEntry E, UserSession S, bool Queued)>();
        foreach (var mine in all.GroupBy(e => e.UserId))
        {
            // nobody signed in with this account since the server started: their download services are not known yet
            if (sessionOf(mine.Key) is not { } s) continue;
            var queued = queuedOf?.Invoke(s) ?? new HashSet<string>();
            var played = new Dictionary<string, long>();
            foreach (var h in s.History) played.TryAdd(Matching.MatchKey(h.Title, h.Artist), h.At);
            ordered.AddRange(UpgradeList.Order(mine, queued, played, now).Select(e => (e, s, queued.Contains(e.Key))));
        }
        // a song in anyone's queue goes before everyone else's, keeping each person's own order
        foreach (var (e, s, _) in ordered.OrderByDescending(x => x.Queued).ToList())
        {
            ct.ThrowIfCancellationRequested();
            if (!s.Services.SoulseekReady || !roomFor(s)) continue;
            var fm = fileMover(s);
            if (!await fm.CanRetireAsync(ct)) return "idle: the file mover has no /retire yet (it needs redeploying); the list is kept";
            if (!log.For(e.UserId).Any(r => r.Done && r.File == e.File)) { store.Remove(e); continue; }
            var old = await fm.ProbeAsync(e.File, ct);
            if (old is { Gone: true }) { store.Remove(e); continue; }   // gone from the library: nothing left to upgrade
            if (old is null) continue;                                   // the file mover could not read it just now: another time
            return await TryAsync(s, fm, e, old, ct);
        }
        return "idle: nothing due";
    }

    async Task<string> TryAsync(UserSession s, IFileMover fm, UpgradeEntry e, ProbeFacts old, CancellationToken ct)
    {
        var name = $"{e.Title} · {e.Artist}";
        var copy = await fetch(s, e, false, ct);
        var lossless = false;
        if (copy is null && e.Attempts == 0 && !e.LosslessTaken && LossyCopy(e))
        {
            copy = await fetch(s, e, true, ct);
            lossless = copy is not null;
        }
        if (copy is null) return Later(e, "no Hi-Res copy on Soulseek yet");
        var facts = await fm.ProbeAsync(copy.File, ct);
        var oldMs = old.DurationMs > 0 ? old.DurationMs : e.DurationMs;
        var why = facts is null || facts.Gone ? "the new copy could not be read back"
            : SmartDownloads.Verify(copy.ExpectedBytes, facts.Size, oldMs, facts.DurationMs, facts.Codec, facts.BitDepth, facts.SampleRate, wantHiRes: !lossless);
        if (why is not null)
        {
            // the old file stays where it is; the copy that failed must not sit next to it as a second song in the library
            await fm.RetireAsync(copy.File, ct);
            return Later(e, why);
        }
        if (!await fm.RetireAsync(e.File, ct))
        {
            await fm.RetireAsync(copy.File, ct);
            return Later(e, "the old file could not be moved aside");
        }
        var at = DateTime.UtcNow;
        try
        {
            log.Add(new DownloadRecord
            {
                Id = Guid.NewGuid().ToString("N"), UserId = e.UserId, UserName = s.DisplayName, Label = name, Title = e.Title, Artist = e.Artist, Album = e.Album, DurationMs = e.DurationMs,
                Kind = "Upgrade", StartedAt = at, FinishedAt = at, Outcome = "done", Source = "soulseek", File = copy.File,
                Message = lossless ? $"\"{e.Title}\" was swapped for a lossless copy" : $"\"{e.Title}\" was upgraded to Hi-Res ({facts!.BitDepth}-bit / {facts.SampleRate / 1000.0:0.#} kHz)",
            });
        }
        catch (Exception) { }
        // the song plays from the new file at once (before Jellyfin has looked again), and FLACie playlists find it by title and artist
        s.ReplaceDownloaded(e.File, copy.Track);
        try { await rescan(s); } catch (Exception) { }
        if (lossless)
        {
            e.File = copy.File; e.Format = Path.GetExtension(copy.File).TrimStart('.').ToLowerInvariant(); e.Source = "soulseek"; e.LosslessTaken = true;
            return Later(e, "swapped for a lossless copy; still looking for Hi-Res");
        }
        store.Remove(e);
        return $"upgraded {name} to Hi-Res";
    }

    string Later(UpgradeEntry e, string why)
    {
        e.Attempts++;
        e.NextTry = DateTime.UtcNow + SmartDownloads.RetryAfter(e.Attempts);
        e.LastNote = why;
        store.Save();
        return $"{e.Title} · {e.Artist}: {why}";
    }
}
