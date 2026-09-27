using IpodSync.Core.ItunesDb;
using IpodSync.Core.LocalLibrary;

namespace IpodSync.Core.Sync;

/// <summary>
/// Executes a <see cref="FolderSync.Plan"/> through <see cref="WritePipeline"/>: adopts
/// matches into the manifest, adds files in verified batches (the manifest records each
/// batch only after its write passed every device check), then optionally removes tracks
/// this sync added whose source file is gone. Shared by the CLI and the app.
/// </summary>
public static class FolderSyncJob
{
    /// <summary>Lossless sources become ALAC (about their own size), lossy non-native ones 256k AAC.</summary>
    public static long EstimateDeviceBytes(IEnumerable<FolderSync.SourceFile> files) => files.Sum(f =>
    {
        string ext = Path.GetExtension(f.Path).ToLowerInvariant();
        if (ext is ".flac" or ".wav" or ".ape" or ".wv" or ".aif" or ".aiff") return (long)(f.Size * 1.1);
        if (ext is ".ogg" or ".oga" or ".opus" or ".wma") return Math.Max(f.Size, (long)(f.Seconds * 32000));
        return f.Size;
    });

    public const long SpaceReserve = 200L * 1024 * 1024;

    public static ChangeSet BatchChangeSet(IEnumerable<FolderSync.SourceFile> files, string? playlist) => new()
    {
        Ops = files.Select(f => new EditOp { Op = "addTrackFromFile", SourcePath = f.Path, Playlist = playlist }).ToList(),
    };

    public sealed record Outcome(int ExitCode, int Added, int Adopted, int Removed, string? LastBackupDir);

    /// <param name="optionsFor">Builds committing pipeline options for one change-set.</param>
    public static Outcome Commit(FolderSync.Plan plan, FolderSync.Manifest manifest, IReadOnlyList<FolderSync.SourceFile> toAdd,
        int batch, string? playlist, bool removeMissing, Func<ChangeSet, WritePipeline.Options> optionsFor,
        Action<string> say, CancellationToken ct = default)
    {
        string? lastBackup = null;
        foreach (var (f, t) in plan.Adopt)
            manifest.Entries.Add(new FolderSync.ManifestEntry { RelativePath = f.RelativePath, Size = f.Size, MtimeTicks = f.MtimeTicks, PersistentId = t.PersistentId, Origin = "adopted" });
        manifest.Save();
        say($"manifest    adopted {plan.Adopt.Count} existing track(s)");

        int done = 0;
        foreach (var chunk in toAdd.Chunk(Math.Max(1, batch)))
        {
            if (ct.IsCancellationRequested) { say($"cancelled before batch {done / batch + 1}; {done} file(s) added."); return new(1, done, plan.Adopt.Count, 0, lastBackup); }
            var cs = BatchChangeSet(chunk, playlist);
            say("");
            say($"=== batch {done / batch + 1}: {chunk.Length} file(s)");
            var bySource = chunk.ToDictionary(f => Path.GetFullPath(f.Path), StringComparer.OrdinalIgnoreCase);
            var r = WritePipeline.Execute(optionsFor(cs).WithCallback(report =>
            {
                foreach (var (src, pid) in report?.AddedTracks ?? [])
                    if (bySource.TryGetValue(Path.GetFullPath(src), out var f))
                        manifest.Entries.Add(new FolderSync.ManifestEntry { RelativePath = f.RelativePath, Size = f.Size, MtimeTicks = f.MtimeTicks, PersistentId = pid, Origin = "added" });
                manifest.Save();
            }));
            lastBackup = r.BackupDir ?? lastBackup;
            if (r.ExitCode != 0) { say($"batch failed (exit {r.ExitCode}); stopping. {done} file(s) added before it."); return new(r.ExitCode, done, plan.Adopt.Count, 0, lastBackup); }
            done += chunk.Length;
        }

        int removed = 0;
        var removals = plan.RemoveCandidates.Where(x => x.Entry.Origin == "added").ToList();
        if (removeMissing && removals.Count > 0)
        {
            var cs = new ChangeSet { Ops = removals.Select(x => new EditOp { Op = "removeTrack", TrackId = x.Track.Id }).ToList() };
            say("");
            say($"=== removing {removals.Count} track(s) whose source file is gone");
            var gone = removals.Select(x => x.Entry).ToHashSet();
            var r = WritePipeline.Execute(optionsFor(cs).WithCallback(_ =>
            {
                manifest.Entries.RemoveAll(gone.Contains);
                manifest.Save();
            }));
            lastBackup = r.BackupDir ?? lastBackup;
            if (r.ExitCode != 0) return new(r.ExitCode, done, plan.Adopt.Count, 0, lastBackup);
            removed = removals.Count;
        }
        say($"SYNC COMPLETE: {done} added, {plan.Adopt.Count} adopted, {plan.Unchanged.Count} already in sync.");
        return new(0, done, plan.Adopt.Count, removed, lastBackup);
    }
}

/// <summary>Turns a playlist file into playlist edit ops against the device library.</summary>
public static class PlaylistImportJob
{
    public sealed record Prepared(PlaylistImport.Result Match, List<EditOp> Ops, List<string> Summary, bool CreatesNew);

    public static Prepared Prepare(ItunesDatabase cdb, IReadOnlyList<PlaylistImport.Entry> entries, string name, bool replace)
    {
        var target = cdb.Playlists.FirstOrDefault(p => !p.IsMaster && p.Name == name);
        var match = PlaylistImport.Match(entries, cdb, target?.TrackIds.ToHashSet());
        var wanted = match.Matched.Select(m => m.Track.Id).Distinct().ToList();
        var ops = new List<EditOp>();
        var lines = new List<string>();
        if (target is null)
        {
            lines.Add($"new playlist '{name}' with {wanted.Count} track(s)");
            if (wanted.Count > 0) ops.Add(new EditOp { Op = "createPlaylist", Name = name, TrackIds = wanted.ToArray() });
        }
        else
        {
            var have = target.TrackIds.ToList();
            var missing = wanted.Where(id => !have.Contains(id)).ToList();
            var extra = have.Where(id => !wanted.Contains(id)).Distinct().ToList();
            lines.Add($"existing playlist '{name}' ({have.Count} tracks): {wanted.Count - missing.Count} already in it, {missing.Count} missing, {extra.Count} not in the file");
            ops.AddRange(missing.Select(id => new EditOp { Op = "addTrackToPlaylist", Playlist = name, TrackId = id }));
            if (replace)
            {
                ops.AddRange(extra.Select(id => new EditOp { Op = "removeTrackFromPlaylist", Playlist = name, TrackId = id }));
                if (!have.Where(wanted.Contains).Concat(missing).SequenceEqual(wanted) || extra.Count > 0)
                    ops.Add(new EditOp { Op = "reorderPlaylist", Playlist = name, TrackIds = wanted.ToArray() });
                lines.Add("replace: playlist becomes exactly the file's order (tracks not in the file leave the playlist, not the iPod)");
            }
            else lines.Add("append missing tracks");
        }
        return new Prepared(match, ops, lines, target is null);
    }
}
