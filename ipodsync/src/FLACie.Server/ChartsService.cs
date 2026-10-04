using FLACie.Core;

namespace FLACie.Server;

/// <summary>
/// Once a day, for each signed-in account that turned it on: reads today's charts (Deezer) and downloads the songs the library doesn't have yet,
/// a few per chart, while the music storage has room. When space is tight it just doesn't download (nothing already on the storage is ever
/// deleted). The playlists "Charts: Top Songs" and so on are rebuilt from whatever of each chart is in the library.
/// </summary>
public sealed class ChartsService(SessionStore sessions, UserStateStore states, StorageGuard guard, DownloadManager downloads, WebCatalog catalog, JellyfinClient jf, ILogger<ChartsService> log) : BackgroundService
{
    public static readonly (int Id, string Name)[] Available = [(0, "Top Songs"), (132, "Pop"), (116, "Hip-Hop"), (152, "Rock"), (113, "Dance"), (165, "R&B"), (85, "Alternative"), (106, "Electronic")];

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        await Task.Delay(TimeSpan.FromSeconds(45), stop);
        while (!stop.IsCancellationRequested)
        {
            foreach (var s in sessions.Active.ToList())
            {
                try { await RunAsync(s, false, stop); }
                catch (OperationCanceledException) { }
                catch (Exception e) { log.LogWarning(e, "Charts run failed"); }
            }
            try { await Task.Delay(TimeSpan.FromMinutes(30), stop); } catch (OperationCanceledException) { }
        }
    }

    readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> busy = new();

    /// <summary>One pass for one account. [force] ignores "already ran today" (the Run now button).</summary>
    public async Task RunAsync(UserSession s, bool force, CancellationToken ct)
    {
        var st = states.For(s); var cfg = st.Charts;
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        if (!cfg.Enabled || (!force && cfg.LastRun == today) || s.Loading || s.LoadedAt == default) return;
        if (!busy.TryAdd(s.Id, true)) return;
        try
        {
            if (!s.Services.Any) { cfg.LastNote = "Downloads aren't set up (Settings > Downloads)"; states.Save(s); return; }
            int fetched = 0, wanted = 0; var skippedForSpace = false;
            foreach (var id in cfg.Lists.Distinct())
            {
                ct.ThrowIfCancellationRequested();
                var name = Available.FirstOrDefault(a => a.Id == id).Name ?? "Chart " + id;
                var chart = await catalog.ChartAsync(id, 50, ct);
                if (chart.Count == 0) continue;
                var todo = chart.Where(c => s.Library.Find(c.Title, c.Artist) is null && !st.ChartSeen.Contains(Matching.MatchKey(c.Title, c.Artist)) && !downloads.RecentlyFailed(s, c.Artist, c.Title)).Take(Math.Max(0, cfg.PerList)).ToList();
                wanted += todo.Count;
                foreach (var c in todo)
                {
                    if (!guard.Ok(s)) { skippedForSpace = true; break; }
                    st.ChartSeen.Add(Matching.MatchKey(c.Title, c.Artist));
                    var r = await downloads.FetchAsync(s, "Chart", c.Artist, c.Title, c.Album, c.ArtUrl, c.DurationMs, ct);
                    if (r.Ok) fetched++;
                }
                var have = chart.Select(c => s.Library.Find(c.Title, c.Artist)).OfType<Track>().ToList();
                if (have.Count > 0) s.ReplacePlaylistTracks(s.PlaylistNamed("Charts: " + name).Id, have);
                if (skippedForSpace) break;
            }
            if (st.ChartSeen.Count > 5000) st.ChartSeen = st.ChartSeen.Skip(2500).ToHashSet();
            cfg.LastRun = today;
            cfg.LastNote = skippedForSpace ? $"Low on free space: got {fetched} of {wanted} new songs, the rest were skipped"
                : wanted == 0 ? "Nothing new today" : $"Downloaded {fetched} of {wanted} new songs";
            states.Save(s);
            try { await s.SaveAsync(jf, ct); } catch (Exception) { }
        }
        finally { busy.TryRemove(s.Id, out _); }
    }
}
