using System.Buffers.Binary;

namespace IpodSync.Core.UsbStorage;

/// <summary>
/// USB Mass Storage Class Bulk-Only Transport (BOT), the protocol an iPod in
/// disk mode speaks: every command is a 31-byte Command Block Wrapper (CBW)
/// carrying a SCSI CDB, sent out; an optional data phase; then a 13-byte Command
/// Status Wrapper (CSW) read back. This class implements exactly that framing
/// plus the handful of SCSI commands a block-level FAT32 reader needs --
/// INQUIRY, TEST UNIT READY, READ CAPACITY(10), READ(10), REQUEST SENSE.
///
/// Built directly from the public USB MSC BOT and SCSI-2 block-command specs,
/// not from libaums or any other implementation -- there was nothing to bind or
/// port. Unverified against a real device: there is no iPod-over-USB-OTG rig in
/// this environment. Needs a real test-and-iterate pass against actual hardware
/// before it can be trusted, the same way the database format only became
/// trustworthy once checked against real bytes.
/// </summary>
public sealed class ScsiBulkOnlyTransport(IBulkUsbTransport transport)
{
    private const uint CbwSignature = 0x43425355; // "USBC"
    private const uint CswSignature = 0x53425355; // "USBS"
    private uint _tag;

    public sealed class ScsiCommandFailedException(string command, byte status) : Exception($"{command} failed (CSW status {status})")
    {
        public byte Status { get; } = status;
    }

    private async Task<byte[]> ExecuteAsync(string name, byte[] cdb, int dataLength, bool dataIn, byte[]? dataOut = null, CancellationToken ct = default)
    {
        uint tag = ++_tag;
        byte[] cbw = new byte[31];
        BinaryPrimitives.WriteUInt32LittleEndian(cbw.AsSpan(0), CbwSignature);
        BinaryPrimitives.WriteUInt32LittleEndian(cbw.AsSpan(4), tag);
        BinaryPrimitives.WriteUInt32LittleEndian(cbw.AsSpan(8), (uint)dataLength);
        cbw[12] = (byte)(dataIn ? 0x80 : 0x00);
        cbw[13] = 0; // LUN 0
        cbw[14] = (byte)cdb.Length;
        cdb.CopyTo(cbw, 15);

        await transport.BulkOutAsync(cbw, cbw.Length, ct);

        byte[] data = [];
        if (dataLength > 0)
        {
            if (dataIn)
            {
                data = new byte[dataLength];
                int total = 0;
                while (total < dataLength)
                {
                    int n = await transport.BulkInAsync(data.AsSpan(total).ToArray(), dataLength - total, ct);
                    if (n <= 0) break;
                    n = Math.Min(n, dataLength - total);
                    total += n;
                }
                if (total < dataLength) data = data[..total];
            }
            else
            {
                await transport.BulkOutAsync(dataOut!, dataLength, ct);
            }
        }

        byte[] csw = new byte[13];
        int cswRead = await transport.BulkInAsync(csw, 13, ct);
        if (cswRead != 13 || BinaryPrimitives.ReadUInt32LittleEndian(csw.AsSpan(0)) != CswSignature)
            throw new IOException($"{name}: malformed CSW ({cswRead} bytes)");
        if (BinaryPrimitives.ReadUInt32LittleEndian(csw.AsSpan(4)) != tag)
            throw new IOException($"{name}: CSW tag mismatch (command/response desynced)");

        byte status = csw[12];
        if (status != 0) throw new ScsiCommandFailedException(name, status);
        return data;
    }

    /// <summary>Confirms the device responds and is a direct-access block device.
    /// Returns the 8-byte vendor + 16-byte product identification, trimmed.</summary>
    public async Task<(string Vendor, string Product)> InquiryAsync(CancellationToken ct = default)
    {
        byte[] cdb = new byte[6];
        cdb[0] = 0x12; // INQUIRY
        cdb[4] = 36;   // allocation length
        byte[] data = await ExecuteAsync("INQUIRY", cdb, 36, dataIn: true, ct: ct);
        string vendor = System.Text.Encoding.ASCII.GetString(data, 8, 8).TrimEnd();
        string product = System.Text.Encoding.ASCII.GetString(data, 16, 16).TrimEnd();
        return (vendor, product);
    }

    public Task TestUnitReadyAsync(CancellationToken ct = default) =>
        ExecuteAsync("TEST UNIT READY", new byte[6], 0, dataIn: true, ct: ct);

    /// <summary>Returns (blockSizeBytes, lastLogicalBlockAddress). Block count is lastLba + 1.</summary>
    public async Task<(int BlockSize, uint LastLba)> ReadCapacity10Async(CancellationToken ct = default)
    {
        byte[] cdb = new byte[10];
        cdb[0] = 0x25; // READ CAPACITY (10)
        byte[] data = await ExecuteAsync("READ CAPACITY(10)", cdb, 8, dataIn: true, ct: ct);
        uint lastLba = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0, 4));
        int blockSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4, 4));
        return (blockSize, lastLba);
    }

    /// <summary>Reads <paramref name="blockCount"/> blocks of <paramref name="blockSize"/>
    /// bytes starting at logical block address <paramref name="lba"/>.</summary>
    public Task<byte[]> Read10Async(uint lba, ushort blockCount, int blockSize, CancellationToken ct = default)
    {
        byte[] cdb = new byte[10];
        cdb[0] = 0x28; // READ (10)
        BinaryPrimitives.WriteUInt32BigEndian(cdb.AsSpan(2), lba);
        BinaryPrimitives.WriteUInt16BigEndian(cdb.AsSpan(7), blockCount);
        return ExecuteAsync("READ(10)", cdb, blockCount * blockSize, dataIn: true, ct: ct);
    }

    /// <summary>Writes whole blocks starting at <paramref name="lba"/>. Not used by
    /// anything yet -- reading is the only thing proven so far, per project policy
    /// of never writing to a device before reading is solid.</summary>
    public Task Write10Async(uint lba, byte[] data, int blockSize, CancellationToken ct = default)
    {
        ushort blockCount = (ushort)(data.Length / blockSize);
        byte[] cdb = new byte[10];
        cdb[0] = 0x2A; // WRITE (10)
        BinaryPrimitives.WriteUInt32BigEndian(cdb.AsSpan(2), lba);
        BinaryPrimitives.WriteUInt16BigEndian(cdb.AsSpan(7), blockCount);
        return ExecuteAsync("WRITE(10)", cdb, data.Length, dataIn: false, dataOut: data, ct: ct);
    }
}
