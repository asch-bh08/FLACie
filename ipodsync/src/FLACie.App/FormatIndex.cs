using System.Collections.Concurrent;
using System.Text.Json;
using FLACie.Core;

namespace FLACie.Server;

/// <summary>What a file really is, as read by ffprobe / Jellyfin / TagLib (never guessed from the extension).</summary>
public sealed record AudioFacts(string Codec, int Rate, int Depth, int Kbps, int Channels)
{
    public bool Lossless => MediaInfo.LosslessCodecs.Contains(Codec.ToLowerInvariant());
    /// <summary>Lossless and better than CD: more than 16 bits or more than 44.1 kHz.</summary>
    public bool HiRes => MediaInfo.IsHiRes(Lossless, Depth, Rate);
    public bool Known => Rate > 0 || Depth > 0 || Kbps > 0;
}

/// <summary>Every song's real audio facts, kept on disk so lists can badge songs (Hi-Res, lossless, rate, depth) and Explore can filter
/// by them without asking a server per row. Filled by <see cref="FormatProbeService"/> in the background and by the Info panel.</summary>
public sealed class FormatIndex
{
    /// <summary>The running instance; lists badge songs from static code, so they reach it here.</summary>
    public static FormatIndex? Current { get; private set; }

    readonly ConcurrentDictionary<string, AudioFacts> facts = new();
    /// <summary>Songs whose read gave nothing usable: how many times in a row, and the earliest time to try again (<see cref="ProbeBackoff"/>). Kept on disk.</summary>
    public sealed record Miss(int Fails, DateTime Next);
    readonly ConcurrentDictionary<string, Miss> misses = new();
    readonly string file, missFile;
    int dirty, missDirty;
    public int Count => facts.Count;
    public int MissCount => misses.Count;

    public FormatIndex(DataPaths paths)
    {
        file = Path.Combine(paths.Root, "audiofacts.json");
        missFile = Path.Combine(paths.Root, "audiofacts-misses.json");
        try { if (File.Exists(file) && JsonSerializer.Deserialize<Dictionary<string, AudioFacts>>(File.ReadAllText(file)) is { } d) foreach (var (k, v) in d) facts[k] = v; }
        catch (Exception) { /* a damaged cache is just re-probed */ }
        try { if (File.Exists(missFile) && JsonSerializer.Deserialize<Dictionary<string, Miss>>(File.ReadAllText(missFile)) is { } m) foreach (var (k, v) in m) misses[k] = v; }
        catch (Exception) { }
        Current = this;
    }

    /// <summary>True when the song has no real read and is not waiting out a back-off: the background probe may try it now.</summary>
    public bool Due(Track t, DateTime now) => !misses.TryGetValue(Key(t), out var m) || now >= m.Next;

    /// <summary>A read of this song gave nothing: leave it alone for longer each time (1 h, 6 h, 24 h, 7 d).</summary>
    public void MarkMiss(Track t, DateTime now)
    {
        misses.AddOrUpdate(Key(t), _ => new Miss(1, now + ProbeBackoff.Delay(1)), (_, old) => new Miss(old.Fails + 1, now + ProbeBackoff.Delay(old.Fails + 1)));
        Interlocked.Exchange(ref missDirty, 1);
    }

    public Miss? MissOf(Track t) => misses.TryGetValue(Key(t), out var m) ? m : null;

    public static string Key(Track t) => t.Path;
    public AudioFacts? Get(Track t) => facts.TryGetValue(Key(t), out var f) ? f : null;
    public bool Has(Track t) => facts.ContainsKey(Key(t));

    public void Set(Track t, MediaInfo i)
    {
        var codec = i.Codec.Length > 0 ? i.Codec : i.Container;
        // the Info panel's rule is "the codec OR the container says lossless" (a raw PCM variant such as pcm_f32le in a WAV): keep the badge in step with it
        if (i.Lossless && !MediaInfo.LosslessCodecs.Contains(codec.ToLowerInvariant()) && i.Container.Length > 0) codec = i.Container;
        var f = new AudioFacts(codec.ToLowerInvariant(), i.SampleRateHz, i.BitDepth, i.BitrateKbps, i.Channels);
        if (!f.Known) return;   // the source could not tell: leave it unprobed so it is tried again later
        // a worse partial read never replaces a more complete fact
        facts.AddOrUpdate(Key(t), f, (_, old) => Score(f) >= Score(old) ? f : old);
        MarkDirty();
        if (misses.TryRemove(Key(t), out _)) Interlocked.Exchange(ref missDirty, 1);
    }

    static int Score(AudioFacts f) => (f.Rate > 0 ? 4 : 0) + (f.Depth > 0 ? 2 : 0) + (f.Kbps > 0 ? 1 : 0);

    /// <summary>A change is written to disk a few seconds later (one write for a burst, such as a library listing carrying thousands of songs), by whoever made it: the Windows app's copy has no probe loop to do it.</summary>
    /// <summary>How long a change waits before it is written (a burst is one write).</summary>
    public static TimeSpan FlushDelay { get; set; } = TimeSpan.FromSeconds(8);
    void MarkDirty()
    {
        if (Interlocked.Exchange(ref dirty, 1) == 0) _ = Task.Delay(FlushDelay).ContinueWith(_ => Flush(), TaskScheduler.Default);
    }

    public void Flush()
    {
        if (Interlocked.Exchange(ref dirty, 0) != 0)
            try { var tmp = file + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(facts)); File.Move(tmp, file, true); }
            catch (Exception) { Interlocked.Exchange(ref dirty, 1); }
        if (Interlocked.Exchange(ref missDirty, 0) != 0)
            try { var tmp = missFile + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(misses)); File.Move(tmp, missFile, true); }
            catch (Exception) { Interlocked.Exchange(ref missDirty, 1); }
    }
}

/// <summary>How long a song whose real format could not be read is left alone before it is tried again: 1 hour, 6 hours, 24 hours, then every 7 days.</summary>
public static class ProbeBackoff
{
    static readonly TimeSpan[] Steps = [TimeSpan.FromHours(1), TimeSpan.FromHours(6), TimeSpan.FromHours(24), TimeSpan.FromDays(7)];
    /// <summary>The wait after the <paramref name="failures"/>th failed read in a row (1 = the first).</summary>
    public static TimeSpan Delay(int failures) => Steps[Math.Clamp(failures, 1, Steps.Length) - 1];
}
