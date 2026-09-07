using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace IpodSync.Core.ItunesDb;

/// <summary>
/// Little-endian binary primitives shared by the reader and the raw chunk tree.
/// Kept in one place so the round-trip writer parses the same way the verified
/// reader does -- any drift between the two would defeat the point of the
/// round-trip test.
/// </summary>
internal static class BinaryIo
{
    public static string Magic(byte[] d, int p) =>
        p + 4 <= d.Length ? Encoding.ASCII.GetString(d, p, 4) : "";

    public static int I32(byte[] d, int p) =>
        p + 4 <= d.Length ? BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(p, 4)) : 0;

    public static ulong U64(byte[] d, int p) =>
        p + 8 <= d.Length ? BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan(p, 8)) : 0;

    public static void WriteI32(byte[] d, int p, int value)
    {
        if (p + 4 <= d.Length) BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(p, 4), value);
    }

    public static byte[] Slice(byte[] d, int start, int len)
    {
        if (len <= 0 || start < 0 || start >= d.Length) return [];
        len = Math.Min(len, d.Length - start);
        return d.AsSpan(start, len).ToArray();
    }

    public static byte[] Concat(params byte[][] parts)
    {
        int total = parts.Sum(p => p.Length);
        var result = new byte[total];
        int pos = 0;
        foreach (var p in parts) { Array.Copy(p, 0, result, pos, p.Length); pos += p.Length; }
        return result;
    }

    /// <summary>
    /// Later iPods ship the database as iTunesCDB: the same mhbd header, plain,
    /// followed by a zlib stream holding the datasets. Expands that into the
    /// uncompressed layout the rest of the code expects, and returns the input
    /// untouched when it is already a plain iTunesDB.
    /// </summary>
    public static byte[] Inflate(byte[] d)
    {
        int hdrLen = I32(d, 0x04);
        if (hdrLen < 0x20 || hdrLen + 2 > d.Length) return d;

        // zlib header: low nibble 8 means deflate, and the two bytes together are
        // a multiple of 31. Cheap and specific enough to use as the marker.
        int cmf = d[hdrLen], flg = d[hdrLen + 1];
        if ((cmf & 0x0F) != 8 || ((cmf << 8) | flg) % 31 != 0) return d;

        byte[] body;
        try
        {
            using var input = new MemoryStream(d, hdrLen, d.Length - hdrLen, writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var expanded = new MemoryStream();
            zlib.CopyTo(expanded);
            body = expanded.ToArray();
        }
        catch (InvalidDataException)
        {
            return d;   // looked like zlib but was not; let the caller parse it raw
        }

        var result = new byte[hdrLen + body.Length];
        Array.Copy(d, 0, result, 0, hdrLen);
        Array.Copy(body, 0, result, hdrLen, body.Length);

        // The stored total length describes the compressed file, so correct it.
        WriteI32(result, 0x08, result.Length);
        return result;
    }

    /// <summary>Re-wraps an inflated mhbd+datasets buffer as an iTunesCDB: the
    /// header stored plain (with its total patched back to the compressed file's
    /// length), followed by a zlib stream of everything after it. Note this will
    /// not reproduce the exact compressed bytes of a file written by Apple's zlib
    /// -- different encoders legitimately produce different bytes for the same
    /// input -- only that inflating it again returns the same content.</summary>
    public static byte[] Deflate(byte[] inflated)
    {
        int hdrLen = I32(inflated, 0x04);
        byte[] header = Slice(inflated, 0, hdrLen);
        byte[] body = Slice(inflated, hdrLen, inflated.Length - hdrLen);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(body);
        byte[] compressedBody = compressed.ToArray();

        byte[] result = Concat(header, compressedBody);
        WriteI32(result, 0x08, result.Length);
        return result;
    }
}
