using IpodSync.Core.Device;
using IpodSync.Core.ItunesDb;
using IpodSync.Core.LocalLibrary;

namespace IpodSync.Shared.Backend;

/// <summary>Talks to IpodSync.Core directly against a mounted drive letter.
/// Correct only when running on the same machine the iPod is physically plugged
/// into (the web host, and the Windows build of the MAUI app) -- never register
/// this for Android, which has no drive letter for an attached iPod.</summary>
public sealed class LocalIpodSyncBackend : IIpodSyncBackend
{
    public Task<List<DeviceSummary>> DetectDevicesAsync(CancellationToken ct = default) =>
        Task.Run(() => IpodDevice.Detect()
            .Select(d => new DeviceSummary(d.RootPath, d.VolumeLabel, d.FileSystem, d.IsFat32, d.HasDatabase))
            .ToList(), ct);

    public Task<ItunesDatabase> LoadLibraryAsync(string deviceRoot, CancellationToken ct = default) =>
        Task.Run(() => ItunesDbReader.Read(IpodDevice.Open(deviceRoot).ItunesDbPath), ct);

    public Task<List<LocalTrack>> ScanLocalAsync(string folder, CancellationToken ct = default) =>
        Task.Run(() => LocalLibraryScanner.Scan(folder), ct);
}
