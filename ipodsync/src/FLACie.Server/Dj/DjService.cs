using System.Collections.Concurrent;
using System.Text.Json;
using FLACie.Core;

namespace FLACie.Server.Dj;

/// <summary>
/// The DJ for every player of this server: the web and Windows apps ask it in process, the phone through POST /api/dj/next. Each person's settings live in their
/// <see cref="UserState"/>; what the DJ remembers (the last lines, artists, skips) is kept in memory per account. A decision is kept for 30 minutes per
/// (account, previous song, next song), so asking again gives the same answer and moves the memory on only once.
/// </summary>
public sealed class DjService : IDjService
{
    public static readonly TimeSpan DecisionLife = TimeSpan.FromMinutes(30);
    public const string TestLine = "This is the FLACie DJ. Here's a test line.";

    readonly UserStateStore states; readonly CloudflareVoice voice; readonly DjClips clips; readonly ILogger<DjService> log; readonly Func<DateTime> clock;
    readonly string pronounceFile;
    readonly ConcurrentDictionary<string, DjMemory> memories = new();
    readonly ConcurrentDictionary<string, (DateTime At, DjLine? Line)> decisions = new();
    DjPronounce pronounce = DjPronounce.Default; DateTime pronounceStamp = DateTime.MinValue, pronounceChecked = DateTime.MinValue;
    int asks;

    public DjService(UserStateStore states, CloudflareVoice voice, DjClips clips, DataPaths paths, ILogger<DjService> log, Func<DateTime>? clock = null)
    {
        this.states = states; this.voice = voice; this.clips = clips; this.log = log; this.clock = clock ?? (() => DateTime.UtcNow);
        pronounceFile = Path.Combine(paths.Root, "dj-pronounce.tsv");
    }

    public bool VoiceReady => voice.Ready;

    static DjSettings Copy(DjSettings s)
    {
        var c = JsonSerializer.Deserialize<DjSettings>(JsonSerializer.Serialize(s)) ?? new();
        c.Playlists ??= new();
        return c;
    }

    /// <summary>A copy of this person's settings (change it and hand it to <see cref="Save"/>).</summary>
    public DjSettings Settings(UserSession s) => Copy(states.For(s).Dj ?? new());

    public void Save(UserSession s, DjSettings settings)
    {
        var c = Copy(settings);
        // playlist keys are ids: no blanks, nothing huge, a sane number of them
        c.Playlists = c.Playlists.Where(kv => kv.Key.Trim().Length is > 0 and <= 200).Take(1000).ToDictionary(kv => kv.Key.Trim(), kv => kv.Value);
        states.For(s).Dj = c;
        states.Save(s);
    }

    /// <summary>The pronunciation list, read again when <c>dj-pronounce.tsv</c> changes (looked at no more than once a minute).</summary>
    DjPronounce Pronounce()
    {
        var now = clock();
        if (now - pronounceChecked < TimeSpan.FromMinutes(1)) return pronounce;
        pronounceChecked = now;
        try
        {
            var stamp = File.Exists(pronounceFile) ? File.GetLastWriteTimeUtc(pronounceFile) : DateTime.MinValue;
            if (stamp != pronounceStamp) { pronounce = DjPronounce.Load(pronounceFile); pronounceStamp = stamp; }
        }
        catch (Exception) { }
        return pronounce;
    }

    static string SongKey(DjSong? t) => t is null ? "" : t.Path.Length > 0 ? t.Path : t.JellyfinId is { Length: > 0 } j ? "jf:" + j : "t:" + t.Artist + "|" + t.Title;

    /// <summary>Repeatable randomness per decision: the same account, songs and point in the memory give the same draw (string.GetHashCode changes per run, so FNV-1a).</summary>
    static int Seed(string key, int songs)
    {
        unchecked
        {
            var h = 2166136261u;
            foreach (var c in key) { h ^= c; h *= 16777619u; }
            h ^= (uint)songs; h *= 16777619u;
            return (int)(h & 0x7fffffff);
        }
    }

    /// <summary>Decides once per (account, previous song, next song) within <see cref="DecisionLife"/>; the same line (or the same silence) after that.</summary>
    DjLine? Decide(UserSession s, DjSettings st, DjRequest req)
    {
        var key = s.Id + "\n" + SongKey(req.Prev) + "\n" + SongKey(req.Next);
        var now = clock();
        if (decisions.TryGetValue(key, out var d) && now - d.At < DecisionLife) return d.Line;
        var mem = memories.GetOrAdd(s.Id, _ => new DjMemory());
        lock (mem)
        {
            if (decisions.TryGetValue(key, out d) && now - d.At < DecisionLife) return d.Line;
            var line = DjPlanner.Plan(st, req, mem, now, new Random(Seed(key, mem.Songs)), Pronounce());
            decisions[key] = (now, line);
            if (Interlocked.Increment(ref asks) % 200 == 0)
                foreach (var old in decisions.Where(x => now - x.Value.At >= DecisionLife).Select(x => x.Key).ToList()) decisions.TryRemove(old, out _);
            return line;
        }
    }

    public Task<DjCue?> PrepareAsync(UserSession s, DjRequest req, CancellationToken ct = default)
    {
        var st = Settings(s);
        // the switches are read fresh every time: turning the DJ off silences an answer given before
        if (!DjRules.EffectiveOn(st, req.Surface, req.PlaylistId, req.QueueDj)) return Task.FromResult<DjCue?>(null);
        DjLine? line;
        try { line = Decide(s, st, req); }
        catch (Exception e) { log.LogWarning("DJ line not written: {Kind}", e.GetType().Name); return Task.FromResult<DjCue?>(null); }
        if (line is null) return Task.FromResult<DjCue?>(null);
        // no voice, over today's cap or a failed clip: the device's own voice says it, if the person allows that; else the DJ stays quiet
        var id = clips.Request(line.Text);
        if (id is null && !st.Backup) return Task.FromResult<DjCue?>(null);
        return Task.FromResult<DjCue?>(new DjCue(line.Text, line.Caption, id is null ? null : DjClips.UrlOf(id), st.Backup));
    }

    /// <summary>The admin's Test button: makes <see cref="TestLine"/> now (said as the DJ says it) and gives its address, or why it could not.</summary>
    public async Task<(string? Clip, string Message)> TestAsync(CancellationToken ct = default)
    {
        var (id, msg) = await clips.MakeNowAsync(Pronounce().Phrases(TestLine), ct);
        return (id is null ? null : DjClips.UrlOf(id), msg);
    }
}
