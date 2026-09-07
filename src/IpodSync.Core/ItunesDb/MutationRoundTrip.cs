using static IpodSync.Core.ItunesDb.BinaryIo;

namespace IpodSync.Core.ItunesDb;

public sealed record MutationTestResult(
    bool SemanticMatch,
    bool BytesContained,
    uint MutatedTrackId,
    int OldPlayCount, int NewPlayCount,
    int OldStars, int NewStars,
    List<string> Problems)
{
    public bool Passed => SemanticMatch && BytesContained && Problems.Count == 0;
}

/// <summary>
/// The actual gate before any real device write: proves that editing one real
/// field on one real track -- through the same RawChunk tree the identity
/// round-trip in <see cref="RoundTrip"/> already proved -- changes only that
/// field. Checked two independent ways: (1) re-reading the result through
/// ItunesDbReader, the already-verified reader, and diffing every other track
/// and every playlist field-by-field; (2) a raw byte diff confirming every
/// changed byte in the output falls inside the one mhit chunk that was targeted.
/// Never writes anything -- this runs entirely in memory against bytes already
/// read off a device.
/// </summary>
public static class MutationRoundTrip
{
    public static MutationTestResult Run(byte[] fileBytes)
    {
        var dbBefore = ItunesDbReader.Read(fileBytes);

        byte[] originalInflated = Inflate(fileBytes);
        var root = RawChunkParser.ParseRoot(originalInflated);
        var trackChunks = RawChunkNavigation.TrackChunks(root);
        if (trackChunks.Count == 0)
            throw new InvalidOperationException("No tracks found to mutate.");

        var target = trackChunks[0];
        uint trackId = (uint)TrackFields.GetId(target);
        int oldPlayCount = TrackFields.GetPlayCount(target);
        int oldStars = TrackFields.GetStars(target);
        int newPlayCount = oldPlayCount + 1;
        int newStars = oldStars >= 5 ? 1 : oldStars + 1;

        TrackFields.SetPlayCount(target, newPlayCount);
        TrackFields.SetStars(target, newStars);

        byte[] modifiedInflated = root.Serialize();
        var dbAfter = ItunesDbReader.Read(modifiedInflated);

        var problems = new List<string>();
        bool semanticMatch = CheckSemantics(dbBefore, dbAfter, trackId, problems);

        bool bytesContained = CheckByteContainment(
            root, target, originalInflated, modifiedInflated, problems);

        return new MutationTestResult(
            semanticMatch, bytesContained, trackId,
            oldPlayCount, newPlayCount, oldStars, newStars, problems);
    }

    private static bool CheckSemantics(ItunesDatabase before, ItunesDatabase after, uint trackId, List<string> problems)
    {
        bool ok = true;

        if (after.Tracks.Count != before.Tracks.Count)
        {
            problems.Add($"track count changed: {before.Tracks.Count} -> {after.Tracks.Count}");
            ok = false;
        }

        var beforeById = before.Tracks.ToDictionary(t => t.Id);
        var afterById = after.Tracks.ToDictionary(t => t.Id);

        foreach (var (id, b) in beforeById)
        {
            if (!afterById.TryGetValue(id, out var a))
            {
                problems.Add($"track {id} missing after mutation");
                ok = false;
                continue;
            }

            bool isTarget = id == trackId;
            if (b.Title != a.Title || b.Artist != a.Artist || b.Album != a.Album || b.AlbumArtist != a.AlbumArtist ||
                b.Genre != a.Genre || b.Composer != a.Composer || b.Comment != a.Comment ||
                b.Location != a.Location || b.SizeBytes != a.SizeBytes || b.LengthMs != a.LengthMs ||
                b.Bitrate != a.Bitrate || b.SampleRate != a.SampleRate || b.TrackNumber != a.TrackNumber ||
                b.DiscNumber != a.DiscNumber || b.Year != a.Year || b.Compilation != a.Compilation ||
                b.LastModified != a.LastModified || b.PersistentId != a.PersistentId ||
                (!isTarget && (b.PlayCount != a.PlayCount || b.Stars != a.Stars)))
            {
                problems.Add($"track {id} changed unexpectedly (expected only track {trackId}'s play count/stars to differ)");
                ok = false;
            }
        }

        if (afterById.Keys.Except(beforeById.Keys).Any())
        {
            problems.Add("a track id appeared that was not present before mutation");
            ok = false;
        }

        if (after.Playlists.Count != before.Playlists.Count)
        {
            problems.Add($"playlist count changed: {before.Playlists.Count} -> {after.Playlists.Count}");
            ok = false;
        }
        else
        {
            for (int i = 0; i < before.Playlists.Count; i++)
            {
                var pb = before.Playlists[i];
                var pa = after.Playlists[i];
                if (pb.Name != pa.Name || pb.IsMaster != pa.IsMaster || pb.IsSmart != pa.IsSmart ||
                    pb.PersistentId != pa.PersistentId || !pb.TrackIds.SequenceEqual(pa.TrackIds))
                {
                    problems.Add($"playlist '{pb.Name}' changed unexpectedly");
                    ok = false;
                }
            }
        }

        return ok;
    }

    private static bool CheckByteContainment(
        RawChunk root, RawChunk target, byte[] original, byte[] modified, List<string> problems)
    {
        if (original.Length != modified.Length)
        {
            problems.Add($"file length changed: {original.Length} -> {modified.Length} (expected no length change for a fixed-size field edit)");
            return false;
        }

        var range = RawChunkNavigation.ByteRangeOf(root, target);
        if (range is null)
        {
            problems.Add("could not locate the mutated chunk's byte range in the tree");
            return false;
        }
        var (start, length) = range.Value;

        for (int i = 0; i < original.Length; i++)
        {
            if (original[i] == modified[i]) continue;
            if (i < start || i >= start + length)
            {
                problems.Add($"unexpected byte difference at 0x{i:X}, outside the mutated track chunk (0x{start:X}-0x{start + length:X})");
                return false;
            }
        }
        return true;
    }
}
