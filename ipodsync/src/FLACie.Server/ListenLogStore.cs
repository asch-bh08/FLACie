using System.Collections.Concurrent;
using System.Text.Json;
using FLACie.Core;

namespace FLACie.Server;

/// <summary>
/// Each account's listening history, one JSON object per line in data/listen/[account].jsonl. It is what the
/// recommendations are worked out from, so a resent event is ignored and old plays are folded into daily totals.
/// </summary>
public sealed class ListenLogStore(DataPaths paths)
{
    /// <summary>One account's log as last read or written: its ids, and its events in time order (handed out as is, never changed in place).</summary>
    sealed class Log { public readonly object Gate = new(); public HashSet<string>? Ids; public IReadOnlyList<ListenEvent>? Events; }
    readonly ConcurrentDictionary<string, Log> logs = new();
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    Log Of(UserSession s) => logs.GetOrAdd(s.Id, _ => new Log());
    string FileOf(UserSession s) => Path.Combine(Directory.CreateDirectory(Path.Combine(paths.Root, "listen")).FullName, s.Id + ".jsonl");

    static List<ListenEvent> ReadFile(string file) => ReadFile(file, out _);

    /// <summary>The good events; lines that can't be read (a half-written one) are skipped here and handed back in [damaged] so they are never lost.</summary>
    static List<ListenEvent> ReadFile(string file, out List<string> damaged)
    {
        var res = new List<ListenEvent>();
        damaged = [];
        if (!File.Exists(file)) return res;
        foreach (var line in File.ReadAllLines(file))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try { if (JsonSerializer.Deserialize<ListenEvent>(line, Json) is { } e) res.Add(e); else damaged.Add(line); }
            catch (JsonException) { damaged.Add(line); }   // one damaged line must not lose the rest
        }
        return res;
    }

    /// <summary>The file is read once; after that the copy in memory is kept up to date by Append and Thin, so a pick never waits on the disk.</summary>
    IReadOnlyList<ListenEvent> EventsOf(Log log, UserSession s) => log.Events ??= ReadFile(FileOf(s)).OrderBy(e => e.At).ToArray();

    HashSet<string> IdsOf(Log log, UserSession s) => log.Ids ??= EventsOf(log, s).Select(e => e.Id).ToHashSet();

    /// <summary>Has this account's log been started yet?</summary>
    public bool Exists(UserSession s) { lock (Of(s).Gate) return File.Exists(FileOf(s)); }

    /// <summary>Makes sure the account has a (possibly empty) log file.</summary>
    public void Touch(UserSession s) { lock (Of(s).Gate) { var f = FileOf(s); if (!File.Exists(f)) File.WriteAllText(f, ""); } }

    /// <summary>Adds the events not already kept and says how many that was.</summary>
    public int Append(UserSession s, IEnumerable<ListenEvent> events)
    {
        var log = Of(s);
        lock (log.Gate)
        {
            var ids = IdsOf(log, s);
            var fresh = events.Where(e => !string.IsNullOrEmpty(e.Id) && ids.Add(e.Id)).ToList();
            if (fresh.Count == 0) return 0;
            var file = FileOf(s);
            if (EndsMidLine(file)) File.AppendAllText(file, "\n");   // a torn last line must not swallow the next event
            File.AppendAllLines(file, fresh.Select(e => JsonSerializer.Serialize(e, Json)));
            // a new array, so a list already handed out stays as it was
            log.Events = EventsOf(log, s).Concat(fresh).OrderBy(e => e.At).ToArray();
            return fresh.Count;
        }
    }

    static bool EndsMidLine(string file)
    {
        if (!File.Exists(file)) return false;
        using var f = File.OpenRead(file);
        if (f.Length == 0) return false;
        f.Seek(-1, SeekOrigin.End);
        return f.ReadByte() != '\n';
    }

    /// <summary>The account's events in time order. The list is shared and never changes: later events go into a new one.</summary>
    public IReadOnlyList<ListenEvent> Read(UserSession s)
    {
        var log = Of(s);
        lock (log.Gate) return EventsOf(log, s);
    }

    /// <summary>Plays older than a year are folded into one play per song per day (time heard added up); hearts and the like stay as they are.</summary>
    public void Thin(UserSession s, DateTime utcNow)
    {
        var log = Of(s);
        lock (log.Gate)
        {
            var cutoff = new DateTimeOffset(utcNow.AddDays(-365), TimeSpan.Zero).ToUnixTimeMilliseconds();
            var all = ReadFile(FileOf(s), out var damaged);
            var old = all.Where(e => e.Kind == ListenKind.Play && e.At < cutoff).ToList();
            if (old.Count == 0) return;
            var folded = old.GroupBy(e => (Day: DateTimeOffset.FromUnixTimeMilliseconds(e.At).UtcDateTime.Date, e.Path)).Select(g =>
            {
                var first = g.First();
                return first with { Id = "day:" + g.Key.Day.ToString("yyyy-MM-dd") + ":" + g.Key.Path, At = new DateTimeOffset(g.Key.Day, TimeSpan.Zero).ToUnixTimeMilliseconds(), Heard = g.Sum(e => e.Heard), Length = g.Sum(e => e.Length) };
            });
            var kept = all.Except(old).Concat(folded).OrderBy(e => e.At).ToList();
            var file = FileOf(s);
            File.WriteAllLines(file + ".tmp", damaged.Concat(kept.Select(e => JsonSerializer.Serialize(e, Json))));
            File.Move(file + ".tmp", file, true);
            log.Ids = kept.Select(e => e.Id).ToHashSet();
            log.Events = kept.ToArray();
        }
    }
}
