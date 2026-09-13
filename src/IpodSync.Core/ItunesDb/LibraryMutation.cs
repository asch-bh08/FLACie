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
        // clone a real mhip: prefer one from this playlist, else any in the file (so a
        // brand-new/empty playlist can still receive its first entry from a real template).
        var template = playlist.Children.FirstOrDefault(c => c.Magic == "mhip")
            ?? RawChunkNavigation.AllPlaylistItems(root).FirstOrDefault()
            ?? throw new InvalidOperationException("No existing playlist item anywhere to clone the layout from.");

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
    public static void RenameTrack(RawChunk mhit, string newTitle) =>
        SetTrackString(mhit, MhodType.Title, newTitle);

    /// <summary>
    /// Sets any string field on a track (title, artist, album, ...) by replacing
    /// the existing mhod of that type, or appending one if the track has none.
    /// Uses the exact same freshly-built mhod layout <see cref="RenameTrack"/>
    /// proved for Title -- the string mhod structure is identical across types.
    /// Artist/Album share Title's layout but have their own round-trip test gate
    /// before being trusted for a real write (see EDIT-PROTOCOL.md).
    /// </summary>
    public static void SetTrackString(RawChunk mhit, MhodType type, string value)
    {
        var newMhod = BuildStringMhod(type, value);
        int idx = mhit.Children.FindIndex(c => c.Magic == "mhod" && (MhodType)I32(c.Header, 0x0C) == type);
        if (idx < 0) mhit.Children.Add(newMhod);
        else mhit.Children[idx] = newMhod;
    }

    /// <summary>Removes every playlist entry (mhip) in the given playlist that
    /// references the track id. Same pure-deletion mechanism as the playlist
    /// cleanup in <see cref="RemoveTrack"/>. Returns how many entries were dropped.</summary>
    public static int RemoveTrackFromPlaylist(RawChunk playlist, uint trackId) =>
        playlist.Children.RemoveAll(c => c.Magic == "mhip" && (uint)I32(c.Header, 0x18) == trackId);

    /// <summary>Renames a playlist by replacing its type-1 name mhod (playlists store
    /// their name in the same type-1 string mhod a track uses for its title).</summary>
    public static void SetPlaylistName(RawChunk mhyp, string name)
    {
        var newMhod = BuildStringMhod(MhodType.Title, name);
        int idx = mhyp.Children.FindIndex(c => c.Magic == "mhod" && (MhodType)I32(c.Header, 0x0C) == MhodType.Title);
        if (idx < 0) mhyp.Children.Insert(0, newMhod); else mhyp.Children[idx] = newMhod;
    }

    /// <summary>Removes a playlist (its whole mhyp) from whichever dataset holds it.
    /// Pure deletion of existing bytes. Refuses the master playlist.</summary>
    public static void DeletePlaylist(RawChunk root, RawChunk mhyp)
    {
        if (mhyp.Header.Length > 0x14 && I32(mhyp.Header, 0x14) == 1)
            throw new InvalidOperationException("Refusing to delete the master playlist.");
        foreach (var mhlp in root.Children.SelectMany(m => m.Children).Where(c => c.Magic == "mhlp"))
            if (mhlp.Children.Remove(mhyp)) return;
        throw new InvalidOperationException("Playlist not found in any dataset.");
    }

    /// <summary>Reorders a playlist's entries to match the given track-id order, by
    /// rearranging the existing mhip nodes (no bytes are constructed). Non-mhip
    /// children (the name mhod etc.) stay first, in their original order; any entry
    /// not named in the order is kept at the end so nothing is silently dropped.</summary>
    public static void ReorderPlaylist(RawChunk mhyp, IReadOnlyList<uint> orderedTrackIds)
    {
        var mhips = mhyp.Children.Where(c => c.Magic == "mhip").ToList();
        var others = mhyp.Children.Where(c => c.Magic != "mhip").ToList();
        var byTrack = new Dictionary<uint, Queue<RawChunk>>();
        foreach (var m in mhips)
        {
            uint id = (uint)I32(m.Header, 0x18);
            if (!byTrack.TryGetValue(id, out var q)) { q = new Queue<RawChunk>(); byTrack[id] = q; }
            q.Enqueue(m);
        }
        var reordered = new List<RawChunk>();
        foreach (var id in orderedTrackIds)
            if (byTrack.TryGetValue(id, out var q) && q.Count > 0) reordered.Add(q.Dequeue());
        foreach (var m in mhips) if (!reordered.Contains(m)) reordered.Add(m); // keep leftovers
        mhyp.Children.Clear();
        mhyp.Children.AddRange(others);
        mhyp.Children.AddRange(reordered);
    }

    /// <summary>
    /// Creates a new user playlist. Clones a real, non-smart, non-master playlist's
    /// mhyp header as a structural template (so header flags/fields are real bytes),
    /// gives it a fresh non-colliding persistent id and the new name, and adds an
    /// entry per existing track id by cloning real mhip layout via
    /// <see cref="AddTrackToPlaylist"/>. Round-trips and re-reads correctly; not yet
    /// confirmed accepted by device firmware (no hardware to test against).
    /// </summary>
    public static RawChunk CreatePlaylist(RawChunk root, string name, IReadOnlyList<uint> trackIds)
    {
        bool IsSmart(RawChunk p) => p.Children.Any(c => c.Magic == "mhod" &&
            ((MhodType)I32(c.Header, 0x0C) is MhodType.SmartPlaylistData or MhodType.SmartPlaylistRules));
        var template = RawChunkNavigation.AllPlaylists(root).FirstOrDefault(p =>
                p.Header.Length > 0x14 && I32(p.Header, 0x14) == 0 && !IsSmart(p) &&
                p.Children.Any(c => c.Magic == "mhod" && (MhodType)I32(c.Header, 0x0C) == MhodType.Title))
            ?? throw new InvalidOperationException("No ordinary user playlist to use as a template.");
        var mhlp = root.Children.SelectMany(m => m.Children)
                       .First(c => c.Magic == "mhlp" && c.Children.Contains(template));

        var pl = new RawChunk { Magic = "mhyp", Header = (byte[])template.Header.Clone(), Payload = (byte[])template.Payload.Clone() };
        ulong maxPid = RawChunkNavigation.AllPlaylists(root).Select(p => U64(p.Header, 0x1C)).DefaultIfEmpty(0UL).Max();
        WriteU64(pl.Header, 0x1C, maxPid + 1);          // fresh, non-colliding persistent id
        SetPlaylistName(pl, name);                       // pl has no children yet -> inserts the name mhod
        foreach (var id in trackIds)
        {
            var track = RawChunkNavigation.TrackChunks(root).FirstOrDefault(t => (uint)TrackFields.GetId(t) == id);
            if (track != null) AddTrackToPlaylist(root, pl, track);
        }
        mhlp.Children.Add(pl);
        return pl;
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
