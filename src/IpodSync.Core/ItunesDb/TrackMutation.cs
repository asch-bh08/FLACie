namespace IpodSync.Core.ItunesDb;

/// <summary>Finds specific chunks within a parsed <see cref="RawChunk"/> tree.</summary>
public static class RawChunkNavigation
{
    public static RawChunk? FindTrackList(RawChunk root) =>
        root.Children.SelectMany(mhsd => mhsd.Children).FirstOrDefault(c => c.Magic == "mhlt");

    public static IReadOnlyList<RawChunk> TrackChunks(RawChunk root) =>
        FindTrackList(root)?.Children.Where(c => c.Magic == "mhit").ToList() ?? [];

    /// <summary>Absolute byte range [start, start+length) the given descendant
    /// chunk occupies when <paramref name="root"/> is serialized. Used to prove a
    /// mutation's effect on the output stayed within the chunk it targeted.</summary>
    public static (int Start, int Length)? ByteRangeOf(RawChunk root, RawChunk target) => Walk(root, target, 0);

    private static (int, int)? Walk(RawChunk node, RawChunk target, int offset)
    {
        if (ReferenceEquals(node, target)) return (offset, node.Length);
        int pos = offset + node.Header.Length;
        foreach (var child in node.Children)
        {
            var found = Walk(child, target, pos);
            if (found is not null) return found;
            pos += child.Length;
        }
        return null;
    }
}

/// <summary>
/// Mutators for the two mhit header fields two-way sync will need to write back:
/// play count and star rating. Offsets match <see cref="ItunesDbReader"/> exactly
/// -- these are read-only there. Chosen as the first fields to prove mutation
/// against because they are fixed-size, in-place values: patching one never
/// changes a chunk's byte length, so nothing else in the tree needs its length or
/// count fields recomputed as a result. Resizable edits (renaming a track, adding
/// one) are a separate, harder problem for later.
/// </summary>
public static class TrackFields
{
    private const int IdOffset = 0x10;
    private const int PlayCountOffset = 0x50;
    private const int StarsOffset = 0x1C + 3;

    public static int GetId(RawChunk mhit) => BinaryIo.I32(mhit.Header, IdOffset);

    public static int GetPlayCount(RawChunk mhit) => BinaryIo.I32(mhit.Header, PlayCountOffset);

    public static void SetPlayCount(RawChunk mhit, int value)
    {
        if (mhit.Header.Length < PlayCountOffset + 4)
            throw new InvalidOperationException("mhit header too short to carry a play count.");
        BinaryIo.WriteI32(mhit.Header, PlayCountOffset, value);
    }

    /// <summary>0-5. Stored on device as 0-100 in steps of 20.</summary>
    public static int GetStars(RawChunk mhit) =>
        mhit.Header.Length > StarsOffset ? mhit.Header[StarsOffset] / 20 : 0;

    public static void SetStars(RawChunk mhit, int stars0To5)
    {
        if (stars0To5 is < 0 or > 5) throw new ArgumentOutOfRangeException(nameof(stars0To5));
        if (mhit.Header.Length <= StarsOffset)
            throw new InvalidOperationException("mhit header too short to carry a star rating.");
        mhit.Header[StarsOffset] = (byte)(stars0To5 * 20);
    }
}
