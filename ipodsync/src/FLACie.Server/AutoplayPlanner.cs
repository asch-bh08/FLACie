using System.Collections.Concurrent;
using FLACie.Core;

namespace FLACie.Server;

/// <summary>
/// What plays when the queue runs out. Looks up songs that go with the one playing (the artist's other well-known songs and those of
/// artists their listeners also play), never another release of a song already played or queued, and, when a song isn't in the library
/// yet, fetches it in the background a few songs ahead so it is there by the time it is needed.
/// </summary>
public sealed class AutoplayPlanner(WebCatalog catalog, DownloadManager downloads, UserStateStore states, StorageGuard guard)
{
    readonly ConcurrentDictionary<string, (DateTime At, List<CatalogSong> Songs)> cache = new();

    public async Task<List<CatalogSong>> RelatedAsync(Track seed, CancellationToken ct = default)
    {
        var key = Matching.MatchKey(seed);
        if (cache.TryGetValue(key, out var c) && DateTime.UtcNow - c.At < TimeSpan.FromMinutes(30)) return c.Songs;
        List<CatalogSong> songs;
        try { songs = await catalog.RelatedAsync(seed.Artist, seed.Title, 24, ct); } catch (OperationCanceledException) { throw; } catch (Exception) { songs = []; }
        if (cache.Count > 300) cache.Clear();
        cache[key] = (DateTime.UtcNow, songs);
        return songs;
    }

    /// <summary>Starts fetching up to [max] of the songs the library doesn't have, [onEach] called as each arrives (or fails).</summary>
    public void Prefetch(UserSession s, IEnumerable<CatalogSong> wanted, int max, Action onEach)
    {
        if (!states.For(s).Prefetch || !s.Services.Any) return;
        var n = 0;
        foreach (var song in wanted)
        {
            if (n >= max) break;
            if (downloads.RecentlyFailed(s, song.Artist, song.Title)) continue;
            if (!guard.Ok(s)) break;
            n++;
            _ = downloads.FetchAsync(s, "Autoplay", song.Artist, song.Title, song.Album, song.ArtUrl, song.DurationMs)
                .ContinueWith(_ => onEach(), TaskScheduler.Default);
        }
    }
}
