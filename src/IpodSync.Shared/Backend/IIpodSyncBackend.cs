using IpodSync.Core.ItunesDb;
using IpodSync.Core.LocalLibrary;

namespace IpodSync.Shared.Backend;

/// <summary>Enough about a detected iPod to show in a list and pick one, without
/// pulling in Device.IpodDevice directly -- that type models a mounted drive
/// letter, which only makes sense for <see cref="LocalIpodSyncBackend"/>.</summary>
public sealed record DeviceSummary(string RootPath, string? VolumeLabel, string? FileSystem, bool IsFat32, bool HasDatabase);

/// <summary>
/// Everything the UI needs that requires touching a filesystem: the iPod, or a
/// local folder of audio files. Two implementations, chosen per host at DI
/// registration time (see MauiProgram.cs and Web's Program.cs):
/// <see cref="LocalIpodSyncBackend"/> calls IpodSync.Core directly against a
/// mounted drive letter (the web host, and the Windows build -- both run on the
/// PC the iPod is plugged into), and UsbIpodSyncBackend (Platforms/Android, not
/// referenced from here since it needs Android-only APIs) talks to an iPod
/// plugged directly into the phone via USB-OTG, through IpodSync.Core's
/// SCSI/FAT32 stack instead of a mounted filesystem -- Android gives no such
/// thing for an arbitrary attached mass-storage device.
/// </summary>
public interface IIpodSyncBackend
{
    Task<List<DeviceSummary>> DetectDevicesAsync(CancellationToken ct = default);
    Task<ItunesDatabase> LoadLibraryAsync(string deviceRoot, CancellationToken ct = default);
    Task<List<LocalTrack>> ScanLocalAsync(string folder, CancellationToken ct = default);
}
