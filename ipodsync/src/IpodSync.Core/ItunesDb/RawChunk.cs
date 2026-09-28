using static IpodSync.Core.ItunesDb.BinaryIo;

namespace IpodSync.Core.ItunesDb;

/// <summary>
/// One node in a lossless parse of the iTunesDB chunk tree. This is the substrate
/// the writer round-trips through: it captures every byte of a chunk so it can be
/// serialized back, and <see cref="Serialize"/> recomputes only the structural
/// fields this format ties to the tree shape (a chunk's own total length, and its
/// children's count). Everything else -- unknown header fields, signature regions,
/// and entire chunk types we do not understand the layout of, like mhla or mhli --
/// is whatever bytes <see cref="Header"/>/<see cref="Payload"/> hold, replayed
/// unchanged.
///
/// Deliberately dumb: this tree knows nothing about what a track or playlist
/// *means*. See <see cref="ItunesDbReader"/> for the semantic view read from a
/// plain parse. The two are independent parses of the same bytes, on purpose --
/// ItunesDbReader stays exactly as verified against real devices, and a bug in
/// this tree cannot corrupt what it reads.
/// </summary>
public sealed class RawChunk
{
    public required string Magic { get; init; }

    /// <summary>
    /// The chunk's declared header, byte for byte as read (magic + header-length +
    /// [total-length] + type-specific fixed fields, out to the chunk's own
    /// declared header length). <see cref="Serialize"/> patches only the count/
    /// total offsets this format defines for the six chunk types it understands
    /// the shape of; every other byte is replayed unchanged.
    /// </summary>
    public byte[] Header { get; set; } = [];

    /// <summary>Nested chunks, in original order. Empty for leaf chunks.</summary>
    public List<RawChunk> Children { get; } = [];

    /// <summary>
    /// Everything after Header that is not further chunk structure: a leaf's flat
    /// payload (an mhod's position/length/flag/reserved/string, an mhip's item
    /// fields, or the entirety of a dataset we do not parse further, such as mhla
    /// or mhli), or trailing padding a container carries beyond its children.
    /// </summary>
    public byte[] Payload { get; set; } = [];

    /// <summary>Magics we could not walk into structurally (mhla's contents,
    /// mhli, or anything unrecognised at a point where only a leaf was expected).
    /// Informational only -- these chunks are still preserved verbatim.</summary>
    public void CollectUnknownMagics(HashSet<string> into)
    {
        if (Magic is not ("mhbd" or "mhsd" or "mhlt" or "mhlp" or "mhit" or "mhyp" or "mhod" or "mhip"
                         or "mhla" or "mhia" or "mhli" or "mhii"))
            into.Add(Magic);
        foreach (var c in Children) c.CollectUnknownMagics(into);
    }

    public int Length => Header.Length + Payload.Length + Children.Sum(c => c.Length);

    /// <summary>
    /// Serializes this chunk and its children back to bytes. Recomputes each
    /// known container's own total length (offset 0x08, except mhlt/mhlp which
    /// have none of their own -- their extent is implied by the parent mhsd) and
    /// child-count field(s) from the tree; everything else in Header is copied
    /// unchanged, and Payload is always copied unchanged.
    /// </summary>
    public byte[] Serialize()
    {
        byte[] header = (byte[])Header.Clone();
        switch (Magic)
        {
            case "mhbd": WriteI32(header, 0x14, Children.Count); break;                  // count of mhsd
            case "mhlt": WriteI32(header, 0x08, Children.Count); break;                  // count of mhit (0x08 is COUNT here, not total)
            case "mhlp": WriteI32(header, 0x08, Children.Count); break;                  // count of mhyp (same)
            case "mhla": WriteI32(header, 0x08, Children.Count); break;                  // count of mhia (0x08 is COUNT, like mhlt)
            case "mhli": WriteI32(header, 0x08, Children.Count); break;                  // count of mhii (same)
            case "mhit": WriteI32(header, 0x0C, Children.Count); break;                  // count of mhod
            case "mhia": WriteI32(header, 0x0C, Children.Count); break;                  // count of mhod (album name/artist strings)
            case "mhii": WriteI32(header, 0x0C, Children.Count); break;                  // count of mhod (artist name)
            case "mhyp":
                WriteI32(header, 0x0C, Children.Count(c => c.Magic == "mhod"));
                WriteI32(header, 0x10, Children.Count(c => c.Magic == "mhip"));
                break;
            // mhsd has exactly one child and no count field. mhod/mhip are leaves.
            // Any other magic is a chunk type we do not know the layout of --
            // its header is replayed completely untouched, below.
        }

        byte[] childrenBytes = Children.Count == 0 ? [] : Concat(Children.Select(c => c.Serialize()).ToArray());
        int total = header.Length + childrenBytes.Length + Payload.Length;

        // Every known magic except mhlt/mhlp carries its own total at +0x08.
        if (Magic is "mhbd" or "mhsd" or "mhit" or "mhyp" or "mhod" or "mhip" or "mhia" or "mhii")
            WriteI32(header, 0x08, total);

        return Concat(header, childrenBytes, Payload);
    }
}

/// <summary>Parses an inflated iTunesDB buffer into a <see cref="RawChunk"/> tree.
/// Mirrors <see cref="ItunesDbReader"/>'s traversal exactly (same offsets, same
/// child counts, same magic dispatch) so the two parses cannot silently disagree
/// about where a chunk ends; the difference is this one keeps the bytes instead of
/// extracting fields.</summary>
public static class RawChunkParser
{
    /// <summary>Inflate (if it's an iTunesCDB) then parse into a chunk tree.</summary>
    public static RawChunk ParseDatabase(byte[] fileBytes) => ParseRoot(BinaryIo.Inflate(fileBytes));

    public static RawChunk ParseRoot(byte[] d)
    {
        if (d.Length < 0x20 || Magic(d, 0) != "mhbd")
            throw new InvalidDataException(
                $"Not an iTunesDB: expected 'mhbd' magic, found '{(d.Length >= 4 ? Magic(d, 0) : "<empty>")}'.");

        int hdrLen = I32(d, 0x04);
        int total = I32(d, 0x08);
        int numChildren = I32(d, 0x14);
        int end = total > 0 ? Math.Min(total, d.Length) : d.Length;

        var root = new RawChunk { Magic = "mhbd", Header = Slice(d, 0, hdrLen) };
        int pos = hdrLen;
        for (int i = 0; i < numChildren && pos + 0x10 <= end; i++)
        {
            var child = ParseMhsd(d, pos, end);
            if (child is null) break;
            root.Children.Add(child);
            pos += child.Length;
        }
        root.Payload = Slice(d, pos, end - pos);
        return root;
    }

    private static RawChunk? ParseMhsd(byte[] d, int pos, int outerEnd)
    {
        if (Magic(d, pos) != "mhsd") return null;
        int hdrLen = I32(d, pos + 0x04);
        int total = I32(d, pos + 0x08);
        if (hdrLen <= 0 || total <= 0 || pos + total > outerEnd) return null;

        var chunk = new RawChunk { Magic = "mhsd", Header = Slice(d, pos, hdrLen) };
        int innerStart = pos + hdrLen;
        int innerEnd = pos + total;

        // Dispatch on the magic of the list chunk inside, exactly like the reader:
        // mhlt/mhlp are the two dataset contents we understand. Anything else
        // (mhla albums, undecoded mhli, or a future dataset kind) we capture as one
        // opaque leaf -- by name, so it is visible in diagnostics -- rather than
        // walking into a layout we have not verified against real bytes. Serialize
        // never patches an unrecognised magic's header, so this is still exactly
        // as verbatim as leaving it as unstructured trailing payload; naming it
        // just makes that visible.
        var child = Magic(d, innerStart) switch
        {
            "mhlt" => ParseMhlt(d, innerStart, innerEnd),
            "mhlp" => ParseMhlp(d, innerStart, innerEnd),
            // Album and artist lists: same count-at-0x08 list shape as mhlt, holding
            // mhia/mhii items that carry mhod strings like mhit. Layout confirmed on
            // real nano 5G databases (see OVERNIGHT-STATUS.md); round-trip re-proven.
            "mhla" => ParseList(d, innerStart, innerEnd, "mhla", "mhia"),
            "mhli" => ParseList(d, innerStart, innerEnd, "mhli", "mhii"),
            _ => ParseLeaf(d, innerStart, innerEnd),
        };
        if (child is not null) chunk.Children.Add(child);

        int consumed = chunk.Children.Sum(c => c.Length);
        chunk.Payload = Slice(d, innerStart + consumed, innerEnd - innerStart - consumed);
        return chunk;
    }

    private static RawChunk ParseMhlt(byte[] d, int start, int bound)
    {
        int hdrLen = I32(d, start + 0x04);
        int count = I32(d, start + 0x08);
        var chunk = new RawChunk { Magic = "mhlt", Header = Slice(d, start, hdrLen) };

        int pos = start + hdrLen;
        for (int i = 0; i < count && pos + 0x10 <= bound; i++)
        {
            var child = Magic(d, pos) == "mhit" ? ParseMhit(d, pos, bound) : ParseLeaf(d, pos, bound);
            if (child is null) break;
            chunk.Children.Add(child);
            pos += child.Length;
        }
        chunk.Payload = Slice(d, pos, bound - pos);
        return chunk;
    }

    private static RawChunk ParseList(byte[] d, int start, int bound, string listMagic, string itemMagic)
    {
        int hdrLen = I32(d, start + 0x04);
        int count = I32(d, start + 0x08);
        var chunk = new RawChunk { Magic = listMagic, Header = Slice(d, start, hdrLen) };

        int pos = start + hdrLen;
        for (int i = 0; i < count && pos + 0x10 <= bound; i++)
        {
            var child = Magic(d, pos) == itemMagic ? ParseMhit(d, pos, bound, itemMagic) : ParseLeaf(d, pos, bound);
            if (child is null) break;
            chunk.Children.Add(child);
            pos += child.Length;
        }
        chunk.Payload = Slice(d, pos, bound - pos);
        return chunk;
    }

    private static RawChunk ParseMhlp(byte[] d, int start, int bound)
    {
        int hdrLen = I32(d, start + 0x04);
        int count = I32(d, start + 0x08);
        var chunk = new RawChunk { Magic = "mhlp", Header = Slice(d, start, hdrLen) };

        int pos = start + hdrLen;
        for (int i = 0; i < count && pos + 0x10 <= bound; i++)
        {
            var child = Magic(d, pos) == "mhyp" ? ParseMhyp(d, pos, bound) : ParseLeaf(d, pos, bound);
            if (child is null) break;
            chunk.Children.Add(child);
            pos += child.Length;
        }
        chunk.Payload = Slice(d, pos, bound - pos);
        return chunk;
    }

    private static RawChunk ParseMhit(byte[] d, int start, int bound, string magic = "mhit")
    {
        int hdrLen = I32(d, start + 0x04);
        int total = I32(d, start + 0x08);
        int numMhods = I32(d, start + 0x0C);
        int end = total > 0 ? Math.Min(start + total, bound) : bound;

        var chunk = new RawChunk { Magic = magic, Header = Slice(d, start, hdrLen) };
        int pos = start + hdrLen;
        for (int i = 0; i < numMhods && pos + 0x10 <= end; i++)
        {
            var child = ParseLeaf(d, pos, end);
            if (child is null) break;
            chunk.Children.Add(child);
            pos += child.Length;
        }
        chunk.Payload = Slice(d, pos, end - pos);
        return chunk;
    }

    private static RawChunk ParseMhyp(byte[] d, int start, int bound)
    {
        int hdrLen = I32(d, start + 0x04);
        int total = I32(d, start + 0x08);
        int numMhods = I32(d, start + 0x0C);
        int numItems = I32(d, start + 0x10);
        int end = total > 0 ? Math.Min(start + total, bound) : bound;

        var chunk = new RawChunk { Magic = "mhyp", Header = Slice(d, start, hdrLen) };
        int pos = start + hdrLen;
        for (int i = 0; i < numMhods + numItems && pos + 0x10 <= end; i++)
        {
            var child = ParseLeaf(d, pos, end);
            if (child is null) break;
            chunk.Children.Add(child);
            pos += child.Length;
        }
        chunk.Payload = Slice(d, pos, end - pos);
        return chunk;
    }

    /// <summary>A leaf: generic magic + header-length + total header, flat payload
    /// after it, no further structure. Used for mhod and mhip, and as the fallback
    /// for any chunk encountered where only a leaf was expected -- capturing it
    /// this way (rather than aborting the parse) is what lets an unrecognised
    /// sibling survive a round trip instead of silently vanishing.</summary>
    private static RawChunk? ParseLeaf(byte[] d, int pos, int bound)
    {
        if (pos + 0x0C > bound) return null;
        string magic = Magic(d, pos);
        int hdrLen = I32(d, pos + 0x04);
        int total = I32(d, pos + 0x08);
        if (hdrLen < 8 || total < hdrLen || pos + total > bound) return null;

        return new RawChunk
        {
            Magic = magic,
            Header = Slice(d, pos, hdrLen),
            Payload = Slice(d, pos + hdrLen, total - hdrLen),
        };
    }
}
