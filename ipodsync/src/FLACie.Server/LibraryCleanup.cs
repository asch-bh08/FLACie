using FLACie.Core;

namespace FLACie.Server;

/// <summary>Why the clean-up moved a file out of the library (F23.4).</summary>
public enum CleanupReason { Duplicate, WorseCopy, Undecodable }

/// <summary>
/// F23.4: when the music disk runs low (under <see cref="PlaylistFetchPolicy.DiskFloor"/> free), files that only take room are moved to the holding
/// folder on the same disk (the file mover's /retire: .flacie-replaced, emptied after 30 days, so a wrong move can be undone): exact duplicates, lower
/// quality copies of a song that has a better copy, and files that fail to decode. Never the only copy of a song: a song keeps its best playable file.
/// </summary>
public static class LibraryCleanup
{
    static readonly HashSet<string> Lossless = new(StringComparer.OrdinalIgnoreCase) { ".flac", ".wav", ".aiff", ".aif", ".alac", ".ape", ".wv" };

    /// <summary>A file's quality, higher is better: lossless above any lossy file, then its rate (bytes a second).</summary>
    public static double Quality(Track t)
    {
        var ext = Path.GetExtension(t.FilePath ?? t.Path);
        var rate = t.Size > 0 && t.DurationMs > 0 ? t.Size * 1000.0 / t.DurationMs : 0;
        return (Lossless.Contains(ext) ? 1e9 : 0) + rate;
    }

    /// <summary>What to move, and why. Copies of a song: the same title and lead artist (match key) and a length within 3 seconds.</summary>
    public static List<(Track Track, CleanupReason Why)> Plan(IEnumerable<Track> library, Func<Track, bool> undecodable)
    {
        var moves = new List<(Track, CleanupReason)>();
        foreach (var byKey in library.Where(t => (t.FilePath ?? "").Length > 0).GroupBy(Matching.MatchKey))
        {
            // one recording: lengths within 3 s of each other (a live take or an extended mix is another song)
            var left = byKey.OrderBy(t => t.DurationMs).ToList();
            while (left.Count > 0)
            {
                var first = left[0];
                var copies = left.Where(t => Math.Abs(t.DurationMs - first.DurationMs) <= 3000).ToList();
                left.RemoveAll(copies.Contains);
                if (copies.Count < 2) continue;
                var playable = copies.Where(t => !undecodable(t)).ToList();
                if (playable.Count == 0) continue;   // never the only copy: nothing better to keep
                var keep = playable.OrderByDescending(Quality).ThenBy(t => t.AddedMs).First();
                foreach (var t in copies.Where(t => t != keep))
                    moves.Add((t, undecodable(t) ? CleanupReason.Undecodable
                        : t.Size == keep.Size && Path.GetExtension(t.FilePath) == Path.GetExtension(keep.FilePath) ? CleanupReason.Duplicate : CleanupReason.WorseCopy));
            }
        }
        return moves;
    }
}

/// <summary>One file the clean-up moved to the holding folder, for Server admin.</summary>
public sealed record CleanupRecord(DateTime AtUtc, string User, string File, string Title, string Artist, CleanupReason Why);

/// <summary>
/// F23.4: runs the clean-up for one person's library when their music disk is under the floor: the holding folder is emptied of files older than
/// 30 days, then each file the plan names is moved there through the file mover (/retire). A file counts as undecodable when the file mover can read
/// it but finds no audio in it. Library songs are found on the file mover by their path under [root] (Jellyfin's path for the music folder,
/// FLACIE_CLEANUP_ROOT); songs FLACie filed itself (/file?path=) are found as they are.
/// </summary>
public sealed class LibraryCleanupRun(Func<UserSession, (long Free, long Total)?> space, Func<UserSession, IFileMover> mover, string? root, string? logFile, Func<DateTime> now)
{
    public const int HoldDays = 30, Keep = 500;
    readonly List<CleanupRecord> moved = Load(logFile);
    public IReadOnlyList<CleanupRecord> Moved { get { lock (moved) return moved.ToList(); } }

    /// <summary>The file's path under the file mover's root, or null when it is not under it.</summary>
    public string? MoverPath(Track t)
    {
        var p = t.Path; var i = p.IndexOf("/file?path=", StringComparison.Ordinal);
        if (i >= 0) return Uri.UnescapeDataString(p[(i + "/file?path=".Length)..]);
        if (root is not { Length: > 0 } r || t.FilePath is not { Length: > 0 } f) return null;
        var prefix = r.TrimEnd('/') + "/";
        return f.Replace('\\', '/').StartsWith(prefix, StringComparison.Ordinal) ? f.Replace('\\', '/')[prefix.Length..] : null;
    }

    public async Task<int> RunAsync(UserSession s, IReadOnlyList<Track> songs, CancellationToken ct)
    {
        if (space(s) is not { Free: < PlaylistFetchPolicy.DiskFloor }) return 0;
        var fm = mover(s);
        if (!await fm.CanRetireAsync(ct)) return 0;
        await fm.PurgeHeldAsync(HoldDays, ct);
        var reachable = songs.Where(t => MoverPath(t) is not null).ToList();
        // only the songs with more than one copy are looked at closely (each probe reads a file)
        var copies = LibraryCleanup.Plan(reachable, _ => false).Select(m => Matching.MatchKey(m.Track)).ToHashSet();
        var close = reachable.Where(t => copies.Contains(Matching.MatchKey(t))).ToList();
        var broken = new HashSet<Track>();
        foreach (var t in close)
            if (await fm.ProbeAsync(MoverPath(t)!, ct) is { Gone: false } f && (f.Codec.Length == 0 || f.DurationMs <= 0)) broken.Add(t);
        var n = 0;
        foreach (var (t, why) in LibraryCleanup.Plan(close, broken.Contains))
        {
            if (!await fm.RetireAsync(MoverPath(t)!, ct)) continue;
            n++;
            lock (moved) { moved.Add(new(now(), s.DisplayName, MoverPath(t)!, t.Title, t.Artist, why)); if (moved.Count > Keep) moved.RemoveRange(0, moved.Count - Keep); }
        }
        if (n > 0) Save();
        return n;
    }

    void Save()
    {
        if (logFile is null) return;
        try { Directory.CreateDirectory(Path.GetDirectoryName(logFile)!); lock (moved) File.WriteAllText(logFile, System.Text.Json.JsonSerializer.Serialize(moved)); } catch (IOException) { }
    }

    static List<CleanupRecord> Load(string? file)
    {
        try { if (file is not null && File.Exists(file) && System.Text.Json.JsonSerializer.Deserialize<List<CleanupRecord>>(File.ReadAllText(file)) is { } l) return l; }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException) { }
        return [];
    }
}

/// <summary>Runs the clean-up for every signed-in library with download services, every six hours (it does nothing while the disk has room).</summary>
public sealed class LibraryCleanupService(SessionStore sessions, LibraryCleanupRun run, ILogger<LibraryCleanupService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(15), stop);
            while (!stop.IsCancellationRequested)
            {
                foreach (var s in sessions.Active.ToList().Where(s => s.Services.FileMoverUrl.Length > 0))
                {
                    try { if (await run.RunAsync(s, s.Library.Songs, stop) is > 0 and var n) log.LogInformation("Clean-up moved {N} files to the holding folder for {User}", n, s.DisplayName); }
                    catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Clean-up failed"); }
                }
                await Task.Delay(TimeSpan.FromHours(6), stop);
            }
        }
        catch (OperationCanceledException) { }
    }
}
