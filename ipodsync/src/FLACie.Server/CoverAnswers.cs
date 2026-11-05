namespace FLACie.Server;

/// <summary>What a catalog cover lookup came to. <see cref="Unasked"/>: the catalog could not be asked, or the lookup was cut short (timed out, Deezer's quota, the
/// browser gave up, the picture itself would not download), so it says nothing about whether a cover exists.</summary>
public enum CatalogAnswer { Found, Nothing, Unasked }

/// <summary>
/// Which missing covers are remembered, and for how long the browser keeps the answer. Owner, 5 Nov: many web covers stayed blank although the catalog had them;
/// a lookup cut short had been remembered as "no cover" (for good, in the album cache). Only a real "the catalog has nothing" is remembered, for an hour and only
/// in memory (a restart forgets it); anything else answers 503 with no-store, so the browser's one retry and the next view ask again.
/// </summary>
public static class CoverAnswers
{
    public static readonly TimeSpan MissKept = TimeSpan.FromHours(1);

    public static bool RememberAsMiss(CatalogAnswer a) => a == CatalogAnswer.Nothing;

    /// <summary>The status and Cache-Control for a cover that is not there: a real miss is kept ten minutes by the browser, a transient one not at all.</summary>
    public static (int Status, string CacheControl) Missing(bool transient) => transient ? (503, "no-store") : (404, "private, max-age=600");
}

/// <summary>A small least-recently-used map whose entries also expire (each with its own time to live). Thread-safe.</summary>
public sealed class TimedLru<TKey, TValue>(int capacity, Func<DateTime>? clock = null) where TKey : notnull
{
    readonly Func<DateTime> now = clock ?? (() => DateTime.UtcNow);
    readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value, DateTime Until)>> map = new();
    readonly LinkedList<(TKey Key, TValue Value, DateTime Until)> order = new();

    public int Count { get { lock (map) return map.Count; } }

    public void Set(TKey key, TValue value, TimeSpan ttl)
    {
        lock (map)
        {
            if (map.Remove(key, out var old)) order.Remove(old);
            map[key] = order.AddFirst((key, value, now() + ttl));
            while (map.Count > capacity && order.Last is { } last) { order.RemoveLast(); map.Remove(last.Value.Key); }
        }
    }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (map)
        {
            if (map.TryGetValue(key, out var node))
            {
                if (node.Value.Until > now()) { order.Remove(node); order.AddFirst(node); value = node.Value.Value; return true; }
                order.Remove(node); map.Remove(key);
            }
            value = default!;
            return false;
        }
    }
}
