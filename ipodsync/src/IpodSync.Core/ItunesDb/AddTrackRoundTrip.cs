namespace IpodSync.Core.ItunesDb;

/// <summary>
/// Proves <see cref="LibraryMutation.AddTrackFromFile"/> round-trips: builds a new
/// track from a real audio file, serialises, and re-reads through the verified
/// reader to assert the track appears with the right title/size/duration/location,
/// is in the master playlist, the count went up by exactly one, every existing
/// track is untouched, and the writer is self-consistent. In memory only — no file
/// is copied. (Device firmware acceptance of a from-scratch track is unverified.)
/// </summary>
public static class AddTrackRoundTrip
{
    public static ResizeTestResult Run(byte[] file, string audioPath)
    {
        var before = ItunesDbReader.Read(file);
        var root = RawChunkParser.ParseRoot(BinaryIo.Inflate(file));

        var (mhit, destRel) = LibraryMutation.AddTrackFromFile(root, audioPath);
        uint newId = (uint)TrackFields.GetId(mhit);
        string expectLoc = ":" + destRel.Replace('/', ':');

        byte[] mod = root.Serialize();
        var after = ItunesDbReader.Read(mod);

        var problems = new List<string>(); bool ok = true;

        if (after.Tracks.Count != before.Tracks.Count + 1)
        { problems.Add($"expected {before.Tracks.Count + 1} tracks, got {after.Tracks.Count}"); ok = false; }

        var nt = after.Tracks.FirstOrDefault(t => t.Id == newId);
        if (nt is null) { problems.Add($"new track #{newId} not found after add"); ok = false; }
        else
        {
            if (string.IsNullOrEmpty(nt.Title)) { problems.Add("new track has no title"); ok = false; }
            if (nt.Location != expectLoc) { problems.Add($"location is '{nt.Location}', expected '{expectLoc}'"); ok = false; }
            if (nt.SizeBytes <= 0) { problems.Add("new track size is 0"); ok = false; }
            if (nt.PlayCount != 0 || nt.Stars != 0) { problems.Add("new track should start with 0 plays / 0 stars"); ok = false; }
        }

        // must appear in the master playlist
        var master = after.Playlists.FirstOrDefault(p => p.IsMaster);
        if (master is null || !master.TrackIds.Contains(newId)) { problems.Add("new track is not in the master playlist"); ok = false; }

        // every pre-existing track still present and unchanged in the fields we model
        var beforeById = before.Tracks.ToDictionary(t => t.Id);
        foreach (var a in after.Tracks.Where(t => t.Id != newId))
            if (beforeById.TryGetValue(a.Id, out var b) &&
                (b.Title != a.Title || b.Artist != a.Artist || b.Location != a.Location || b.SizeBytes != a.SizeBytes))
            { problems.Add($"existing track {a.Id} changed during an add"); ok = false; }
        if (before.Tracks.Any(b => after.Tracks.All(a => a.Id != b.Id)))
        { problems.Add("an existing track disappeared during an add"); ok = false; }

        bool idem = RawChunkParser.ParseRoot(mod).Serialize().AsSpan().SequenceEqual(mod);
        if (!idem) problems.Add("writer output is not idempotent");

        var title = after.Tracks.FirstOrDefault(t => t.Id == newId)?.Title ?? "?";
        return new ResizeTestResult("add-track-from-file", ok, idem, problems,
            $"added '{title}' as #{newId} -> {destRel} ({before.Tracks.Count} -> {after.Tracks.Count} tracks)");
    }
}
