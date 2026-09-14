using IpodSync.Core.ItunesDb;
using IpodSync.Core.LocalLibrary;
using IpodSync.Core.Sync;

namespace IpodSync.Shared.Backend;

/// <summary>Enough about a detected iPod to show in a list and pick one, without
/// pulling in Device.IpodDevice directly -- that type models a mounted drive
/// letter, which only makes sense for <see cref="LocalIpodSyncBackend"/>.</summary>
public sealed record DeviceSummary(string RootPath, string? VolumeLabel, string? FileSystem, bool IsFat32, bool HasDatabase,
    long TotalBytes = 0, long FreeBytes = 0);

/// <summary>What a backend can do. Writes need the full verified pipeline (backup,
/// staged SQLite, signing, read-back verification), which only runs where the iPod
/// is a real filesystem path.</summary>
public sealed record BackendCapabilities(bool CanWrite, bool CanTranscode, bool CanScanFolders, string? ReadOnlyReason = null);

/// <summary>Read-only health of a device: are both databases consistent and signed?</summary>
public sealed record DeviceHealth(
    int Tracks, int Playlists, long FreeBytes, long TotalBytes,
    bool? SignaturesValid, string SignatureDetail,
    bool? DatabasesInSync, string SyncDetail,
    bool? ArtworkOk, string ArtworkDetail);

public sealed record BackupInfo(string Path, string Name, DateTime Created, string Kind, bool HasArtwork, string? Outcome, string? LogPath);

public sealed record FolderSyncPreview(
    string DeviceRoot, string Folder, IReadOnlyList<FolderSync.SourceFile> Files, FolderSync.Plan Plan,
    FolderSync.Manifest Manifest, long EstimatedBytes, long FreeBytes, string ManifestPath);

public sealed record ImportPreview(string DeviceRoot, string File, string Name, bool Replace, int Entries, PlaylistImportJob.Prepared Prepared);

/// <summary>
/// Everything the UI needs that requires touching a filesystem: the iPod, or a
/// local folder of audio files. Implementations are chosen per host at DI
/// registration time (see MauiProgram.cs and Web's Program.cs):
/// <see cref="LocalIpodSyncBackend"/> calls IpodSync.Core directly against a
/// mounted drive letter (the web host and the Windows build, both on the PC the
/// iPod is plugged into); Android's SafIpodSyncBackend reads through the Storage
/// Access Framework and is read-only.
/// </summary>
public interface IIpodSyncBackend
{
    BackendCapabilities Capabilities => new(false, false, true, "This platform can read an iPod but not write to it yet.");

    /// <summary>True when loading a library needs the user to act first (e.g. Android's folder
    /// picker), so the app must not load it automatically at start-up.</summary>
    bool LoadNeedsUserAction => false;

    Task<List<DeviceSummary>> DetectDevicesAsync(CancellationToken ct = default);
    Task<ItunesDatabase> LoadLibraryAsync(string deviceRoot, CancellationToken ct = default);
    Task<List<LocalTrack>> ScanLocalAsync(string folder, CancellationToken ct = default);

    Task<DeviceHealth?> CheckHealthAsync(string deviceRoot, CancellationToken ct = default) => Task.FromResult<DeviceHealth?>(null);

    /// <summary>240px cover for an artwork id as a data: URL, or null.</summary>
    Task<string?> GetArtworkDataUrlAsync(string deviceRoot, uint artworkId, CancellationToken ct = default) => Task.FromResult<string?>(null);

    /// <summary>Runs a change-set through the verified write pipeline. commit=false is a dry run.</summary>
    Task<WritePipeline.Result> RunChangesAsync(string deviceRoot, ChangeSet changes, bool commit, string label, Action<string> log, CancellationToken ct = default)
        => throw new NotSupportedException(Capabilities.ReadOnlyReason);

    Task<FolderSyncPreview> PreviewFolderSyncAsync(string deviceRoot, string folder, bool removeMissing, Action<int>? progress = null, CancellationToken ct = default)
        => throw new NotSupportedException(Capabilities.ReadOnlyReason);

    Task<FolderSyncJob.Outcome> CommitFolderSyncAsync(FolderSyncPreview preview, IReadOnlyList<FolderSync.SourceFile> toAdd, int batch, string? playlist,
        bool removeMissing, Action<string> log, CancellationToken ct = default)
        => throw new NotSupportedException(Capabilities.ReadOnlyReason);

    Task<ImportPreview> PreviewImportAsync(string deviceRoot, string playlistFile, string name, bool replace, CancellationToken ct = default)
        => throw new NotSupportedException(Capabilities.ReadOnlyReason);

    string BackupRoot { get => ""; set { } }
    Task<List<BackupInfo>> ListBackupsAsync(CancellationToken ct = default) => Task.FromResult(new List<BackupInfo>());
}

/// <summary>Native file/folder pickers where the host has them (the Windows app).
/// Hosts without pickers register <see cref="NoHostPickers"/>, and the UI falls back
/// to typed paths.</summary>
public interface IHostPickers
{
    bool Available { get; }
    Task<IReadOnlyList<string>> PickFilesAsync(PickKind kind, bool multiple);
    Task<string?> PickFolderAsync();
    Task OpenFolderAsync(string path);
}

public enum PickKind { Audio, Image, Playlist }

public sealed class NoHostPickers : IHostPickers
{
    public bool Available => false;
    public Task<IReadOnlyList<string>> PickFilesAsync(PickKind kind, bool multiple) => Task.FromResult<IReadOnlyList<string>>([]);
    public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);
    public Task OpenFolderAsync(string path) => Task.CompletedTask;
}
