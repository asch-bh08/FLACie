using System.Collections.Concurrent;
using FLACie.Core;

namespace FLACie.Server;

/// <summary>
/// The best-known songs of the artists an account plays most (Deezer's top list for each), as match keys, so the Quick picks can bring forward the songs of
/// those artists that most people love, not just any song of theirs. Kept for a day per artist; only artist names are sent out.
/// </summary>
public sealed class HitsService(WebCatalog catalog)
{
    readonly ConcurrentDictionary<string, (DateTime At, string[] Titles)> cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Match keys (title|artist) of the best-known songs of [artists]. Slow the first time (a few lookups), instant after.</summary>
    public async Task<HashSet<string>> KeysAsync(IEnumerable<string> artists, CancellationToken ct = default)
    {
        var keys = new HashSet<string>();
        using var gate = new SemaphoreSlim(4);
        var work = artists.Select(async name =>
        {
            var lead = Matching.PrimaryArtist(name);
            string[] titles;
            if (cache.TryGetValue(lead, out var c) && DateTime.UtcNow - c.At < TimeSpan.FromHours(24)) titles = c.Titles;
            else
            {
                await gate.WaitAsync(ct);
                try { titles = [.. await catalog.TopSongsAsync(name, 25, ct)]; } catch (OperationCanceledException) { throw; } catch (Exception) { titles = []; } finally { gate.Release(); }
                if (titles.Length > 0) cache[lead] = (DateTime.UtcNow, titles);
            }
            lock (keys) foreach (var t in titles) keys.Add(Matching.MatchKey(t, name));
        });
        await Task.WhenAll(work);
        return keys;
    }
}
