using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using IpodSync.Core.ItunesDb;

namespace IpodSync.Core.Itlp;

/// <summary>
/// Brings the SQLite library bundle (<c>iTunes Library.itlp</c>) into line with
/// the classic database. Newer click-wheel iPods drive their Playlists menu and
/// search from <c>Library.itdb</c>, so a CDB-only edit is invisible on the device.
///
/// Always operates on a <b>staged copy</b> of the bundle, never on the device's
/// files: the caller stages, this mutates the staged files, the caller verifies the
/// staged result and only then copies those exact bytes onto the device. That way
/// the bytes that reach the hardware are the bytes that were proven.
///
/// Playlist scope (this file): containers (create / rename / delete), their
/// membership (item_to_container), the name_order sort rank, and the per-playlist
/// container_ui row in Dynamic.itdb. Every column value used for a new row is the
/// value every ordinary user playlist on the real device carries.
/// </summary>
public static class ItlpSync
{
    public static readonly string[] BundleFiles =
        ["Library.itdb", "Dynamic.itdb", "Locations.itdb", "Locations.itdb.cbk", "Extras.itdb", "Genius.itdb"];

    public sealed class Result
    {
        public List<string> Actions { get; } = [];
        public List<string> Problems { get; } = [];
        public HashSet<string> TouchedTables { get; } = [];
        public bool Ok => Problems.Count == 0;
    }

    /// <summary>Copies the device bundle into a fresh staging directory.</summary>
    public static string Stage(string itlpDir, string stagingRoot)
    {
        string dir = Path.Combine(stagingRoot, "itlp-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(dir);
        foreach (var f in BundleFiles)
        {
            string src = Path.Combine(itlpDir, f);
            if (File.Exists(src)) File.Copy(src, Path.Combine(dir, f));
        }
        return dir;
    }

    public static Result SyncPlaylists(string stagedItlpDir, ItunesDatabase cdb, DateTimeOffset now)
    {
        var result = new Result();
        string library = Path.Combine(stagedItlpDir, "Library.itdb");
        string dynamic = Path.Combine(stagedItlpDir, "Dynamic.itdb");
        if (!File.Exists(library)) { result.Problems.Add("Library.itdb missing from staged bundle"); return result; }
        if (!File.Exists(dynamic)) { result.Problems.Add("Dynamic.itdb missing from staged bundle"); return result; }
        int stamp = AppleSeconds(now);

        using var db = OpenReadWrite(library);
        using var dyn = OpenReadWrite(dynamic);
        using var tx = db.BeginTransaction();
        using var dtx = dyn.BeginTransaction();

        var existing = new Dictionary<long, (string? Name, long NameOrder)>();
        var ordinary = new HashSet<long>();
        using (var cmd = Cmd(db, tx, "SELECT pid, name, name_order, distinguished_kind, is_hidden, smart_criteria IS NULL, smart_is_folder, parent_pid FROM container"))
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                existing[r.GetInt64(0)] = (r.IsDBNull(1) ? null : r.GetString(1), r.IsDBNull(2) ? 0 : r.GetInt64(2));
                bool plain = !r.IsDBNull(3) && r.GetInt64(3) == 0 && !r.IsDBNull(4) && r.GetInt64(4) == 0 && r.GetInt64(5) == 1
                             && (r.IsDBNull(6) || r.GetInt64(6) == 0) && (r.IsDBNull(7) || r.GetInt64(7) == 0);
                if (plain) ordinary.Add(r.GetInt64(0));
            }

        var items = new HashSet<long>();
        using (var cmd = Cmd(db, tx, "SELECT pid FROM item"))
        using (var r = cmd.ExecuteReader())
            while (r.Read()) items.Add(r.GetInt64(0));

        var ui = new HashSet<long>();
        using (var cmd = Cmd(dyn, dtx, "SELECT container_pid FROM container_ui"))
        using (var r = cmd.ExecuteReader())
            while (r.Read()) ui.Add(r.GetInt64(0));

        var master = cdb.MasterPlaylist;
        if (master is null) { result.Problems.Add("CDB has no master playlist"); return result; }
        long masterPid = unchecked((long)master.PersistentId);
        if (!existing.ContainsKey(masterPid))
        {
            result.Problems.Add($"SQLite has no container for the CDB master playlist 0x{master.PersistentId:X16}; refusing to guess");
            return result;
        }

        var mirrored = cdb.Playlists.Where(ItlpCompare.IsMirroredPlaylist).ToList();
        var mirroredPids = mirrored.Select(p => unchecked((long)p.PersistentId)).ToHashSet();
        var unmirroredNames = cdb.Playlists
            .Where(p => !p.IsMaster && !ItlpCompare.IsMirroredPlaylist(p))
            .Select(p => p.Name ?? "").ToList();

        // ---- name_order: prove the rule on the device's own data before using it ----
        // Rule (derived from the real device): master = 100, then every other CDB
        // playlist (including built-in smart ones, which have no container row)
        // sorted case-insensitively, 100 apart. Check it reproduces every existing
        // container's name_order from the pre-edit names; refuse otherwise.
        var baselineNames = unmirroredNames
            .Concat(existing.Where(e => e.Key != masterPid).Select(e => e.Value.Name ?? ""));
        var baselineRanks = Ranks(baselineNames);
        foreach (var (pid, (name, order)) in existing)
        {
            long expect = pid == masterPid ? 100 : baselineRanks[name ?? ""];
            if (expect != order)
            {
                result.Problems.Add($"name_order rule does not reproduce the device: container '{name}' has {order}, rule gives {expect}");
            }
        }
        if (!result.Ok) return result;
        var newRanks = Ranks(unmirroredNames.Concat(mirrored.Select(p => p.Name!)));

        // ---- deletes: containers for user playlists no longer in the CDB ----
        foreach (var (pid, (name, _)) in existing)
        {
            if (pid == masterPid || mirroredPids.Contains(pid)) continue;
            if (!ordinary.Contains(pid))
            {
                result.Problems.Add($"container '{name}' is not in the CDB but is not an ordinary playlist (hidden/smart/folder/distinguished); refusing to delete it");
                continue;
            }
            Exec(db, tx, "DELETE FROM item_to_container WHERE container_pid = $p", ("$p", pid));
            Exec(db, tx, "DELETE FROM container_seed WHERE container_pid = $p", ("$p", pid));
            Exec(db, tx, "DELETE FROM container WHERE pid = $p", ("$p", pid));
            Exec(dyn, dtx, "DELETE FROM container_ui WHERE container_pid = $p", ("$p", pid));
            result.Actions.Add($"delete container '{name}' (0x{unchecked((ulong)pid):X16})");
            result.TouchedTables.UnionWith(["Library.itdb:container", "Library.itdb:item_to_container", "Library.itdb:container_seed", "Dynamic.itdb:container_ui"]);
        }

        // ---- creates / renames / name_order ----
        foreach (var p in mirrored)
        {
            long pid = unchecked((long)p.PersistentId);
            long rank = newRanks[p.Name!];
            if (!existing.TryGetValue(pid, out var cur))
            {
                Exec(db, tx, """
                    INSERT INTO container
                      (pid, distinguished_kind, date_created, date_modified, name, name_order,
                       parent_pid, media_kinds, workout_template_id, is_hidden, smart_is_folder)
                    VALUES ($p, 0, $now, $now, $name, $order, 0, 1, 0, 0, 0)
                    """, ("$p", pid), ("$now", stamp), ("$name", p.Name!), ("$order", rank));
                result.Actions.Add($"create container '{p.Name}' (0x{p.PersistentId:X16}) name_order {rank}");
                result.TouchedTables.Add("Library.itdb:container");
            }
            else if (cur.Name != p.Name)
            {
                Exec(db, tx, "UPDATE container SET name = $name, name_order = $order, date_modified = $now WHERE pid = $p",
                    ("$p", pid), ("$name", p.Name!), ("$order", rank), ("$now", stamp));
                result.Actions.Add($"rename container '{cur.Name}' -> '{p.Name}' name_order {cur.NameOrder} -> {rank}");
                result.TouchedTables.Add("Library.itdb:container");
            }
            else if (cur.NameOrder != rank)
            {
                Exec(db, tx, "UPDATE container SET name_order = $order WHERE pid = $p", ("$p", pid), ("$order", rank));
                result.Actions.Add($"re-rank container '{p.Name}' name_order {cur.NameOrder} -> {rank}");
                result.TouchedTables.Add("Library.itdb:container");
            }

            if (!ui.Contains(pid))
            {
                // Values every ordinary playlist's container_ui row carries on the device.
                Exec(dyn, dtx, """
                    INSERT INTO container_ui
                      (container_pid, play_order, is_reversed, album_field_order, repeat_mode, shuffle_items, has_been_shuffled)
                    VALUES ($p, 1, 0, 1, 0, 0, 0)
                    """, ("$p", pid));
                result.Actions.Add($"create container_ui row for '{p.Name}'");
                result.TouchedTables.Add("Dynamic.itdb:container_ui");
            }
        }

        // ---- membership (user playlists + master), only rewritten where it differs ----
        var idToPid = cdb.Tracks.ToDictionary(t => t.Id, t => unchecked((long)t.PersistentId));
        foreach (var p in mirrored.Append(master))
        {
            long pid = unchecked((long)p.PersistentId);
            var wanted = new List<long>();
            int deferred = 0;
            foreach (var id in p.TrackIds)
            {
                if (!idToPid.TryGetValue(id, out long itemPid)) { result.Problems.Add($"'{p.Name}' references missing CDB track {id}"); continue; }
                if (!items.Contains(itemPid)) { deferred++; continue; }
                wanted.Add(itemPid);
            }
            if (deferred > 0)
                result.Actions.Add($"'{p.Name}': {deferred} entr{(deferred == 1 ? "y" : "ies")} skipped - track not in Library.itdb yet (track mirror)");

            var have = ItlpCompare.Members(db, pid, tx);
            if (have.SequenceEqual(wanted)) continue;

            // Appending to the end (e.g. a new track in the master playlist): insert only
            // the new tail, leaving every existing row untouched.
            if (have.Count < wanted.Count && wanted.Take(have.Count).SequenceEqual(have))
            {
                for (int i = have.Count; i < wanted.Count; i++)
                    Exec(db, tx, "INSERT INTO item_to_container (item_pid, container_pid, physical_order, shuffle_order) VALUES ($i, $p, $o, NULL)",
                        ("$i", wanted[i]), ("$p", pid), ("$o", i));
                result.Actions.Add($"membership '{p.Name}': appended {wanted.Count - have.Count} entr{(wanted.Count - have.Count == 1 ? "y" : "ies")} ({have.Count} -> {wanted.Count})");
                result.TouchedTables.Add("Library.itdb:item_to_container");
                continue;
            }

            Exec(db, tx, "DELETE FROM item_to_container WHERE container_pid = $p", ("$p", pid));
            for (int i = 0; i < wanted.Count; i++)
            {
                // shuffle_order is NULL on every real row on this device.
                Exec(db, tx, "INSERT INTO item_to_container (item_pid, container_pid, physical_order, shuffle_order) VALUES ($i, $p, $o, NULL)",
                    ("$i", wanted[i]), ("$p", pid), ("$o", i));
            }
            result.Actions.Add($"membership '{p.Name}': {have.Count} -> {wanted.Count} entries");
            result.TouchedTables.Add("Library.itdb:item_to_container");
        }

        if (!result.Ok) { tx.Rollback(); dtx.Rollback(); return result; }
        tx.Commit();
        dtx.Commit();
        return result;
    }

    // ------------------------------------------------------------------ verification

    /// <summary>SHA-256 of every table's full content (all columns, ordered by rowid or
    /// primary key), keyed "file:table". Used to prove a sync touched only the tables
    /// it declared.</summary>
    public static Dictionary<string, string> TableHashes(string itlpDir)
    {
        var hashes = new Dictionary<string, string>();
        foreach (var f in BundleFiles.Where(f => f.EndsWith(".itdb")))
        {
            string path = Path.Combine(itlpDir, f);
            if (!File.Exists(path)) continue;
            using var c = ItlpCompare.OpenReadOnly(path);
            var tables = new List<string>();
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name";
                using var r = cmd.ExecuteReader();
                while (r.Read()) tables.Add(r.GetString(0));
            }
            foreach (var t in tables)
            {
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                using var cmd = c.CreateCommand();
                // Every table in these bundles is a rowid table; rowid order is stable for
                // rows nobody touched, which is exactly what this hash is used to prove.
                cmd.CommandText = $"SELECT * FROM \"{t}\" ORDER BY rowid";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    for (int i = 0; i < r.FieldCount; i++)
                    {
                        object v = r.GetValue(i);
                        byte[] b = v switch
                        {
                            DBNull => [0xFF],
                            byte[] blob => [0xB0, .. blob],
                            _ => System.Text.Encoding.UTF8.GetBytes(Convert.ToString(v, CultureInfo.InvariantCulture) + ""),
                        };
                        sha.AppendData(b);
                    }
                    sha.AppendData([0x0A]);
                }
                hashes[$"{f}:{t}"] = Convert.ToHexString(sha.GetHashAndReset());
            }
        }
        return hashes;
    }

    /// <summary>PRAGMA integrity_check on every staged .itdb; returns problems.</summary>
    public static List<string> IntegrityCheck(string itlpDir)
    {
        var problems = new List<string>();
        foreach (var f in BundleFiles.Where(f => f.EndsWith(".itdb")))
        {
            string path = Path.Combine(itlpDir, f);
            if (!File.Exists(path)) continue;
            using var c = ItlpCompare.OpenReadOnly(path);
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check";
            string res = Convert.ToString(cmd.ExecuteScalar()) ?? "";
            if (res != "ok") problems.Add($"{f}: integrity_check -> {res}");
            // Journal mode must still be rollback-journal and the header must still say
            // legacy (non-WAL) read/write versions -- the firmware's SQLite predates WAL.
            byte[] header = new byte[100];
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) fs.ReadExactly(header);
            if (header[18] != 1 || header[19] != 1) problems.Add($"{f}: header read/write version is {header[18]}/{header[19]}, expected 1/1 (non-WAL)");
            foreach (var side in new[] { "-journal", "-wal", "-shm" })
                if (File.Exists(path + side)) problems.Add($"{f}: leftover {side} file");
        }
        return problems;
    }

    public static string Sha1(string path) => Convert.ToHexString(SHA1.HashData(File.ReadAllBytes(path)));

    // ------------------------------------------------------------------ helpers

    private static Dictionary<string, long> Ranks(IEnumerable<string> names)
    {
        var sorted = names.Distinct().OrderBy(n => n, StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: true)).ToList();
        var ranks = new Dictionary<string, long>();
        for (int i = 0; i < sorted.Count; i++) ranks[sorted[i]] = 100L * (i + 2); // master holds 100
        return ranks;
    }

    private static SqliteConnection OpenReadWrite(string path)
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());
        c.Open();
        return c;
    }

    private static SqliteCommand Cmd(SqliteConnection c, SqliteTransaction tx, string sql)
    {
        var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        return cmd;
    }

    private static void Exec(SqliteConnection c, SqliteTransaction tx, string sql, params (string Name, object Value)[] args)
    {
        using var cmd = Cmd(c, tx, sql);
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    public static int AppleSeconds(DateTimeOffset when) =>
        (int)(when - new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero)).TotalSeconds;
}
