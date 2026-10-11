using System.Collections.Concurrent;

namespace FLACie.Core;

/// <summary>Cheap counters of the calls this server makes to Jellyfin, grouped by kind (users/me, items, stream, image, info, other), over the last minute.
/// One bucket per second, so a call costs a short lock and an add; used to see what overloads a Jellyfin next time.</summary>
public sealed class JellyfinCallStats
{
    public static readonly string[] Kinds = ["users/me", "items", "stream", "image", "info", "other"];
    const int Seconds = 60;
    readonly Dictionary<string, long[]> buckets = new();   // kind -> per-second counts
    readonly long[] stamps = new long[Seconds];             // which absolute second each slot holds
    readonly object gate = new();
    readonly ConcurrentDictionary<string, long> totals = new();
    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    /// <summary>Which kind a Jellyfin request path (with or without the query) is.</summary>
    public static string Classify(string pathAndQuery)
    {
        var q = pathAndQuery.IndexOf('?'); var p = q < 0 ? pathAndQuery : pathAndQuery[..q];
        if (p.EndsWith("/Users/Me", StringComparison.OrdinalIgnoreCase)) return "users/me";
        if (p.Contains("/Images/", StringComparison.OrdinalIgnoreCase)) return "image";
        if (p.StartsWith("/Audio/", StringComparison.OrdinalIgnoreCase)) return "stream";
        var segs = p.Trim('/').Split('/');
        var user = segs.Length >= 3 && segs[0].Equals("Users", StringComparison.OrdinalIgnoreCase) && segs[2].Equals("Items", StringComparison.OrdinalIgnoreCase);
        // /Users/{id}/Items/{itemId}: one song's own record (the Info panel)
        if (user && segs.Length == 4) return "info";
        if (user || p.StartsWith("/Playlists/", StringComparison.OrdinalIgnoreCase)) return "items";
        return "other";
    }

    /// <summary>Counts a call by its URL (any base: only the path matters).</summary>
    public void HitUrl(string url)
    {
        var s = url.IndexOf("://", StringComparison.Ordinal);
        var slash = s < 0 ? 0 : url.IndexOf('/', s + 3);
        HitKind(slash < 0 ? "other" : Classify(url[slash..]));
    }

    public void HitKind(string kind)
    {
        var now = new DateTimeOffset(Clock()).ToUnixTimeSeconds();
        var slot = (int)(now % Seconds);
        lock (gate)
        {
            if (stamps[slot] != now)
            {
                stamps[slot] = now;
                foreach (var b in buckets.Values) b[slot] = 0;
            }
            if (!buckets.TryGetValue(kind, out var arr)) buckets[kind] = arr = new long[Seconds];
            arr[slot]++;
        }
        totals.AddOrUpdate(kind, 1, (_, n) => n + 1);
    }

    /// <summary>Calls per kind in the last minute (every known kind is present, zero when none).</summary>
    public Dictionary<string, long> LastMinute()
    {
        var now = new DateTimeOffset(Clock()).ToUnixTimeSeconds();
        var res = Kinds.ToDictionary(k => k, _ => 0L);
        lock (gate)
            foreach (var (kind, b) in buckets)
            {
                long sum = 0;
                for (var i = 0; i < Seconds; i++) if (now - stamps[i] is >= 0 and < Seconds) sum += b[i];
                res[kind] = sum;
            }
        return res;
    }

    /// <summary>Calls per kind since the server started.</summary>
    public Dictionary<string, long> Totals() => Kinds.ToDictionary(k => k, k => totals.GetValueOrDefault(k));
}
