using System.Text.Json;
using IpodSync.Core.ItunesDb;
using IpodSync.Core.Sync;
using IpodSync.Shared.Backend;

namespace IpodSync.Shared.State;

/// <summary>One queued edit, with a human label for the Changes panel.</summary>
public sealed record PendingChange(EditOp Op, string Label);

/// <summary>A playlist as it will look once the queued changes are written.</summary>
public sealed class PlaylistView
{
    /// <summary>The playlist's current name on the device (how ops address it); null for a playlist created in the queue.</summary>
    public string? DeviceName { get; init; }
    public EditOp? CreateOp { get; init; }
    public string Name { get; set; } = "";
    public List<uint> TrackIds { get; init; } = [];
    public bool IsSmart { get; init; }
    public bool IsPodcast { get; init; }
    public bool HasPendingChanges { get; set; }
    public int PendingFileAdds { get; set; }
    public string Key => DeviceName is not null ? "d:" + DeviceName : "n:" + (CreateOp?.GetHashCode() ?? 0);
}

/// <summary>
/// UI state shared by every tab: the selected iPod, its library, a queue of pending
/// edits, and the last pipeline run. Nothing reaches the device except through
/// <see cref="DryRunAsync"/> (always allowed) and <see cref="WriteAsync"/>, which is only
/// allowed after a dry run of exactly the same queue against the same loaded library
/// has passed.
/// </summary>
public sealed class AppState(IIpodSyncBackend backend)
{
    public IIpodSyncBackend Backend { get; } = backend;
    public event Action? Changed;
    public void Notify() => Changed?.Invoke();

    public List<DeviceSummary> Devices { get; private set; } = [];
    public string? ActiveRoot { get; private set; }
    public DeviceSummary? ActiveDevice => Devices.FirstOrDefault(d => d.RootPath == ActiveRoot);
    public ItunesDatabase? Db { get; private set; }
    public DeviceHealth? Health { get; private set; }
    public bool HealthLoading { get; private set; }
    public string? Error { get; set; }
    public bool Scanning { get; private set; }
    public bool Loading { get; private set; }
    private Guid _loadToken = Guid.NewGuid();

    public List<PendingChange> Pending { get; } = [];
    public List<string> Log { get; } = [];
    public bool Busy { get; private set; }
    public string? BusyText { get; private set; }
    public WritePipeline.Result? LastResult { get; private set; }
    public bool LastWasCommit { get; private set; }
    private string? _dryRunFingerprint;

    public bool CanWrite => Backend.Capabilities.CanWrite;
    public bool DryRunPassed => _dryRunFingerprint is not null && _dryRunFingerprint == Fingerprint();

    // ------------------------------------------------------------------ devices

    public async Task RefreshDevicesAsync()
    {
        Scanning = true; Error = null; Notify();
        try
        {
            Devices = await Backend.DetectDevicesAsync();
            if (ActiveRoot is not null && Devices.All(d => d.RootPath != ActiveRoot))
            {
                ActiveRoot = null; Db = null; Health = null; Pending.Clear(); _dryRunFingerprint = null;
            }
            if (ActiveRoot is null && Devices.FirstOrDefault(d => d.HasDatabase && !d.NeedsUserAction) is { } first) await SelectDeviceAsync(first.RootPath);
        }
        catch (Exception ex) { Error = Format(ex); }
        finally { Scanning = false; Notify(); }
    }

    public async Task SelectDeviceAsync(string root)
    {
        if (root != ActiveRoot) { Pending.Clear(); LastResult = null; _dryRunFingerprint = null; }
        ActiveRoot = root;
        await ReloadAsync();
    }

    public async Task ReloadAsync(bool health = true)
    {
        if (ActiveRoot is null) return;
        Loading = true; Error = null; Notify();
        try
        {
            Db = await Backend.LoadLibraryAsync(ActiveRoot);
            _loadToken = Guid.NewGuid();
        }
        catch (Exception ex) { Error = Format(ex); Db = null; }
        finally { Loading = false; Notify(); }
        if (health && Db is not null) _ = RefreshHealthAsync();
    }

    public async Task RefreshHealthAsync()
    {
        if (ActiveRoot is null) return;
        HealthLoading = true; Notify();
        try { Health = await Backend.CheckHealthAsync(ActiveRoot); }
        catch (Exception ex) { Health = null; AddLog($"health check failed: {Format(ex)}"); }
        finally { HealthLoading = false; Notify(); }
    }

    // ------------------------------------------------------------------ queue

    public void Enqueue(EditOp op, string label)
    {
        if (LastWasCommit) DismissResult();
        Pending.Add(new PendingChange(op, label));
        Notify();
    }

    public void DismissResult()
    {
        LastResult = null;
        LastWasCommit = false;
        lock (Log) Log.Clear();
        Notify();
    }

    public void RemovePending(PendingChange c) { Pending.Remove(c); Notify(); }
    public void ClearPending() { Pending.Clear(); Notify(); }

    public Track? TrackById(uint id) => Db?.Tracks.FirstOrDefault(t => t.Id == id);
    public string TrackName(uint id) => TrackById(id) is { } t ? $"{t.Artist} – {t.Title}" : $"track #{id}";

    public bool IsPendingRemoval(uint trackId) => Pending.Any(p => p.Op.Op == "removeTrack" && p.Op.TrackId == trackId);

    public EditFields? PendingFields(uint trackId) =>
        Pending.LastOrDefault(p => p.Op.Op == "setTrackFields" && p.Op.TrackId == trackId)?.Op.Fields;

    public int? PendingStars(uint trackId) =>
        Pending.LastOrDefault(p => p.Op.Op == "setTrackRating" && p.Op.TrackId == trackId)?.Op.Stars;

    public string? PendingArtwork(uint trackId) => Pending.LastOrDefault(p =>
        (p.Op.Op == "setTrackArtwork" && (p.Op.TrackId == trackId || p.Op.TrackIds?.Contains(trackId) == true)) ||
        (p.Op.Op == "removeTrackArtwork" && p.Op.TrackId == trackId)) is { } c
        ? (c.Op.Op == "removeTrackArtwork" ? "remove" : Path.GetFileName(c.Op.ImagePath)) : null;

    /// <summary>Title/artist/album edit; replaces an earlier queued edit of the same track.</summary>
    public void SetTrackFields(Track t, string title, string artist, string album)
    {
        Pending.RemoveAll(p => p.Op.Op == "setTrackFields" && p.Op.TrackId == t.Id);
        var f = new EditFields
        {
            Title = title != (t.Title ?? "") ? title : null,
            Artist = artist != (t.Artist ?? "") ? artist : null,
            Album = album != (t.Album ?? "") ? album : null,
        };
        if (f.Title is null && f.Artist is null && f.Album is null) { Notify(); return; }
        var parts = new[] { f.Title is null ? null : $"title “{f.Title}”", f.Artist is null ? null : $"artist “{f.Artist}”", f.Album is null ? null : $"album “{f.Album}”" };
        Enqueue(new EditOp { Op = "setTrackFields", TrackId = t.Id, Fields = f }, $"Edit {t.Title}: {string.Join(", ", parts.Where(x => x is not null))}");
    }

    public void SetRating(Track t, int stars)
    {
        Pending.RemoveAll(p => p.Op.Op == "setTrackRating" && p.Op.TrackId == t.Id);
        if (stars == t.Stars) { Notify(); return; }
        Enqueue(new EditOp { Op = "setTrackRating", TrackId = t.Id, Stars = stars }, $"Rate {t.Title}: {(stars == 0 ? "no rating" : new string('★', stars))}");
    }

    public void SetArtwork(IReadOnlyList<Track> tracks, string imagePath)
    {
        var ids = tracks.Select(t => t.Id).ToHashSet();
        Pending.RemoveAll(p => (p.Op.Op == "removeTrackArtwork" && ids.Contains(p.Op.TrackId ?? 0)));
        foreach (var p in Pending.Where(p => p.Op.Op == "setTrackArtwork").ToList())
        {
            var remaining = (p.Op.TrackIds ?? []).Where(i => !ids.Contains(i)).ToArray();
            if (remaining.Length == 0) Pending.Remove(p); else p.Op.TrackIds = remaining;
        }
        string what = tracks.Count == 1 ? tracks[0].Title ?? "track" : $"{tracks.Count} tracks ({tracks[0].Album})";
        Enqueue(new EditOp { Op = "setTrackArtwork", TrackIds = ids.ToArray(), ImagePath = imagePath }, $"Cover art for {what} ← {Path.GetFileName(imagePath)}");
    }

    public void RemoveArtwork(Track t)
    {
        Pending.RemoveAll(p => p.Op.Op == "removeTrackArtwork" && p.Op.TrackId == t.Id);
        if (!t.HasArtwork) { Notify(); return; }
        Enqueue(new EditOp { Op = "removeTrackArtwork", TrackId = t.Id }, $"Remove cover art from {t.Title}");
    }

    /// <summary>Deleting a track drops every other queued edit that refers to it.</summary>
    public void RemoveTrack(Track t)
    {
        Pending.RemoveAll(p => p.Op.TrackId == t.Id);
        foreach (var p in Pending.Where(p => p.Op.TrackIds is not null).ToList())
        {
            p.Op.TrackIds = p.Op.TrackIds!.Where(i => i != t.Id).ToArray();
            if (p.Op.Op == "setTrackArtwork" && p.Op.TrackIds.Length == 0) Pending.Remove(p);
        }
        Enqueue(new EditOp { Op = "removeTrack", TrackId = t.Id }, $"Delete from iPod: {t.Artist} – {t.Title}");
    }

    public void AddFiles(IEnumerable<string> paths, string transcode, string? playlistDeviceName)
    {
        foreach (var path in paths)
        {
            if (Pending.Any(p => p.Op.Op == "addTrackFromFile" && string.Equals(p.Op.SourcePath, path, StringComparison.OrdinalIgnoreCase))) continue;
            Enqueue(new EditOp { Op = "addTrackFromFile", SourcePath = path, Transcode = transcode, Playlist = playlistDeviceName },
                $"Add {Path.GetFileName(path)}{(transcode != "auto" ? $" (convert: {transcode})" : "")}{(playlistDeviceName is null ? "" : $" → {playlistDeviceName}")}");
        }
    }

    // ------------------------------------------------------------------ playlists

    public List<PlaylistView> EffectivePlaylists()
    {
        var views = new List<PlaylistView>();
        if (Db is null) return views;
        foreach (var p in Db.Playlists.Where(p => !p.IsMaster))
            views.Add(new PlaylistView { DeviceName = p.Name, Name = p.Name ?? "", TrackIds = [.. p.TrackIds], IsSmart = p.IsSmart, IsPodcast = p.IsPodcast });
        foreach (var c in Pending)
        {
            var op = c.Op;
            PlaylistView? Target() => views.FirstOrDefault(v => v.DeviceName is not null && v.DeviceName == op.Playlist);
            switch (op.Op)
            {
                case "createPlaylist":
                    views.Add(new PlaylistView { CreateOp = op, Name = op.Name ?? "", TrackIds = [.. op.TrackIds ?? []], HasPendingChanges = true });
                    break;
                case "renamePlaylist" when Target() is { } v: v.Name = op.Name ?? v.Name; v.HasPendingChanges = true; break;
                case "deletePlaylist" when Target() is { } v: views.Remove(v); break;
                case "addTrackToPlaylist" when Target() is { } v: v.TrackIds.Add(op.TrackId ?? 0); v.HasPendingChanges = true; break;
                case "removeTrackFromPlaylist" when Target() is { } v: v.TrackIds.RemoveAll(i => i == op.TrackId); v.HasPendingChanges = true; break;
                case "reorderPlaylist" when Target() is { } v:
                {
                    var rest = new List<uint>(v.TrackIds);
                    var ordered = new List<uint>();
                    foreach (var id in op.TrackIds ?? []) if (rest.Remove(id)) ordered.Add(id);
                    v.TrackIds.Clear(); v.TrackIds.AddRange(ordered.Concat(rest)); v.HasPendingChanges = true;
                    break;
                }
                case "removeTrack":
                    foreach (var v in views) v.TrackIds.RemoveAll(i => i == op.TrackId);
                    break;
                case "addTrackFromFile" when op.Playlist is not null && Target() is { } v: v.PendingFileAdds++; v.HasPendingChanges = true; break;
            }
        }
        return views;
    }

    public bool NameInUse(string name, PlaylistView? except = null) =>
        EffectivePlaylists().Any(v => v != null && v.Key != except?.Key && string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase))
        || string.Equals(Db?.Playlists.FirstOrDefault(p => p.IsMaster)?.Name, name, StringComparison.OrdinalIgnoreCase);

    public void CreatePlaylist(string name, IEnumerable<uint> trackIds)
    {
        var ids = trackIds.ToArray();
        Enqueue(new EditOp { Op = "createPlaylist", Name = name, TrackIds = ids }, $"New playlist “{name}” ({ids.Length} tracks)");
    }

    public void RenamePlaylist(PlaylistView v, string name)
    {
        if (v.CreateOp is not null)
        {
            v.CreateOp.Name = name;
            Relabel(v.CreateOp, $"New playlist “{name}” ({v.CreateOp.TrackIds?.Length ?? 0} tracks)");
            return;
        }
        Pending.RemoveAll(p => p.Op.Op == "renamePlaylist" && p.Op.Playlist == v.DeviceName);
        if (name == v.DeviceName) { Notify(); return; }
        Enqueue(new EditOp { Op = "renamePlaylist", Playlist = v.DeviceName, Name = name }, $"Rename playlist “{v.DeviceName}” → “{name}”");
    }

    public void DeletePlaylist(PlaylistView v)
    {
        if (v.CreateOp is not null) { Pending.RemoveAll(p => ReferenceEquals(p.Op, v.CreateOp)); Notify(); return; }
        Pending.RemoveAll(p => p.Op.Playlist == v.DeviceName && p.Op.Op is "renamePlaylist" or "addTrackToPlaylist" or "removeTrackFromPlaylist" or "reorderPlaylist");
        foreach (var p in Pending.Where(p => p.Op.Op == "addTrackFromFile" && p.Op.Playlist == v.DeviceName)) p.Op.Playlist = null;
        Enqueue(new EditOp { Op = "deletePlaylist", Playlist = v.DeviceName }, $"Delete playlist “{v.DeviceName}” (its songs stay on the iPod)");
    }

    public void AddToPlaylist(PlaylistView v, IEnumerable<uint> trackIds)
    {
        var ids = trackIds.ToList();
        if (v.CreateOp is not null)
        {
            v.CreateOp.TrackIds = [.. v.CreateOp.TrackIds ?? [], .. ids];
            Relabel(v.CreateOp, $"New playlist “{v.CreateOp.Name}” ({v.CreateOp.TrackIds.Length} tracks)");
            return;
        }
        foreach (var id in ids)
            Enqueue(new EditOp { Op = "addTrackToPlaylist", Playlist = v.DeviceName, TrackId = id }, $"Add {TrackName(id)} to “{v.DeviceName}”");
    }

    public void RemoveFromPlaylist(PlaylistView v, uint trackId)
    {
        if (v.CreateOp is not null)
        {
            v.CreateOp.TrackIds = (v.CreateOp.TrackIds ?? []).Where(i => i != trackId).ToArray();
            Relabel(v.CreateOp, $"New playlist “{v.CreateOp.Name}” ({v.CreateOp.TrackIds.Length} tracks)");
            return;
        }
        // Undo a queued add instead of queueing add-then-remove.
        var queuedAdd = Pending.LastOrDefault(p => p.Op.Op == "addTrackToPlaylist" && p.Op.Playlist == v.DeviceName && p.Op.TrackId == trackId);
        bool onDevice = Db?.Playlists.FirstOrDefault(p => !p.IsMaster && p.Name == v.DeviceName)?.TrackIds.Contains(trackId) == true;
        Pending.RemoveAll(p => p.Op.Op == "addTrackToPlaylist" && p.Op.Playlist == v.DeviceName && p.Op.TrackId == trackId);
        if (onDevice)
            Enqueue(new EditOp { Op = "removeTrackFromPlaylist", Playlist = v.DeviceName, TrackId = trackId }, $"Remove {TrackName(trackId)} from “{v.DeviceName}”");
        else if (queuedAdd is not null) Notify();
    }

    public void ReorderPlaylist(PlaylistView v, List<uint> newOrder)
    {
        if (v.CreateOp is not null) { v.CreateOp.TrackIds = [.. newOrder]; Notify(); return; }
        Pending.RemoveAll(p => p.Op.Op == "reorderPlaylist" && p.Op.Playlist == v.DeviceName);
        Enqueue(new EditOp { Op = "reorderPlaylist", Playlist = v.DeviceName, TrackIds = [.. newOrder] }, $"Reorder “{v.DeviceName}”");
    }

    private void Relabel(EditOp op, string label)
    {
        int i = Pending.FindIndex(p => ReferenceEquals(p.Op, op));
        if (i >= 0) Pending[i] = Pending[i] with { Label = label };
        Notify();
    }

    // ------------------------------------------------------------------ pipeline

    private string Fingerprint() =>
        $"{ActiveRoot}|{_loadToken}|{JsonSerializer.Serialize(Pending.Select(p => p.Op))}";

    public ChangeSet CurrentChangeSet() => new() { Ops = Pending.Select(p => p.Op).ToList() };

    public async Task<WritePipeline.Result?> DryRunAsync() => await RunAsync(commit: false);

    public async Task<WritePipeline.Result?> WriteAsync()
    {
        if (!DryRunPassed) { Error = "Run a check (dry run) of these exact changes first."; Notify(); return null; }
        return await RunAsync(commit: true);
    }

    private async Task<WritePipeline.Result?> RunAsync(bool commit)
    {
        if (ActiveRoot is null || Pending.Count == 0 || Busy) return null;
        string fp = Fingerprint();
        Busy = true; BusyText = commit ? "Writing to the iPod and verifying…" : "Checking changes (dry run)…";
        Error = null; LastResult = null; LastWasCommit = commit;
        Log.Clear();
        Notify();
        WritePipeline.Result? result = null;
        try
        {
            result = await Backend.RunChangesAsync(ActiveRoot, CurrentChangeSet(), commit, "app", AddLogThrottled);
            LastResult = result;
            if (!commit) _dryRunFingerprint = result.ExitCode == 0 && fp == Fingerprint() ? fp : null;
            else
            {
                _dryRunFingerprint = null;
                if (result.Written) Pending.Clear();
            }
        }
        catch (Exception ex) { Error = Format(ex); AddLog("ERROR: " + Format(ex)); }
        finally
        {
            Busy = false; BusyText = null;
            Notify();
        }
        if (commit) await ReloadAsync();
        return result;
    }

    public async Task<T?> RunJobAsync<T>(string busyText, Func<Action<string>, Task<T>> job, bool reloadAfter)
    {
        if (Busy) return default;
        Busy = true; BusyText = busyText; Error = null; Log.Clear(); Notify();
        try { return await job(AddLogThrottled); }
        catch (Exception ex) { Error = Format(ex); AddLog("ERROR: " + Format(ex)); return default; }
        finally
        {
            Busy = false; BusyText = null; Notify();
            if (reloadAfter) { _dryRunFingerprint = null; await ReloadAsync(); }
        }
    }

    private DateTime _lastNotify = DateTime.MinValue;
    private void AddLogThrottled(string line)
    {
        lock (Log)
        {
            Log.Add(line);
            if (Log.Count > 5000) Log.RemoveRange(0, Log.Count - 5000);
        }
        if ((DateTime.UtcNow - _lastNotify).TotalMilliseconds > 150) { _lastNotify = DateTime.UtcNow; Notify(); }
    }

    public void AddLog(string line) { lock (Log) Log.Add(line); Notify(); }

    public static string Format(Exception ex) =>
        $"{ex.GetType().Name}: {ex.Message}" + (ex.InnerException is { } inner ? $" (inner: {inner.GetType().Name}: {inner.Message})" : "");
}
