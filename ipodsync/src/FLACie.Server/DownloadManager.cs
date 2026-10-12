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
    string? key;
    /// <summary>What the song is matched by (title and lead artist, like the rest of the app); empty for a job with no song name.</summary>
    public string Key => key ??= Title.Length > 0 ? Matching.MatchKey(Title, Artist) : "";
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
public sealed class DownloadManager(IHttpClientFactory hf, WebCatalog catalog, DownloadLog log, JellyfinClient jf, DownloadDigest digest)
{
    sealed class Jobs
    {
        public readonly List<DownloadJob> List = [];
        public readonly Dictionary<string, Task<DownloadResult>> InFlight = [];
        public readonly Dictionary<string, DateTime> Failed = [];
        public readonly RecentKeys Succeeded = new(500);
        /// <summary>For the tags under songs (see <see cref="DownloadManager.StatusOf"/>): the song downloads running now, and when each song last finished or failed
        /// (unix ms, by match key), kept only for the "recent" window. Under <see cref="Sync"/>.</summary>
        public readonly object Sync = new();
        public readonly HashSet<DownloadJob> Active = [];
        public readonly Dictionary<string, long> LastDone = [];
        public readonly Dictionary<string, long> LastFailed = [];
        public bool Seeded;
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
        if (!isAlbum && job.Key.Length > 0) lock (j.Sync) j.Active.Add(job);
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
                    if (st.NewTracks is { Count: > 0 } t) { s.AddDownloaded(t); NoteDone(jobs, t); }
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
        NoteFinished(jobs, job);
        if (job.Stage == DownloadStage.Done && job.File is not null) ScanSoon(s);
        // "Dad downloaded ...": only what the user asked for (searches), not songs fetched ahead for the queue, charts or imports (those have their own messages)
        if (job.Stage == DownloadStage.Done && job.Kind == "Search") digest.Done(s.Id, s.DisplayName, job.Title.Length > 0 ? $"{job.Title} - {job.Artist}" : job.Label);
        jobs.Raise();
    }

    /// <summary>The newest job (running or finished this session) for this song, so a search result can show how its download is going.</summary>
    public DownloadJob? JobFor(UserSession s, string artist, string title)
    {
        var key = Matching.MatchKey(title, artist);
        var j = For(s);
        lock (j.List) return j.List.FirstOrDefault(x => x.Title.Length > 0 && Matching.MatchKey(x.Title, x.Artist) == key);
    }

    // ---- the tag under a song: "Downloading · <stage>", "Recently downloaded · 1m ago", "Couldn't download" ----

    static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    static long Ms(DateTime t) => new DateTimeOffset(t.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(t, DateTimeKind.Utc) : t.ToUniversalTime()).ToUnixTimeMilliseconds();

    /// <summary>Keeps only what is still inside the window, so the two maps stay as small as the downloads of the last hour.</summary>
    static void Prune(Dictionary<string, long> map, long nowMs)
    {
        if (map.Count < 2000) return;
        foreach (var k in map.Where(kv => nowMs - kv.Value > DownloadTags.RecentWindowMs).Select(kv => kv.Key).ToList()) map.Remove(k);
    }

    static void Newest(Dictionary<string, long> map, string key, long ms) { if (key.Length > 0 && (!map.TryGetValue(key, out var old) || ms > old)) map[key] = ms; }

    /// <summary>A job is over: it is no longer "running", and its end is remembered for the tag (a success or a failure, by song).</summary>
    void NoteFinished(Jobs j, DownloadJob job)
    {
        if (job.IsAlbum || job.Key.Length == 0) return;
        var at = Ms(job.FinishedAt ?? DateTime.UtcNow);
        lock (j.Sync)
        {
            j.Active.Remove(job);
            if (job.Stage == DownloadStage.Done) { Newest(j.LastDone, job.Key, at); j.Succeeded.Add(job.Key); }
            else Newest(j.LastFailed, job.Key, at);
            Prune(j.LastDone, at); Prune(j.LastFailed, at);
        }
    }

    /// <summary>Songs that arrived (an album's, or a song found under another name) are recent downloads too, and are not fetched again.</summary>
    static void NoteDone(Jobs j, IEnumerable<Track> tracks)
    {
        var now = NowMs();
        lock (j.Sync)
            foreach (var t in tracks)
            {
                var k = Matching.MatchKey(t);
                Newest(j.LastDone, k, now); j.Succeeded.Add(k);
            }
    }

    /// <summary>The last hour of the log, read once per session: downloads from before a restart, and the phone's reports, count as recent too.</summary>
    void Seed(UserSession s, Jobs j)
    {
        lock (j.Sync) { if (j.Seeded) return; j.Seeded = true; }
        var now = NowMs();
        IReadOnlyList<DownloadRecord> mine;
        try { mine = log.For(s.Id); } catch (Exception) { return; }
        lock (j.Sync)
            foreach (var r in mine.Take(600))
            {
                if (r.IsAlbum || r.Title.Length == 0) continue;
                var at = Ms(r.FinishedAt);
                if (now - at > DownloadTags.RecentWindowMs) continue;
                var k = Matching.MatchKey(r.Title, r.Artist);
                if (r.Done) { Newest(j.LastDone, k, at); j.Succeeded.Add(k); } else Newest(j.LastFailed, k, at);
            }
    }

    /// <summary>
    /// What to say under a song (the one lookup behind every tag): a download running for it now, else how long ago it finished (only within the last hour; a "new
    /// download" song also counts from the time it was added), else a recent failure, else nothing. Songs are matched by title and lead artist like the rest of the app.
    /// </summary>
    public DownloadTagStatus StatusOf(UserSession s, string artist, string title, Track? track = null, long? nowMs = null) =>
        title.Length == 0 ? DownloadTagStatus.None : StatusOfKey(s, Matching.MatchKey(title, artist), track is { Source: TrackSource.Cloud } ? track.AddedMs : 0, nowMs);

    /// <summary>The same, for a key the caller already worked out (a list that asks for every row on every change).</summary>
    public DownloadTagStatus StatusOfKey(UserSession s, string key, long cloudAddedMs = 0, long? nowMs = null)
    {
        var j = For(s);
        Seed(s, j);
        var now = nowMs ?? NowMs();
        string? running = null; long done = 0, failed = 0;
        lock (j.Sync)
        {
            foreach (var a in j.Active) if (a.Key == key && !a.Finished) { running = a.Message; break; }
            j.LastDone.TryGetValue(key, out done); j.LastFailed.TryGetValue(key, out failed);
        }
        return DownloadTags.Resolve(running, done, failed, cloudAddedMs, now);
    }

    // ---- a download the phone did itself and reported (POST /api/downloaded) ----

    static readonly object ReportLock = new();
    static readonly Dictionary<string, Queue<long>> ReportTimes = [];
    /// <summary>At most this many reports per user per hour.</summary>
    public const int ReportsPerHour = 200;

    /// <summary>True when this user may still report a download (at most <see cref="ReportsPerHour"/> in any hour); counts the call.</summary>
    public static bool TakeReportTurn(string userId, long nowMs)
    {
        lock (ReportLock)
        {
            if (!ReportTimes.TryGetValue(userId, out var q)) ReportTimes[userId] = q = new();
            while (q.Count > 0 && nowMs - q.Peek() > 3_600_000) q.Dequeue();
            if (q.Count >= ReportsPerHour) return false;
            q.Enqueue(nowMs);
            if (ReportTimes.Count > 2000) foreach (var k in ReportTimes.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key).ToList()) ReportTimes.Remove(k);
            return true;
        }
    }

    /// <summary>
    /// The phone finished a download on its own and says so: it is written to the log (so the Downloads page and the tag see it, and it survives a restart), the song counts as
    /// fetched (Autoplay and Search do not fetch it again) and, when <paramref name="track"/> is given, it is a playable "new download" song in this session at once.
    /// A library scan is asked for, like after a download of our own.
    /// </summary>
    public void Reported(UserSession s, string artist, string title, string album, long durationMs, string? source, long finishedMs, string? file, Track? track)
    {
        var j = For(s);
        var at = DateTimeOffset.FromUnixTimeMilliseconds(finishedMs).UtcDateTime;
        var label = $"{title} · {artist}";
        try
        {
            log.Add(new DownloadRecord
            {
                Id = Guid.NewGuid().ToString("N"), UserId = s.Id, UserName = s.DisplayName, Label = label, Title = title, Artist = artist, Kind = "Phone", StartedAt = at, FinishedAt = at,
                Outcome = "done", Source = source, Message = $"\"{title}\" was downloaded on the phone", File = file, Album = album, DurationMs = durationMs,
                ArtKey = track?.ArtKey,
            });
        }
        catch (Exception) { }
        var key = Matching.MatchKey(title, artist);
        lock (j.Sync) { Newest(j.LastDone, key, finishedMs); j.Succeeded.Add(key); }
        if (track is not null) s.AddDownloaded([track]);
        ScanSoon(s);
        j.Raise();
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

    /// <summary>True when this song was fetched successfully lately (the last 500 per user), so autoplay does not download it again.</summary>
    public bool RecentlyFetched(UserSession s, string artist, string title) => For(s).Succeeded.Contains(Key(artist, title));

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
            if (kind == "Autoplay" && j.Succeeded.Contains(key)) return Task.FromResult(new DownloadResult(true, [], "Fetched recently"));
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
                if (st.NewTracks is { Count: > 0 } t) { got.AddRange(t); s.AddDownloaded(t); NoteDone(jobs, t); }
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

/// <summary>A bounded, thread-safe memory of keys: when full, the oldest is forgotten.</summary>
public sealed class RecentKeys(int capacity)
{
    readonly Queue<string> order = new(); readonly HashSet<string> set = [];
    public void Add(string key) { lock (set) { if (!set.Add(key)) return; order.Enqueue(key); while (order.Count > capacity) set.Remove(order.Dequeue()); } }
    public bool Contains(string key) { lock (set) return set.Contains(key); }
    public int Count { get { lock (set) return set.Count; } }
}
