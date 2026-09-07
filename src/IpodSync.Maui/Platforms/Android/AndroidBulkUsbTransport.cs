using Android.Hardware.Usb;
using IpodSync.Core.UsbStorage;

namespace IpodSync.Maui.Platforms.Android;

/// <summary>Wraps an already-opened, already-permitted UsbDeviceConnection's
/// bulk IN/OUT endpoint pair as the platform-agnostic transport the SCSI layer
/// in IpodSync.Core needs. Claims the mass-storage interface on construction and
/// releases it on Dispose.</summary>
public sealed class AndroidBulkUsbTransport : IBulkUsbTransport, IDisposable
{
    private readonly UsbDeviceConnection _connection;
    private readonly UsbInterface _iface;
    private readonly UsbEndpoint _in;
    private readonly UsbEndpoint _out;
    private const int TimeoutMs = 5000;

    public AndroidBulkUsbTransport(UsbDeviceConnection connection, UsbInterface iface, UsbEndpoint inEndpoint, UsbEndpoint outEndpoint)
    {
        _connection = connection;
        _iface = iface;
        _in = inEndpoint;
        _out = outEndpoint;
        if (!connection.ClaimInterface(iface, true))
            throw new InvalidOperationException("Could not claim the USB mass-storage interface (in use by another process?).");
    }

    public int MaxPacketSize => _in.MaxPacketSize;

    public Task<int> BulkOutAsync(byte[] data, int length, CancellationToken ct = default) =>
        Task.Run(() => _connection.BulkTransfer(_out, data, length, TimeoutMs), ct);

    public Task<int> BulkInAsync(byte[] buffer, int length, CancellationToken ct = default) =>
        Task.Run(() => _connection.BulkTransfer(_in, buffer, length, TimeoutMs), ct);

    public Task ClearHaltAsync(bool inEndpoint, CancellationToken ct = default)
    {
        // Android's UsbDeviceConnection has no direct "clear halt" call; issue it as
        // the raw control transfer the USB spec defines: bmRequestType=0x02 (host-to-
        // device, standard, endpoint recipient), bRequest=1 (CLEAR_FEATURE),
        // wValue=0 (ENDPOINT_HALT), wIndex=the endpoint address.
        int endpointAddress = (int)(inEndpoint ? _in.Address : _out.Address);
        return Task.Run(() => _connection.ControlTransfer((UsbAddressing)0x02, 1, 0, endpointAddress, null, 0, TimeoutMs), ct);
    }

    public void Dispose()
    {
        _connection.ReleaseInterface(_iface);
        _connection.Close();
    }
}
