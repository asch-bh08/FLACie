namespace IpodSync.Core.UsbStorage;

/// <summary>A sector-addressable block device backed by SCSI READ(10)/WRITE(10)
/// over Bulk-Only Transport. Chunks large requests since READ(10)'s block count
/// is a 16-bit field and a single USB bulk transfer has practical size limits
/// well below that anyway.</summary>
public sealed class ScsiBlockDevice
{
    private readonly ScsiBulkOnlyTransport _scsi;
    private const int MaxBlocksPerTransfer = 128; // 64KB at 512 bytes/block -- conservative

    public int BlockSize { get; private set; }
    public long BlockCount { get; private set; }
    public string VendorId { get; private set; } = "";
    public string ProductId { get; private set; } = "";

    private ScsiBlockDevice(ScsiBulkOnlyTransport scsi) => _scsi = scsi;

    public static async Task<ScsiBlockDevice> OpenAsync(IBulkUsbTransport transport, CancellationToken ct = default)
    {
        var scsi = new ScsiBulkOnlyTransport(transport);
        var device = new ScsiBlockDevice(scsi);

        var (vendor, product) = await scsi.InquiryAsync(ct);
        device.VendorId = vendor;
        device.ProductId = product;

        await scsi.TestUnitReadyAsync(ct);
        var (blockSize, lastLba) = await scsi.ReadCapacity10Async(ct);
        device.BlockSize = blockSize;
        device.BlockCount = lastLba + 1L;
        return device;
    }

    public async Task<byte[]> ReadBlocksAsync(uint startBlock, int count, CancellationToken ct = default)
    {
        var result = new byte[count * BlockSize];
        int done = 0;
        while (done < count)
        {
            int batch = Math.Min(MaxBlocksPerTransfer, count - done);
            byte[] chunk = await _scsi.Read10Async(startBlock + (uint)done, (ushort)batch, BlockSize, ct);
            Array.Copy(chunk, 0, result, done * BlockSize, chunk.Length);
            done += batch;
        }
        return result;
    }
}
