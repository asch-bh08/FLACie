using System.Collections.Concurrent;
using System.Text.Json;
using FLACie.Core;

namespace FLACie.Server.Dj;

/// <summary>
/// F11: how well known each song is, from Deezer's track rank (<see cref="WebCatalog.SongRankAsync"/>), kept on disk on frank for good (a song's rank barely
/// moves, and the DJ asks while someone waits). A lookup Deezer could not answer is not kept, so it is asked again next time.
/// </summary>
public sealed class DjPickPopularity
{
    readonly Func<string, string, CancellationToken, Task<long?>> rank;
    readonly string? file;
    readonly ConcurrentDictionary<string, long> known = new();
    readonly SemaphoreSlim saving = new(1, 1);

    public DjPickPopularity(WebCatalog catalog, string? file) : this(catalog.SongRankAsync, file) { }

    public DjPickPopularity(Func<string, string, CancellationToken, Task<long?>> rank, string? file)
    {
        this.rank = rank; this.file = file;
        try { if (file is not null && File.Exists(file) && JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(file)) is { } saved) foreach (var (k, v) in saved) known[k] = v; }
        catch (Exception e) when (e is IOException or JsonException) { }
    }

    static string Key(Track t) => Matching.MatchKey(t);

    /// <summary>The rank found so far (0 when unknown).</summary>
    public long Of(Track t) => known.GetValueOrDefault(Key(t));

    /// <summary>Looks up the songs not known yet (a handful at most: the versions of one title).</summary>
    public async Task LookUpAsync(IEnumerable<Track> songs, CancellationToken ct)
    {
        var added = false;
        // side by side: one after another, 8 lookups held "play my favourite ..." up for many seconds (owner, 5 Nov)
        await Task.WhenAll(songs.DistinctBy(Key).Where(t => !known.ContainsKey(Key(t))).Take(8).Select(async t =>
        {
            if (await rank(t.Artist, t.Title, ct) is { } r) { known[Key(t)] = r; added = true; }
        }));
        if (added && file is not null)
        {
            await saving.WaitAsync(ct);
            try { Directory.CreateDirectory(Path.GetDirectoryName(file)!); await File.WriteAllTextAsync(file + ".tmp", JsonSerializer.Serialize(known), ct); File.Move(file + ".tmp", file, true); }
            catch (IOException) { }
            finally { saving.Release(); }
        }
    }
}
