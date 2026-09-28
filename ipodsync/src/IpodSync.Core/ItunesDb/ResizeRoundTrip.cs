namespace IpodSync.Core.ItunesDb;

public sealed record ResizeTestResult(string Operation, bool SemanticOk, bool IdempotentOk, List<string> Problems, string Detail)
{
    public bool Passed => SemanticOk && IdempotentOk && Problems.Count == 0;
}

/// <summary>
/// Proves the three resizing edits in <see cref="LibraryMutation"/> round-trip
/// correctly. Unlike <see cref="MutationRoundTrip"/> (a fixed-size field patch,
/// checkable by raw byte containment against the original), these change a
/// chunk's byte length, so positions in the output do not line up with the
/// original file at all. Each is checked two ways instead: semantically, by
/// re-reading the result through ItunesDbReader (the already-verified reader)
/// and asserting only the intended change appears; and for internal
/// consistency, by parsing the writer's own output a second time and
/// serializing it again -- if that second pass does not reproduce the first
/// pass's bytes exactly, the writer produced something that does not even
/// agree with itself, regardless of whether it happens to look right once.
/// Runs entirely in memory; never writes anything.
/// </summary>
public static class ResizeRoundTrip
{
    public static ResizeTestResult RemoveTrack(byte[] fileBytes)
    {
        var dbBefore = ItunesDbReader.Read(fileBytes);
        byte[] inflated = BinaryIo.Inflate(fileBytes);
        var root = RawChunkParser.ParseRoot(inflated);

        var target = RawChunkNavigation.TrackChunks(root).First();
        uint trackId = (uint)TrackFields.GetId(target);
        int expectedPlaylistDrops = dbBefore.Playlists.Count(p => p.TrackIds.Contains(trackId));

        LibraryMutation.RemoveTrack(root, trackId);
        byte[] modified = root.Serialize();
        var dbAfter = ItunesDbReader.Read(modified);

        var problems = new List<string>();
        bool semanticOk = true;

        if (dbAfter.Tracks.Count != dbBefore.Tracks.Count - 1)
        { problems.Add($"expected {dbBefore.Tracks.Count - 1} tracks after removal, got {dbAfter.Tracks.Count}"); semanticOk = false; }
        if (dbAfter.Tracks.Any(t => t.Id == trackId))
        { problems.Add($"track {trackId} still present after removal"); semanticOk = false; }

        var beforeOthers = dbBefore.Tracks.Where(t => t.Id != trackId).ToDictionary(t => t.Id);
        var afterOthers = dbAfter.Tracks.ToDictionary(t => t.Id);
        if (!beforeOthers.Keys.OrderBy(x => x).SequenceEqual(afterOthers.Keys.OrderBy(x => x)))
        { problems.Add("the set of remaining tracks changed beyond just removing the target"); semanticOk = false; }
        foreach (var (id, b) in beforeOthers)
            if (afterOthers.TryGetValue(id, out var a) && (b.Title != a.Title || b.PlayCount != a.PlayCount || b.Stars != a.Stars || b.Location != a.Location))
            { problems.Add($"track {id} changed unexpectedly during an unrelated removal"); semanticOk = false; }

        int actualPlaylistDrops = 0;
        for (int i = 0; i < dbBefore.Playlists.Count; i++)
        {
            var pb = dbBefore.Playlists[i];
            var pa = dbAfter.Playlists.FirstOrDefault(p => p.PersistentId == pb.PersistentId);
            if (pa is null) { problems.Add($"playlist '{pb.Name}' disappeared"); semanticOk = false; continue; }
            var expectedIds = pb.TrackIds.Where(id => id != trackId).ToList();
            if (!expectedIds.SequenceEqual(pa.TrackIds))
            { problems.Add($"playlist '{pb.Name}' membership changed beyond removing track {trackId}"); semanticOk = false; }
            if (pb.TrackIds.Contains(trackId)) actualPlaylistDrops++;
        }
        if (actualPlaylistDrops != expectedPlaylistDrops)
        { problems.Add($"expected the track dropped from {expectedPlaylistDrops} playlists, saw {actualPlaylistDrops}"); semanticOk = false; }

        bool idempotentOk = CheckIdempotent(modified, problems);

        return new ResizeTestResult("remove-track", semanticOk, idempotentOk, problems,
            $"removed track {trackId}, which was in {expectedPlaylistDrops} playlist(s); {dbBefore.Tracks.Count} -> {dbAfter.Tracks.Count} tracks");
    }

    public static ResizeTestResult AddTrackToPlaylist(byte[] fileBytes)
    {
        var dbBefore = ItunesDbReader.Read(fileBytes);
        byte[] inflated = BinaryIo.Inflate(fileBytes);
        var root = RawChunkParser.ParseRoot(inflated);

        var playlist = RawChunkNavigation.AllPlaylists(root)
            .FirstOrDefault(p => p.Header.Length > 0x14 && p.Header[0x14] == 0 && p.Children.Count(c => c.Magic == "mhip") >= 1)
            ?? throw new InvalidOperationException("No non-master playlist with an existing item found to test against.");
        var playlistPersistentId = BinaryIo.U64(playlist.Header, 0x1C);
        var beforePlaylistModel = dbBefore.Playlists.First(p => p.PersistentId == playlistPersistentId);

        var existingIds = new HashSet<uint>(beforePlaylistModel.TrackIds);
        var trackToAdd = RawChunkNavigation.TrackChunks(root).First(t => !existingIds.Contains((uint)TrackFields.GetId(t)));
        uint addedTrackId = (uint)TrackFields.GetId(trackToAdd);

        LibraryMutation.AddTrackToPlaylist(root, playlist, trackToAdd);
        byte[] modified = root.Serialize();
        var dbAfter = ItunesDbReader.Read(modified);

        var problems = new List<string>();
        bool semanticOk = true;

        if (dbAfter.Tracks.Count != dbBefore.Tracks.Count)
        { problems.Add("track count changed -- adding to a playlist must not add a track"); semanticOk = false; }

        var afterPlaylistModel = dbAfter.Playlists.FirstOrDefault(p => p.PersistentId == playlistPersistentId);
        if (afterPlaylistModel is null) { problems.Add("target playlist disappeared"); semanticOk = false; }
        else
        {
            var expected = beforePlaylistModel.TrackIds.Append(addedTrackId).ToList();
            if (!expected.SequenceEqual(afterPlaylistModel.TrackIds))
            { problems.Add($"target playlist membership is not [original entries] + [{addedTrackId}]"); semanticOk = false; }
        }

        for (int i = 0; i < dbBefore.Playlists.Count; i++)
        {
            var pb = dbBefore.Playlists[i];
            if (pb.PersistentId == playlistPersistentId) continue;
            var pa = dbAfter.Playlists.FirstOrDefault(p => p.PersistentId == pb.PersistentId);
            if (pa is null || !pb.TrackIds.SequenceEqual(pa.TrackIds))
            { problems.Add($"unrelated playlist '{pb.Name}' changed"); semanticOk = false; }
        }

        bool idempotentOk = CheckIdempotent(modified, problems);

        return new ResizeTestResult("add-to-playlist", semanticOk, idempotentOk, problems,
            $"added track {addedTrackId} to '{beforePlaylistModel.Name}' ({beforePlaylistModel.TrackIds.Count} -> {beforePlaylistModel.TrackIds.Count + 1} entries)");
    }

    public static ResizeTestResult RenameTrack(byte[] fileBytes)
    {
        var dbBefore = ItunesDbReader.Read(fileBytes);
        byte[] inflated = BinaryIo.Inflate(fileBytes);
        var root = RawChunkParser.ParseRoot(inflated);

        var target = RawChunkNavigation.TrackChunks(root).First();
        uint trackId = (uint)TrackFields.GetId(target);
        string oldTitle = dbBefore.Tracks.First(t => t.Id == trackId).Title ?? "";
        string newTitle = "ipodsync round-trip test ★ " + Guid.NewGuid().ToString("N")[..8];

        LibraryMutation.RenameTrack(target, newTitle);
        byte[] modified = root.Serialize();
        var dbAfter = ItunesDbReader.Read(modified);

        var problems = new List<string>();
        bool semanticOk = true;

        var renamed = dbAfter.Tracks.FirstOrDefault(t => t.Id == trackId);
        if (renamed is null) { problems.Add($"track {trackId} missing after rename"); semanticOk = false; }
        else if (renamed.Title != newTitle)
        { problems.Add($"title is '{renamed.Title}', expected '{newTitle}'"); semanticOk = false; }

        if (dbAfter.Tracks.Count != dbBefore.Tracks.Count)
        { problems.Add("track count changed during a rename"); semanticOk = false; }

        var beforeOthers = dbBefore.Tracks.Where(t => t.Id != trackId).ToDictionary(t => t.Id);
        foreach (var a in dbAfter.Tracks.Where(t => t.Id != trackId))
            if (beforeOthers.TryGetValue(a.Id, out var b) && (b.Title != a.Title || b.Artist != a.Artist || b.PlayCount != a.PlayCount))
            { problems.Add($"track {a.Id} changed unexpectedly during an unrelated rename"); semanticOk = false; }

        if (dbAfter.Playlists.Count != dbBefore.Playlists.Count ||
            !dbBefore.Playlists.Zip(dbAfter.Playlists).All(p => p.First.TrackIds.SequenceEqual(p.Second.TrackIds)))
        { problems.Add("playlist membership/order changed during a rename"); semanticOk = false; }

        bool idempotentOk = CheckIdempotent(modified, problems);

        return new ResizeTestResult("rename-track", semanticOk, idempotentOk, problems,
            $"track {trackId}: '{oldTitle}' -> '{newTitle}'");
    }

    /// <summary>Parses the writer's own output a second time and serializes it
    /// again; the writer disagreeing with itself is a stronger red flag than any
    /// single check above, since it means the tree it built is not even
    /// well-formed by its own rules.</summary>
    private static bool CheckIdempotent(byte[] modified, List<string> problems)
    {
        var reparsed = RawChunkParser.ParseRoot(modified);
        byte[] reserialized = reparsed.Serialize();
        if (reserialized.AsSpan().SequenceEqual(modified)) return true;

        problems.Add("writer output is not idempotent: parsing it again and re-serializing did not reproduce the same bytes");
        return false;
    }
}
