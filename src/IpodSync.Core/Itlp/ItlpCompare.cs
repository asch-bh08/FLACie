using Microsoft.Data.Sqlite;
using IpodSync.Core.ItunesDb;

namespace IpodSync.Core.Itlp;

/// <summary>
/// Read-only comparison of the classic database (iTunesCDB, parsed into
/// <see cref="ItunesDatabase"/>) against the SQLite library bundle
/// (<c>iTunes Library.itlp</c>). Newer click-wheel iPods drive their menus from
/// the SQLite side, so "the edit is on the device" only means something once
/// both agree. This is the verifier every two-database write is checked with:
/// a write is only considered correct when the post-write diff is empty for the
/// aspects that write was meant to bring into line.
///
/// Keys: SQLite <c>item.pid</c> == CDB track persistent id and
/// <c>container.pid</c> == CDB playlist persistent id, both stored as signed
/// 64-bit (confirmed against the real device: every one of 635 items and the 7
/// containers match a CDB id bit-for-bit).
/// </summary>
public static class ItlpCompare
{
    public sealed class Diff
    {
        public List<string> TracksOnlyInCdb { get; } = [];
        public List<string> ItemsOnlyInSqlite { get; } = [];
        public List<string> TrackFieldMismatches { get; } = [];
        public List<string> PlaylistsOnlyInCdb { get; } = [];
        public List<string> ContainersOnlyInSqlite { get; } = [];
        public List<string> PlaylistNameMismatches { get; } = [];
        public List<string> MembershipMismatches { get; } = [];
        public List<string> ContainerUiMissing { get; } = [];
        public List<string> Notes { get; } = [];

        public bool PlaylistsInSync =>
            PlaylistsOnlyInCdb.Count == 0 && ContainersOnlyInSqlite.Count == 0 &&
            PlaylistNameMismatches.Count == 0 && MembershipMismatches.Count == 0 &&
            ContainerUiMissing.Count == 0;

        public bool TracksInSync =>
            TracksOnlyInCdb.Count == 0 && ItemsOnlyInSqlite.Count == 0 && TrackFieldMismatches.Count == 0;

        public bool InSync => PlaylistsInSync && TracksInSync;

        public IEnumerable<(string Section, List<string> Lines)> Sections()
        {
            yield return ("tracks only in CDB", TracksOnlyInCdb);
            yield return ("items only in SQLite", ItemsOnlyInSqlite);
            yield return ("track field mismatches", TrackFieldMismatches);
            yield return ("playlists only in CDB", PlaylistsOnlyInCdb);
            yield return ("containers only in SQLite", ContainersOnlyInSqlite);
            yield return ("playlist name mismatches", PlaylistNameMismatches);
            yield return ("membership mismatches", MembershipMismatches);
            yield return ("container_ui rows missing (Dynamic.itdb)", ContainerUiMissing);
        }
    }

    /// <summary>The SQLite playlists ipodsync mirrors: ordinary user playlists.
    /// The master playlist is compared as the library membership; smart and
    /// built-in playlists live only in the CDB's type-5 dataset on this device
    /// and have no container row.</summary>
    public static bool IsMirroredPlaylist(Playlist p) =>
        !p.IsMaster && !p.IsSmart && !p.IsPodcast && !string.IsNullOrWhiteSpace(p.Name);

    public static Diff Compare(string itlpDir, ItunesDatabase cdb)
    {
        var diff = new Diff();
        string library = Path.Combine(itlpDir, "Library.itdb");
        string dynamic = Path.Combine(itlpDir, "Dynamic.itdb");

        using var db = OpenReadOnly(library);

        // ---- tracks ----
        var items = new Dictionary<long, (string? Title, string? Artist, string? Album, string? AlbumArtist, string? Genre, string? Composer, long AlbumPid, long ArtistPid)>();
        using (var cmd = db.CreateCommand())
        {
            // Unknown-genre/composer rows (is_unknown = 1) stand for "no value".
            cmd.CommandText = """
                SELECT i.pid, i.title, i.artist, i.album, i.album_artist,
                       CASE WHEN g.is_unknown = 1 THEN NULL ELSE g.genre END,
                       i.composer, i.album_pid, i.artist_pid
                FROM item i LEFT JOIN genre_map g ON g.id = i.genre_id
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
                items[r.GetInt64(0)] = (Str(r, 1), Str(r, 2), Str(r, 3), Str(r, 4), Str(r, 5), Str(r, 6), r.GetInt64(7), r.GetInt64(8));
        }

        var tracksByPid = new Dictionary<long, Track>();
        foreach (var t in cdb.Tracks)
        {
            long pid = unchecked((long)t.PersistentId);
            tracksByPid[pid] = t;
            if (!items.TryGetValue(pid, out var it))
            {
                diff.TracksOnlyInCdb.Add($"#{t.Id} pid 0x{t.PersistentId:X16}  {t}  ({t.RelativePath})");
                continue;
            }
            Field(diff, t, "title", t.Title, it.Title);
            Field(diff, t, "artist", t.Artist, it.Artist);
            Field(diff, t, "album", t.Album, it.Album);
            Field(diff, t, "album_artist", t.AlbumArtist, it.AlbumArtist);
            Field(diff, t, "genre", t.Genre, it.Genre);
            Field(diff, t, "composer", t.Composer, it.Composer);
            // Album/artist identity: the CDB list link's persistent id must be the item's
            // album_pid / artist_pid (true for every iTunes-written track on the device;
            // album-less tracks use SQLite's unknown album instead, as iTunes does).
            if (!string.IsNullOrEmpty(t.Album) && t.AlbumPersistentId != 0 && it.AlbumPid != unchecked((long)t.AlbumPersistentId))
                diff.TrackFieldMismatches.Add($"#{t.Id} pid 0x{t.PersistentId:X16} album_pid: CDB 0x{t.AlbumPersistentId:X16} vs SQLite 0x{unchecked((ulong)it.AlbumPid):X16}");
            if ((t.AlbumArtist ?? t.Artist) is not null && t.ArtistPersistentId != 0 && it.ArtistPid != unchecked((long)t.ArtistPersistentId))
                diff.TrackFieldMismatches.Add($"#{t.Id} pid 0x{t.PersistentId:X16} artist_pid: CDB 0x{t.ArtistPersistentId:X16} vs SQLite 0x{unchecked((ulong)it.ArtistPid):X16}");
        }
        foreach (var (pid, it) in items)
            if (!tracksByPid.ContainsKey(pid))
                diff.ItemsOnlyInSqlite.Add($"pid 0x{unchecked((ulong)pid):X16}  {it.Artist} - {it.Title}");

        // ---- playlists ----
        var containers = new Dictionary<long, string?>();
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT pid, name FROM container";
            using var r = cmd.ExecuteReader();
            while (r.Read()) containers[r.GetInt64(0)] = Str(r, 1);
        }

        var ui = new HashSet<long>();
        if (File.Exists(dynamic))
        {
            using var dyn = OpenReadOnly(dynamic);
            using var cmd = dyn.CreateCommand();
            cmd.CommandText = "SELECT container_pid FROM container_ui";
            using var r = cmd.ExecuteReader();
            while (r.Read()) ui.Add(r.GetInt64(0));
        }
        else diff.Notes.Add("Dynamic.itdb not found; container_ui not checked");

        var idToPid = cdb.Tracks.ToDictionary(t => t.Id, t => unchecked((long)t.PersistentId));
        var mirrored = cdb.Playlists.Where(p => p.IsMaster || IsMirroredPlaylist(p)).ToList();
        var mirroredPids = new HashSet<long>();
        foreach (var p in mirrored)
        {
            long pid = unchecked((long)p.PersistentId);
            mirroredPids.Add(pid);
            if (!containers.TryGetValue(pid, out var name))
            {
                diff.PlaylistsOnlyInCdb.Add($"'{p.Name}' pid 0x{p.PersistentId:X16} ({p.TrackIds.Count} tracks)");
                continue;
            }
            // The master playlist's name is not compared: this device's SQLite copy
            // stores it with a replacement character where the CDB has U+2019.
            if (!p.IsMaster && name != p.Name)
                diff.PlaylistNameMismatches.Add($"pid 0x{p.PersistentId:X16}: CDB '{p.Name}' vs SQLite '{name}'");
            if (!ui.Contains(pid) && File.Exists(dynamic))
                diff.ContainerUiMissing.Add($"'{p.Name}' pid 0x{p.PersistentId:X16}");

            // Entries for tracks the SQLite side doesn't have yet are already reported
            // under "tracks only in CDB"; membership is compared over the rest.
            var expected = p.TrackIds.Where(idToPid.ContainsKey).Select(id => idToPid[id]).Where(items.ContainsKey).ToList();
            var actual = Members(db, pid);
            if (!expected.SequenceEqual(actual))
            {
                int firstDiff = Enumerable.Range(0, Math.Min(expected.Count, actual.Count))
                    .FirstOrDefault(i => expected[i] != actual[i], Math.Min(expected.Count, actual.Count));
                diff.MembershipMismatches.Add(
                    $"'{p.Name}': CDB {expected.Count} entries vs SQLite {actual.Count}, first difference at position {firstDiff}");
            }
        }
        foreach (var (pid, name) in containers)
            if (!mirroredPids.Contains(pid))
                diff.ContainersOnlyInSqlite.Add($"'{name}' pid 0x{unchecked((ulong)pid):X16}");

        return diff;
    }

    internal static List<long> Members(SqliteConnection db, long containerPid, SqliteTransaction? tx = null)
    {
        var list = new List<long>();
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT item_pid FROM item_to_container WHERE container_pid = $c ORDER BY physical_order, rowid";
        cmd.Parameters.AddWithValue("$c", containerPid);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetInt64(0));
        return list;
    }

    internal static SqliteConnection OpenReadOnly(string path)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();
        var c = new SqliteConnection(cs);
        c.Open();
        return c;
    }

    private static void Field(Diff diff, Track t, string name, string? cdb, string? sql)
    {
        if ((cdb ?? "") != (sql ?? ""))
            diff.TrackFieldMismatches.Add($"#{t.Id} pid 0x{t.PersistentId:X16} {name}: CDB '{cdb}' vs SQLite '{sql}'");
    }

    private static string? Str(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
}
