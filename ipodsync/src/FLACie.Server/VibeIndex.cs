using System.Collections.Concurrent;
using System.Text.Json;
using FLACie.Core;

namespace FLACie.Server;

/// <summary>Every analysed song's <see cref="Vibe"/>, kept in data/vibe.json (shared by every account: a song feels the same to everyone).
/// Filled by <see cref="VibeFillService"/>; a song not analysed yet has none, and recommendations simply leave feel out for it.</summary>
public sealed class VibeIndex
{
    readonly ConcurrentDictionary<string, Vibe> vibes = new();
    readonly string file;
    public int Count => vibes.Count;

    public VibeIndex(DataPaths paths)
    {
        file = Path.Combine(paths.Root, "vibe.json");
        try { if (File.Exists(file) && JsonSerializer.Deserialize<Dictionary<string, Vibe>>(File.ReadAllText(file)) is { } d) foreach (var (k, v) in d) vibes[k] = v; }
        catch (Exception) { /* a damaged file is just analysed again */ }
    }

    static string Key(Track t) => t.Path;
    public Vibe? Of(Track t) => vibes.TryGetValue(Key(t), out var v) ? v : null;
    public bool Has(Track t) => vibes.ContainsKey(Key(t));
    /// <summary>Analysed, intro included (a song analysed before intros were worked out is analysed again, once).</summary>
    public bool Done(Track t) => vibes.TryGetValue(Key(t), out var v) && v.Intro is not null;
    public void Set(Track t, Vibe v) => vibes[Key(t)] = v;

    public void Save()
    {
        try { var tmp = file + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(vibes)); File.Move(tmp, file, true); }
        catch (Exception) { /* written again at the next save */ }
    }
}

/// <summary>
/// Works out songs' vibes in the background, one at a time, while nothing is playing: the songs people played most recently first, so what they
/// actually listen to gets recommendations that fit soonest. Only FLAC files (their frame sizes give the level envelope without decoding). Saved every
/// 200 songs and at the end of a pass; a pass every hour. Never part of a request, and not run inside the Windows app (it shares the owner's Jellyfin).
/// The same pass works out each song's intro for the DJ (<see cref="DjIntro"/>): from its synced lyrics when there are some (Jellyfin's, else LRCLIB's, kept in
/// data/lyrics like the lyrics screen's), else from the same level envelope.
/// </summary>
public sealed class VibeFillService(SessionStore sessions, VibeIndex index, ListenLogStore listens, JellyfinClient jf, IHttpClientFactory hf, PlaybackActivity activity, ILogger<VibeFillService> log, LyricsService? lyrics = null) : BackgroundService
{
    public const int SaveEvery = 200;
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    // songs that could not be read are left alone until the server restarts, so a broken file is not fetched every hour
    readonly HashSet<string> failed = [];

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (ChartsService.InDesktopShell) return;
        try { await Task.Delay(TimeSpan.FromMinutes(2), ct); } catch (OperationCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            // saved however the pass ends, shutdown included, so the songs worked out since the last save are kept
            try { foreach (var s in sessions.Active.ToList()) if (!s.Loading) await PassAsync(s, ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception e) { log.LogWarning(e, "vibe pass failed"); }
            finally { index.Save(); }
            try { await Task.Delay(Interval, ct); } catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>The account's songs still to analyse, the most recently played first, then the rest in library order.</summary>
    public IReadOnlyList<Track> Todo(UserSession s)
    {
        var lastPlayed = new Dictionary<string, long>();
        foreach (var e in listens.Read(s)) lastPlayed[e.Path] = Math.Max(lastPlayed.GetValueOrDefault(e.Path), e.At);
        return s.Library.Songs
            .Where(t => !index.Done(t) && !failed.Contains(t.Path) && MediaInfo.FromExtension(t.FilePath ?? t.Path) == "flac")
            .OrderByDescending(t => lastPlayed.GetValueOrDefault(t.Path, long.MinValue))
            .ToList();
    }

    /// <summary>A song's vibe and intro, from its level envelope and its lyrics (null when it has none).</summary>
    public static Vibe Analyse(float[] levels, double secondsPerLevel, Lyrics? words) =>
        VibeMath.FromLevels(levels, secondsPerLevel) with { Intro = DjIntro.Best(words, levels, secondsPerLevel) };

    async Task PassAsync(UserSession s, CancellationToken ct)
    {
        var todo = Todo(s);
        if (todo.Count == 0) return;
        log.LogInformation("working out the vibe of {n} songs", todo.Count);
        var done = 0;
        foreach (var t in todo)
        {
            await activity.WaitIdleAsync(TimeSpan.FromSeconds(3), ct);
            if (await MediaEndpoints.ReadWhole(s, t, jf, hf, ct) is { } bytes && WaveShape.FrameLevels(bytes) is ({ } levels, var step))
            {
                Lyrics? words = null;
                try { if (lyrics is not null) words = await lyrics.GetAsync(t, s.Jellyfin, ct); }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { /* no lyrics: the levels alone */ }
                index.Set(t, Analyse(levels, step, words));
            }
            else if (ct.IsCancellationRequested) break;   // the server is stopping: the song is not broken, only unread
            else failed.Add(t.Path);
            if (++done % SaveEvery == 0) index.Save();
        }
    }
}
