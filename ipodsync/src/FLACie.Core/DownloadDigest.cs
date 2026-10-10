namespace FLACie.Core;

/// <summary>
/// Collects the songs a user's downloads finished and says it once, for example "Dad downloaded: 3 songs: A - X, B - Y, C - Z". A burst of downloads
/// (an album, a handful of searches) becomes one message, sent when no new song arrived for <see cref="Quiet"/> (or after <see cref="Longest"/> at the latest).
/// </summary>
public sealed class DownloadDigest
{
    public static readonly TimeSpan Quiet = TimeSpan.FromSeconds(45);
    public static readonly TimeSpan Longest = TimeSpan.FromMinutes(5);

    sealed class Batch { public string Name = ""; public readonly List<string> Songs = []; public DateTime First, Last; }

    readonly Action<string, string> publish;
    readonly Func<DateTime> now;
    readonly Func<bool> enabled;
    readonly object gate = new();
    readonly Dictionary<string, Batch> batches = [];

    public DownloadDigest(Action<string, string> publish, Func<DateTime>? now = null, Func<bool>? enabled = null)
    {
        this.publish = publish; this.now = now ?? (() => DateTime.UtcNow); this.enabled = enabled ?? (() => true);
    }

    /// <summary>One song finished downloading for this user.</summary>
    public void Done(string user, string userName, string song)
    {
        if (!enabled()) return;
        lock (gate)
        {
            var t = now();
            if (!batches.TryGetValue(user, out var b)) batches[user] = b = new() { First = t };
            b.Name = userName; b.Last = t;
            if (!b.Songs.Contains(song)) b.Songs.Add(song);
        }
    }

    /// <summary>Sends the batches that went quiet. Called every half minute.</summary>
    public void Sweep()
    {
        var say = new List<(string, string)>();
        lock (gate)
        {
            var t = now();
            foreach (var (user, b) in batches.ToList())
            {
                if (t - b.Last < Quiet && t - b.First < Longest) continue;
                batches.Remove(user);
                var n = b.Songs.Count;
                var shown = string.Join(", ", b.Songs.Take(3)) + (n > 3 ? $" and {n - 3} more" : "");
                say.Add(($"{PlayTracker.TitlePrefix}{(b.Name.Length == 0 ? "Someone" : b.Name)} downloaded", $"{n} song{(n == 1 ? "" : "s")}: {shown}"));
            }
        }
        foreach (var (title, msg) in say) publish(title, msg);
    }
}
