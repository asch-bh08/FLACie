using static IpodSync.Core.ItunesDb.BinaryIo;

namespace IpodSync.Core.ItunesDb;

public sealed record RoundTripResult(
    bool StructureMatches,
    int OriginalInflatedLength,
    int ReconstructedLength,
    int? FirstDiffOffset,
    string? DiffContext,
    HashSet<string> UnknownMagics,
    bool WasCompressed,
    int OriginalCompressedLength,
    bool? CompressionRoundTripOk,
    byte[] ReconstructedInflated);

/// <summary>
/// The safety gate for writing to a device: parses a real iTunesDB/iTunesCDB into
/// the lossless <see cref="RawChunk"/> tree, serializes it straight back out, and
/// reports whether that reproduced the original bytes exactly. Never writes
/// anything -- this only proves the writer, it does not exercise it on a device.
/// </summary>
public static class RoundTrip
{
    public static RoundTripResult Run(byte[] fileBytes)
    {
        byte[] inflated = Inflate(fileBytes);
        bool wasCompressed = inflated.Length != fileBytes.Length;

        var root = RawChunkParser.ParseRoot(inflated);
        byte[] reconstructed = root.Serialize();

        var unknown = new HashSet<string>();
        root.CollectUnknownMagics(unknown);

        int? firstDiff = FindFirstDiff(inflated, reconstructed);
        bool structMatch = firstDiff is null && inflated.Length == reconstructed.Length;

        // Recompressing will not reproduce Apple's exact zlib bytes -- different
        // encoders legitimately differ -- so this only checks that our compressed
        // stream still inflates back to the same content, not that it matches the
        // original compressed file byte for byte.
        bool? compressionOk = null;
        if (wasCompressed)
        {
            try
            {
                byte[] recompressed = Deflate(reconstructed);
                byte[] reinflated = Inflate(recompressed);
                compressionOk = reinflated.AsSpan().SequenceEqual(reconstructed);
            }
            catch { compressionOk = false; }
        }

        return new RoundTripResult(
            structMatch,
            inflated.Length,
            reconstructed.Length,
            firstDiff,
            firstDiff is int off ? HexContext(inflated, reconstructed, off) : null,
            unknown,
            wasCompressed,
            fileBytes.Length,
            compressionOk,
            reconstructed);
    }

    private static int? FindFirstDiff(byte[] a, byte[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
            if (a[i] != b[i]) return i;
        return a.Length == b.Length ? null : n;
    }

    private static string HexContext(byte[] original, byte[] reconstructed, int offset)
    {
        const int span = 16;
        int start = Math.Max(0, offset - span);
        string Hex(byte[] d, int from) =>
            string.Join(' ', Enumerable.Range(from, Math.Min(span * 2, d.Length - from))
                .Select(i => d[i].ToString("X2")));
        return $"  original      @0x{start:X}: {Hex(original, start)}\n" +
               $"  reconstructed  @0x{start:X}: {Hex(reconstructed, start)}";
    }
}
