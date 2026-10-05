using System.Collections.Concurrent;
using System.Text.Json;
using FLACie.Core;

namespace FLACie.Server;

/// <summary>One step of a download's story: which source was tried and what it said ("Soulseek had nothing it could finish", "Downloading from YouTube...").</summary>
public sealed record TrailStep(DateTime At, string? Source, string Text, bool Miss);

/// <summary>A finished download as it is kept in the log (<c>data/downloads/&lt;user&gt;.jsonl</c>, one line each, newest 5000 per user).</summary>
public sealed class DownloadRecord
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string UserName { get; set; } = "";
    public string Label { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    /// <summary>Search (asked for), Autoplay (fetched ahead), Playlist, Import, Chart.</summary>
    public string Kind { get; set; } = "Search";
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    /// <summary>"done" or "failed".</summary>
    public string Outcome { get; set; } = "failed";
    /// <summary>Where a finished download came from: soulseek, lidarr, ytdl, archive, audius, jamendo. Null when it failed.</summary>
    public string? Source { get; set; }
    public string Message { get; set; } = "";
    /// <summary>Where the file was put, relative to the file mover's root (null for Lidarr, which files it itself).</summary>
    public string? File { get; set; }
    public string? ArtKey { get; set; }
    public List<TrailStep> Trail { get; set; } = [];

    public bool Done => Outcome == "done";
    public TimeSpan Took => FinishedAt - StartedAt;
}

/// <summary>The history of every download the server ran for each user, kept across restarts. Also answers "where did this file come from?".</summary>
public sealed class DownloadLog(DataPaths paths)
{
    const int Keep = 5000;
    readonly ConcurrentDictionary<string, List<DownloadRecord>> cache = new();
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    string Dir => Directory.CreateDirectory(Path.Combine(paths.Root, "downloads")).FullName;
    static string Safe(string id) => new(id.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
    string FileOf(string userId) => Path.Combine(Dir, Safe(userId) + ".jsonl");

    List<DownloadRecord> Of(string userId) => cache.GetOrAdd(userId, id =>
    {
        var list = new List<DownloadRecord>();
        try
        {
            var f = FileOf(id);
            if (File.Exists(f))
            {
                var lines = File.ReadAllLines(f);
                foreach (var line in lines.Reverse().Take(Keep))
                    try { if (JsonSerializer.Deserialize<DownloadRecord>(line, Json) is { } r) list.Add(r); } catch (Exception) { }
                // the file only ever grows by appending: cut it back to the newest ones when it has got long
                if (lines.Length > Keep * 3 / 2) File.WriteAllLines(f, list.AsEnumerable().Reverse().Select(r => JsonSerializer.Serialize(r)));
            }
        }
        catch (Exception) { }
        return list;
    });

    public void Add(DownloadRecord r)
    {
        var l = Of(r.UserId);
        lock (l) { l.Insert(0, r); if (l.Count > Keep) l.RemoveRange(Keep, l.Count - Keep); }
        try { File.AppendAllText(FileOf(r.UserId), JsonSerializer.Serialize(r) + "\n"); } catch (Exception) { }
    }

    /// <summary>This user's finished downloads, newest first.</summary>
    public IReadOnlyList<DownloadRecord> For(string userId) { var l = Of(userId); lock (l) return l.ToList(); }

    /// <summary>Every user's, newest first (for admins).</summary>
    public IReadOnlyList<DownloadRecord> Everyone()
    {
        var ids = new HashSet<string>(cache.Keys);
        try { foreach (var f in Directory.EnumerateFiles(Dir, "*.jsonl")) ids.Add(Path.GetFileNameWithoutExtension(f)); } catch (Exception) { }
        return ids.SelectMany(For).OrderByDescending(r => r.FinishedAt).ToList();
    }

    static string Key(string path)
    {
        var p = path;
        var q = p.IndexOf("path=", StringComparison.Ordinal); if (q >= 0) p = Uri.UnescapeDataString(p[(q + 5)..]);
        var segs = p.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segs.Length < 2 ? "" : string.Join('/', segs.TakeLast(3)).ToLowerInvariant();
    }

    /// <summary>The newest successful download that filed this file (matched on the last three path segments, so a Jellyfin path and the file mover's path agree), from anyone's log.</summary>
    public DownloadRecord? FindByFile(string path)
    {
        var key = Key(path);
        if (key.Length == 0) return null;
        return Everyone().FirstOrDefault(r => r.Done && r.File is { Length: > 0 } f && Key(f) == key);
    }
}
