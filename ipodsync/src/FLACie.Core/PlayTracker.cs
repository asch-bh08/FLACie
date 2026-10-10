namespace FLACie.Core;

/// <summary>
/// Turns "this song is playing" into at most a few "music played" notifications, for plays that Jellyfin does not already announce.
/// <list type="bullet">
/// <item>A play only counts for songs whose source was decided at play time to be outside Jellyfin (<see cref="Served"/> with a label such as "NAS");
/// a Jellyfin play is never announced here: the watcher on the Jellyfin side covers it.</item>
/// <item>Every (user, song) has its own listening clock, so a short song is never credited with the length of the one played after it.</item>
/// <item>A song counts after <see cref="MinListen"/> of actual listening; the same song again within <see cref="Debounce"/> is the same play.</item>
/// <item>Plays are announced as runs: one message when a run starts (the first song after <see cref="Idle"/> of silence), one summary when it ends.</item>
/// </list>
/// Nothing here blocks: the publish callback is expected to be fire and forget.
/// </summary>
public sealed class PlayTracker
{
    public static readonly TimeSpan MinListen = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan Debounce = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan Idle = TimeSpan.FromMinutes(10);
    public const string TitlePrefix = "🎵 FLACie · ";

    sealed class Song
    {
        public string Title = "", Artist = "", Device = "", Context = "";
        public double Listened;
        public DateTime? LastTick;
        public bool Decided;          // the 20 s were reached for this load and the outcome is settled
        public bool InRun;            // counted into the user's run: further listening adds to it
        public DateTime? CountedAt;
        public DateTime Touched;
    }

    sealed class Run { public int Tracks; public double Seconds; public DateTime Last; public string Via = ""; }

    readonly Action<string, string> publish;
    readonly Func<DateTime> now;
    readonly Func<bool> enabled;
    readonly object gate = new();
    readonly Dictionary<(string User, string Key), Song> songs = [];
    readonly Dictionary<(string User, string Key), string?> via = [];   // what /stream decided for this song: null = Jellyfin
    readonly Dictionary<string, Run> runs = [];
    readonly Dictionary<string, string> names = [];

    /// <param name="publish">title, message. Called outside the lock, never awaited.</param>
    /// <param name="enabled">whether plays are switched on; when off, nothing is tracked at all.</param>
    public PlayTracker(Action<string, string> publish, Func<DateTime>? now = null, Func<bool>? enabled = null)
    {
        this.publish = publish; this.now = now ?? (() => DateTime.UtcNow); this.enabled = enabled ?? (() => true);
    }

    /// <summary>The stream endpoint resolved this song to a source. [source] is null for Jellyfin, or a label ("NAS", "direct") for a play Jellyfin does not see.</summary>
    public void Served(string user, string key, string? source)
    {
        lock (gate) via[(user, key)] = source;
    }

    /// <summary>A song was loaded in a player: its listening clock starts from zero.</summary>
    public void Begin(string user, string userName, string key, string title, string artist, string device, string? context = null)
    {
        lock (gate)
        {
            names[user] = userName;
            if (!songs.TryGetValue((user, key), out var s)) songs[(user, key)] = s = new();
            s.Title = title; s.Artist = artist; s.Device = device; s.Context = context ?? "";
            s.Listened = 0; s.LastTick = null; s.Decided = false; s.InRun = false; s.Touched = now();
        }
    }

    /// <summary>The player reports it is (not) playing this song. [step] is the longest gap one report may add: a report after a long silence is not listening.</summary>
    public void Tick(string user, string key, bool playing, double step = 5)
    {
        if (!enabled()) return;
        var say = new List<(string, string)>();
        lock (gate)
        {
            if (!songs.TryGetValue((user, key), out var s)) return;
            var t = now(); s.Touched = t;
            if (playing && s.LastTick is { } last)
            {
                var d = Math.Clamp((t - last).TotalSeconds, 0, step);
                s.Listened += d;
                if (s.InRun && runs.TryGetValue(user, out var r)) { r.Seconds += d; r.Last = t; }
            }
            s.LastTick = playing ? t : null;
            if (s.Decided || s.Listened < MinListen.TotalSeconds) goto done;
            s.Decided = true;
            if (!via.TryGetValue((user, key), out var src) || src is null) goto done;    // Jellyfin, or never resolved here: not ours to announce
            if (s.CountedAt is { } at && t - at < Debounce) goto done;                    // the same song again straight away is the same play
            s.CountedAt = t; s.InRun = true;
            runs.TryGetValue(user, out var run);
            if (run is not null && t - run.Last >= Idle) { say.Add(Summary(user, run)); runs.Remove(user); run = null; }
            if (run is null) runs[user] = new Run { Tracks = 1, Seconds = s.Listened, Last = t, Via = src };
            else { run.Tracks++; run.Seconds += s.Listened; run.Last = t; run.Via = src; }
            // live: every song that is really listened to (past the 20 s) is announced as it goes; skipping through songs says nothing
            say.Add(($"{TitlePrefix}{Name(user)} is listening", $"{Label(s)}{(s.Context.Length > 0 ? " · from " + s.Context : "")} · via {src} · {s.Device}"));
        }
    done:
        foreach (var (title, msg) in say) publish(title, msg);
    }

    /// <summary>Closes runs that have been quiet for ten minutes (one summary each) and forgets old songs. Called every half minute.</summary>
    public void Sweep()
    {
        var say = new List<(string, string)>();
        lock (gate)
        {
            var t = now();
            foreach (var (user, run) in runs.ToList())
            {
                if (t - run.Last < Idle) continue;
                say.Add(Summary(user, run)); runs.Remove(user);
            }
            foreach (var k in songs.Where(p => t - p.Value.Touched > TimeSpan.FromHours(1)).Select(p => p.Key).ToList()) { songs.Remove(k); via.Remove(k); }
        }
        foreach (var (title, msg) in say) publish(title, msg);
    }

    (string, string) Summary(string user, Run run)
    {
        var mins = Math.Max(1, (int)Math.Round(run.Seconds / 60));
        return ($"{TitlePrefix}{Name(user)} finished", $"{run.Tracks} track{(run.Tracks == 1 ? "" : "s")} · {mins} min · via {run.Via}");
    }

    string Name(string user) => names.TryGetValue(user, out var n) && n.Length > 0 ? n : "Someone";
    static string Label(Song s) => s.Artist.Length == 0 ? s.Title : $"{s.Title} - {s.Artist}";
}
