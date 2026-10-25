using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FLACie.Core;

namespace FLACie.Server;

/// <summary>
/// The songs that go with a song (<see cref="WebCatalog.RelatedAsync"/>), kept 7 days in data/related/. Looking them up online takes seconds and can
/// fail, so a recommendation never waits for it: <see cref="Peek"/> answers at once with what is kept (or nothing) and fetches it in the background
/// when it is missing or old, for the next time.
/// </summary>
public sealed class RelatedCache(DataPaths paths, Func<string, string, CancellationToken, Task<List<CatalogSong>>> fetch)
{
    static readonly TimeSpan Keep = TimeSpan.FromDays(7);
    sealed record Entry(DateTime At, List<CatalogSong> Songs);
    readonly ConcurrentDictionary<string, Entry> kept = new();
    readonly ConcurrentDictionary<string, Task> fetching = new();

    public RelatedCache(DataPaths paths, WebCatalog catalog) : this(paths, (a, t, ct) => catalog.RelatedAsync(a, t, 24, ct)) { }

    /// <summary>Done when every lookup started so far has finished (for tests).</summary>
    public Task Pending => Task.WhenAll(fetching.Values);

    string FileOf(string key) => Path.Combine(Directory.CreateDirectory(Path.Combine(paths.Root, "related")).FullName,
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant() + ".json");

    public IReadOnlyList<CatalogSong> Peek(string artist, string title)
    {
        var key = Matching.MatchKey(title, artist);
        var e = kept.TryGetValue(key, out var m) ? m : Load(key);
        if (e is null || DateTime.UtcNow - e.At > Keep) Refresh(key, artist, title);
        return e?.Songs ?? [];
    }

    Entry? Load(string key)
    {
        try
        {
            var f = FileOf(key);
            if (!File.Exists(f) || JsonSerializer.Deserialize<List<CatalogSong>>(File.ReadAllText(f)) is not { } songs) return null;
            return kept[key] = new Entry(File.GetLastWriteTimeUtc(f), songs);
        }
        catch (Exception) { return null; }   // a damaged file is fetched again
    }

    void Refresh(string key, string artist, string title)
    {
        var done = new TaskCompletionSource();
        if (!fetching.TryAdd(key, done.Task)) return;   // already on its way
        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var songs = await fetch(artist, title, cts.Token);
                kept[key] = new Entry(DateTime.UtcNow, songs);
                var f = FileOf(key);
                File.WriteAllText(f + ".tmp", JsonSerializer.Serialize(songs));
                File.Move(f + ".tmp", f, true);
            }
            catch (Exception) { /* offline or refused: tried again at the next Peek */ }
            finally { fetching.TryRemove(key, out _); done.SetResult(); }
        });
    }
}
