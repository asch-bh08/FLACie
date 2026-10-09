using System.Runtime.CompilerServices;
using FLACie.Core;

namespace FLACie.Server;

public sealed class DownloadJob(string id, string label, string? artKey, string kind = "Search", string artist = "", string title = "", string album = "", long durationMs = 0, string? artUrl = null, bool isAlbum = false)
{
    public string Album { get; } = album;
    public long DurationMs { get; } = durationMs;
    public string? ArtUrl { get; } = artUrl;
    public bool IsAlbum { get; } = isAlbum;
    public string Id { get; } = id;
    public string Label { get; } = label;
    public string Artist { get; } = artist;
    public string Title { get; } = title;
    public string? ArtKey { get; } = artKey;
    /// <summary>Where it came from: Search (the user asked), Autoplay (fetched ahead for the queue), Import (a playlist file) or Chart.</summary>
    public string Kind { get; } = kind;
    public DateTime At { get; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; internal set; }
    public DownloadStage Stage { get; internal set; } = DownloadStage.Requested;
    public string Message { get; internal set; } = "Requested";
    /// <summary>The source being worked on right now (soulseek, lidarr, ytdl, archive, audius, jamendo), null before the first one starts.</summary>
    public string? Current { get; internal set; }
    /// <summary>The source that delivered it, once it is done.</summary>
    public string? Source { get; internal set; }
    public string? File { get; internal set; }
    public List<TrailStep> Trail { get; } = [];
    public bool Finished => Stage is DownloadStage.Done or DownloadStage.Failed;

    /// <summary>Takes one status from the download code: moves the job along and keeps the step for the story shown later.</summary>
    internal void Apply(DownloadStatus st)
    {
        lock (Trail)
        {
            if (!st.Miss) { Stage = st.Stage; Message = st.Message; if (st.Source is not null) Current = st.Source; }
            if (st.Stage == DownloadStage.Done) { Source = st.Source; if (st.NewTracks is { Count: > 0 } t) File = FileOf(t[0].Path); }
            if (Trail.Count == 0 || Trail[^1].Text != st.Message || Trail[^1].Source != st.Source) { Trail.Add(new(DateTime.UtcNow, st.Source, st.Message, st.Miss)); if (Trail.Count > 40) Trail.RemoveAt(1); }
        }
    }

    static string? FileOf(string path) { var i = path.IndexOf("path=", StringComparison.Ordinal); return i < 0 ? null : Uri.UnescapeDataString(path[(i + 5)..]); }

    internal DownloadRecord ToRecord(UserSession s) => new()
    {
        Id = Id, UserId = s.Id, UserName = s.DisplayName, Label = Label, Title = Title, Artist = Artist, Kind = Kind, StartedAt = At, FinishedAt = FinishedAt ?? DateTime.UtcNow,
        Outcome = Stage == DownloadStage.Done ? "done" : "failed", Source = Stage == DownloadStage.Done ? Source ?? Current : null, Message = Message, File = File, ArtKey = ArtKey,
        Album = Album, DurationMs = DurationMs, ArtUrl = ArtUrl, IsAlbum = IsAlbum, Trail = [.. Trail],
    };
}

/// <summary>Runs downloads in the background for the signed-in user (they keep going when the tab closes) and keeps their progress for
/// the page. The Soulseek and Lidarr addresses and keys come from the user's profile and never reach the browser.</summary>
public sealed class DownloadManager(IHttpClientFactory hf, WebCatalog catalog, DownloadLog log, JellyfinClient jf)
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

    DownloadJob Add(UserSession s, string label, string? artUrl, string kind = "Search", string artist = "", string title = "", string album = "", long durationMs = 0, bool isAlbum = false)
    {
        var j = For(s); var job = new DownloadJob(Guid.NewGuid().ToString("N"), label, artUrl is null ? null : Art.ExternalKey(artUrl), kind, artist, title, album, durationMs, artUrl, isAlbum);
        lock (j.List) { j.List.Insert(0, job); while (j.List.Count > 400) { var i = j.List.FindLastIndex(x => x.Finished); if (i < 0) break; j.List.RemoveAt(i); } }
        j.Raise();
        return job;
    }

    void Run(UserSession s, DownloadJob job, Func<DownloadCoordinator, Action<DownloadStatus>, Task> work)
    {
        var jobs = For(s);
        var coordinator = new DownloadCoordinator(hf.CreateClient("downloads"), catalog, s.Services, hf.CreateClient("downloads-long"));
        _ = Task.Run(async () =>
        {
            try
            {
                await work(coordinator, st =>
                {
                    job.Apply(st);
                    if (st.NewTracks is { Count: > 0 } t) s.AddDownloaded(t);
                    jobs.Raise();
                });
            }
            catch (Exception e) { job.Apply(new(DownloadStage.Failed, e.Message)); }
            if (!job.Finished) job.Apply(new(DownloadStage.Failed, "Stopped"));
            Finish(s, job, jobs);
        });
    }

    // ---- a finished download is only a file on the NAS until Jellyfin scans it; ask for a scan soon after (once per minute at most) so the song joins the library ----
    static long lastScanAt;
    void ScanSoon(UserSession s)
    {
        if (s.Jellyfin is not { } a) return;
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref lastScanAt) < 60_000) return;
        Interlocked.Exchange(ref lastScanAt, now);
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(TimeSpan.FromSeconds(15)); await jf.PostAsync(a, "/Library/Refresh", null); } catch (Exception) { /* only administrators may start a scan; the next scheduled one still finds it */ }
        });
    }

    /// <summary>A job is over: stamp it and write it to the log.</summary>
    void Finish(UserSession s, DownloadJob job, Jobs jobs)
    {
        job.FinishedAt ??= DateTime.UtcNow;
        try { log.Add(job.ToRecord(s)); } catch (Exception) { }
        if (job.Stage == DownloadStage.Done && job.File is not null) ScanSoon(s);
        jobs.Raise();
    }

    /// <summary>The newest job (running or finished this session) for this song, so a search result can show how its download is going.</summary>
    public DownloadJob? JobFor(UserSession s, string artist, string title)
    {
        var key = Matching.MatchKey(title, artist);
        var j = For(s);
        lock (j.List) return j.List.FirstOrDefault(x => x.Title.Length > 0 && Matching.MatchKey(x.Title, x.Artist) == key);
    }

    public void Song(UserSession s, CatalogSong song, bool hiRes = false, bool fallbackToNormal = true) =>
        Run(s, Add(s, $"{song.Title} · {song.Artist}{(hiRes ? " (Hi-Res)" : "")}", song.ArtUrl, "Search", song.Artist, song.Title, song.Album, song.DurationMs),
            (c, on) => hiRes ? c.DownloadHiResAsync(song.Artist, song.Title, song.Album, song.ArtUrl, song.DurationMs, on, default, fallbackToNormal) : c.DownloadAsync(song.Artist, song.Title, song.Album, song.ArtUrl, song.DurationMs, on));

    public void Album(UserSession s, CatalogAlbum album) =>
        Run(s, Add(s, $"{album.CleanTitle} · {album.Artist}", album.ArtUrl, "Search", album.Artist, album.CleanTitle, isAlbum: true), (c, on) => c.DownloadAlbumAsync(album, on));

    // ---- trying a failed download again ----

    static readonly SemaphoreSlim RetryTurn = new(3);

    /// <summary>True when this failed record can be asked for again (a song: a whole album can't be rebuilt from the log).</summary>
    public static bool CanRetry(DownloadRecord r) => !r.Done && !r.IsAlbum && r.Title.Length > 0 && r.Artist.Length > 0;

    /// <summary>Asks for a failed song again, as a new download that starts now. Nothing happens when that song is already being fetched.</summary>
    public bool Retry(UserSession s, DownloadRecord r)
    {
        if (!CanRetry(r) || JobFor(s, r.Artist, r.Title) is { Finished: false }) return false;
        var j = For(s); lock (j.Failed) j.Failed.Remove(Key(r.Artist, r.Title));
        var job = Add(s, r.Label, r.ArtUrl, r.Kind == "Autoplay" ? "Autoplay" : "Search", r.Artist, r.Title, r.Album, r.DurationMs);
        Run(s, job, async (c, on) =>
        {
            // Retry all can start dozens at once; slskd and the file mover time out when asked for that much together, so they go through three at a time
            on(new(DownloadStage.Requested, "Waiting for its turn..."));
            await RetryTurn.WaitAsync();
            try { await c.DownloadAsync(r.Artist, r.Title, r.Album, r.ArtUrl, r.DurationMs, on); } finally { RetryTurn.Release(); }
        });
        return true;
    }

    /// <summary>Retries every song whose latest try failed (one per song, newest failure), at most [max]. Returns how many were started.</summary>
    public int RetryAll(UserSession s, IEnumerable<DownloadRecord> mine, int max = 100)
    {
        var done = new Dictionary<string, DateTime>();
        foreach (var r in mine.Where(r => r.Done)) { var dk = Key(r.Artist, r.Title); if (!done.TryGetValue(dk, out var at) || r.FinishedAt > at) done[dk] = r.FinishedAt; }
        var n = 0; var seen = new HashSet<string>();
        foreach (var r in mine.Where(CanRetry).OrderByDescending(r => r.FinishedAt))
        {
            var k = Key(r.Artist, r.Title);
            if (!seen.Add(k) || (done.TryGetValue(k, out var got) && got >= r.FinishedAt)) continue;
            if (n >= max) break;
            if (Retry(s, r)) n++;
        }
        return n;
    }

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
        var job = Add(s, $"{title} · {artist}", artUrl, kind, artist, title, album, durationMs);
        var coordinator = new DownloadCoordinator(hf.CreateClient("downloads"), catalog, s.Services, hf.CreateClient("downloads-long"));
        var got = new List<Track>(); string last = "";
        try
        {
            await coordinator.DownloadAsync(artist, title, album, artUrl, durationMs, st =>
            {
                job.Apply(st); if (!st.Miss) last = st.Message;
                if (st.NewTracks is { Count: > 0 } t) { got.AddRange(t); s.AddDownloaded(t); }
                jobs.Raise();
            }, ct);
        }
        catch (OperationCanceledException) { job.Apply(new(DownloadStage.Failed, "Cancelled")); Finish(s, job, jobs); return new(false, [], "Cancelled"); }
        catch (Exception e) { job.Apply(new(DownloadStage.Failed, e.Message)); last = e.Message; }
        if (!job.Finished) job.Apply(new(DownloadStage.Failed, "Stopped"));
        if (job.Stage == DownloadStage.Failed && got.Count == 0) lock (jobs.Failed) jobs.Failed[Key(artist, title)] = DateTime.UtcNow;
        Finish(s, job, jobs);
        jobs.Raise();
        return new(job.Stage == DownloadStage.Done, got, last);
    }
}
