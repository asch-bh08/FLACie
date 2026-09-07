using System.Buffers.Binary;
using System.Text;

namespace IpodSync.Core.UsbStorage;

/// <summary>
/// A minimal, read-only FAT32 reader over a <see cref="ScsiBlockDevice"/> --
/// enough to find and read one file by path (iPod_Control/iTunes/iTunesDB or
/// iTunesCDB), which is all an iPod-over-USB-OTG read needs. Handles both a
/// partitioned device (MBR with a FAT32 partition entry) and a "superfloppy"
/// device with no partition table, since both are seen in the wild.
///
/// Built from the public Microsoft FAT32 specification, not from any existing
/// implementation. Unverified against a real device -- see the class comment on
/// ScsiBulkOnlyTransport.
/// </summary>
public sealed class Fat32Volume
{
    private readonly ScsiBlockDevice _device;
    private int _bytesPerSector;
    private int _sectorsPerCluster;
    private uint _fatStartLba;
    private uint _dataStartLba;
    private uint _rootCluster;

    // Caches the most recently read FAT sector -- consecutive clusters in a chain
    // very often fall in the same 512-byte FAT sector (128 entries), so this
    // turns what would be one block read per cluster into one per 128.
    private uint _cachedFatSector = uint.MaxValue;
    private byte[]? _cachedFatSectorData;

    private Fat32Volume(ScsiBlockDevice device) => _device = device;

    private sealed record DirEntry(string Name, bool IsDirectory, uint FirstCluster, uint Size);

    public static async Task<Fat32Volume> OpenAsync(ScsiBlockDevice device, CancellationToken ct = default)
    {
        var vol = new Fat32Volume(device);

        byte[] sector0 = await device.ReadBlocksAsync(0, 1, ct);
        uint partitionStartLba = 0;

        if (sector0[510] == 0x55 && sector0[511] == 0xAA)
        {
            for (int i = 0; i < 4; i++)
            {
                int off = 0x1BE + i * 16;
                byte type = sector0[off + 4];
                if (type is 0x0B or 0x0C) // FAT32 (CHS or LBA)
                {
                    partitionStartLba = BinaryPrimitives.ReadUInt32LittleEndian(sector0.AsSpan(off + 8, 4));
                    break;
                }
            }
        }

        byte[] boot = partitionStartLba == 0
            ? sector0
            : await device.ReadBlocksAsync(partitionStartLba, 1, ct);

        vol._bytesPerSector = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(0x0B, 2));
        vol._sectorsPerCluster = boot[0x0D];
        int reservedSectors = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(0x0E, 2));
        int numFats = boot[0x10];
        uint sectorsPerFat32 = BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(0x24, 4));
        vol._rootCluster = BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(0x2C, 4));

        if (vol._bytesPerSector == 0 || vol._sectorsPerCluster == 0 || sectorsPerFat32 == 0)
            throw new InvalidDataException("Not a FAT32 volume (boot sector fields are zero).");

        vol._fatStartLba = partitionStartLba + (uint)reservedSectors;
        vol._dataStartLba = vol._fatStartLba + (uint)numFats * sectorsPerFat32;
        return vol;
    }

    /// <summary>Reads a file by its device-relative path, e.g.
    /// "iPod_Control/iTunes/iTunesDB". Case-insensitive, '/' or ':' separated
    /// (the iPod's own path convention uses ':'). Returns null if any path
    /// segment is not found.</summary>
    public async Task<byte[]?> ReadFileAsync(string path, CancellationToken ct = default)
    {
        string[] segments = path.Split(['/', ':'], StringSplitOptions.RemoveEmptyEntries);
        uint cluster = _rootCluster;
        DirEntry? entry = null;

        for (int i = 0; i < segments.Length; i++)
        {
            var dir = await ReadDirectoryAsync(cluster, ct);
            entry = dir.FirstOrDefault(e => string.Equals(e.Name, segments[i], StringComparison.OrdinalIgnoreCase));
            if (entry is null) return null;
            bool isLast = i == segments.Length - 1;
            if (!isLast && !entry.IsDirectory) return null; // a path component that isn't a directory
            cluster = entry.FirstCluster;
        }

        if (entry is null || entry.IsDirectory) return null;
        return await ReadClusterChainAsync(entry.FirstCluster, entry.Size, ct);
    }

    private async Task<List<DirEntry>> ReadDirectoryAsync(uint cluster, CancellationToken ct)
    {
        var result = new List<DirEntry>();
        var lfnParts = new SortedDictionary<int, string>();
        byte[] raw = await ReadClusterChainRawAsync(cluster, ct);

        for (int off = 0; off + 32 <= raw.Length; off += 32)
        {
            byte first = raw[off];
            if (first == 0x00) break;       // no more entries
            if (first == 0xE5) continue;    // deleted

            byte attr = raw[off + 11];
            if (attr == 0x0F) // LFN fragment
            {
                int seq = first & 0x1F;
                lfnParts[seq] = ExtractLfnChars(raw, off);
                continue;
            }

            string name;
            if (lfnParts.Count > 0)
            {
                name = string.Concat(lfnParts.OrderBy(kv => kv.Key).Select(kv => kv.Value));
                int nul = name.IndexOf('\0');
                if (nul >= 0) name = name[..nul];
                lfnParts.Clear();
            }
            else
            {
                name = ParseShortName(raw, off);
            }

            if (name is "." or "..") continue;

            bool isDir = (attr & 0x10) != 0;
            uint firstClusterHi = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(off + 20, 2));
            uint firstClusterLo = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(off + 26, 2));
            uint firstCluster = (firstClusterHi << 16) | firstClusterLo;
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(off + 28, 4));
            result.Add(new DirEntry(name, isDir, firstCluster, size));
        }
        return result;
    }

    private static string ExtractLfnChars(byte[] entry, int off)
    {
        Span<int> charOffsets = [1, 3, 5, 7, 9, 14, 16, 18, 20, 22, 24, 28, 30];
        var sb = new StringBuilder(13);
        foreach (int o in charOffsets)
            sb.Append((char)BinaryPrimitives.ReadUInt16LittleEndian(entry.AsSpan(off + o, 2)));
        return sb.ToString();
    }

    private static string ParseShortName(byte[] entry, int off)
    {
        string name = Encoding.ASCII.GetString(entry, off, 8).TrimEnd();
        string ext = Encoding.ASCII.GetString(entry, off + 8, 3).TrimEnd();
        return ext.Length > 0 ? $"{name}.{ext}" : name;
    }

    private async Task<uint> NextClusterAsync(uint cluster, CancellationToken ct)
    {
        uint fatOffsetBytes = cluster * 4;
        uint fatSector = _fatStartLba + fatOffsetBytes / (uint)_bytesPerSector;
        int offsetInSector = (int)(fatOffsetBytes % _bytesPerSector);

        if (fatSector != _cachedFatSector)
        {
            _cachedFatSectorData = await _device.ReadBlocksAsync(fatSector, 1, ct);
            _cachedFatSector = fatSector;
        }
        return BinaryPrimitives.ReadUInt32LittleEndian(_cachedFatSectorData!.AsSpan(offsetInSector, 4)) & 0x0FFFFFFF;
    }

    private async Task<byte[]> ReadClusterChainRawAsync(uint startCluster, CancellationToken ct)
    {
        var chunks = new List<byte[]>();
        uint cluster = startCluster;
        while (cluster is >= 2 and < 0x0FFFFFF8)
        {
            uint lba = _dataStartLba + (cluster - 2) * (uint)_sectorsPerCluster;
            chunks.Add(await _device.ReadBlocksAsync(lba, _sectorsPerCluster, ct));
            cluster = await NextClusterAsync(cluster, ct);
        }
        return chunks.Count == 1 ? chunks[0] : chunks.SelectMany(c => c).ToArray();
    }

    private async Task<byte[]> ReadClusterChainAsync(uint startCluster, long fileSize, CancellationToken ct)
    {
        byte[] raw = await ReadClusterChainRawAsync(startCluster, ct);
        if (raw.LongLength == fileSize) return raw;
        var result = new byte[fileSize];
        Array.Copy(raw, result, Math.Min(raw.LongLength, fileSize));
        return result;
    }
}
