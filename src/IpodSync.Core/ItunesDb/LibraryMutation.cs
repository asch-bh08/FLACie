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
        bool HasTitle(RawChunk p) => p.Children.Any(c => c.Magic == "mhod" && (MhodType)I32(c.Header, 0x0C) == MhodType.Title);
        bool UserPl(RawChunk p) => p.Header.Length > 0x14 && I32(p.Header, 0x14) == 0 && !IsSmart(p) && HasTitle(p);

        ulong newPid = RawChunkNavigation.AllPlaylists(root).Select(p => U64(p.Header, 0x1C)).DefaultIfEmpty(0UL).Max() + 1;
        var tracks = trackIds
            .Select(id => RawChunkNavigation.TrackChunks(root).FirstOrDefault(t => (uint)TrackFields.GetId(t) == id))
            .Where(t => t != null).Select(t => t!).ToList();

        // iTunes keeps playlists in more than one mhlp dataset and the device only shows
        // one of them -- which one isn't guaranteed -- so a new playlist must exist in
        // EVERY dataset that holds user playlists, exactly like the real ones (a
        // first attempt that added it to a single dataset didn't appear on the iPod).
        var datasets = root.Children.SelectMany(mhsd => mhsd.Children).Where(c => c.Magic == "mhlp").ToList();
        RawChunk? firstCopy = null;
        foreach (var mhlp in datasets)
        {
            var template = mhlp.Children.FirstOrDefault(m => m.Magic == "mhyp" && UserPl(m));
            if (template is null) continue;   // e.g. the built-in-smart dataset -- nothing to base on
            var pl = new RawChunk { Magic = "mhyp", Header = (byte[])template.Header.Clone(), Payload = (byte[])template.Payload.Clone() };
            // The playlist's persistent id lives in TWO places in the mhyp header: 0x1C
            // and 0x44 (confirmed by decoding real playlists on-device -- 0x40..0x43 is a
            // constant, the id repeats at 0x44). Patch both -- if 0x44 keeps the template
            // playlist's id, the device treats the new playlist as a duplicate and hides it.
            WriteU64(pl.Header, 0x1C, newPid);
            if (pl.Header.Length >= 0x4C) WriteU64(pl.Header, 0x44, newPid);
            // Keep the template's playlist-level mhods (type 1 name, plus type 100/102
            // display/column settings). Real playlists carry all three; a created one with
            // only the name mhod parses fine but the device refuses to display it.
            foreach (var c in template.Children.Where(c => c.Magic == "mhod"))
                pl.Children.Add(new RawChunk { Magic = c.Magic, Header = (byte[])c.Header.Clone(), Payload = (byte[])c.Payload.Clone() });
            SetPlaylistName(pl, name);                    // replaces the cloned name mhod with ours
            foreach (var track in tracks) AddTrackToPlaylist(root, pl, track);
            mhlp.Children.Add(pl);
            firstCopy ??= pl;
        }
        return firstCopy ?? throw new InvalidOperationException("No dataset with a user playlist to base a new one on.");
    }

    /// <summary>
    /// Constructs a brand-new track from an audio file and adds it to the library.
    /// Reads real duration/bitrate/tags via TagLibSharp; assigns a fresh track id and
    /// a collision-free persistent id; picks a scrambled <c>F##/XXXX.ext</c> path the
    /// way the device lays music out; clones a real same-extension mhit as a template
    /// (so media-type/format header fields stay real bytes) then patches the numeric
    /// fields and gives it a clean set of string mhods (title/artist/album/genre/
    /// filetype/location); appends it to the track list and to every master-playlist
    /// copy. Returns the new mhit and the device-relative path its file must be copied
    /// to (the caller does the copy on a real write). Round-trips + re-reads correctly;
    /// on-device acceptance unverified (no hardware).
    /// </summary>
    public static (RawChunk mhit, string destRel) AddTrackFromFile(RawChunk root, string sourcePath)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("source audio file not found", sourcePath);
        string ext = Path.GetExtension(sourcePath).TrimStart('.').ToLowerInvariant();
        long size = new FileInfo(sourcePath).Length;

        string? title = null, artist = null, album = null, genre = null;
        int durationMs = 0, bitrate = 0, year = 0, trackNo = 0;
        try
        {
            using var tf = TagLib.File.Create(sourcePath);
            title = Nz(tf.Tag.Title); artist = Nz(tf.Tag.FirstPerformer); album = Nz(tf.Tag.Album); genre = Nz(tf.Tag.FirstGenre);
            durationMs = (int)tf.Properties.Duration.TotalMilliseconds;
            bitrate = tf.Properties.AudioBitrate;
            year = (int)tf.Tag.Year; trackNo = (int)tf.Tag.Track;
        }
        catch { /* unreadable tags -> fall back to the filename for the title */ }
        title ??= Path.GetFileNameWithoutExtension(sourcePath);

        var mhlt = RawChunkNavigation.FindTrackList(root) ?? throw new InvalidOperationException("No track list in this database.");
        var existing = RawChunkNavigation.TrackChunks(root).ToList();
        if (existing.Count == 0) throw new InvalidOperationException("No existing track to use as a template.");

        uint newId = (uint)(existing.Select(TrackFields.GetId).DefaultIfEmpty(0).Max() + 1);
        var pids = new HashSet<ulong>(existing.Select(TrackFields.GetPersistentId));
        var rnd = new Random();
        ulong newPid; do { newPid = ((ulong)(uint)rnd.Next() << 32) | (uint)rnd.Next(); } while (newPid == 0 || pids.Contains(newPid));

        string control = existing.Select(GetLocation).FirstOrDefault(l => l != null)?.Contains("iTunes_Control") == true
            ? "iTunes_Control" : "iPod_Control";
        var used = new HashSet<string>(existing.Select(GetLocation).Where(l => l != null)
            .Select(l => l!.Replace(':', '/').TrimStart('/').ToLowerInvariant()));
        const string A = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        string destRel, destColon;
        do
        {
            string dir = $"F{rnd.Next(0, 50):00}";
            string name = new string(Enumerable.Range(0, 4).Select(_ => A[rnd.Next(A.Length)]).ToArray());
            destRel = $"{control}/Music/{dir}/{name}.{ext}";
            destColon = $":{control}:Music:{dir}:{name}.{ext}";
        } while (used.Contains(destRel.ToLowerInvariant()));

        var template = existing.FirstOrDefault(t => GetLocation(t)?.ToLowerInvariant().EndsWith("." + ext) == true) ?? existing[0];
        var mhit = new RawChunk { Magic = "mhit", Header = (byte[])template.Header.Clone(), Payload = (byte[])template.Payload.Clone() };
        WriteI32(mhit.Header, 0x10, (int)newId);
        WriteI32(mhit.Header, 0x24, (int)Math.Min(size, int.MaxValue));   // size bytes
        WriteI32(mhit.Header, 0x28, durationMs);                          // length ms
        WriteI32(mhit.Header, 0x2C, trackNo);
        WriteI32(mhit.Header, 0x34, year);
        if (bitrate > 0) WriteI32(mhit.Header, 0x38, bitrate);
        WriteI32(mhit.Header, 0x50, 0);                                   // play count
        WriteI32(mhit.Header, 0x58, 0);                                   // last played
        if (mhit.Header.Length > 0x1C + 3) mhit.Header[0x1C + 3] = 0;     // stars
        WriteI32(mhit.Header, 0x20, NowAsMacSeconds());                   // last modified / added
        WriteU64(mhit.Header, 0x70, newPid);

        mhit.Children.Clear();
        mhit.Children.Add(BuildStringMhod(MhodType.Title, title));
        if (artist != null) mhit.Children.Add(BuildStringMhod(MhodType.Artist, artist));
        if (album != null) mhit.Children.Add(BuildStringMhod(MhodType.Album, album));
        if (genre != null) mhit.Children.Add(BuildStringMhod(MhodType.Genre, genre));
        mhit.Children.Add(BuildStringMhod(MhodType.FileType, FileTypeDesc(ext)));
        mhit.Children.Add(BuildStringMhod(MhodType.Location, destColon));

        mhlt.Children.Add(mhit);
        foreach (var master in RawChunkNavigation.AllPlaylists(root).Where(m => m.Header.Length > 0x14 && I32(m.Header, 0x14) == 1))
            AddTrackToPlaylist(root, master, mhit);

        return (mhit, destRel);
    }

    private static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? GetLocation(RawChunk mhit)
    {
        var m = mhit.Children.FirstOrDefault(c => c.Magic == "mhod" && (MhodType)I32(c.Header, 0x0C) == MhodType.Location);
        if (m is null || m.Payload.Length < 16) return null;
        int len = I32(m.Payload, 4);
        if (len <= 0 || 16 + len > m.Payload.Length) return null;
        var bytes = m.Payload.AsSpan(16, len).ToArray();
        return (len % 2 == 0 ? System.Text.Encoding.Unicode : System.Text.Encoding.UTF8).GetString(bytes);
    }

    private static string FileTypeDesc(string ext) => ext switch
    {
        "mp3" => "MPEG audio file",
        "m4a" or "aac" or "mp4" => "AAC audio file",
        "m4b" => "AAC audio book",
        "wav" => "WAV audio file",
        "aif" or "aiff" => "AIFF audio file",
        "alac" => "Apple Lossless audio file",
        _ => ext.ToUpperInvariant() + " audio file",
    };

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
