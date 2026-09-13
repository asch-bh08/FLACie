namespace IpodSync.Core.ItunesDb;

/// <summary>
/// Proves the playlist edits in <see cref="LibraryMutation"/> (rename, reorder,
/// delete, create) round-trip: each is applied to a real database, then the
/// result is re-read through the verified <see cref="ItunesDbReader"/> to assert
/// exactly the intended change appears and nothing else moved, and re-parsed +
/// re-serialized to prove the writer agrees with itself. Runs entirely in memory.
/// (create-playlist round-trips and re-reads correctly; whether device firmware
/// accepts a from-scratch playlist is unverified — no hardware to test on.)
/// </summary>
public static class PlaylistRoundTrip
{
    private static bool Idem(byte[] modified, List<string> problems)
    {
        var reserialized = RawChunkParser.ParseRoot(modified).Serialize();
        if (reserialized.AsSpan().SequenceEqual(modified)) return true;
        problems.Add("writer output is not idempotent (re-parse + re-serialize changed the bytes)");
        return false;
    }

    private static (RawChunk root, ItunesDatabase before) Load(byte[] file)
    {
        var before = ItunesDbReader.Read(file);
        return (RawChunkParser.ParseRoot(BinaryIo.Inflate(file)), before);
    }

    private static Playlist PickUser(ItunesDatabase db, int minTracks = 1) =>
        db.Playlists.FirstOrDefault(p => !p.IsMaster && !p.IsSmart && p.Name != null && p.TrackIds.Count >= minTracks)
        ?? throw new InvalidOperationException($"no user playlist with >= {minTracks} track(s) to test against");

    // all raw copies of a playlist (iTunes duplicates them across datasets)
    private static List<RawChunk> Copies(RawChunk root, ulong pid) =>
        RawChunkNavigation.AllPlaylists(root).Where(m => BinaryIo.U64(m.Header, 0x1C) == pid).ToList();

    public static ResizeTestResult Rename(byte[] file)
    {
        var (root, before) = Load(file);
        var target = PickUser(before);
        string nn = "ipodsync renamed " + Guid.NewGuid().ToString("N")[..6];
        foreach (var c in Copies(root, target.PersistentId)) LibraryMutation.SetPlaylistName(c, nn);
        byte[] mod = root.Serialize(); var after = ItunesDbReader.Read(mod);
        var problems = new List<string>(); bool ok = true;
        var pa = after.Playlists.FirstOrDefault(p => p.PersistentId == target.PersistentId);
        if (pa is null) { problems.Add("playlist vanished"); ok = false; }
        else
        {
            if (pa.Name != nn) { problems.Add($"name is '{pa.Name}', expected '{nn}'"); ok = false; }
            if (!pa.TrackIds.SequenceEqual(target.TrackIds)) { problems.Add("membership changed during rename"); ok = false; }
        }
        if (after.Playlists.Count != before.Playlists.Count) { problems.Add("playlist count changed"); ok = false; }
        foreach (var pb in before.Playlists.Where(p => p.PersistentId != target.PersistentId))
        {
            var q = after.Playlists.FirstOrDefault(p => p.PersistentId == pb.PersistentId);
            if (q is null || q.Name != pb.Name || !q.TrackIds.SequenceEqual(pb.TrackIds)) { problems.Add($"unrelated playlist '{pb.Name}' changed"); ok = false; }
        }
        return new ResizeTestResult("rename-playlist", ok, Idem(mod, problems), problems, $"'{target.Name}' -> '{nn}'");
    }

    public static ResizeTestResult Reorder(byte[] file)
    {
        var (root, before) = Load(file);
        var target = PickUser(before, 2);
        var reversed = Enumerable.Reverse(target.TrackIds).ToList();
        foreach (var c in Copies(root, target.PersistentId)) LibraryMutation.ReorderPlaylist(c, reversed);
        byte[] mod = root.Serialize(); var after = ItunesDbReader.Read(mod);
        var problems = new List<string>(); bool ok = true;
        var pa = after.Playlists.FirstOrDefault(p => p.PersistentId == target.PersistentId);
        if (pa is null) { problems.Add("playlist vanished"); ok = false; }
        else
        {
            if (!pa.TrackIds.SequenceEqual(reversed)) { problems.Add("order is not the requested reversal"); ok = false; }
            if (!pa.TrackIds.OrderBy(x => x).SequenceEqual(target.TrackIds.OrderBy(x => x))) { problems.Add("membership set changed during reorder"); ok = false; }
        }
        if (after.Tracks.Count != before.Tracks.Count) { problems.Add("track count changed during reorder"); ok = false; }
        return new ResizeTestResult("reorder-playlist", ok, Idem(mod, problems), problems, $"'{target.Name}' reversed ({target.TrackIds.Count} entries)");
    }

    public static ResizeTestResult Delete(byte[] file)
    {
        var (root, before) = Load(file);
        var target = PickUser(before);
        foreach (var c in Copies(root, target.PersistentId)) LibraryMutation.DeletePlaylist(root, c);
        byte[] mod = root.Serialize(); var after = ItunesDbReader.Read(mod);
        var problems = new List<string>(); bool ok = true;
        if (after.Playlists.Any(p => p.PersistentId == target.PersistentId)) { problems.Add("playlist still present after delete"); ok = false; }
        if (after.Playlists.Count != before.Playlists.Count - 1) { problems.Add($"expected {before.Playlists.Count - 1} playlists, got {after.Playlists.Count}"); ok = false; }
        if (after.Tracks.Count != before.Tracks.Count) { problems.Add("track count changed by a playlist delete"); ok = false; }
        foreach (var pb in before.Playlists.Where(p => p.PersistentId != target.PersistentId))
        {
            var q = after.Playlists.FirstOrDefault(p => p.PersistentId == pb.PersistentId);
            if (q is null || !q.TrackIds.SequenceEqual(pb.TrackIds)) { problems.Add($"unrelated playlist '{pb.Name}' changed"); ok = false; }
        }
        return new ResizeTestResult("delete-playlist", ok, Idem(mod, problems), problems, $"deleted '{target.Name}' ({before.Playlists.Count} -> {after.Playlists.Count})");
    }

    public static ResizeTestResult Create(byte[] file)
    {
        var (root, before) = Load(file);
        var ids = before.Tracks.Take(5).Select(t => t.Id).ToList();
        string nn = "ipodsync new " + Guid.NewGuid().ToString("N")[..6];
        LibraryMutation.CreatePlaylist(root, nn, ids);
        byte[] mod = root.Serialize(); var after = ItunesDbReader.Read(mod);
        var problems = new List<string>(); bool ok = true;
        if (after.Tracks.Count != before.Tracks.Count) { problems.Add("track count changed by create"); ok = false; }
        if (after.Playlists.Count != before.Playlists.Count + 1) { problems.Add($"expected {before.Playlists.Count + 1} playlists, got {after.Playlists.Count}"); ok = false; }
        var np = after.Playlists.FirstOrDefault(p => p.Name == nn);
        if (np is null) { problems.Add("new playlist not found after create"); ok = false; }
        else if (!np.TrackIds.SequenceEqual(ids)) { problems.Add($"new playlist tracks [{string.Join(",", np.TrackIds)}] != requested [{string.Join(",", ids)}]"); ok = false; }
        foreach (var pb in before.Playlists)
        {
            var q = after.Playlists.FirstOrDefault(p => p.PersistentId == pb.PersistentId);
            if (q is null || !q.TrackIds.SequenceEqual(pb.TrackIds)) { problems.Add($"existing playlist '{pb.Name}' changed"); ok = false; }
        }
        return new ResizeTestResult("create-playlist", ok, Idem(mod, problems), problems, $"created '{nn}' with {ids.Count} track(s)");
    }
}
