using System.Runtime.CompilerServices;
using FLACie.Core;

namespace FLACie.Server;

public sealed class DownloadJob(string id, string label, string? artKey, string kind = "Search")
{
    public string Id { get; } = id;
    public string Label { get; } = label;
    public string? ArtKey { get; } = artKey;
    /// <summary>Where it came from: Search (the user asked), Autoplay (fetched ahead for the queue), Import (a playlist file) or Chart.</summary>
    public string Kind { get; } = kind;
    public DateTime At { get; } = DateTime.UtcNow;
    public DownloadStage Stage { get; internal set; } = DownloadStage.Requested;
    public string Message { get; internal set; } = "Requested";
    public bool Finished => Stage is DownloadStage.Done or DownloadStage.Failed;
}

/// <summary>Runs downloads in the background for the signed-in user (they keep going when the tab closes) and keeps their progress for
/// the page. The Soulseek and Lidarr addresses and keys come from the user's profile and never reach the browser.</summary>
public sealed class DownloadManager(IHttpClientFactory hf, WebCatalog catalog)
{
    sealed class Jobs
    {
        public readonly List<DownloadJob> List = [];
        public readonly Dictionary<string, Task<DownloadResult>> InFlight = [];
        public readonly Dictionary<string, DateTime> Failed = [];
        public event Action? Changed;
        public void Raise() => Changed?.Invoke();
    }
    readonly ConditionalWeakTable<UserSession, Jobs> table = new();

    /// <summary>What a background fetch came to: the songs now playable (empty when Lidarr took it, which arrives through the next library scan).</summary>
    public sealed record DownloadResult(bool Ok, IReadOnlyList<Track> Tracks, string Message);

    Jobs For(UserSession s) => table.GetValue(s, _ => new Jobs());
    public IReadOnlyList<DownloadJob> JobsOf(UserSession s) { var j = For(s); lock (j.List) return j.List.ToList(); }
    public void Subscribe(UserSession s, Action a) => For(s).Changed += a;
    public void Unsubscribe(UserSession s, Action a) => For(s).Changed -= a;

    public void DismissFinished(UserSession s) { var j = For(s); lock (j.List) j.List.RemoveAll(x => x.Finished); j.Raise(); }

    DownloadJob Add(UserSession s, string label, string? artUrl, string kind = "Search")
    {
        var j = For(s); var job = new DownloadJob(Guid.NewGuid().ToString("N"), label, artUrl is null ? null : Art.ExternalKey(artUrl), kind);
        lock (j.List) { j.List.Insert(0, job); while (j.List.Count > 400) { var i = j.List.FindLastIndex(x => x.Finished); if (i < 0) break; j.List.RemoveAt(i); } }
        j.Raise();
        return job;
    }

    void Run(UserSession s, DownloadJob job, Func<DownloadCoordinator, Action<DownloadStatus>, Task> work)
    {
        var jobs = For(s);
        var coordinator = new DownloadCoordinator(hf.CreateClient("downloads"), catalog, s.Services);
        _ = Task.Run(async () =>
        {
            try
            {
                await work(coordinator, st =>
                {
                    job.Stage = st.Stage; job.Message = st.Message;
                    if (st.NewTracks is { Count: > 0 } t) s.AddDownloaded(t);
                    jobs.Raise();
                });
            }
            catch (Exception e) { job.Stage = DownloadStage.Failed; job.Message = e.Message; jobs.Raise(); }
            if (!job.Finished) { job.Stage = DownloadStage.Failed; job.Message = "Stopped"; jobs.Raise(); }
        });
    }

    public void Song(UserSession s, CatalogSong song) =>
        Run(s, Add(s, $"{song.Title} · {song.Artist}", song.ArtUrl), (c, on) => c.DownloadAsync(song.Artist, song.Title, song.Album, song.ArtUrl, song.DurationMs, on));

    public void Album(UserSession s, CatalogAlbum album) =>
        Run(s, Add(s, $"{album.CleanTitle} · {album.Artist}", album.ArtUrl), (c, on) => c.DownloadAlbumAsync(album, on));

    // ---- downloads nobody is watching: autoplay look-ahead, playlist imports, charts ----

    static string Key(string artist, string title) => Matching.MatchKey(title, artist);

    /// <summary>True when this song was tried a little while ago and nothing was found, so it isn't searched for again at once.</summary>
    public bool RecentlyFailed(UserSession s, string artist, string title)
    {
        var j = For(s);
        lock (j.Failed) return j.Failed.TryGetValue(Key(artist, title), out var at) && DateTime.UtcNow - at < TimeSpan.FromHours(6);
    }

    /// <summary>Fetches one song for [kind] and tells when it is done. A song already on its way is shared, not fetched twice.</summary>
    public Task<DownloadResult> FetchAsync(UserSession s, string kind, string artist, string title, string album, string? artUrl, long durationMs, CancellationToken ct = default)
    {
        var j = For(s); var key = Key(artist, title);
        lock (j.InFlight)
        {
            if (j.InFlight.TryGetValue(key, out var running)) return running;
            var task = Task.Run(() => FetchCore(s, j, kind, artist, title, album, artUrl, durationMs, ct));
            j.InFlight[key] = task;
            _ = task.ContinueWith(_ => { lock (j.InFlight) j.InFlight.Remove(key); });
            return task;
        }
    }

    async Task<DownloadResult> FetchCore(UserSession s, Jobs jobs, string kind, string artist, string title, string album, string? artUrl, long durationMs, CancellationToken ct)
    {
        var job = Add(s, $"{title} · {artist}", artUrl, kind);
        var coordinator = new DownloadCoordinator(hf.CreateClient("downloads"), catalog, s.Services);
        var got = new List<Track>(); string last = "";
        try
        {
            await coordinator.DownloadAsync(artist, title, album, artUrl, durationMs, st =>
            {
                job.Stage = st.Stage; job.Message = st.Message; last = st.Message;
                if (st.NewTracks is { Count: > 0 } t) { got.AddRange(t); s.AddDownloaded(t); }
                jobs.Raise();
            }, ct);
        }
        catch (OperationCanceledException) { job.Stage = DownloadStage.Failed; job.Message = "Cancelled"; jobs.Raise(); return new(false, [], "Cancelled"); }
        catch (Exception e) { job.Stage = DownloadStage.Failed; job.Message = e.Message; last = e.Message; }
        if (!job.Finished) { job.Stage = DownloadStage.Failed; job.Message = "Stopped"; }
        if (job.Stage == DownloadStage.Failed && got.Count == 0) lock (jobs.Failed) jobs.Failed[Key(artist, title)] = DateTime.UtcNow;
        jobs.Raise();
        return new(job.Stage == DownloadStage.Done, got, last);
    }
}
