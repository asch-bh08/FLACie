using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FLACie.Core;

namespace FLACie.Server.Dj;

/// <summary>What the pronunciation check needs from the voice: make a clip, hear one back, and the budget. <see cref="CloudflareEar"/> on the server; fakes in tests.</summary>
public interface IDjEar
{
    /// <summary>The voice in use ("aura-1:helios", "melotts").</summary>
    string Voice { get; }
    bool Ready { get; }
    /// <summary>What saying [text] and hearing it back costs, roughly, in neurons.</summary>
    double Cost(string text);
    /// <summary>Books [neurons] to today's check; false when they don't fit under its share (the check stops for today).</summary>
    bool Take(double neurons);
    Task<byte[]?> SayAsync(string text, string voice, CancellationToken ct);
    Task<string?> HearAsync(byte[] audio, CancellationToken ct);
}

/// <summary>The real ear: Cloudflare's voice for the clip, Whisper to hear it, at most <see cref="CloudflareVoice.CheckShare"/> of the daily cap.</summary>
public sealed class CloudflareEar(CloudflareVoice v) : IDjEar
{
    public string Voice => v.Current.Voice;
    public bool Ready => v.Ready;
    public double Cost(string text) => CloudflareVoice.EstimateNeurons(Voice, text) + CloudflareVoice.EstimateHearNeurons(DjTiming.EstimateMs(text) * 6);
    public bool Take(double neurons) => v.TakeForChecks(neurons);
    public Task<byte[]?> SayAsync(string text, string voice, CancellationToken ct) => v.SynthesizeAsync(text, voice, ct);
    public Task<string?> HearAsync(byte[] audio, CancellationToken ct) => v.TranscribeAsync(audio, ct);
}

/// <summary>data/dj-pronounce.json: per voice, every name checked by ear and what was kept; and the library's most played artists, last seen.</summary>
public sealed class DjPronounceFile
{
    public Dictionary<string, DjVoiceChecks> Voices { get; set; } = new();
    public List<string> Artists { get; set; } = [];
}

public sealed class DjVoiceChecks
{
    /// <summary>The list of names (and respellings) this voice was last fully checked against; when the list changes, what is new is checked.</summary>
    public string Table { get; set; } = "";
    /// <summary>By the written name, lower case.</summary>
    public Dictionary<string, DjNameCheck> Names { get; set; } = new();
}

public sealed class DjNameCheck
{
    public string Written { get; set; } = "";
    /// <summary>The respelling tried; null when there was none (the name alone was checked).</summary>
    public string? Candidate { get; set; }
    /// <summary>What is said: the respelling when it was heard better, null for the written name.</summary>
    public string? Say { get; set; }
    public string HeardPlain { get; set; } = "";
    public string? HeardRespelt { get; set; }
    public double Plain { get; set; }
    public double Respelt { get; set; }
    public DateTime At { get; set; }
}

/// <summary>
/// The DJ's names checked by ear, per voice (docs/spec/ai-dj.md, A.5): for every name in the respelling list and each of the library's ~50 most played
/// artists, the voice says the plain name and the respelling, Whisper writes down what it heard, and whichever comes back nearer the written name is kept
/// (<see cref="DjHearing"/>). A respelling that is no better than the plain name is dropped. Runs by itself when the voice or the list changes, and from
/// the dashboard's "Check pronunciations"; never more than <see cref="CloudflareVoice.CheckShare"/> of a day's cap, so a long list takes a few days.
/// </summary>
public sealed class DjPronounceCheck
{
    public const int MostPlayed = 50;
    readonly IDjEar ear; readonly string file, tableFile; readonly ILogger log; readonly Func<DateTime> clock;
    readonly SemaphoreSlim running = new(1, 1);
    readonly object gate = new();
    DjPronounceFile data = new();
    /// <summary>The library's most played artists, now (the server's signed-in people); null in tests that leave it out.</summary>
    public Func<IReadOnlyList<string>>? Artists { get; set; }
    /// <summary>The last run's summary, for the dashboard ("" before any).</summary>
    public string LastSummary { get; private set; } = "";

    public DjPronounceCheck(IDjEar ear, DataPaths paths, ILogger<DjPronounceCheck> log, Func<DateTime>? clock = null)
    {
        this.ear = ear; this.log = log; this.clock = clock ?? (() => DateTime.UtcNow);
        file = FileIn(paths.Root); tableFile = Path.Combine(paths.Root, "dj-pronounce.tsv");
        try { if (File.Exists(file)) data = JsonSerializer.Deserialize<DjPronounceFile>(File.ReadAllText(file)) ?? new(); } catch (Exception) { }
    }

    public static string FileIn(string root) => Path.Combine(root, "dj-pronounce.json");

    public sealed record Item(string Written, string? Candidate);
    public sealed record Summary(int Kept, int Dropped, int Failed, int Left, bool Stopped)
    {
        public override string ToString() => $"kept {Kept}, dropped {Dropped}, failed {Failed}" + (Left > 0 ? $", {Left} left for another day" : "") + (Stopped ? " (today's share used)" : "");
    }

    /// <summary>What to check: every entry of the respelling list (the built-in one and dj-pronounce.tsv), then the most played artists with the respelling the
    /// list would give them (none when it leaves them alone). One per name.</summary>
    public List<Item> Items()
    {
        var table = DjPronounce.Load(tableFile);
        var res = new List<Item>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in table.Entries) if (seen.Add(e.Written)) res.Add(new(e.Written, e.Say));
        IReadOnlyList<string> artists;
        try { artists = Artists?.Invoke() ?? []; } catch (Exception) { artists = []; }
        lock (gate)
        {
            if (artists.Count > 0) data.Artists = artists.Take(MostPlayed).ToList();
            artists = data.Artists;
        }
        foreach (var a in artists.Take(MostPlayed))
        {
            if (!seen.Add(a)) continue;
            var say = table.Artist(a) ?? table.Phrases(a);
            res.Add(new(a, string.Equals(say, a, StringComparison.Ordinal) ? null : say));
        }
        return res;
    }

    static string Hash(IEnumerable<Item> items) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', items.Select(i => i.Written + "\t" + i.Candidate)))))[..16];

    /// <summary>The voice was fully checked against today's list.</summary>
    public bool UpToDate(string voice, IReadOnlyList<Item> items) { lock (gate) return data.Voices.TryGetValue(voice, out var v) && v.Table == Hash(items); }

    /// <summary>A copy of what is known for [voice].</summary>
    public IReadOnlyList<DjNameCheck> Checked(string voice) { lock (gate) return data.Voices.TryGetValue(voice, out var v) ? v.Names.Values.ToList() : []; }

    /// <summary>Runs the check when the voice hasn't been checked against the list as it is now. Null when it had nothing to do or another run is on.</summary>
    public async Task<Summary?> RunIfDueAsync(CancellationToken ct = default)
    {
        if (!ear.Ready) return null;
        var items = Items();
        return UpToDate(ear.Voice, items) ? null : await RunAsync(items, ct);
    }

    /// <summary>The dashboard's button: checks what isn't checked yet for the voice in use.</summary>
    public Task<Summary?> RunNowAsync(CancellationToken ct = default) => ear.Ready ? RunAsync(Items(), ct) : Task.FromResult<Summary?>(null);

    /// <summary>Checks every item not yet checked with its present respelling in the voice in use, until today's share is used.</summary>
    public async Task<Summary?> RunAsync(IReadOnlyList<Item> items, CancellationToken ct = default)
    {
        if (!await running.WaitAsync(0, ct)) return null;
        try
        {
            var voice = ear.Voice;
            int kept = 0, dropped = 0, failed = 0; var stopped = false;
            var todo = items.Where(i => !Done(voice, i)).ToList();
            var left = todo.Count;
            foreach (var it in todo)
            {
                if (ct.IsCancellationRequested) break;
                var cost = ear.Cost(it.Written) + (it.Candidate is { } c0 ? ear.Cost(c0) : 0);
                if (!ear.Take(cost)) { stopped = true; break; }
                left--;
                var plain = await HearAsync(it.Written, voice, ct);
                string? respelt = null;
                if (plain is not null && it.Candidate is { } cand) respelt = await HearAsync(cand, voice, ct);
                if (plain is null || (it.Candidate is not null && respelt is null)) { failed++; continue; }
                var ps = DjHearing.Score(it.Written, plain);
                var rs = respelt is null ? 0 : DjHearing.Score(it.Written, respelt);
                var keep = it.Candidate is not null && DjHearing.Choose(ps, rs) == DjHearing.Verdict.Respelt;
                if (it.Candidate is not null) { if (keep) kept++; else dropped++; }
                Record(voice, new DjNameCheck
                {
                    Written = it.Written, Candidate = it.Candidate, Say = keep ? it.Candidate : null, HeardPlain = plain, HeardRespelt = respelt, Plain = ps, Respelt = rs, At = clock(),
                });
            }
            if (!stopped && failed == 0 && !ct.IsCancellationRequested) lock (gate) Of(voice).Table = Hash(items);
            Save();
            var sum = new Summary(kept, dropped, failed, left, stopped);
            LastSummary = $"{voice}: {sum}";
            log.LogInformation("DJ pronunciation check ({Voice}): {Summary}", voice, sum.ToString());
            return sum;
        }
        finally { running.Release(); }
    }

    async Task<string?> HearAsync(string text, string voice, CancellationToken ct)
    {
        try
        {
            var audio = await ear.SayAsync(text, voice, ct);
            return audio is { Length: > 0 } ? await ear.HearAsync(audio, ct) : null;
        }
        catch (Exception e) when (e is not OperationCanceledException) { return null; }
    }

    DjVoiceChecks Of(string voice) => data.Voices.TryGetValue(voice, out var v) ? v : data.Voices[voice] = new();

    bool Done(string voice, Item i)
    {
        lock (gate) return data.Voices.TryGetValue(voice, out var v) && v.Names.TryGetValue(i.Written.ToLowerInvariant(), out var n) && n.Candidate == i.Candidate;
    }

    void Record(string voice, DjNameCheck n) { lock (gate) Of(voice).Names[n.Written.ToLowerInvariant()] = n; }

    void Save()
    {
        string json; lock (gate) json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        try { Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, json); } catch (Exception e) { log.LogWarning("Saving the DJ's pronunciation checks failed: {Kind}", e.GetType().Name); }
    }

    /// <summary>The checked table for [voice] as <see cref="DjPronounce"/> takes it: the written name → the respelling kept, or null to say it as written.
    /// Only names that had a respelling to try are in it.</summary>
    public static Dictionary<string, string?> Heard(DjPronounceFile f, string voice) =>
        f.Voices.TryGetValue(voice, out var v)
            ? v.Names.Values.Where(n => n.Candidate is not null).ToDictionary(n => n.Written, n => n.Say, StringComparer.OrdinalIgnoreCase)
            : new(StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, string?> Heard(string path, string voice)
    {
        try { if (File.Exists(path)) return Heard(JsonSerializer.Deserialize<DjPronounceFile>(File.ReadAllText(path)) ?? new(), voice); } catch (Exception) { }
        return new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The most played artists of everyone signed in here, in order (each person's own top list, merged in turn).</summary>
    public static List<string> TopArtists(IEnumerable<UserSession> sessions, DateTime utcNow, int n = MostPlayed)
    {
        var lists = new List<List<string>>();
        foreach (var s in sessions)
            try { lists.Add(HomeBuilder.TopArtists(s.Library, s.History, s.Favorites, [], utcNow, s.PlayStats, n)); } catch (Exception) { }
        var res = new List<string>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; res.Count < n && lists.Any(l => i < l.Count); i++)
            foreach (var l in lists) if (i < l.Count && seen.Add(l[i]) && res.Count < n) res.Add(l[i]);
        return res;
    }
}

/// <summary>Looks once an hour (and soon after a start) for a pronunciation check that is due: a new voice, a changed list, or yesterday's share used up.</summary>
public sealed class DjPronounceJob(DjPronounceCheck check, ILogger<DjPronounceJob> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), stop); } catch (OperationCanceledException) { return; }
        while (!stop.IsCancellationRequested)
        {
            try { await check.RunIfDueAsync(stop); }
            catch (OperationCanceledException) { return; }
            catch (Exception e) { log.LogWarning("DJ pronunciation check failed: {Kind}", e.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromHours(1), stop); } catch (OperationCanceledException) { return; }
        }
    }
}
