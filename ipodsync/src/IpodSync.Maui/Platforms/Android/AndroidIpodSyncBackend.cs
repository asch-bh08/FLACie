using Android.Content;
using Android.OS;
using Android.OS.Storage;
using IpodSync.Core.Artwork;
using IpodSync.Core.Device;
using IpodSync.Core.ItunesDb;
using IpodSync.Core.Signing;
using IpodSync.Shared.Backend;
using AndroidApplication = Android.App.Application;
using AndroidEnvironment = Android.OS.Environment;

namespace IpodSync.Maui.Platforms.Android;

/// <summary>
/// Android backend with writes. The iPod plugged in over USB-OTG is mounted by the OS as
/// a removable volume (<c>/storage/XXXX-XXXX</c>). With Android's "All files access"
/// (MANAGE_EXTERNAL_STORAGE) granted, that volume is an ordinary filesystem path, so the
/// same verified <see cref="Core.Sync.WritePipeline"/> the PC uses runs unchanged —
/// backup, staged SQLite, signing, write, read-back re-verify, auto-restore — instead of a
/// second, unproven writer. This works *with* the OS mount (unlike the raw USB approach,
/// which fought it; see HANDOFF.md).
///
/// Without that permission it falls back to <see cref="SafIpodSyncBackend"/> (read-only,
/// through the document picker) and offers a button to grant it.
///
/// Signing: hash72 comes from the device's own files as on the PC. hash58 needs the
/// FirewireGuid, which on Windows comes from the registry; here it is the iPod's USB
/// serial number (asked for once through Android's USB permission prompt — only the
/// serial is read, the USB interface is never claimed), then remembered once proven.
/// No transcoding (no ffmpeg on Android): only iPod-native files can be added. Cover
/// art works, via <see cref="AndroidRasterizer"/>.
/// </summary>
public sealed class AndroidIpodSyncBackend() : LocalIpodSyncBackend(DefaultBackupRoot())
{
    private readonly SafIpodSyncBackend _saf = new();
    private readonly AndroidUsbIpodConnector _usb = new();

    private static Context Ctx => AndroidApplication.Context;

    public static bool HasAllFilesAccess => AndroidEnvironment.IsExternalStorageManager;

    private static string DefaultBackupRoot()
    {
        var docs = AndroidEnvironment.GetExternalStoragePublicDirectory(AndroidEnvironment.DirectoryDocuments)?.AbsolutePath
                   ?? System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
        return Path.Combine(docs, "ipodsync", "ipod-backups");
    }

    public override BackendCapabilities Capabilities => HasAllFilesAccess
        ? new(CanWrite: true, CanTranscode: false, CanScanFolders: true, ReadOnlyReason: null, CanArtwork: Thumbnailer.Available)
        : new(CanWrite: false, CanTranscode: false, CanScanFolders: false,
              ReadOnlyReason: "to change the iPod from this phone, allow ipodsync “All files access” (Android's permission for writing to USB storage), then come back and tap ⟳.");

    public override string? SetupActionLabel => HasAllFilesAccess ? null : "Allow access";

    public override Task RunSetupActionAsync()
    {
        var intent = new Intent(global::Android.Provider.Settings.ActionManageAppAllFilesAccessPermission,
            global::Android.Net.Uri.Parse("package:" + Ctx.PackageName));
        intent.AddFlags(ActivityFlags.NewTask);
        try { Ctx.StartActivity(intent); }
        catch (ActivityNotFoundException)
        {
            var general = new Intent(global::Android.Provider.Settings.ActionManageAllFilesAccessPermission);
            general.AddFlags(ActivityFlags.NewTask);
            Ctx.StartActivity(general);
        }
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------ devices

    public override async Task<List<DeviceSummary>> DetectDevicesAsync(CancellationToken ct = default)
    {
        var mounted = HasAllFilesAccess ? await base.DetectDevicesAsync(ct) : [];
        if (mounted.Count > 0) return mounted;
        // No writable iPod volume visible: offer the read-only document-picker route.
        var saf = await _saf.DetectDevicesAsync(ct);
        return saf.Select(d => d with
        {
            NeedsUserAction = true,
            VolumeLabel = HasAllFilesAccess
                ? "No iPod found as USB storage. Plug it in and tap ⟳, or load it read-only by choosing its folder."
                : d.VolumeLabel,
        }).ToList();
    }

    protected override List<DeviceSummary> DetectMounted()
    {
        var list = new List<DeviceSummary>();
        var sm = (StorageManager?)Ctx.GetSystemService(Context.StorageService);
        foreach (var v in sm?.StorageVolumes ?? [])
        {
            try
            {
                if (v.IsPrimary || v.State != AndroidEnvironment.MediaMounted) continue;
                string? dir = v.Directory?.AbsolutePath;
                if (dir is null || !Directory.Exists(Path.Combine(dir, "iPod_Control"))) continue;
                var dev = IpodDevice.Open(dir);
                var (free, total) = Space(dir);
                list.Add(new DeviceSummary(dir, v.GetDescription(Ctx), "USB storage", true, dev.HasDatabase, total, free));
            }
            catch (Exception) { /* volume vanished or unreadable: skip */ }
        }
        return list;
    }

    protected override (long Free, long Total) Space(string deviceRoot)
    {
        try { var st = new StatFs(deviceRoot); return (st.AvailableBytes, st.TotalBytes); }
        catch { return (0, 0); }
    }

    public override Task<ItunesDatabase> LoadLibraryAsync(string deviceRoot, CancellationToken ct = default) =>
        deviceRoot == SafIpodSyncBackend.DeviceToken ? _saf.LoadLibraryAsync(deviceRoot, ct) : base.LoadLibraryAsync(deviceRoot, ct);

    public override Task<DeviceHealth?> CheckHealthAsync(string deviceRoot, CancellationToken ct = default) =>
        deviceRoot == SafIpodSyncBackend.DeviceToken ? Task.FromResult<DeviceHealth?>(null) : base.CheckHealthAsync(deviceRoot, ct);

    // ------------------------------------------------------------------ signing

    /// <summary>A remembered FirewireGuid if one reproduces this device's hash58; otherwise
    /// the serial numbers of attached Apple USB devices (asking permission if needed).</summary>
    protected override async Task<List<string>> FirewireCandidatesAsync(string deviceRoot, CancellationToken ct)
    {
        var known = KnownFirewireCandidates(deviceRoot);
        byte[]? cdb = null;
        try { cdb = File.ReadAllBytes(IpodDevice.Open(deviceRoot).ItunesDbPath); } catch { }
        if (cdb is not null && !DeviceSigning.RequiresSigning(cdb)) return known;
        if (cdb is not null)
        {
            foreach (var k in known)
            {
                try { if (Hash58.Verify(Hash58.ParseFirewireGuid(k), cdb)) return [k]; } catch { }
            }
        }

        var list = new List<string>(known);
        foreach (var device in _usb.FindAppleDevices())
        {
            try
            {
                if (!await MainThread.InvokeOnMainThreadAsync(() => _usb.RequestPermissionAsync(device, ct))) continue;
                string? serial = device.SerialNumber;
                if (!string.IsNullOrWhiteSpace(serial)) list.Add(serial.Trim());
            }
            catch (Exception) { /* permission denied or device gone: signing will say it can't prove a key */ }
        }
        return list;
    }

    /// <summary>Quick-access folders for the in-app file browser.</summary>
    public static IReadOnlyList<(string Label, string Path)> PickerRoots()
    {
        var roots = new List<(string, string)>();
        string? phone = AndroidEnvironment.ExternalStorageDirectory?.AbsolutePath;
        if (phone is not null)
        {
            foreach (var (label, sub) in new[] { ("Music", AndroidEnvironment.DirectoryMusic), ("Downloads", AndroidEnvironment.DirectoryDownloads), ("Phone", "") })
            {
                string p = string.IsNullOrEmpty(sub) ? phone : Path.Combine(phone, sub);
                if (Directory.Exists(p)) roots.Add((label, p));
            }
        }
        var sm = (StorageManager?)Ctx.GetSystemService(Context.StorageService);
        foreach (var v in sm?.StorageVolumes ?? [])
        {
            try
            {
                if (v.IsPrimary || v.State != AndroidEnvironment.MediaMounted || v.Directory?.AbsolutePath is not { } d) continue;
                roots.Add((v.GetDescription(Ctx) ?? d, d));
            }
            catch { }
        }
        return roots;
    }
}
