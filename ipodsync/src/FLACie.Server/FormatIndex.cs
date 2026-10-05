using System.Collections.Concurrent;
using System.Text.Json;
using FLACie.Core;

namespace FLACie.Server;

/// <summary>What a file really is, as read by ffprobe / Jellyfin / TagLib (never guessed from the extension).</summary>
public sealed record AudioFacts(string Codec, int Rate, int Depth, int Kbps, int Channels)
{
    public bool Lossless => MediaInfo.LosslessCodecs.Contains(Codec.ToLowerInvariant());
    /// <summary>Lossless and better than CD: more than 16 bits or more than 44.1 kHz.</summary>
    public bool HiRes => Lossless && (Depth > 16 || Rate > 44_100);
    public bool Known => Rate > 0 || Depth > 0 || Kbps > 0;
}

/// <summary>Every song's real audio facts, kept on disk so lists can badge songs (Hi-Res, lossless, rate, depth) and Explore can filter
/// by them without asking a server per row. Filled by <see cref="FormatProbeService"/> in the background and by the Info panel.</summary>
public sealed class FormatIndex
{
    /// <summary>The running instance; lists badge songs from static code, so they reach it here.</summary>
    public static FormatIndex? Current { get; private set; }

    readonly ConcurrentDictionary<string, AudioFacts> facts = new();
    readonly string file;
    int dirty;
    public int Count => facts.Count;

    public FormatIndex(DataPaths paths)
    {
        file = Path.Combine(paths.Root, "audiofacts.json");
        try { if (File.Exists(file) && JsonSerializer.Deserialize<Dictionary<string, AudioFacts>>(File.ReadAllText(file)) is { } d) foreach (var (k, v) in d) facts[k] = v; }
        catch (Exception) { /* a damaged cache is just re-probed */ }
        Current = this;
    }

    public static string Key(Track t) => t.Path;
    public AudioFacts? Get(Track t) => facts.TryGetValue(Key(t), out var f) ? f : null;
    public bool Has(Track t) => facts.ContainsKey(Key(t));

    public void Set(Track t, MediaInfo i)
    {
        var codec = i.Codec.Length > 0 ? i.Codec : i.Container;
        var f = new AudioFacts(codec.ToLowerInvariant(), i.SampleRateHz, i.BitDepth, i.BitrateKbps, i.Channels);
        if (!f.Known) return;   // the source could not tell: leave it unprobed so it is tried again later
        facts[Key(t)] = f;
        Interlocked.Exchange(ref dirty, 1);
    }

    public void Flush()
    {
        if (Interlocked.Exchange(ref dirty, 0) == 0) return;
        try { var tmp = file + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(facts)); File.Move(tmp, file, true); }
        catch (Exception) { Interlocked.Exchange(ref dirty, 1); }
    }
}

/// <summary>Walks every signed-in library and reads the real format of each song that has no entry yet. Resumable: what is already in the
/// index is skipped, and the index is saved every few hundred songs, so a restart carries on where it stopped.</summary>
public sealed class FormatProbeService(SessionStore sessions, InfoService info, FormatIndex index, ILogger<FormatProbeService> log) : BackgroundService
{
    public int Pending { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), ct);
        while (!ct.IsCancellationRequested)
        {
            try { await PassAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception e) { log.LogWarning(e, "format probe pass failed"); }
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
        }
    }

    async Task PassAsync(CancellationToken ct)
    {
        foreach (var s in sessions.Active.ToList())
        {
            var todo = s.Library.Songs.Where(t => t.Source is TrackSource.Jellyfin or TrackSource.Nas or TrackSource.Cloud && !index.Has(t)).ToList();
            Pending = todo.Count;
            if (todo.Count == 0) continue;
            log.LogInformation("probing {n} songs for real audio facts", todo.Count);
            var sem = new SemaphoreSlim(6);
            int done = 0;
            await Task.WhenAll(todo.Select(async t =>
            {
                await sem.WaitAsync(ct);
                try { await info.GetAsync(s, t); }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { }
                finally
                {
                    sem.Release(); Pending = Math.Max(0, Pending - 1);
                    if (Interlocked.Increment(ref done) % 200 == 0) index.Flush();
                }
            }));
            index.Flush();
        }
        Pending = 0;
    }
}
