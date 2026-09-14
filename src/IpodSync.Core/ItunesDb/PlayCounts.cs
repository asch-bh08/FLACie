namespace IpodSync.Core.ItunesDb;

/// <summary>
/// <c>iPod_Control/iTunes/Play Counts</c>: the firmware's per-track play/skip/rating
/// log for iTunes to merge, one fixed-size entry per track, matched to tracks by
/// <b>position</b> in the CDB track list (header "mhdp", header length @0x04, entry
/// length @0x08, entry count @0x0C; on the nano 5G: 96 / 28 / 635).
///
/// Adding tracks at the end keeps every position; removing or reordering tracks does
/// not, and a stale file would credit plays to the wrong songs the next time iTunes
/// reads it. <see cref="Realign"/> rebuilds the file so each entry follows its track.
/// </summary>
public static class PlayCounts
{
    public const string FileName = "Play Counts";

    /// <summary>Returns realigned bytes, or null when every entry already sits at its
    /// track's position (the common case: only appends).</summary>
    public static byte[]? Realign(byte[] file, IReadOnlyList<ulong> oldOrder, IReadOnlyList<ulong> newOrder)
    {
        if (file.Length < 0x10 || System.Text.Encoding.ASCII.GetString(file, 0, 4) != "mhdp")
            throw new InvalidDataException("Play Counts: no mhdp header");
        int hdr = BitConverter.ToInt32(file, 4), entry = BitConverter.ToInt32(file, 8), count = BitConverter.ToInt32(file, 12);
        if (hdr <= 0 || entry <= 0 || count < 0 || hdr + (long)entry * count > file.Length)
            throw new InvalidDataException("Play Counts: header does not match file size");

        int covered = Math.Min(count, oldOrder.Count);
        var entryFor = new Dictionary<ulong, int>();
        for (int i = 0; i < covered; i++) entryFor.TryAdd(oldOrder[i], i);

        bool aligned = newOrder.Count >= covered && Enumerable.Range(0, covered).All(i => newOrder[i] == oldOrder[i]);
        if (aligned) return null;

        // New file: an entry for every new position up to the last track that had one;
        // tracks without an old entry (new ones) get zeroed entries ("nothing to merge").
        int last = -1;
        for (int i = 0; i < newOrder.Count; i++) if (entryFor.ContainsKey(newOrder[i])) last = i;
        int newCount = last + 1;
        byte[] result = new byte[hdr + entry * newCount];
        Array.Copy(file, result, hdr);
        BitConverter.TryWriteBytes(result.AsSpan(12, 4), newCount);
        for (int i = 0; i < newCount; i++)
            if (entryFor.TryGetValue(newOrder[i], out int old))
                Array.Copy(file, hdr + old * entry, result, hdr + i * entry, entry);
        return result;
    }

    /// <summary>Persistent ids in CDB track-list order.</summary>
    public static List<ulong> TrackOrder(ItunesDatabase db) => db.Tracks.Select(t => t.PersistentId).ToList();
}
