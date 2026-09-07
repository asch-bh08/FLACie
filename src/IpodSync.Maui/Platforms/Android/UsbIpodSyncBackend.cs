using IpodSync.Core.ItunesDb;
using IpodSync.Core.LocalLibrary;
using IpodSync.Core.UsbStorage;
using IpodSync.Shared.Backend;

namespace IpodSync.Maui.Platforms.Android;

/// <summary>
/// IIpodSyncBackend for the Android build: an iPod plugged directly into the
/// phone via USB-OTG, read through the SCSI/FAT32 stack in
/// IpodSync.Core.UsbStorage rather than through a mounted filesystem -- Android
/// gives no such thing for an arbitrary attached mass-storage device. This is
/// the piece that cannot be verified from a dev machine; see the class comments
/// on ScsiBulkOnlyTransport and Fat32Volume.
/// </summary>
public sealed class UsbIpodSyncBackend : IIpodSyncBackend
{
    // There is only ever one thing this backend can mean by "device": whatever
    // Apple-vendor USB device is currently attached. RootPath is a fixed token
    // rather than a real path, just to satisfy the shared interface's shape.
    private const string DeviceToken = "usb-ipod";

    private readonly AndroidUsbIpodConnector _connector = new();

    public Task<List<DeviceSummary>> DetectDevicesAsync(CancellationToken ct = default)
    {
        var summaries = _connector.FindAppleDevices()
            .Select(d => new DeviceSummary(DeviceToken, d.ProductName ?? "iPod (USB)", "FAT32 (assumed)", IsFat32: true, HasDatabase: true))
            .ToList();
        return Task.FromResult(summaries);
    }

    public async Task<ItunesDatabase> LoadLibraryAsync(string deviceRoot, CancellationToken ct = default)
    {
        var device = _connector.FindAppleDevices().FirstOrDefault()
            ?? throw new InvalidOperationException("No iPod attached.");

        if (!await _connector.RequestPermissionAsync(device, ct))
            throw new InvalidOperationException("USB permission was not granted for the attached device.");

        using IBulkUsbTransport transport = _connector.Open(device);
        var block = await ScsiBlockDevice.OpenAsync(transport, ct);
        var volume = await Fat32Volume.OpenAsync(block, ct);

        byte[]? bytes = await volume.ReadFileAsync("iPod_Control/iTunes/iTunesCDB", ct)
            ?? await volume.ReadFileAsync("iPod_Control/iTunes/iTunesDB", ct);
        if (bytes is null)
            throw new InvalidOperationException("No iTunesDB/iTunesCDB found on the attached device.");

        return ItunesDbReader.Read(bytes);
    }

    // Phone-local storage -- unlike the iPod itself, this is a normal filesystem
    // path the Core scanner can read directly (subject to Android scoped-storage
    // permissions on the folder the user picks).
    public Task<List<LocalTrack>> ScanLocalAsync(string folder, CancellationToken ct = default) =>
        Task.Run(() => LocalLibraryScanner.Scan(folder), ct);
}
