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
    sealed class Log { public readonly object Gate = new(); public HashSet<string>? Ids; }
    readonly ConcurrentDictionary<string, Log> logs = new();
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    Log Of(UserSession s) => logs.GetOrAdd(s.Id, _ => new Log());
    string FileOf(UserSession s) => Path.Combine(Directory.CreateDirectory(Path.Combine(paths.Root, "listen")).FullName, s.Id + ".jsonl");

    static List<ListenEvent> ReadFile(string file)
    {
        var res = new List<ListenEvent>();
        if (!File.Exists(file)) return res;
        foreach (var line in File.ReadAllLines(file))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try { if (JsonSerializer.Deserialize<ListenEvent>(line, Json) is { } e) res.Add(e); } catch (Exception) { }   // one damaged line must not lose the rest
        }
        return res;
    }

    HashSet<string> IdsOf(Log log, UserSession s) => log.Ids ??= ReadFile(FileOf(s)).Select(e => e.Id).ToHashSet();

    public void Append(UserSession s, IEnumerable<ListenEvent> events)
    {
        var log = Of(s);
        lock (log.Gate)
        {
            var ids = IdsOf(log, s);
            var fresh = events.Where(e => !string.IsNullOrEmpty(e.Id) && ids.Add(e.Id)).ToList();
            if (fresh.Count == 0) return;
            File.AppendAllLines(FileOf(s), fresh.Select(e => JsonSerializer.Serialize(e, Json)));
        }
    }

    public IReadOnlyList<ListenEvent> Read(UserSession s)
    {
        var log = Of(s);
        lock (log.Gate) return ReadFile(FileOf(s)).OrderBy(e => e.At).ToList();
    }

    /// <summary>Plays older than a year are folded into one play per song per day (time heard added up); hearts and the like stay as they are.</summary>
    public void Thin(UserSession s, DateTime utcNow)
    {
        var log = Of(s);
        lock (log.Gate)
        {
            var cutoff = new DateTimeOffset(utcNow.AddDays(-365), TimeSpan.Zero).ToUnixTimeMilliseconds();
            var all = ReadFile(FileOf(s));
            var old = all.Where(e => e.Kind == ListenKind.Play && e.At < cutoff).ToList();
            if (old.Count == 0) return;
            var folded = old.GroupBy(e => (Day: DateTimeOffset.FromUnixTimeMilliseconds(e.At).UtcDateTime.Date, e.Path)).Select(g =>
            {
                var first = g.First();
                return first with { Id = "day:" + g.Key.Day.ToString("yyyy-MM-dd") + ":" + g.Key.Path, At = new DateTimeOffset(g.Key.Day, TimeSpan.Zero).ToUnixTimeMilliseconds(), Heard = g.Sum(e => e.Heard), Length = g.Sum(e => e.Length) };
            });
            var kept = all.Except(old).Concat(folded).OrderBy(e => e.At).ToList();
            var file = FileOf(s);
            File.WriteAllLines(file + ".tmp", kept.Select(e => JsonSerializer.Serialize(e, Json)));
            File.Move(file + ".tmp", file, true);
            log.Ids = kept.Select(e => e.Id).ToHashSet();
        }
    }
}
