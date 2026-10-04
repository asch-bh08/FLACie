using System.Collections.Concurrent;
using FLACie.Core;

namespace FLACie.Server;

/// <summary>
/// Playlist import that runs entirely on the server. The phone or browser only hands over the file and later asks how far it got: the
/// searching and downloading happen here, a few songs at a time, and the songs are put into the account's playlists as they arrive. A song
/// the library already has is simply added. It pauses while the music storage is nearly full and carries on after a restart.
/// </summary>
public sealed class ImportManager(DownloadManager downloads, UserStateStore states, StorageGuard guard, JellyfinClient jf, ILogger<ImportManager> log)
{
    readonly ConcurrentDictionary<string, Task> running = new();
    readonly ConcurrentDictionary<string, CancellationTokenSource> cancels = new();
    public event Action<UserSession>? Changed;

    /// <summary>Reads the file and starts the import. Throws a message-carrying exception if nothing in it looks like songs.</summary>
    public ImportJob Start(UserSession s, string fileName, byte[] bytes)
    {
        List<ImportedPlaylist> lists;
        try { lists = PlaylistImport.Parse(fileName, bytes); }
        catch (Exception e) { throw new InvalidDataException("Couldn't read that file: " + e.Message); }
        if (lists.Count == 0) throw new InvalidDataException("No songs found in that file. It can be an iTunes Library.xml, an M3U playlist, a Spotify CSV, or a text list with one \"Artist - Title\" per line.");
        var job = new ImportJob
        {
            File = fileName,
            Lists = lists.Select(l => new ImportList { Name = l.Name, Items = l.Tracks.Select(t => new ImportItem { Artist = t.Artist, Title = t.Title, Album = t.Album }).ToList() }).ToList(),
        };
        var st = states.For(s);
        lock (st) { st.Imports.Insert(0, job); while (st.Imports.Count > 30) st.Imports.RemoveAt(st.Imports.Count - 1); }
        states.Save(s);
        Launch(s, job);
        return job;
    }

    /// <summary>Picks up unfinished imports after a restart.</summary>
    public void Resume(UserSession s)
    {
        foreach (var job in states.For(s).Imports.Where(j => j.State is "running" or "waiting").ToList()) Launch(s, job);
    }

    public void Cancel(UserSession s, string id)
    {
        if (cancels.TryGetValue(id, out var c)) c.Cancel();
        if (states.For(s).Imports.FirstOrDefault(j => j.Id == id) is { } job && job.State is "running" or "waiting") { job.State = "cancelled"; states.Save(s); Changed?.Invoke(s); }
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
        for (var i = 0; i < 240 && (s.Loading || s.Library.Songs.Count == 0 && s.LoadedAt == default); i++) await Task.Delay(500, ct);
        var gate = new SemaphoreSlim(3);
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
        job.State = "done";
        job.Note = job.Failed == 0 ? null : $"{job.Failed} song{(job.Failed == 1 ? "" : "s")} couldn't be found";
        states.Save(s); Changed?.Invoke(s);
    }

    async Task HandleAsync(UserSession s, ImportJob job, ImportList list, ImportItem item, CancellationToken ct)
    {
        var plId = EnsurePlaylist(s, list);
        var have = s.Library.Find(item.Title, item.Artist);
        if (have is not null) { s.AddToPlaylist(plId, have); item.State = "owned"; Touch(s, job); return; }
        if (!s.Services.Any) { item.State = "failed"; item.Message = "Downloads aren't set up"; Touch(s, job); return; }
        // out of room: hold everything until there is some, instead of failing the rest of the file
        while (!guard.Ok(s))
        {
            job.State = "waiting"; job.Note = "Waiting for free space on the music storage"; Touch(s, job);
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
        }
        if (job.State == "waiting") { job.State = "running"; job.Note = null; }
        var r = await downloads.FetchAsync(s, "Import", item.Artist, item.Title, item.Album, null, 0, ct);
        if (r.Ok && r.Tracks.Count > 0) { foreach (var t in r.Tracks) s.AddToPlaylist(plId, t); item.State = "done"; }
        else if (r.Ok) { item.State = "requested"; item.Message = "Requested from Lidarr; it appears after the next scan"; }
        else { item.State = "failed"; item.Message = r.Message; }
        Touch(s, job);
    }

    readonly ConcurrentDictionary<string, object> listLocks = new();
    string EnsurePlaylist(UserSession s, ImportList list)
    {
        lock (listLocks.GetOrAdd(s.Id, _ => new object()))
        {
            if (list.PlaylistId is { } id && s.Playlists.Any(p => p.Id == id)) return id;
            var existing = s.Playlists.FirstOrDefault(p => p.Name.Equals(list.Name, StringComparison.OrdinalIgnoreCase));
            list.PlaylistId = (existing ?? s.CreatePlaylist(list.Name)).Id;
            return list.PlaylistId;
        }
    }

    long lastSave;
    void Touch(UserSession s, ImportJob job)
    {
        Changed?.Invoke(s);
        if (Environment.TickCount64 - lastSave > 3_000) { lastSave = Environment.TickCount64; states.Save(s); }
    }

    async Task FlushAsync(UserSession s, ImportJob job)
    {
        states.Save(s);
        try { await s.SaveAsync(jf); } catch (Exception e) { log.LogWarning(e, "Saving the profile after an import step failed"); }
    }
}
