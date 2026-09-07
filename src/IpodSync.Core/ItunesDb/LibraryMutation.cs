using System.Text;
using static IpodSync.Core.ItunesDb.BinaryIo;

namespace IpodSync.Core.ItunesDb;

/// <summary>
/// Edits that change a chunk's byte length, unlike the fixed-size field patches
/// in <see cref="TrackFields"/>. These cascade into every ancestor's total-length
/// and child-count fields, which <see cref="RawChunk.Serialize"/> already
/// recomputes generically from tree shape -- the point of these methods is to
/// exercise that path with real, structurally valid content instead of a bare
/// child-count change.
///
/// Every fixed offset used here was read directly off two real devices via the
/// CLI's (now removed) `inspect` command, not from published documentation --
/// see README.md's "The writer" section for the two structural quirks that
/// would have been easy to get wrong (mhlt/mhlp have no total field, and mhip
/// embeds a nested mhod rather than being a pure flat leaf).
/// </summary>
public static class LibraryMutation
{
    private static readonly DateTimeOffset MacEpoch = new(1904, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static int NowAsMacSeconds() => (int)(DateTimeOffset.UtcNow - MacEpoch).TotalSeconds;

    /// <summary>Removes one track from the library and every playlist entry that
    /// references it. Pure deletion -- no new bytes are constructed, so this
    /// carries none of the risk a field we don't fully understand would.</summary>
    public static void RemoveTrack(RawChunk root, uint trackId)
    {
        var mhlt = RawChunkNavigation.FindTrackList(root)
            ?? throw new InvalidOperationException("No track list in this database.");
        int removed = mhlt.Children.RemoveAll(c => c.Magic == "mhit" && (uint)TrackFields.GetId(c) == trackId);
        if (removed == 0)
            throw new InvalidOperationException($"Track {trackId} not found.");

        foreach (var mhyp in RawChunkNavigation.AllPlaylists(root))
            mhyp.Children.RemoveAll(c => c.Magic == "mhip" && (uint)I32(c.Header, 0x18) == trackId);
    }

    /// <summary>
    /// Adds an existing track to a playlist by cloning a real sibling mhip in
    /// that same playlist and patching the fields confirmed against real bytes:
    /// the referenced track id (header +0x18), that track's persistent id,
    /// duplicated into the entry for integrity (+0x2C, 8 bytes), and a "date
    /// added" timestamp (+0x1C, Mac-epoch seconds) set to now.
    ///
    /// One field is patched conservatively rather than confidently: a value at
    /// header +0x14 that is also duplicated inside the entry's own embedded mhod
    /// payload (at the mhod's relative +0x18) -- comparing two real entries in
    /// the same playlist showed this value differs per entry with no relation to
    /// the track id, consistent with it being a unique id from its own counter
    /// (mirroring how tracks and playlists each get one). Its exact semantics
    /// are not confirmed against libgpod, so rather than guess whether it must
    /// be globally unique, must follow some other rule, or does not matter at
    /// all, this generates a value one past the highest one already present in
    /// the file -- safe under a uniqueness requirement, and no worse than the
    /// alternatives if it turns out not to matter.
    /// </summary>
    public static void AddTrackToPlaylist(RawChunk root, RawChunk playlist, RawChunk track)
    {
        var template = playlist.Children.FirstOrDefault(c => c.Magic == "mhip")
            ?? throw new InvalidOperationException("Playlist has no existing item to clone the layout from.");

        var clone = new RawChunk
        {
            Magic = "mhip",
            Header = (byte[])template.Header.Clone(),
            Payload = (byte[])template.Payload.Clone(),
        };

        int freshItemId = RawChunkNavigation.AllPlaylistItems(root).Select(m => I32(m.Header, 0x14)).DefaultIfEmpty(0).Max() + 1;
        WriteI32(clone.Header, 0x14, freshItemId);
        WriteI32(clone.Header, 0x18, TrackFields.GetId(track));
        WriteI32(clone.Header, 0x1C, NowAsMacSeconds());
        WriteU64(clone.Header, 0x2C, TrackFields.GetPersistentId(track));
        // The embedded mhod (playlist column info) carries its own copy of the
        // same item id, at the mhod's own header-relative offset 0x18.
        WriteI32(clone.Payload, 0x18, freshItemId);

        playlist.Children.Add(clone);
    }

    /// <summary>
    /// Replaces a track's Title mhod with a freshly built one carrying a new
    /// string. Every fixed field here (position=1, the encoding word=1 despite
    /// the payload being UTF-16LE, reserved=0) matches what six real Title mhods
    /// across two devices actually contain -- not what published docs describe.
    /// </summary>
    public static void RenameTrack(RawChunk mhit, string newTitle)
    {
        var newMhod = BuildStringMhod(MhodType.Title, newTitle);
        int idx = mhit.Children.FindIndex(c => c.Magic == "mhod" && (MhodType)I32(c.Header, 0x0C) == MhodType.Title);
        if (idx < 0) mhit.Children.Add(newMhod);
        else mhit.Children[idx] = newMhod;
    }

    private static RawChunk BuildStringMhod(MhodType type, string value)
    {
        byte[] strBytes = Encoding.Unicode.GetBytes(value); // UTF-16LE, matching every real sample seen
        byte[] payload = new byte[16 + strBytes.Length];
        WriteI32(payload, 0, 1);                 // "position" -- 1 in every real sample, never 0
        WriteI32(payload, 4, strBytes.Length);    // byte length
        WriteI32(payload, 8, 1);                  // encoding word -- 1 in every real sample despite UTF-16LE content
        WriteI32(payload, 12, 0);                 // reserved -- 0 in every real sample
        strBytes.CopyTo(payload, 16);

        byte[] header = new byte[24];
        Encoding.ASCII.GetBytes("mhod").CopyTo(header, 0);
        WriteI32(header, 0x04, header.Length);
        WriteI32(header, 0x08, header.Length + payload.Length); // Serialize() recomputes this; set correctly anyway
        WriteI32(header, 0x0C, (int)type);
        // +0x10, +0x14 are zero in every real sample; header is already zeroed.

        return new RawChunk { Magic = "mhod", Header = header, Payload = payload };
    }
}
