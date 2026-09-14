using System.Text;

namespace IpodSync.Core.Artwork;

/// <summary>
/// Lossless chunk tree for <c>iPod_Control/Artwork/ArtworkDB</c>, the same idea as
/// <see cref="ItunesDb.RawChunk"/>: every byte is kept, and serializing recomputes
/// only lengths and child counts. Layout confirmed on the real nano 5G database
/// (529 images, see OVERNIGHT-STATUS.md):
///
///   mhfd  total@0x08, mhsd count@0x14, next image id@0x1C
///   └ mhsd total@0x08  (type@0x0C: 1 images, 2 photo albums, 3 files)
///     └ mhli / mhla / mhlf   count@0x08
///       ├ mhii  total@0x08, mhod count@0x0C, id@0x10, track pid@0x14,
///       │       source size+1@0x30, reference count@0x38
///       │  ├ mhod type 2 (container) └ mhni total@0x08, child count@0x0C, format@0x10,
///       │  │     ithmb offset@0x14, size@0x18, pad top/left@0x1C/0x1E,
///       │  │     pad+height/pad+width@0x20/0x22  └ mhod type 3 ":F1056_1.ithmb"
///       │  └ mhod type 6 (container) └ mhaf  (length = its header; +0x08 is NOT a total)
///       └ mhif  format@0x10, image size@0x14
/// </summary>
public sealed class ArtChunk
{
    public required string Magic { get; init; }
    public byte[] Header { get; set; } = [];
    public List<ArtChunk> Children { get; } = [];
    public byte[] Payload { get; set; } = [];

    public int Length => Header.Length + Children.Sum(c => c.Length) + Payload.Length;

    public byte[] Serialize()
    {
        byte[] header = (byte[])Header.Clone();
        switch (Magic)
        {
            case "mhfd": W(header, 0x14, Children.Count); break;
            case "mhli" or "mhla" or "mhlf": W(header, 0x08, Children.Count); break;
            case "mhii" or "mhni": W(header, 0x0C, Children.Count); break;
        }
        var body = new MemoryStream();
        foreach (var c in Children) body.Write(c.Serialize());
        body.Write(Payload);
        int total = header.Length + (int)body.Length;
        if (Magic is "mhfd" or "mhsd" or "mhii" or "mhni" or "mhod" or "mhif") W(header, 0x08, total);
        return [.. header, .. body.ToArray()];
    }

    internal static void W(byte[] b, int off, int v) => BitConverter.TryWriteBytes(b.AsSpan(off, 4), v);
    internal static int R(byte[] b, int off) => BitConverter.ToInt32(b, off);
}

public static class ArtworkDb
{
    public static ArtChunk Parse(byte[] d)
    {
        if (d.Length < 0x20 || Encoding.ASCII.GetString(d, 0, 4) != "mhfd") throw new InvalidDataException("not an ArtworkDB (no mhfd)");
        int hdr = I(d, 4), total = I(d, 8), count = I(d, 0x14);
        var root = new ArtChunk { Magic = "mhfd", Header = d[..hdr] };
        int pos = hdr;
        for (int i = 0; i < count; i++)
        {
            var s = ParseMhsd(d, pos);
            root.Children.Add(s);
            pos += s.Length;
        }
        root.Payload = d[pos..Math.Min(total, d.Length)];
        return root;
    }

    private static ArtChunk ParseMhsd(byte[] d, int pos)
    {
        Expect(d, pos, "mhsd");
        int hdr = I(d, pos + 4), total = I(d, pos + 8);
        var s = new ArtChunk { Magic = "mhsd", Header = d[pos..(pos + hdr)] };
        int inner = pos + hdr, end = pos + total;
        string listMagic = Magic(d, inner);
        if (listMagic is "mhli" or "mhla" or "mhlf")
        {
            int lh = I(d, inner + 4), n = I(d, inner + 8);
            var list = new ArtChunk { Magic = listMagic, Header = d[inner..(inner + lh)] };
            int q = inner + lh;
            for (int i = 0; i < n; i++)
            {
                var item = ParseItem(d, q, end);
                list.Children.Add(item);
                q += item.Length;
            }
            list.Payload = d[q..end];
            s.Children.Add(list);
        }
        else s.Payload = d[inner..end];
        return s;
    }

    private static ArtChunk ParseItem(byte[] d, int pos, int bound)
    {
        string magic = Magic(d, pos);
        int hdr = I(d, pos + 4);
        switch (magic)
        {
            case "mhii" or "mhni":
            {
                int total = I(d, pos + 8), n = I(d, pos + 0x0C);
                var c = new ArtChunk { Magic = magic, Header = d[pos..(pos + hdr)] };
                int q = pos + hdr;
                for (int i = 0; i < n; i++)
                {
                    var m = ParseItem(d, q, pos + total);
                    c.Children.Add(m);
                    q += m.Length;
                }
                c.Payload = d[q..(pos + total)];
                return c;
            }
            case "mhod":
            {
                int total = I(d, pos + 8);
                var c = new ArtChunk { Magic = magic, Header = d[pos..(pos + hdr)] };
                int q = pos + hdr, end = pos + total;
                // Container mhods (type 2 -> mhni, type 6 -> mhaf) hold exactly one chunk.
                if (end - q >= 8 && Magic(d, q) is "mhni" or "mhaf")
                {
                    var child = ParseItem(d, q, end);
                    c.Children.Add(child);
                    q += child.Length;
                }
                c.Payload = d[q..end];
                return c;
            }
            case "mhaf":
                return new ArtChunk { Magic = magic, Header = d[pos..(pos + hdr)] };
            default:
            {
                int total = I(d, pos + 8);
                if (total < hdr || pos + total > bound) throw new InvalidDataException($"bad {magic} chunk at 0x{pos:X}");
                return new ArtChunk { Magic = magic, Header = d[pos..(pos + hdr)], Payload = d[(pos + hdr)..(pos + total)] };
            }
        }
    }

    // ---------------------------------------------------------------- semantic helpers

    public static ArtChunk ImageList(ArtChunk root) =>
        root.Children.SelectMany(s => s.Children).First(c => c.Magic == "mhli");

    public static ArtChunk FileList(ArtChunk root) =>
        root.Children.SelectMany(s => s.Children).First(c => c.Magic == "mhlf");

    public static IEnumerable<ArtChunk> Images(ArtChunk root) => ImageList(root).Children.Where(c => c.Magic == "mhii");

    public static int ImageId(ArtChunk mhii) => ArtChunk.R(mhii.Header, 0x10);
    public static ulong ImageTrackPid(ArtChunk mhii) => BitConverter.ToUInt64(mhii.Header, 0x14);
    public static int RefCount(ArtChunk mhii) => ArtChunk.R(mhii.Header, 0x38);
    public static int NextImageId(ArtChunk root) => ArtChunk.R(root.Header, 0x1C);

    public sealed record Thumb(int Format, int Offset, int Size, int PadTop, int PadLeft, int Bottom, int Right);

    public static IEnumerable<Thumb> Thumbs(ArtChunk mhii) =>
        mhii.Children.Where(m => m.Magic == "mhod").SelectMany(m => m.Children).Where(c => c.Magic == "mhni").Select(n =>
            new Thumb(ArtChunk.R(n.Header, 0x10), ArtChunk.R(n.Header, 0x14), ArtChunk.R(n.Header, 0x18),
                BitConverter.ToUInt16(n.Header, 0x1C), BitConverter.ToUInt16(n.Header, 0x1E),
                BitConverter.ToUInt16(n.Header, 0x20), BitConverter.ToUInt16(n.Header, 0x22)));

    /// <summary>(format id, image byte size) from the mhlf file list.</summary>
    public static List<(int Format, int Size)> Formats(ArtChunk root) =>
        FileList(root).Children.Where(c => c.Magic == "mhif").Select(c => (ArtChunk.R(c.Header, 0x10), ArtChunk.R(c.Header, 0x14))).ToList();

    private static string Magic(byte[] d, int p) => p + 4 <= d.Length ? Encoding.ASCII.GetString(d, p, 4) : "";
    private static int I(byte[] d, int p) => BitConverter.ToInt32(d, p);
    private static void Expect(byte[] d, int p, string m)
    {
        if (Magic(d, p) != m) throw new InvalidDataException($"expected {m} at 0x{p:X}, found '{Magic(d, p)}'");
    }
}
