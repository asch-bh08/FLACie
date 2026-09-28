using Android.App;
using Android.Content;
using Android.Hardware.Usb;
using IpodSync.Core.UsbStorage;
using AndroidApplication = Android.App.Application;

namespace IpodSync.Maui.Platforms.Android;

/// <summary>
/// Finds an iPod attached via USB-OTG, asks Android for permission to talk to
/// it (a per-device runtime grant, separate from anything in the manifest), and
/// opens its mass-storage bulk endpoints. Apple's USB vendor id (0x05AC) is used
/// to identify candidate devices rather than a product id allowlist, since the
/// exact ids for the user's specific iPod models are not confirmed -- narrower
/// than "any mass-storage device" but not dependent on a guessed list either.
/// </summary>
public sealed class AndroidUsbIpodConnector
{
    private const int AppleVendorId = 0x05AC;
    private const string UsbPermissionAction = "dev.ashley.ipodsync.USB_PERMISSION";
    private static readonly TimeSpan PermissionTimeout = TimeSpan.FromSeconds(60);

    private UsbManager Manager => (UsbManager)AndroidApplication.Context.GetSystemService(Context.UsbService)!;

    public IReadOnlyList<UsbDevice> FindAppleDevices() =>
        Manager.DeviceList?.Values.Where(d => d.VendorId == AppleVendorId).ToList() ?? [];

    /// <summary>
    /// Requests permission and waits for the user's response. Once the broadcast
    /// fires (regardless of what its own "granted" extra says) the real answer is
    /// read back from <see cref="UsbManager.HasPermission(UsbDevice)"/> rather than trusted
    /// from the broadcast's extras directly -- some Android versions fail to
    /// attach those extras correctly when the requesting PendingIntent is
    /// immutable, which reads as "denied" even after the user taps Allow. Using
    /// a mutable PendingIntent (below) is the primary fix for that; re-querying
    /// HasPermission as well means a broken extra can't cause a false negative
    /// even if some other version of the same bug turns up on a device this
    /// hasn't been tested against.
    /// </summary>
    public async Task<bool> RequestPermissionAsync(UsbDevice device, CancellationToken ct = default)
    {
        if (Manager.HasPermission(device)) return true;

        var tcs = new TaskCompletionSource<bool>();
        var receiver = new UsbPermissionReceiver(tcs, () => Manager.HasPermission(device));
        // The flags overload's Exported value only exists from Android 13; older versions (the RG Rotate runs 12)
        // take the plain overload, which registers exported by default.
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            AndroidApplication.Context.RegisterReceiver(receiver, new IntentFilter(UsbPermissionAction), ReceiverFlags.Exported);
        else
            AndroidApplication.Context.RegisterReceiver(receiver, new IntentFilter(UsbPermissionAction));

        try
        {
            var intent = new Intent(UsbPermissionAction).SetPackage(AndroidApplication.Context.PackageName);
            // Mutable, not Immutable: the system needs to attach its own extras
            // (EXTRA_PERMISSION_GRANTED, EXTRA_DEVICE) to this intent when it
            // fires the broadcast. An immutable PendingIntent can silently drop
            // those on some Android versions, which is what an immutable one
            // here was doing.
            var pendingIntent = PendingIntent.GetBroadcast(AndroidApplication.Context, 0, intent, PendingIntentFlags.Mutable);
            Manager.RequestPermission(device, pendingIntent);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(PermissionTimeout);
            using var reg = timeoutCts.Token.Register(() => tcs.TrySetCanceled());

            try { return await tcs.Task; }
            catch (TaskCanceledException)
            {
                // Timed out or the caller cancelled -- neither is "denied," so
                // check once more directly rather than assuming false.
                return Manager.HasPermission(device);
            }
        }
        finally
        {
            try { AndroidApplication.Context.UnregisterReceiver(receiver); }
            catch (Java.Lang.IllegalArgumentException) { /* already unregistered -- fine */ }
        }
    }

    /// <summary>Opens the device's mass-storage interface and returns a ready-to-use
    /// transport. Caller must have already been granted permission (see
    /// <see cref="RequestPermissionAsync"/>) and must Dispose the result when done.</summary>
    public IBulkUsbTransport Open(UsbDevice device)
    {
        UsbInterface? msc = null;
        UsbEndpoint? inEp = null;
        UsbEndpoint? outEp = null;

        for (int i = 0; i < device.InterfaceCount && msc is null; i++)
        {
            var iface = device.GetInterface(i);
            if (iface.InterfaceClass != UsbClass.MassStorage) continue;

            UsbEndpoint? candidateIn = null, candidateOut = null;
            for (int e = 0; e < iface.EndpointCount; e++)
            {
                var ep = iface.GetEndpoint(e);
                if (ep is null || ep.Type != UsbAddressing.XferBulk) continue;
                if (ep.Direction == UsbAddressing.In) candidateIn = ep; else candidateOut = ep;
            }
            if (candidateIn is not null && candidateOut is not null)
            {
                msc = iface;
                inEp = candidateIn;
                outEp = candidateOut;
            }
        }

        if (msc is null || inEp is null || outEp is null)
            throw new InvalidOperationException("No USB mass-storage bulk interface found on this device.");

        var connection = Manager.OpenDevice(device)
            ?? throw new InvalidOperationException("Could not open the USB device (no permission, or already in use).");
        return new AndroidBulkUsbTransport(connection, msc, inEp, outEp);
    }

    private sealed class UsbPermissionReceiver(TaskCompletionSource<bool> tcs, Func<bool> checkPermission) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent is null || intent.Action != UsbPermissionAction) return;
            tcs.TrySetResult(checkPermission());
        }
    }
}
