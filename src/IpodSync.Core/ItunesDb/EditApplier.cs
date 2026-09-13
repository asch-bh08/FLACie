using System.Text.Json.Serialization;

namespace IpodSync.Core.ItunesDb;

/// <summary>
/// Applies a JSON change-set (see EDIT-PROTOCOL.md) to a parsed iTunesDB via the
/// round-trip-proven mutation code in <see cref="LibraryMutation"/> /
/// <see cref="TrackFields"/>. This is the bridge the read-only iPod Player uses to
/// edit a device without growing a second, unproven writer: the player emits the
/// change-set, this applies it, and every result is checked (re-read through the
/// verified reader + idempotent re-serialize) before any bytes are allowed near a
/// device. Applying is entirely in memory; writing to disk is the caller's step,
/// gated on <see cref="ApplyReport.AllOk"/> and a backup.
/// </summary>
public static class EditApplier
{
    public static ApplyReport Apply(byte[] fileBytes, ChangeSet changeSet)
    {
        var report = new ApplyReport();
        var before = ItunesDbReader.Read(fileBytes);
        report.TracksBefore = before.Tracks.Count;
        report.PlaylistsBefore = before.Playlists.Count(p => !p.IsMaster);

        byte[] inflated = BinaryIo.Inflate(fileBytes);
        report.WasCompressed = !ReferenceEquals(inflated, fileBytes);
        var root = RawChunkParser.ParseRoot(inflated);

        foreach (var op in changeSet.Ops ?? [])
        {
            try { report.Ops.Add(ApplyOne(root, before, op)); }
            catch (Exception ex) { report.Ops.Add(new OpResult(op.Op ?? "(missing op)", false, ex.Message)); }
        }

        byte[] modified = root.Serialize();
        report.ModifiedInflated = modified;

        // Gate 1: the verified reader must still parse the result.
        try
        {
            var after = ItunesDbReader.Read(modified);
            report.Parseable = true;
            report.TracksAfter = after.Tracks.Count;
            report.PlaylistsAfter = after.Playlists.Count(p => !p.IsMaster);
        }
        catch (Exception ex) { report.Problems.Add("result is not parseable by the verified reader: " + ex.Message); }

        // Gate 2: the writer must agree with itself (parse its output, re-serialize, compare).
        var reparsed = RawChunkParser.ParseRoot(modified);
        report.Idempotent = reparsed.Serialize().AsSpan().SequenceEqual(modified);
        if (!report.Idempotent)
            report.Problems.Add("writer output is not idempotent (re-parsing and re-serializing changed the bytes)");

        report.ModifiedOnDisk = report.WasCompressed ? BinaryIo.Deflate(modified) : modified;
        return report;
    }

    private static OpResult ApplyOne(RawChunk root, ItunesDatabase before, EditOp op)
    {
        switch ((op.Op ?? "").Trim())
        {
            case "setTrackFields":
            {
                var mhit = FindTrack(root, Require(op.TrackId, "trackId"));
                var f = op.Fields ?? throw new InvalidOperationException("setTrackFields needs a 'fields' object.");
                var changed = new List<string>();
                if (f.Title is not null) { LibraryMutation.SetTrackString(mhit, MhodType.Title, f.Title); changed.Add("title"); }
                if (f.Artist is not null) { LibraryMutation.SetTrackString(mhit, MhodType.Artist, f.Artist); changed.Add("artist"); }
                if (f.Album is not null) { LibraryMutation.SetTrackString(mhit, MhodType.Album, f.Album); changed.Add("album"); }
                if (changed.Count == 0) throw new InvalidOperationException("setTrackFields had no title/artist/album to set.");
                return new OpResult(op.Op!, true, $"track {op.TrackId}: set {string.Join(", ", changed)}");
            }
            case "setTrackRating":
            {
                var mhit = FindTrack(root, Require(op.TrackId, "trackId"));
                int stars = Require(op.Stars, "stars");
                TrackFields.SetStars(mhit, stars);
                return new OpResult(op.Op!, true, $"track {op.TrackId}: rating -> {stars} star(s)");
            }
            case "setPlayCount":
            {
                var mhit = FindTrack(root, Require(op.TrackId, "trackId"));
                int count = Require(op.Count, "count");
                TrackFields.SetPlayCount(mhit, count);
                return new OpResult(op.Op!, true, $"track {op.TrackId}: play count -> {count}");
            }
            case "removeTrack":
            {
                uint id = Require(op.TrackId, "trackId");
                LibraryMutation.RemoveTrack(root, id);
                return new OpResult(op.Op!, true, $"removed track {id} (and any playlist entries referencing it)");
            }
            case "addTrackToPlaylist":
            {
                var copies = FindPlaylistCopies(root, before, RequireStr(op.Playlist, "playlist"));
                var track = FindTrack(root, Require(op.TrackId, "trackId"));
                foreach (var pl in copies) LibraryMutation.AddTrackToPlaylist(root, pl, track);
                return new OpResult(op.Op!, true, $"added track {op.TrackId} to playlist '{op.Playlist}'{CopyNote(copies.Count)}");
            }
            case "removeTrackFromPlaylist":
            {
                var copies = FindPlaylistCopies(root, before, RequireStr(op.Playlist, "playlist"));
                uint id = Require(op.TrackId, "trackId");
                int n = copies.Sum(pl => LibraryMutation.RemoveTrackFromPlaylist(pl, id));
                if (n == 0) throw new InvalidOperationException($"track {id} was not in playlist '{op.Playlist}'.");
                return new OpResult(op.Op!, true, $"removed track {id} from playlist '{op.Playlist}'{CopyNote(copies.Count)}");
            }
            case "renamePlaylist":
            {
                var copies = FindPlaylistCopies(root, before, RequireStr(op.Playlist, "playlist"));
                var nm = RequireStr(op.Name, "name");
                foreach (var pl in copies) LibraryMutation.SetPlaylistName(pl, nm);
                return new OpResult(op.Op!, true, $"renamed playlist '{op.Playlist}' -> '{nm}'{CopyNote(copies.Count)}");
            }
            case "deletePlaylist":
            {
                var copies = FindPlaylistCopies(root, before, RequireStr(op.Playlist, "playlist"));
                foreach (var pl in copies) LibraryMutation.DeletePlaylist(root, pl);
                return new OpResult(op.Op!, true, $"deleted playlist '{op.Playlist}'{CopyNote(copies.Count)}");
            }
            case "reorderPlaylist":
            {
                var copies = FindPlaylistCopies(root, before, RequireStr(op.Playlist, "playlist"));
                var ids = op.TrackIds ?? throw new InvalidOperationException("reorderPlaylist needs 'trackIds'.");
                foreach (var pl in copies) LibraryMutation.ReorderPlaylist(pl, ids);
                return new OpResult(op.Op!, true, $"reordered playlist '{op.Playlist}' ({ids.Length} entries){CopyNote(copies.Count)}");
            }
            case "createPlaylist":
            {
                var nm = RequireStr(op.Name, "name");
                var ids = op.TrackIds ?? Array.Empty<uint>();
                LibraryMutation.CreatePlaylist(root, nm, ids);
                return new OpResult(op.Op!, true, $"created playlist '{nm}' with {ids.Length} track(s)");
            }
            case "addTrackFromFile":
                throw new NotSupportedException(
                    $"op '{op.Op}' is not implemented yet — see EDIT-PROTOCOL.md.");
            default:
                throw new NotSupportedException($"unknown op '{op.Op}'.");
        }
    }

    private static RawChunk FindTrack(RawChunk root, uint id) =>
        RawChunkNavigation.TrackChunks(root).FirstOrDefault(t => (uint)TrackFields.GetId(t) == id)
        ?? throw new InvalidOperationException($"track {id} not found on the device.");

    // Playlists are addressed by name; resolve the name through the verified reader's
    // model, then match EVERY raw mhyp with that persistent id (header +0x1C). iTunes
    // writes playlists into more than one dataset, so a per-playlist edit must touch
    // all copies or the reader's de-dup can surface a stale one (this is what made an
    // early delete leave the playlist behind).
    private static List<RawChunk> FindPlaylistCopies(RawChunk root, ItunesDatabase before, string name)
    {
        var model = before.Playlists.FirstOrDefault(p => !p.IsMaster && p.Name == name)
            ?? throw new InvalidOperationException($"playlist '{name}' not found.");
        var copies = RawChunkNavigation.AllPlaylists(root)
            .Where(m => BinaryIo.U64(m.Header, 0x1C) == model.PersistentId).ToList();
        if (copies.Count == 0) throw new InvalidOperationException($"playlist '{name}' could not be located in the raw tree.");
        return copies;
    }
    private static string CopyNote(int n) => n > 1 ? $" (across {n} dataset copies)" : "";

    private static T Require<T>(T? v, string field) where T : struct =>
        v ?? throw new InvalidOperationException($"missing required field '{field}'.");
    private static string RequireStr(string? v, string field) =>
        v ?? throw new InvalidOperationException($"missing required field '{field}'.");
}

public sealed class ChangeSet
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("dbPath")] public string? DbPath { get; set; }
    [JsonPropertyName("ops")] public List<EditOp>? Ops { get; set; }
}

public sealed class EditOp
{
    [JsonPropertyName("op")] public string? Op { get; set; }
    [JsonPropertyName("trackId")] public uint? TrackId { get; set; }
    [JsonPropertyName("playlist")] public string? Playlist { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("stars")] public int? Stars { get; set; }
    [JsonPropertyName("count")] public int? Count { get; set; }
    [JsonPropertyName("position")] public int? Position { get; set; }
    [JsonPropertyName("trackIds")] public uint[]? TrackIds { get; set; }
    [JsonPropertyName("fields")] public EditFields? Fields { get; set; }
}

public sealed class EditFields
{
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("artist")] public string? Artist { get; set; }
    [JsonPropertyName("album")] public string? Album { get; set; }
}

public sealed record OpResult(string Op, bool Ok, string Detail);

public sealed class ApplyReport
{
    public List<OpResult> Ops { get; } = [];
    public int TracksBefore { get; set; }
    public int TracksAfter { get; set; }
    public int PlaylistsBefore { get; set; }
    public int PlaylistsAfter { get; set; }
    public bool Parseable { get; set; }
    public bool Idempotent { get; set; }
    public bool WasCompressed { get; set; }
    public List<string> Problems { get; } = [];
    public byte[] ModifiedInflated { get; set; } = [];
    public byte[] ModifiedOnDisk { get; set; } = [];

    /// <summary>Safe to write to a device only when every op succeeded, the result
    /// re-reads through the verified reader, the writer is self-consistent, and no
    /// problems were flagged.</summary>
    public bool AllOk => Ops.Count > 0 && Ops.All(o => o.Ok) && Parseable && Idempotent && Problems.Count == 0;
}
