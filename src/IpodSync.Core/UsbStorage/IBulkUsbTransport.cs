namespace IpodSync.Core.UsbStorage;

/// <summary>
/// Abstracts a USB device's pair of bulk endpoints (one IN, one OUT) so the SCSI
/// Bulk-Only Transport layer below has no platform dependency. The Android
/// implementation wraps UsbDeviceConnection.BulkTransfer; nothing in this
/// interface assumes Android.
/// </summary>
public interface IBulkUsbTransport : IDisposable
{
    /// <summary>The bulk endpoints' max packet size. BOT framing (CBW/CSW) is
    /// always sent as a single transfer regardless, but callers reading large
    /// data phases may want this to size buffers.</summary>
    int MaxPacketSize { get; }

    Task<int> BulkOutAsync(byte[] data, int length, CancellationToken ct = default);

    /// <summary>Reads up to <paramref name="length"/> bytes into <paramref name="buffer"/>
    /// and returns how many were actually read.</summary>
    Task<int> BulkInAsync(byte[] buffer, int length, CancellationToken ct = default);

    /// <summary>Clears a stalled endpoint (either direction) after a failed
    /// transfer, per the Mass Storage BOT spec's error-recovery procedure.</summary>
    Task ClearHaltAsync(bool inEndpoint, CancellationToken ct = default);
}
