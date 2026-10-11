using FLACie.Core;

namespace FLACie.Server;

/// <summary>
/// Fills the <see cref="FormatIndex"/> in the background, gently. A Jellyfin library is read in pages of 300 songs with their streams (about 30
/// requests for 9,000 songs, not 9,000); only NAS and just-downloaded songs, and a Jellyfin library with only a few songs missing, are read one by one,
/// two at a time. A song whose read gives nothing is left alone for 1 hour, then 6, 24 and finally 7 days (<see cref="ProbeBackoff"/>). It waits while
/// anything is being played (<see cref="PlaybackActivity"/>), runs at most once an hour, and does not run at all inside the Windows app, whose copy of
/// this server shares the owner's Jellyfin. Songs someone is looking at (the Info panel, Now Playing) are read at once by <see cref="InfoService"/>,
/// never waiting for this.
/// </summary>
public sealed class FormatProbeService(SessionStore sessions, InfoService info, FormatIndex index, JellyfinClient jf, PlaybackActivity activity, ILogger<FormatProbeService> log) : BackgroundService
{
    public const int PageSize = 300;
    /// <summary>With fewer Jellyfin songs than this missing, asking for them one by one is cheaper than paging the whole library.</summary>
    public const int BulkThreshold = 20;
    public const int SongConcurrency = 2;
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(10);

    public int Pending { get; private set; }
    public TimeSpan Poll { get; set; } = TimeSpan.FromSeconds(3);
    readonly HashSet<string> probed = [];

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // the Windows app's copy shares the owner's Jellyfin with the always-on server: it only ever reads the song someone is looking at
        if (ChartsService.InDesktopShell) return;
        try { await Task.Delay(TimeSpan.FromSeconds(20), ct); } catch (OperationCanceledException) { return; }
        var lastPass = DateTime.MinValue;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // a pass every hour, and one for an account that signed in since the last one (its library is loaded by then)
                var fresh = sessions.Active.Any(s => !s.Loading && s.Library.Songs.Count > 0 && !probed.Contains(s.Id));
                if (fresh || DateTime.UtcNow - lastPass >= Interval)
                {
                    var ok = await PassAsync(ct);
                    lastPass = ok ? DateTime.UtcNow : DateTime.UtcNow - Interval + RetryAfterFailure;
                }
                index.Flush();   // facts that arrived with the library listing
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e) { log.LogWarning(e, "format probe pass failed"); lastPass = DateTime.UtcNow - Interval + RetryAfterFailure; try { await Task.Delay(TimeSpan.FromSeconds(30), ct); } catch (OperationCanceledException) { break; } }
        }
    }

    static bool Probeable(Track t) => t.Source is TrackSource.Jellyfin or TrackSource.Nas or TrackSource.Cloud;

    /// <summary>One pass over every signed-in library. False when part of it failed (tried again sooner than the hour).</summary>
    public async Task<bool> PassAsync(CancellationToken ct)
    {
        var allOk = true;
        foreach (var s in sessions.Active.ToList())
        {
            if (s.Loading) { allOk = false; continue; }
            try { await PassAsync(s, ct); probed.Add(s.Id); }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { allOk = false; log.LogWarning(e, "format probe failed for {user}", s.DisplayName); }
        }
        Pending = 0;
        return allOk;
    }

    public async Task PassAsync(UserSession s, CancellationToken ct)
    {
        using var prio = JellyfinGate.Use(GatePriority.Background);
        var now = DateTime.UtcNow;
        var todo = s.Library.Songs.Where(t => Probeable(t) && !index.Has(t) && index.Due(t, now)).ToList();
        Pending = todo.Count;
        if (todo.Count == 0) return;
        log.LogInformation("reading the real audio facts of {n} songs", todo.Count);
        var jfTodo = todo.Where(t => t.Source == TrackSource.Jellyfin && t.JellyfinId is not null).ToList();
        if (jfTodo.Count > BulkThreshold && s.Jellyfin is { } acct)
        {
            await BulkAsync(s, acct, jfTodo, ct);
            todo = todo.Where(t => t.Source != TrackSource.Jellyfin || t.JellyfinId is null).ToList();   // Jellyfin answered for all of them: what it could not read waits out its back-off
        }
        using var sem = new SemaphoreSlim(SongConcurrency);
        await Task.WhenAll(todo.Select(async t =>
        {
            await sem.WaitAsync(ct);
            try
            {
                await activity.WaitIdleAsync(Poll, ct);
                try { await info.GetAsync(s, t, retry: false); } catch (OperationCanceledException) { throw; } catch (Exception) { }
                if (!index.Has(t)) index.MarkMiss(t, DateTime.UtcNow);
            }
            finally { sem.Release(); Pending = Math.Max(0, Pending - 1); }
        }));
        index.Flush();
    }

    async Task BulkAsync(UserSession s, JellyfinAccount acct, List<Track> wanted, CancellationToken ct)
    {
        var byId = wanted.GroupBy(t => t.JellyfinId!.Replace("-", ""), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var remaining = new HashSet<Track>(wanted);
        for (int start = 0, total = int.MaxValue; start < total && remaining.Count > 0; start += PageSize)
        {
            await activity.WaitIdleAsync(Poll, ct);
            var (items, count) = await jf.MediaInfoPageAsync(acct, start, PageSize, ct);
            total = count;
            if (items.Count == 0) break;
            foreach (var (id, mi) in items)
            {
                if (!byId.TryGetValue(id.Replace("-", ""), out var tracks)) continue;
                foreach (var t in tracks)
                {
                    if (mi is { Known: true }) { index.Set(t, mi); info.Remember(t, mi); remaining.Remove(t); Pending = Math.Max(0, Pending - 1); }
                }
            }
            index.Flush();
        }
        // whatever the library listing could not tell (no media source, no usable rate) is tried again later, not at the next pass
        var now = DateTime.UtcNow;
        foreach (var t in remaining) if (!index.Has(t)) index.MarkMiss(t, now);
        index.Flush();
    }
}
