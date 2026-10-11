namespace FLACie.Core;

/// <summary>
/// "Dad downloaded ..." for what a user asked FLACie to fetch. A few songs are said live, one message each, as they land. If a lot arrives in a short
/// time (an album, a playlist) the first <see cref="LiveCount"/> are live and the rest become one summary, sent when nothing new arrived for
/// <see cref="Quiet"/> (or after <see cref="Longest"/> at the latest): "47 more songs: A - X, B - Y and 45 more".
/// </summary>
public sealed class DownloadDigest
{
    public const int LiveCount = 3;
    public static readonly TimeSpan Quiet = TimeSpan.FromSeconds(45);
    public static readonly TimeSpan Longest = TimeSpan.FromMinutes(5);
    /// <summary>A burst is over when nothing finished for this long: the live allowance starts again.</summary>
    public static readonly TimeSpan BurstGap = TimeSpan.FromMinutes(5);

    sealed class Batch
    {
        public string Name = ""; public int Live; public DateTime First, Last;
        /// <summary>The first few titles held back (the summary names three), and how many songs there are in all: a burst of thousands stays small.</summary>
        public readonly List<string> Held = []; public readonly HashSet<string> Seen = []; public int HeldCount;
        public void Clear() { Held.Clear(); Seen.Clear(); HeldCount = 0; }
    }

    readonly Action<string, string> publish;
    readonly Func<DateTime> now;
    readonly Func<bool> enabled;
    readonly object gate = new();
    readonly Dictionary<string, Batch> batches = [];

    public DownloadDigest(Action<string, string> publish, Func<DateTime>? now = null, Func<bool>? enabled = null)
    {
        this.publish = publish; this.now = now ?? (() => DateTime.UtcNow); this.enabled = enabled ?? (() => true);
    }

    static string Who(string n) => n.Length == 0 ? "Someone" : n;

    /// <summary>One song finished downloading for this user.</summary>
    public void Done(string user, string userName, string song)
    {
        if (!enabled()) return;
        (string, string)? live = null; (string, string)? old = null;
        lock (gate)
        {
            var t = now();
            if (batches.TryGetValue(user, out var prev) && t - prev.Last >= BurstGap) { if (prev.HeldCount > 0) old = Say(prev, true); batches.Remove(user); }
            if (!batches.TryGetValue(user, out var b)) batches[user] = b = new() { First = t };
            b.Name = userName; b.Last = t;
            if (b.Live < LiveCount) { b.Live++; live = ($"{PlayTracker.TitlePrefix}{Who(userName)} downloaded", song); }
            else if (b.Seen.Count >= 5000 || b.Seen.Add(song)) { b.HeldCount++; if (b.Held.Count < 3) b.Held.Add(song); }
        }
        if (old is { } o) publish(o.Item1, o.Item2);
        if (live is { } l) publish(l.Item1, l.Item2);
    }

    static (string, string) Say(Batch b, bool more)
    {
        var n = b.HeldCount;
        var shown = string.Join(", ", b.Held.Take(3)) + (n > 3 ? $" and {n - 3} more" : "");
        return ($"{PlayTracker.TitlePrefix}{Who(b.Name)} downloaded {n} more", shown);
    }

    /// <summary>Sends the summaries of bursts that went quiet. Called every half minute.</summary>
    public void Sweep()
    {
        var say = new List<(string, string)>();
        lock (gate)
        {
            var t = now();
            foreach (var (user, b) in batches.ToList())
            {
                if (b.HeldCount > 0 && (t - b.Last >= Quiet || t - b.First >= Longest)) { say.Add(Say(b, true)); b.Clear(); b.First = t; }
                if (t - b.Last >= BurstGap) batches.Remove(user);
            }
        }
        foreach (var (title, msg) in say) publish(title, msg);
    }
}
