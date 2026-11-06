using System.Collections.Concurrent;
using FLACie.Core;

namespace FLACie.Server;

/// <summary>This person may not import playlist files on this server (see <see cref="DownloadPolicy.MayBulk"/>).</summary>
public sealed class ImportNotAllowedException() : InvalidOperationException("Importing playlists is turned off for your account on this server. Ask the person who runs it.");

/// <summary>
/// Playlist import that runs entirely on the server. The phone or browser only hands over the file and later asks how far it got: the
/// searching and downloading happen here, a few songs at a time, and the songs are put into the account's playlists as they arrive. A song
/// the library already has is simply added. It pauses while the music storage is nearly full and carries on after a restart.
/// </summary>
public sealed class ImportManager(DownloadManager downloads, UserStateStore states, StorageGuard guard, JellyfinClient jf, ILogger<ImportManager> log, Notifier notify, ActivityLog activity)
{
    readonly ConcurrentDictionary<string, Task> running = new();
    readonly ConcurrentDictionary<string, CancellationTokenSource> cancels = new();
    public event Action<UserSession>? Changed;

    /// <summary>Reads the file and starts the import. Throws a message-carrying exception if nothing in it looks like songs.</summary>
    public ImportJob Start(UserSession s, string fileName, byte[] bytes, bool replace = false)
    {
        // imports fetch whole lists: administrators may, and everyone else when the Dashboard lets them (and never while the server doesn't download)
        if (!downloads.MayBulk(s)) throw new ImportNotAllowedException();
        List<ImportedPlaylist> lists;
        try { lists = PlaylistImport.Parse(fileName, bytes); }
        catch (Exception e) { throw new InvalidDataException("Couldn't read that file: " + e.Message); }
        if (lists.Count == 0) throw new InvalidDataException("No songs found in that file. It can be an iTunes Library.xml, an M3U playlist, a Spotify CSV, or a text list with one \"Artist - Title\" per line.");
        return StartLists(s, fileName, lists, replace);
    }

    /// <summary>Starts an import of lists already read (a playlist link from another service, PlaylistLinkReader). [title] names the job.</summary>
    public ImportJob StartLists(UserSession s, string title, List<ImportedPlaylist> lists, bool replace = false)
    {
        if (!downloads.MayBulk(s)) throw new ImportNotAllowedException();
        var fileName = title;
        var job = new ImportJob
        {
            File = fileName, Replace = replace,
            Lists = lists.Select(l => new ImportList { Name = l.Name, Items = l.Tracks.Select(t => new ImportItem { Artist = t.Artist, Title = t.Title, Album = t.Album }).ToList() }).ToList(),
        };
        var st = states.For(s);
        lock (st) { st.Imports.Insert(0, job); while (st.Imports.Count > 30) st.Imports.RemoveAt(st.Imports.Count - 1); }
        states.Save(s);
        Launch(s, job);
        return job;
    }

    /// <summary>A Spotify or Deezer link, as the Import page and the phone's <c>POST /api/import/link</c> both start it (see <see cref="LinkImport"/>).</summary>
    public Task<ImportJob> StartLinkAsync(UserSession s, PlaylistLinkReader reader, string? url, bool replace = false, CancellationToken ct = default) =>
        LinkImport.StartAsync(reader, list =>
        {
            var job = StartLists(s, list.Name, [list], replace);
            job.Source = PlaylistLinks.Recognise(url)?.Service.ToString().ToLowerInvariant();
            return job;
        }, url, ct, s.Services);

    /// <summary>Picks up unfinished imports after a restart.</summary>
    public void Resume(UserSession s)
    {
        if (!downloads.MayBulk(s)) return;   // left as they were: they carry on if the server lets this person import again
        foreach (var job in states.For(s).Imports.Where(j => j.State is "running" or "waiting").ToList()) Launch(s, job);
    }

    /// <summary>
    /// Songs of an import that are in the library but no longer in its playlist go back in, in the import's order (owner, 6 Nov: 25 of 99 dropped out of
    /// "80s - 90s rally" when their files were swapped for Hi-Res copies). A playlist the person deleted is left deleted. Returns how many went back.
    /// </summary>
    public static int Heal(UserSession s, ImportList list)
    {
        if (list.PlaylistId is not { } id || s.Playlists.FirstOrDefault(p => p.Id == id) is not { } pl) return 0;
        var have = pl.Entries.Select(e => Matching.MatchKey(e.Title, e.Artist)).ToHashSet();
        var healed = 0; var at = 0;
        foreach (var item in list.Items)
        {
            if (item.State is not ("owned" or "done")) continue;
            var t = Have(s, item);
            if (t is null) continue;
            var key = Matching.MatchKey(t);
            if (!have.Contains(key) && !have.Contains(Matching.MatchKey(item.Title, item.Artist)))
            {
                s.InsertIntoPlaylist(id, at, new PlaylistEntry(t.Path, t.Title, t.Artist));
                have.Add(key); healed++;
            }
            at++;
        }
        return healed;
    }

    /// <summary>After sign-in: every finished import's playlist is checked once (see <see cref="Heal(UserSession, ImportList)"/>) and saved when one changed.</summary>
    public async Task HealAsync(UserSession s)
    {
        for (var i = 0; i < 360 && (s.Loading || s.LoadedAt == default); i++) await Task.Delay(500);
        if (s.Library.Songs.Count == 0) return;
        var healed = 0; var checkedAny = false;
        foreach (var job in states.For(s).Imports.Where(j => j.State == "done" && !j.Healed).ToList())
        {
            foreach (var l in job.Lists) healed += Heal(s, l);
            job.Healed = true; checkedAny = true;
        }
        if (checkedAny) states.Save(s);
        if (healed == 0) return;
        log.LogInformation("Imports: {Count} songs put back in their playlists", healed);
        try { await s.SaveAsync(jf); } catch (Exception e) { log.LogWarning(e, "Saving the profile after putting import songs back failed"); }
    }

    public void Cancel(UserSession s, string id)
    {
        // only the account's own imports: the id is looked up in this account's list before anything is cancelled
        if (states.For(s).Imports.FirstOrDefault(j => j.Id == id) is not { } job) return;
        if (cancels.TryGetValue(id, out var c)) c.Cancel();
        if (job.State is "running" or "waiting") { job.State = "cancelled"; states.Save(s); Changed?.Invoke(s); }
    }

    /// <summary>Tries the songs that failed again.</summary>
    public void Retry(UserSession s, string id)
    {
        if (states.For(s).Imports.FirstOrDefault(j => j.Id == id) is not { } job || running.ContainsKey(id)) return;
        foreach (var i in job.Lists.SelectMany(l => l.Items).Where(i => i.State == "failed")) { i.State = "pending"; i.Message = null; }
        job.State = "running"; states.Save(s);
        Launch(s, job);
    }

    void Launch(UserSession s, ImportJob job)
    {
        var cts = cancels[job.Id] = new CancellationTokenSource();
        running.GetOrAdd(job.Id, id0 => Task.Run(async () =>
        {
            try { await RunAsync(s, job, cts.Token); }
            catch (OperationCanceledException) { }
            catch (Exception e) { log.LogWarning(e, "Import {File} stopped", job.File); job.Note = e.Message; }
            finally { running.TryRemove(job.Id, out _); }
        }));
    }

    async Task RunAsync(UserSession s, ImportJob job, CancellationToken ct)
    {
        // wait for the library to load, or "already have it" would be answered wrongly for every song
        for (var i = 0; i < 360 && (s.Loading || s.LoadedAt == default); i++) await Task.Delay(500, ct);
        var gate = new SemaphoreSlim(3);
        if (job.Replace && !job.Consolidated) { foreach (var l in job.Lists) Consolidate(s, l); job.Consolidated = true; states.Save(s); }
        // first everything the library already has, so playlists fill at once; downloads (slow) come after
        foreach (var list in job.Lists)
        {
            ct.ThrowIfCancellationRequested();
            var plId = EnsurePlaylist(s, list, job.Replace);
            foreach (var item in list.Items.Where(i => i.State == "pending"))
            {
                var have = Have(s, item);
                if (have is null) continue;
                s.AddToPlaylist(plId, have); item.State = "owned";
            }
            Touch(s, job);
        }
        await FlushAsync(s, job);
        foreach (var list in job.Lists)
        {
            ct.ThrowIfCancellationRequested();
            var tasks = new List<Task>();
            foreach (var item in list.Items.Where(i => i.State == "pending").ToList())
            {
                await gate.WaitAsync(ct);
                tasks.Add(Task.Run(async () =>
                {
                    try { await HandleAsync(s, job, list, item, ct); }
                    catch (OperationCanceledException) { }
                    catch (Exception e) { item.State = "failed"; item.Message = e.Message; }
                    finally { gate.Release(); }
                }, ct));
            }
            await Task.WhenAll(tasks);
            await FlushAsync(s, job);
        }
        if (ct.IsCancellationRequested) return;
        await FlushAsync(s, job);
        job.State = "done";
        job.Note = job.Failed == 0 ? null : $"{job.Failed} song{(job.Failed == 1 ? "" : "s")} couldn't be found";
        activity.Add("import", s.DisplayName, $"Import finished: {job.File}, {job.Total - job.Failed} of {job.Total} songs");
        _ = notify.NotifyAsync("imports", "Imports finished", $"{job.File}: {job.Total - job.Failed} of {job.Total} songs ({s.DisplayName})", job.Failed == 0 ? 3 : 4, job.Failed == 0 ? "white_check_mark" : "warning");
        states.Save(s); Changed?.Invoke(s);
    }

    async Task HandleAsync(UserSession s, ImportJob job, ImportList list, ImportItem item, CancellationToken ct)
    {
        var plId = EnsurePlaylist(s, list, job.Replace);
        var have = Have(s, item);
        if (have is not null) { s.AddToPlaylist(plId, have); item.State = "owned"; Touch(s, job); return; }
        if (!downloads.Allowed) { item.State = "failed"; item.Message = DownloadManager.OffMessage; Touch(s, job); return; }
        if (!s.Services.Any) { item.State = "failed"; item.Message = "Downloads aren't set up"; Touch(s, job); return; }
        // out of room: hold everything until there is some, instead of failing the rest of the file
        while (!guard.Ok(s))
        {
            job.State = "waiting"; job.Note = "Waiting for free space on the music storage"; Touch(s, job);
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
        }
        if (job.State == "waiting") { job.State = "running"; job.Note = null; }
        // a playlist file is filled in the background: Hi-Res where it exists (smart downloads), the quick way where it does not
        var r = await downloads.FetchAsync(s, "Import", item.Artist, item.Title, item.Album, null, 0, ct, DownloadMode.HiRes);
        if (r.Ok && r.Tracks.Count > 0) foreach (var t in r.Tracks) s.AddToPlaylist(plId, t);
        (item.State, item.Message) = StateAfter(r);
        Touch(s, job);
    }

    /// <summary>The library's copy of an import's song: by title and artist, the import's loose match, then the same song under a longer or shorter title.</summary>
    static Track? Have(UserSession s, ImportItem item) =>
        s.Library.Find(item.Title, item.Artist) ?? s.Library.FindLoose(item.Title, item.Artist) ?? s.Library.Songs.FirstOrDefault(x => Matching.SameSongLoosely(item.Title, item.Artist, x));

    /// <summary>What a song of an import becomes after its fetch. A fetch that was cancelled (FLACie Web restarting, a deploy) goes back in the queue
    /// and the import picks it up again: it was kept as failed, and the owner's playlist lost 3 songs to deploys (5 Nov).</summary>
    public static (string State, string? Message) StateAfter(DownloadManager.DownloadResult r) =>
        r.Ok && r.Tracks.Count > 0 ? ("done", null)
        : r.Ok ? ("requested", "Requested from Lidarr; it appears after the next scan")
        : r.Message == "Cancelled" ? ("pending", null)
        : ("failed", r.Message);

    readonly ConcurrentDictionary<string, object> listLocks = new();
    static readonly System.Text.RegularExpressions.Regex QualityTag = new(@"\s*\((f?lac|alac|mp3|aac|wav|ogg|opus)\)\s*$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    static string Base(string n) => QualityTag.Replace(n, "").Trim().ToLowerInvariant();

    /// <summary>Replace mode, once per playlist: every account playlist with this name (a quality tag such as "(LAC)" ignored) is one and the same list.
    /// The one that is also a Jellyfin playlist (else the first) is kept, renamed to the file's name and emptied; the others are removed; the file's songs
    /// go back in. Items are reset so songs already in the library are simply added again.</summary>
    void Consolidate(UserSession s, ImportList list)
    {
        lock (listLocks.GetOrAdd(s.Id, _ => new object()))
        {
            var group = s.Playlists.Where(p => Base(p.Name) == Base(list.Name)).ToList();
            if (group.Count == 0) return;
            var keep = group.FirstOrDefault(p => p.JellyfinId is not null) ?? group.First();
            foreach (var p in group.Where(p => p.Id != keep.Id)) s.DeletePlaylist(p.Id);
            if (keep.Name != UserSession.CleanName(list.Name)) s.RenamePlaylist(keep.Id, UserSession.CleanName(list.Name));
            s.ReplacePlaylistTracks(keep.Id, []);
            list.PlaylistId = keep.Id;
            foreach (var i in list.Items) { i.State = "pending"; i.Message = null; }
        }
    }

    string EnsurePlaylist(UserSession s, ImportList list, bool replace)
    {
        lock (listLocks.GetOrAdd(s.Id, _ => new object()))
        {
            if (list.PlaylistId is { } id && s.Playlists.Any(p => p.Id == id)) return id;
            var name = UserSession.CleanName(list.Name);
            var existing = s.Playlists.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing is null && replace)
            {
                // "2026" in the account and "2026(LAC)" in the file are the same playlist: it takes the file's songs, and the name without the tag
                existing = s.Playlists.FirstOrDefault(p => Base(p.Name) == Base(list.Name));
                if (existing is not null) s.RenamePlaylist(existing.Id, name);
            }
            if (existing is not null && replace) s.ReplacePlaylistTracks(existing.Id, []);
            list.PlaylistId = (existing ?? s.CreatePlaylist(name)).Id;
            return list.PlaylistId;
        }
    }

    long lastSave, lastProfileSave;
    void Touch(UserSession s, ImportJob job)
    {
        Changed?.Invoke(s);
        if (Environment.TickCount64 - lastSave > 3_000) { lastSave = Environment.TickCount64; states.Save(s); }
        // the account itself is saved every half minute while songs arrive, so a restart never loses a playlist's progress
        if (Environment.TickCount64 - lastProfileSave > 30_000) { lastProfileSave = Environment.TickCount64; _ = Task.Run(async () => { try { await s.SaveAsync(jf); } catch (Exception) { } }); }
    }

    async Task FlushAsync(UserSession s, ImportJob job)
    {
        states.Save(s);
        try { await s.SaveAsync(jf); } catch (Exception e) { log.LogWarning(e, "Saving the profile after an import step failed"); }
    }
}
