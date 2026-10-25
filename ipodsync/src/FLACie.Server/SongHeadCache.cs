namespace FLACie.Server;

/// <summary>
/// The start of the songs about to play, kept in memory. The page reads the start of the next song early (pre=1); the server used to throw those bytes away,
/// so Next waited for Jellyfin's first byte all over again: 3-4 s of nothing between songs on the owner's server. Now the early read keeps them here and the
/// real request sends them at once, while the rest of the song is asked of Jellyfin behind them. Per account (never another account's bytes), a few songs,
/// a few MB, and only for a while: light on memory.
/// </summary>
public sealed class SongHeadCache(int maxEntries, long maxBytes, TimeSpan keep, Func<long>? clockMs = null)
{
    public sealed record Head(byte[] Bytes, long Total, string ContentType);
    sealed record Entry(Head Head, long At, long Seq);

    readonly Func<long> now = clockMs ?? (() => Environment.TickCount64);
    readonly Dictionary<(string, string), Entry> map = [];
    readonly object gate = new();
    long seq;

    public SongHeadCache() : this(6, 8L << 20, TimeSpan.FromMinutes(10)) { }

    public void Put(string owner, string path, byte[] bytes, long total, string contentType)
    {
        if (bytes.Length == 0 || bytes.Length > maxBytes) return;
        lock (gate)
        {
            map[(owner, path)] = new Entry(new Head(bytes, total, contentType), now(), ++seq);
            // the oldest go first, until the count and the size are within bounds
            while (map.Count > maxEntries || map.Values.Sum(e => (long)e.Head.Bytes.Length) > maxBytes)
                map.Remove(map.MinBy(kv => kv.Value.Seq).Key);   // by order kept: two in the same millisecond still have an order
        }
    }

    public Head? Get(string owner, string path)
    {
        lock (gate)
        {
            if (!map.TryGetValue((owner, path), out var e)) return null;
            if (now() - e.At > keep.TotalMilliseconds) { map.Remove((owner, path)); return null; }
            return e.Head;
        }
    }

    readonly Dictionary<(string, string), TaskCompletionSource> reading = [];

    /// <summary>An early read of a song's start has begun; call what is returned when it ends (kept or not).</summary>
    public Action Begin(string owner, string path)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate) reading[(owner, path)] = done;
        return () => { lock (gate) { if (reading.TryGetValue((owner, path), out var d) && d == done) reading.Remove((owner, path)); } done.TrySetResult(); };
    }

    /// <summary>The kept start, waiting (up to <paramref name="wait"/>) for an early read of it that is still on its way: Next pressed soon after a song started
    /// used to start the next one from scratch while its early read was moments from done.</summary>
    public async Task<Head?> GetAsync(string owner, string path, TimeSpan wait)
    {
        Task? onItsWay;
        lock (gate) onItsWay = reading.TryGetValue((owner, path), out var d) ? d.Task : null;
        if (onItsWay is not null) await Task.WhenAny(onItsWay, Task.Delay(wait));
        return Get(owner, path);
    }

    /// <summary>What a request is sent from: the kept start ([HeadFrom..HeadTo]) and then Jellyfin ([UpFrom..UpTo], none when -1). Null when the request
    /// does not begin inside the kept start (a seek further on): Jellyfin as usual.</summary>
    public readonly record struct Served(bool Partial, long From, long To, long HeadFrom, long HeadTo, long UpFrom, long UpTo);

    public static Served? Plan(string range, int headLength, long total)
    {
        var r = ByteRanges.Resolve(range, total);
        if (!r.Satisfiable || r.From >= headLength) return null;
        var headTo = Math.Min(r.To, headLength - 1);
        return r.To < headLength
            ? new Served(r.Partial, r.From, r.To, r.From, headTo, -1, -1)
            : new Served(r.Partial, r.From, r.To, r.From, headTo, headLength, r.To);
    }
}
