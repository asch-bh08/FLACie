using System.Text.Json;
using IpodSync.Core.Artwork;
using IpodSync.Core.Device;
using IpodSync.Core.ItunesDb;
using IpodSync.Core.Itlp;
using IpodSync.Core.LocalLibrary;
using IpodSync.Core.Signing;
using IpodSync.Core.Sync;

namespace IpodSync.Shared.Backend;

/// <summary>Talks to IpodSync.Core directly against a mounted drive letter.
/// Correct only when running on the same machine the iPod is physically plugged
/// into (the web host, and the Windows build of the MAUI app) -- never register
/// this for Android, which has no drive letter for an attached iPod.
///
/// Every write goes through <see cref="WritePipeline"/>, the same code path the CLI
/// uses: dry run, verified backup, write, read-back, device re-verify, auto-restore.
/// Only one pipeline runs at a time.</summary>
public sealed class LocalIpodSyncBackend : IIpodSyncBackend
{
    private static readonly SemaphoreSlim WriteLock = new(1, 1);
    private readonly AppSettings _settings = AppSettings.Load();

    public BackendCapabilities Capabilities => new(true, Core.Transcode.Transcoder.FfmpegAvailable(), true);

    public string BackupRoot
    {
        get => _settings.BackupRoot;
        set { _settings.BackupRoot = value; _settings.Save(); }
    }

    public Task<List<DeviceSummary>> DetectDevicesAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        var list = IpodDevice.Detect()
            .Select(d => new DeviceSummary(d.RootPath, d.VolumeLabel, d.FileSystem, d.IsFat32, d.HasDatabase, d.TotalBytes, d.FreeBytes))
            .ToList();
        // Testing aid: IPODSYNC_EXTRA_ROOTS=path;path lists folders laid out like an iPod
        // (e.g. the fake roots tools/fake-root-regression.sh builds) as extra devices.
        foreach (var extra in (Environment.GetEnvironmentVariable("IPODSYNC_EXTRA_ROOTS") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Directory.Exists(Path.Combine(extra, "iPod_Control"))) continue;
            var d = IpodDevice.Open(extra);
            long free = 0, total = 0;
            try { var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(extra))!); free = drive.AvailableFreeSpace; total = drive.TotalSize; } catch { }
            list.Add(new DeviceSummary(extra, "(folder)", "FAT32", true, d.HasDatabase, total, free));
        }
        return list;
    }, ct);

    public Task<ItunesDatabase> LoadLibraryAsync(string deviceRoot, CancellationToken ct = default) =>
        Task.Run(() => ItunesDbReader.Read(File.ReadAllBytes(IpodDevice.Open(deviceRoot).ItunesDbPath)), ct);

    public Task<List<LocalTrack>> ScanLocalAsync(string folder, CancellationToken ct = default) =>
        Task.Run(() => LocalLibraryScanner.Scan(folder), ct);

    public Task<DeviceHealth?> CheckHealthAsync(string deviceRoot, CancellationToken ct = default) => Task.Run<DeviceHealth?>(() =>
    {
        var device = IpodDevice.Open(deviceRoot);
        string itunesDir = Path.GetDirectoryName(device.ItunesDbPath)!;
        byte[] cdbBytes = File.ReadAllBytes(device.ItunesDbPath);
        var cdb = ItunesDbReader.Read(cdbBytes);
        long free = 0, total = 0;
        try { var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(deviceRoot))!); free = drive.AvailableFreeSpace; total = drive.TotalSize; } catch { }

        // Signatures: hash72 must validate on the CDB and the Locations cbk; hash58 is
        // valid when a FirewireGuid candidate reproduces it (DeviceSigning.Resolve).
        bool? sigOk = null;
        string sigDetail = "not signed (older iPod)";
        if (DeviceSigning.RequiresSigning(cdbBytes))
        {
            var parts = new List<string>();
            bool h72 = Hash72.ExtractFromDatabase(cdbBytes) is not null;
            parts.Add(h72 ? "hash72 valid" : "hash72 INVALID");
            var problems = new List<string>();
            var signer = DeviceSigning.Resolve(itunesDir, Path.GetFileName(device.ItunesDbPath),
                SigningInputs.FirewireCandidates([]), SigningInputs.References(BackupRoot, []), problems);
            bool h58 = signer is not null && signer.VerifyDatabase(cdbBytes).Count == 0;
            parts.Add(h58 ? "hash58 valid" : "hash58 not proven");
            bool cbkOk = true;
            string loc = Path.Combine(itunesDir, "iTunes Library.itlp", "Locations.itdb");
            if (File.Exists(loc) && File.Exists(loc + ".cbk"))
            {
                cbkOk = Hash72.VerifyCbk(File.ReadAllBytes(loc), File.ReadAllBytes(loc + ".cbk")).Problems.Count == 0;
                parts.Add(cbkOk ? "cbk valid" : "cbk INVALID");
            }
            sigOk = h72 && h58 && cbkOk;
            sigDetail = string.Join(", ", parts);
        }

        bool? inSync = null;
        string syncDetail = "no SQLite library on this iPod (classic database only)";
        string itlp = Path.Combine(itunesDir, "iTunes Library.itlp");
        if (File.Exists(Path.Combine(itlp, "Library.itdb")))
        {
            var diff = ItlpCompare.Compare(itlp, cdb);
            inSync = diff.InSync;
            syncDetail = diff.InSync ? "iTunesCDB and SQLite library agree"
                : $"playlists {(diff.PlaylistsInSync ? "agree" : "differ")}, tracks {(diff.TracksInSync ? "agree" : "differ")}";
        }

        bool? artOk = null;
        string artDetail = "no artwork database";
        string artDir = Path.Combine(device.ControlPath, "Artwork");
        if (File.Exists(Path.Combine(artDir, "ArtworkDB")))
        {
            var root = ArtworkDb.Parse(File.ReadAllBytes(Path.Combine(artDir, "ArtworkDB")));
            var lengths = ArtworkDb.Formats(root).Select(f => f.Format).Distinct()
                .ToDictionary(f => f, f => File.Exists(Path.Combine(artDir, $"F{f}_1.ithmb")) ? new FileInfo(Path.Combine(artDir, $"F{f}_1.ithmb")).Length : -1L);
            var problems = ArtworkDb.Check(root, cdb, lengths);
            artOk = problems.Count == 0;
            artDetail = artOk == true ? $"{ArtworkDb.Images(root).Count()} images, {cdb.Tracks.Count(t => t.HasArtwork)} tracks with art" : $"{problems.Count} problem(s): {problems[0]}";
        }

        return new DeviceHealth(cdb.Tracks.Count, cdb.Playlists.Count(p => !p.IsMaster), free, total, sigOk, sigDetail, inSync, syncDetail, artOk, artDetail);
    }, ct);

    public Task<string?> GetArtworkDataUrlAsync(string deviceRoot, uint artworkId, CancellationToken ct = default) => Task.Run(() =>
    {
        string artDir = Path.Combine(deviceRoot, "iPod_Control", "Artwork");
        return ArtworkPreview.DataUrl(artDir, artworkId, ArtworkCache);
    }, ct);

    private static readonly ArtworkPreview.Cache ArtworkCache = new();

    public async Task<WritePipeline.Result> RunChangesAsync(string deviceRoot, ChangeSet changes, bool commit, string label, Action<string> log, CancellationToken ct = default)
    {
        await WriteLock.WaitAsync(ct);
        try { return await Task.Run(() => WritePipeline.Execute(Options(deviceRoot, changes, commit, label, log)), ct); }
        finally { WriteLock.Release(); ArtworkCache.Clear(); }
    }

    private WritePipeline.Options Options(string root, ChangeSet? changes, bool commit, string label, Action<string> log)
    {
        Directory.CreateDirectory(BackupRoot);
        return new WritePipeline.Options
        {
            Root = root, Changes = changes, Commit = commit, Label = label, BackupRoot = BackupRoot, Log = log,
            FirewireCandidates = SigningInputs.FirewireCandidates([]),
            SigningReferences = SigningInputs.References(BackupRoot, []),
        };
    }

    public Task<FolderSyncPreview> PreviewFolderSyncAsync(string deviceRoot, string folder, bool removeMissing, Action<int>? progress = null, CancellationToken ct = default) => Task.Run(() =>
    {
        folder = Path.GetFullPath(folder);
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"folder not found: {folder}");
        var cdb = ItunesDbReader.Read(File.ReadAllBytes(IpodDevice.Open(deviceRoot).ItunesDbPath));
        var manifest = FolderSync.Manifest.Load(cdb.LibraryPersistentId, folder);
        var files = FolderSync.Scan(folder, progress);
        var plan = FolderSync.MakePlan(files, cdb, manifest, removeMissing);
        long free = 0;
        try { free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(deviceRoot))!).AvailableFreeSpace; } catch { }
        return new FolderSyncPreview(deviceRoot, folder, files, plan, manifest, FolderSyncJob.EstimateDeviceBytes(plan.Add), free,
            FolderSync.Manifest.PathFor(cdb.LibraryPersistentId, folder));
    }, ct);

    public async Task<FolderSyncJob.Outcome> CommitFolderSyncAsync(FolderSyncPreview preview, IReadOnlyList<FolderSync.SourceFile> toAdd, int batch,
        string? playlist, bool removeMissing, Action<string> log, CancellationToken ct = default)
    {
        await WriteLock.WaitAsync(ct);
        try
        {
            return await Task.Run(() =>
            {
                long need = FolderSyncJob.EstimateDeviceBytes(toAdd);
                long free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(preview.DeviceRoot))!).AvailableFreeSpace;
                if (need > free - FolderSyncJob.SpaceReserve)
                {
                    log($"refusing: need ~{need / 1048576.0:F0} MB but the iPod has {free / 1048576.0:F0} MB free (keeping {FolderSyncJob.SpaceReserve / 1048576} MB spare).");
                    return new FolderSyncJob.Outcome(1, 0, 0, 0, null);
                }
                return FolderSyncJob.Commit(preview.Plan, preview.Manifest, toAdd, batch, playlist, removeMissing,
                    cs => Options(preview.DeviceRoot, cs, true, "syncfolder", log), log, ct);
            }, CancellationToken.None);
        }
        finally { WriteLock.Release(); ArtworkCache.Clear(); }
    }

    public Task<ImportPreview> PreviewImportAsync(string deviceRoot, string playlistFile, string name, bool replace, CancellationToken ct = default) => Task.Run(() =>
    {
        var cdb = ItunesDbReader.Read(File.ReadAllBytes(IpodDevice.Open(deviceRoot).ItunesDbPath));
        var entries = PlaylistImport.Read(playlistFile);
        return new ImportPreview(deviceRoot, playlistFile, name, replace, entries.Count, PlaylistImportJob.Prepare(cdb, entries, name, replace));
    }, ct);

    public Task<List<BackupInfo>> ListBackupsAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        var list = new List<BackupInfo>();
        if (!Directory.Exists(BackupRoot)) return list;
        foreach (var dir in Directory.EnumerateDirectories(BackupRoot))
        {
            if (!Directory.Exists(Path.Combine(dir, "iTunes"))) continue;
            string name = Path.GetFileName(dir);
            string logPath = Path.Combine(dir, "write-log.txt");
            string? outcome = null;
            if (File.Exists(logPath))
            {
                var lines = File.ReadAllLines(logPath);
                outcome = lines.LastOrDefault(l => l.StartsWith("WRITE VERIFIED") || l.StartsWith("Device restored") || l.StartsWith("RESTORE INCOMPLETE"))
                    ?? lines.LastOrDefault(l => l.Trim().Length > 0);
            }
            string kind = name.Contains('-') ? name[..name.IndexOf('-')] : name;
            list.Add(new BackupInfo(dir, name, Directory.GetCreationTime(dir), kind, Directory.Exists(Path.Combine(dir, "Artwork")), outcome,
                File.Exists(logPath) ? logPath : null));
        }
        return list.OrderByDescending(b => b.Created).ToList();
    }, ct);
}

/// <summary>Per-user app settings, stored off-device.</summary>
public sealed class AppSettings
{
    public string BackupRoot { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ipodsync", "ipod-backups");
    public string? LastSyncFolder { get; set; }

    private static string FilePath => Environment.GetEnvironmentVariable("IPODSYNC_APP_SETTINGS") is { Length: > 0 } custom ? custom
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ipodsync", "app-settings.json");

    public static AppSettings Load()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
