using Microsoft.Data.Sqlite;
using IpodSync.Core.ItunesDb;

namespace IpodSync.Core.Itlp;

/// <summary>
/// Synchronizes the modern SQLite library bundle used by newer click-wheel iPods.
/// These devices keep iTunesCDB for the classic database and Library.itdb for the
/// on-device menu/search index; changing only the former leaves new playlists and
/// metadata invisible to the firmware.
///
/// This first slice deliberately handles normal user playlists containing tracks
/// already present in Library.itdb. It is used to prove the two-database playlist
/// contract before the track-import mirror is added.
/// </summary>
public static class ItlpPlaylistSync
{
    public sealed record Result(int Created, int Updated, int Memberships, List<string> Problems)
    {
        public bool Ok => Problems.Count == 0;
    }

    public static Result Preview(string libraryPath, ItunesDatabase cdb)
    {
        string scratch = Path.Combine(Path.GetTempPath(), "ipodsync-itlp-" + Guid.NewGuid().ToString("N") + ".itdb");
        try
        {
            File.Copy(libraryPath, scratch);
            return Synchronize(scratch, cdb);
        }
        finally
        {
            try { File.Delete(scratch); } catch { /* best effort only */ }
        }
    }

    public static Result Synchronize(string libraryPath, ItunesDatabase cdb)
    {
        var problems = new List<string>();
        int created = 0, updated = 0, memberships = 0;
        if (!File.Exists(libraryPath))
        {
            problems.Add($"Library.itdb not found: {libraryPath}");
            return new Result(created, updated, memberships, problems);
        }

        try
        {
            var cs = new SqliteConnectionStringBuilder { DataSource = libraryPath, Mode = SqliteOpenMode.ReadWrite }.ToString();
            using var db = new SqliteConnection(cs);
            db.Open();
            using var tx = db.BeginTransaction();

            var userLists = cdb.Playlists.Where(p => !p.IsMaster && !p.IsSmart && !string.IsNullOrWhiteSpace(p.Name)).ToList();
            foreach (var list in userLists)
            {
                long pid = unchecked((long)list.PersistentId);
                bool exists;
                using (var check = db.CreateCommand())
                {
                    check.Transaction = tx;
                    check.CommandText = "SELECT EXISTS(SELECT 1 FROM container WHERE pid = $pid)";
                    check.Parameters.AddWithValue("$pid", pid);
                    exists = Convert.ToInt32(check.ExecuteScalar()) != 0;
                }

                if (!exists)
                {
                    // These values match every ordinary visible playlist on the real device.
                    using var add = db.CreateCommand();
                    add.Transaction = tx;
                    add.CommandText = """
                        INSERT INTO container
                          (pid, distinguished_kind, date_created, date_modified, name, name_order, parent_pid, media_kinds, is_hidden)
                        VALUES ($pid, 0, $now, $now, $name, 0, 0, 1, 0)
                        """;
                    add.Parameters.AddWithValue("$pid", pid);
                    add.Parameters.AddWithValue("$now", AppleSeconds());
                    add.Parameters.AddWithValue("$name", list.Name!);
                    add.ExecuteNonQuery();
                    created++;
                }
                else
                {
                    using var update = db.CreateCommand();
                    update.Transaction = tx;
                    update.CommandText = "UPDATE container SET name = $name, date_modified = $now, is_hidden = 0 WHERE pid = $pid";
                    update.Parameters.AddWithValue("$pid", pid);
                    update.Parameters.AddWithValue("$name", list.Name!);
                    update.Parameters.AddWithValue("$now", AppleSeconds());
                    update.ExecuteNonQuery();
                    updated++;
                }

                using (var clear = db.CreateCommand())
                {
                    clear.Transaction = tx;
                    clear.CommandText = "DELETE FROM item_to_container WHERE container_pid = $pid";
                    clear.Parameters.AddWithValue("$pid", pid);
                    clear.ExecuteNonQuery();
                }

                int order = 0;
                foreach (uint trackId in list.TrackIds)
                {
                    var track = cdb.Tracks.FirstOrDefault(t => t.Id == trackId);
                    if (track is null) { problems.Add($"playlist '{list.Name}' references missing CDB track {trackId}"); continue; }
                    long itemPid = unchecked((long)track.PersistentId);
                    using var itemCheck = db.CreateCommand();
                    itemCheck.Transaction = tx;
                    itemCheck.CommandText = "SELECT EXISTS(SELECT 1 FROM item WHERE pid = $pid)";
                    itemCheck.Parameters.AddWithValue("$pid", itemPid);
                    if (Convert.ToInt32(itemCheck.ExecuteScalar()) == 0)
                    {
                        problems.Add($"playlist '{list.Name}' track {trackId} (pid 0x{track.PersistentId:X16}) is absent from Library.itdb");
                        continue;
                    }
                    using var link = db.CreateCommand();
                    link.Transaction = tx;
                    link.CommandText = "INSERT INTO item_to_container (item_pid, container_pid, physical_order, shuffle_order) VALUES ($item, $container, $order, $order)";
                    link.Parameters.AddWithValue("$item", itemPid);
                    link.Parameters.AddWithValue("$container", pid);
                    link.Parameters.AddWithValue("$order", order++);
                    link.ExecuteNonQuery();
                    memberships++;
                }
            }

            if (problems.Count > 0) { tx.Rollback(); return new Result(created, updated, memberships, problems); }
            tx.Commit();
        }
        catch (Exception ex) { problems.Add(ex.Message); }
        return new Result(created, updated, memberships, problems);
    }

    private static int AppleSeconds()
    {
        // Library.itdb uses seconds from 2001-01-01. Stored signed, current dates fit.
        return (int)(DateTimeOffset.UtcNow - new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero)).TotalSeconds;
    }
}
