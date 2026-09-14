using Android.Content;
using IpodSync.Core.ItunesDb;
using IpodSync.Core.LocalLibrary;
using IpodSync.Shared.Backend;
using AndroidApplication = Android.App.Application;

namespace IpodSync.Maui.Platforms.Android;

/// <summary>
/// IIpodSyncBackend for the Android build, take two: reads an iPod through
/// Android's own Storage Access Framework instead of talking raw USB/SCSI.
///
/// The first attempt (<see cref="UsbIpodSyncBackend"/>, still in the tree)
/// claimed the USB interface directly and implemented Mass Storage Bulk-Only
/// Transport plus a FAT32 reader from scratch. Real-hardware testing showed
/// why that fights the platform: Android (at least Samsung's One UI, on the
/// device this was tested against) auto-mounts a recognised USB Mass Storage
/// device as browsable storage the moment it's attached. Force-claiming the
/// interface out from under that native mount doesn't coexist with it -- it
/// yanks the device out from under the OS's own driver mid-operation, which
/// surfaced as a "USB storage device was removed unsafely" system notification
/// (despite nothing being physically unplugged) and a failed bulk transfer.
///
/// Since the OS already recognises and mounts the device, there's a simpler
/// and more robust option: ask the user to pick it via the standard document-
/// tree picker (<see cref="SafBridge"/>) and read the target file through
/// Android's own DocumentsContract API (<see cref="SafDocumentReader"/>) --
/// working with the platform's mount instead of contending with it.
/// </summary>
public sealed class SafIpodSyncBackend : IIpodSyncBackend
{
    public const string DeviceToken = "saf-ipod";

    public Task<List<DeviceSummary>> DetectDevicesAsync(CancellationToken ct = default) =>
        Task.FromResult(new List<DeviceSummary>
        {
            new(DeviceToken, "Tap \"Load library\" to choose the iPod's folder (read-only)", "FAT32 (via Android)", IsFat32: true, HasDatabase: true, NeedsUserAction: true),
        });

    public async Task<ItunesDatabase> LoadLibraryAsync(string deviceRoot, CancellationToken ct = default)
    {
        var treeUri = await SafBridge.PickTreeAsync()
            ?? throw new InvalidOperationException("No folder was selected.");

        var resolver = AndroidApplication.Context.ContentResolver!;
        try { resolver.TakePersistableUriPermission(treeUri, ActivityFlags.GrantReadUriPermission); }
        catch (Exception) { /* not fatal -- just means access won't survive an app restart */ }

        byte[]? bytes = await SafDocumentReader.ReadFileAsync(resolver, treeUri, "iPod_Control/iTunes/iTunesCDB", ct)
            ?? await SafDocumentReader.ReadFileAsync(resolver, treeUri, "iPod_Control/iTunes/iTunesDB", ct);
        if (bytes is null)
            throw new InvalidOperationException("No iTunesDB/iTunesCDB found under the selected folder -- pick the iPod's root, not a subfolder.");

        return ItunesDbReader.Read(bytes);
    }

    // Phone-local storage -- a normal filesystem path, not a SAF document tree.
    public Task<List<LocalTrack>> ScanLocalAsync(string folder, CancellationToken ct = default) =>
        Task.Run(() => LocalLibraryScanner.Scan(folder), ct);
}
