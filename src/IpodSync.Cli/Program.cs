using System.Text.Json;
using IpodSync.Core.Device;
using IpodSync.Core.ItunesDb;
using IpodSync.Core.Itlp;

string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "detect";

try
{
    switch (cmd)
    {
        case "detect":         return Detect();
        case "dump":           return Dump(args.Skip(1).ToArray());
        case "roundtrip":      return RoundTripCmd(args.Skip(1).ToArray());
        case "mutate-test":    return MutateTestCmd(args.Skip(1).ToArray());
        case "resize-test":    return ResizeTestCmd(args.Skip(1).ToArray());
        case "playlist-test":  return PlaylistTestCmd(args.Skip(1).ToArray());
        case "addtrack-test":  return AddTrackTestCmd(args.Skip(1).ToArray());
        case "pl-inspect":     return PlInspectCmd(args.Skip(1).ToArray());
        case "pid-refs":       return PlaylistPidRefsCmd(args.Skip(1).ToArray());
        case "itlp-sync":      return ItlpSyncCmd(args.Skip(1).ToArray());
        case "itlp-diff":      return ItlpDiffCmd(args.Skip(1).ToArray());
        case "apply-edits":    return ApplyEditsCmd(args.Skip(1).ToArray());
        default:
            Console.Error.WriteLine($"Unknown command '{cmd}'.");
            Usage();
            return 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

static void Usage()
{
    Console.WriteLine("""
        ipodsync - read an iPod without iTunes

          detect              find connected iPods
          dump [path] [-n N]  dump database (path = iPod drive, or an iTunesDB file)
                              -n limits how many tracks are printed (default 25, 0 = all)
          roundtrip [path]    read a database, rebuild it, and diff against the original bytes.
                              Read-only -- never writes to the device. This is the writer's
                              safety gate: it must pass before anything is allowed to write.
          mutate-test [path]  bump one track's play count and star rating, rebuild the database,
                              and prove nothing else in the file changed. Read-only -- runs
                              entirely in memory, never writes to the device. This is the gate
                              before any real write is attempted.
          resize-test [path]  remove a track, add an existing track to a playlist, and rename a
                              track -- three edits that change a chunk's byte length, unlike
                              mutate-test. Read-only, entirely in memory.
          itlp-diff <root|itunes-dir> [--all]
                              compare iTunesCDB against the SQLite bundle (iTunes Library.itlp).
                              Read-only.
          itlp-sync <root> [--yes]
                              bring the SQLite bundle's playlists into line with the CDB.
                              Dry run by default (works on a staged copy); --yes backs up,
                              writes, re-verifies from the device, restores on failure.
          apply-edits [path] --changes <file.json> [--yes] [--backup-root dir]
                              apply a JSON change-set (see EDIT-PROTOCOL.md) from the iPod Player.
                              Default is a DRY RUN: applies in memory, runs the reader re-parse
                              and idempotent-write checks, prints what would change, writes NOTHING.
                              --yes performs the real device write -- but only if every check
                              passed, and it backs up iPod_Control/iTunes/ first. Mirrors every
                              edit into the SQLite bundle in the same operation.
        """);
}

static int Detect()
{
    var devices = IpodDevice.Detect();
    if (devices.Count == 0)
    {
        Console.WriteLine("No iPods found.");
        Console.WriteLine();
        Console.WriteLine("If one is plugged in, check that disk mode is enabled:");
        Console.WriteLine("  iTunes > device > Summary > Options > Enable disk use");
        return 1;
    }

    foreach (var d in devices)
    {
        Console.WriteLine($"{d.RootPath}  {d.VolumeLabel}");
        Console.WriteLine($"  filesystem   {d.FileSystem}{(d.IsFat32 ? "" : "   <-- not FAT32: unreadable from Android")}");
        if (d.TotalBytes > 0)
            Console.WriteLine($"  capacity     {Gb(d.TotalBytes)} total, {Gb(d.FreeBytes)} free");
        if (d.ModelNumber is not null) Console.WriteLine($"  model        {d.ModelNumber}");
        if (d.Serial is not null) Console.WriteLine($"  serial       {d.Serial}");
        if (d.FirewireGuid is not null) Console.WriteLine($"  firewire id  {d.FirewireGuid}");
        Console.WriteLine($"  database     {(d.HasDatabase ? d.ItunesDbPath : "none")}");
        Console.WriteLine($"  sysinfoext   {(d.HasSysInfoExtended ? "present" : "absent")}");
        Console.WriteLine();
    }
    return 0;
}

static int Dump(string[] rest)
{
    int limit = 25;
    string? path = null;

    for (int i = 0; i < rest.Length; i++)
    {
        if (rest[i] == "-n" && i + 1 < rest.Length) limit = int.Parse(rest[++i]);
        else path ??= rest[i];
    }

    if (path is null)
    {
        var devices = IpodDevice.Detect();
        if (devices.Count == 0) { Console.Error.WriteLine("No iPod found. Pass a path explicitly."); return 1; }
        path = devices[0].RootPath;
        Console.WriteLine($"Using {path}");
    }

    // Accept either the iPod root or a direct path to an iTunesDB file.
    string dbPath = Directory.Exists(path) ? IpodDevice.Open(path).ItunesDbPath : path;
    if (!File.Exists(dbPath)) { Console.Error.WriteLine($"No iTunesDB at {dbPath}"); return 1; }

    var db = ItunesDbReader.Read(dbPath);

    Console.WriteLine();
    Console.WriteLine($"database version   {db.Version}");
    Console.WriteLine($"library id         0x{db.LibraryPersistentId:X16}");
    Console.WriteLine($"tracks             {db.Tracks.Count}");
    Console.WriteLine($"playlists          {db.Playlists.Count(p => !p.IsMaster)} (+1 master)");

    if (db.UnknownChunks.Count > 0)
        Console.WriteLine($"undecoded chunks   {string.Join(", ", db.UnknownChunks.OrderBy(x => x))}");

    Console.WriteLine();
    Console.WriteLine("PLAYLISTS");
    foreach (var p in db.Playlists.OrderByDescending(p => p.IsMaster).ThenBy(p => p.Name))
    {
        string tags = (p.IsMaster ? " [master]" : "") + (p.IsSmart ? " [smart]" : "") + (p.IsPodcast ? " [podcast]" : "");
        Console.WriteLine($"  {p.TrackIds.Count,6}  {p.Name ?? "(unnamed)"}{tags}");
    }

    var shown = limit == 0 ? db.Tracks : db.Tracks.Take(limit).ToList();
    Console.WriteLine();
    Console.WriteLine($"TRACKS ({shown.Count} of {db.Tracks.Count})");
    foreach (var t in shown)
    {
        string stars = t.Stars > 0 ? new string('*', t.Stars) : "";
        Console.WriteLine($"  {t.Duration:mm\\:ss}  {t.Artist ?? "?"} - {t.Title ?? "?"}");
        Console.WriteLine($"           {t.Album ?? "?"}  |  {t.Bitrate}kbps  |  plays {t.PlayCount}  {stars}");
        Console.WriteLine($"           {t.RelativePath ?? "(no location)"}  #{t.Id} pid 0x{t.PersistentId:X16}");
    }

    return 0;
}

static int RoundTripCmd(string[] rest)
{
    string? outPath = null;
    string? path = null;
    for (int i = 0; i < rest.Length; i++)
    {
        if (rest[i] == "-o" && i + 1 < rest.Length) outPath = rest[++i];
        else path ??= rest[i];
    }

    if (path is null)
    {
        var devices = IpodDevice.Detect();
        if (devices.Count == 0) { Console.Error.WriteLine("No iPod found. Pass a path explicitly."); return 1; }
        path = devices[0].RootPath;
        Console.WriteLine($"Using {path}");
    }

    string dbPath = Directory.Exists(path) ? IpodDevice.Open(path).ItunesDbPath : path;
    if (!File.Exists(dbPath)) { Console.Error.WriteLine($"No iTunesDB at {dbPath}"); return 1; }

    byte[] original = File.ReadAllBytes(dbPath);
    var result = RoundTrip.Run(original);

    Console.WriteLine();
    Console.WriteLine($"file                {dbPath}");
    Console.WriteLine($"on disk             {(result.WasCompressed ? "iTunesCDB (zlib-compressed)" : "iTunesDB (plain)")}, {result.OriginalCompressedLength:N0} bytes");
    Console.WriteLine($"inflated length     original {result.OriginalInflatedLength:N0}  reconstructed {result.ReconstructedLength:N0}");
    if (result.UnknownMagics.Count > 0)
        Console.WriteLine($"opaque chunk types  {string.Join(", ", result.UnknownMagics.OrderBy(x => x))}  (preserved verbatim, not decoded)");

    Console.WriteLine();
    if (result.StructureMatches)
    {
        Console.WriteLine("STRUCTURE: byte-identical  -- PASS");
    }
    else
    {
        Console.WriteLine($"STRUCTURE: mismatch, first differing byte at 0x{result.FirstDiffOffset:X}  -- FAIL");
        Console.WriteLine(result.DiffContext);
    }

    if (result.CompressionRoundTripOk is bool ok)
    {
        Console.WriteLine(ok
            ? "compression         our re-deflated bytes reinflate back to the same content"
            : "compression         FAILED to reinflate back to the same content");
        Console.WriteLine("                    (compressed bytes are not expected to match Apple's zlib output byte for byte -- only the inflated content is)");
    }

    if (outPath is not null)
    {
        File.WriteAllBytes(outPath, result.ReconstructedInflated);
        Console.WriteLine();
        Console.WriteLine($"wrote reconstructed inflated bytes to {outPath}");
    }

    return result.StructureMatches ? 0 : 1;
}

static int MutateTestCmd(string[] rest)
{
    string? path = rest.FirstOrDefault();
    if (path is null)
    {
        var devices = IpodDevice.Detect();
        if (devices.Count == 0) { Console.Error.WriteLine("No iPod found. Pass a path explicitly."); return 1; }
        path = devices[0].RootPath;
        Console.WriteLine($"Using {path}");
    }

    string dbPath = Directory.Exists(path) ? IpodDevice.Open(path).ItunesDbPath : path;
    if (!File.Exists(dbPath)) { Console.Error.WriteLine($"No iTunesDB at {dbPath}"); return 1; }

    byte[] original = File.ReadAllBytes(dbPath);
    var result = MutationRoundTrip.Run(original);

    Console.WriteLine();
    Console.WriteLine($"file                {dbPath}");
    Console.WriteLine($"mutated track       id {result.MutatedTrackId}");
    Console.WriteLine($"  play count        {result.OldPlayCount} -> {result.NewPlayCount}");
    Console.WriteLine($"  stars             {result.OldStars} -> {result.NewStars}");
    Console.WriteLine();
    Console.WriteLine($"semantic check      {(result.SemanticMatch ? "every other track and playlist unchanged -- PASS" : "FAIL")}");
    Console.WriteLine($"byte containment    {(result.BytesContained ? "every changed byte is inside the mutated track's chunk -- PASS" : "FAIL")}");

    if (result.Problems.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("problems:");
        foreach (var p in result.Problems) Console.WriteLine($"  - {p}");
    }

    Console.WriteLine();
    Console.WriteLine(result.Passed ? "MUTATION ROUND-TRIP: PASS" : "MUTATION ROUND-TRIP: FAIL");
    return result.Passed ? 0 : 1;
}

static int ResizeTestCmd(string[] rest)
{
    string? path = rest.FirstOrDefault();
    if (path is null)
    {
        var devices = IpodDevice.Detect();
        if (devices.Count == 0) { Console.Error.WriteLine("No iPod found. Pass a path explicitly."); return 1; }
        path = devices[0].RootPath;
        Console.WriteLine($"Using {path}");
    }

    string dbPath = Directory.Exists(path) ? IpodDevice.Open(path).ItunesDbPath : path;
    if (!File.Exists(dbPath)) { Console.Error.WriteLine($"No iTunesDB at {dbPath}"); return 1; }

    byte[] original = File.ReadAllBytes(dbPath);
    Console.WriteLine();
    Console.WriteLine($"file  {dbPath}");

    var results = new[]
    {
        ResizeRoundTrip.RemoveTrack(original),
        ResizeRoundTrip.AddTrackToPlaylist(original),
        ResizeRoundTrip.RenameTrack(original),
    };

    bool allPassed = true;
    foreach (var r in results)
    {
        Console.WriteLine();
        Console.WriteLine($"{r.Operation}");
        Console.WriteLine($"  {r.Detail}");
        Console.WriteLine($"  semantic check    {(r.SemanticOk ? "PASS" : "FAIL")}");
        Console.WriteLine($"  idempotent check  {(r.IdempotentOk ? "PASS" : "FAIL")}");
        foreach (var p in r.Problems) Console.WriteLine($"    - {p}");
        allPassed &= r.Passed;
    }

    Console.WriteLine();
    Console.WriteLine(allPassed ? "RESIZE ROUND-TRIP: PASS (all three)" : "RESIZE ROUND-TRIP: FAIL");
    return allPassed ? 0 : 1;
}

static int PlaylistTestCmd(string[] rest)
{
    string? path = rest.FirstOrDefault();
    if (path is null)
    {
        var devices = IpodDevice.Detect();
        if (devices.Count == 0) { Console.Error.WriteLine("No iPod found. Pass a path explicitly."); return 1; }
        path = devices[0].RootPath;
        Console.WriteLine($"Using {path}");
    }
    string dbPath = Directory.Exists(path) ? IpodDevice.Open(path).ItunesDbPath : path;
    if (!File.Exists(dbPath)) { Console.Error.WriteLine($"No iTunesDB at {dbPath}"); return 1; }

    byte[] original = File.ReadAllBytes(dbPath);
    Console.WriteLine();
    Console.WriteLine($"file  {dbPath}");

    var results = new[]
    {
        PlaylistRoundTrip.Rename(original),
        PlaylistRoundTrip.Reorder(original),
        PlaylistRoundTrip.Delete(original),
        PlaylistRoundTrip.Create(original),
    };

    bool allPassed = true;
    foreach (var r in results)
    {
        Console.WriteLine();
        Console.WriteLine($"{r.Operation}");
        Console.WriteLine($"  {r.Detail}");
        Console.WriteLine($"  semantic check    {(r.SemanticOk ? "PASS" : "FAIL")}");
        Console.WriteLine($"  idempotent check  {(r.IdempotentOk ? "PASS" : "FAIL")}");
        foreach (var p in r.Problems) Console.WriteLine($"    - {p}");
        allPassed &= r.Passed;
    }

    Console.WriteLine();
    Console.WriteLine(allPassed ? "PLAYLIST ROUND-TRIP: PASS (all four)" : "PLAYLIST ROUND-TRIP: FAIL");
    return allPassed ? 0 : 1;
}

static int PlInspectCmd(string[] rest)
{
    string? path = rest.ElementAtOrDefault(0);
    string? filter = rest.ElementAtOrDefault(1);
    if (path is null) { Console.Error.WriteLine("usage: pl-inspect <db-or-ipod> [name-filter]"); return 2; }
    string dbPath = Directory.Exists(path) ? IpodDevice.Open(path).ItunesDbPath : path;
    if (!File.Exists(dbPath)) { Console.Error.WriteLine($"No iTunesDB at {dbPath}"); return 1; }

    var root = RawChunkParser.ParseDatabase(File.ReadAllBytes(dbPath));
    int i32(byte[] h, int o) => o + 4 <= h.Length ? BitConverter.ToInt32(h, o) : -1;

    int ds = 0;
    foreach (var mhsd in root.Children.Where(c => c.Magic == "mhsd"))
    {
        var mhlp = mhsd.Children.FirstOrDefault(c => c.Magic == "mhlp");
        if (mhlp is null) continue;
        ds++;
        foreach (var pl in mhlp.Children.Where(c => c.Magic == "mhyp"))
        {
            // name from the first type-1 mhod
            string name = "(none)";
            foreach (var c in pl.Children.Where(c => c.Magic == "mhod" && i32(c.Header, 0x0C) == 1))
            {
                int len = c.Payload.Length >= 8 ? BitConverter.ToInt32(c.Payload, 4) : 0;
                if (len > 0 && 16 + len <= c.Payload.Length)
                    name = (len % 2 == 0 ? System.Text.Encoding.Unicode : System.Text.Encoding.UTF8).GetString(c.Payload, 16, len);
                break;
            }
            if (filter != null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;

            var mhodTypes = pl.Children.Where(c => c.Magic == "mhod")
                .Select(c => $"{i32(c.Header, 0x0C)}(hdr{c.Header.Length}/pay{c.Payload.Length})");
            int mhips = pl.Children.Count(c => c.Magic == "mhip");
            Console.WriteLine($"[ds{ds}] '{name}'  master={i32(pl.Header, 0x14)}  numItems={i32(pl.Header, 0x10)}  mhips={mhips}  mhods=[{string.Join(" ", mhodTypes)}]");
            if (filter != null)
            {
                ulong u64(int o) => o + 8 <= pl.Header.Length ? BitConverter.ToUInt64(pl.Header, o) : 0;
                Console.WriteLine($"        pid@0x1C=0x{u64(0x1C):X16}  val@0x40=0x{u64(0x40):X16}  const@0x38=0x{u64(0x38):X16}");
                var firstItem = pl.Children.FirstOrDefault(c => c.Magic == "mhip");
                if (firstItem is not null)
                    Console.WriteLine($"        first-mhip hdr={BitConverter.ToString(firstItem.Header)} payload={BitConverter.ToString(firstItem.Payload)}");
            }
        }
    }
    return 0;
}

/// <summary>Diagnostic only: finds every occurrence of a playlist's persistent
/// id across the lossless raw tree. Used to discover secondary playlist indexes
/// that the firmware may rely on but the semantic reader deliberately ignores.</summary>
static int PlaylistPidRefsCmd(string[] rest)
{
    if (rest.Length < 2) { Console.Error.WriteLine("usage: pid-refs <db-or-ipod> <playlist-name>"); return 2; }
    string path = rest[0], name = rest[1];
    string dbPath = Directory.Exists(path) ? IpodDevice.Open(path).ItunesDbPath : path;
    if (!File.Exists(dbPath)) { Console.Error.WriteLine($"No iTunesDB at {dbPath}"); return 1; }

    var root = RawChunkParser.ParseDatabase(File.ReadAllBytes(dbPath));
    string? PlaylistName(RawChunk p)
    {
        foreach (var c in p.Children.Where(c => c.Magic == "mhod" && c.Header.Length >= 0x10 && BitConverter.ToInt32(c.Header, 0x0C) == 1))
        {
            int len = c.Payload.Length >= 8 ? BitConverter.ToInt32(c.Payload, 4) : 0;
            if (len > 0 && 16 + len <= c.Payload.Length)
                return (len % 2 == 0 ? System.Text.Encoding.Unicode : System.Text.Encoding.UTF8).GetString(c.Payload, 16, len);
        }
        return null;
    }
    var target = RawChunkNavigation.AllPlaylists(root).FirstOrDefault(p => string.Equals(PlaylistName(p), name, StringComparison.OrdinalIgnoreCase));
    if (target is null) { Console.Error.WriteLine($"Playlist '{name}' not found."); return 1; }
    ulong pid = BitConverter.ToUInt64(target.Header, 0x1C);
    byte[] needle = BitConverter.GetBytes(pid);
    Console.WriteLine($"playlist '{name}', pid 0x{pid:X16}");

    int hits = 0;
    void Scan(byte[] bytes, string label)
    {
        for (int i = 0; i <= bytes.Length - needle.Length; i++)
            if (bytes.AsSpan(i, needle.Length).SequenceEqual(needle))
            { Console.WriteLine($"  {label}+0x{i:X}"); hits++; }
    }
    void Walk(RawChunk n, string pathLabel)
    {
        Scan(n.Header, $"{pathLabel}/{n.Magic}.header");
        Scan(n.Payload, $"{pathLabel}/{n.Magic}.payload");
        for (int i = 0; i < n.Children.Count; i++) Walk(n.Children[i], $"{pathLabel}/{n.Magic}[{i}]");
    }
    Walk(root, "root");
    Console.WriteLine($"hits {hits}");
    return 0;
}

static int ItlpSyncCmd(string[] rest)
{
    string? path = null, backupRoot = null;
    bool commit = false;
    for (int i = 0; i < rest.Length; i++)
    {
        if (rest[i] == "--yes") commit = true;
        else if (rest[i] == "--backup-root" && i + 1 < rest.Length) backupRoot = rest[++i];
        else path ??= rest[i];
    }
    if (path is null) { Console.Error.WriteLine("usage: itlp-sync <ipod-root> [--yes] [--backup-root dir]"); return 2; }
    return WritePipeline.Run(new WritePipeline.Options
    {
        Root = path, Commit = commit, Label = "itlpsync",
        BackupRoot = backupRoot ?? Path.Combine(Directory.GetCurrentDirectory(), "ipod-backups"),
    });
}

// Resolves an iPod root, or an iTunes directory (e.g. a backup copy holding
// iTunesCDB + "iTunes Library.itlp"), to its CDB file and itlp directory.
static (string Cdb, string Itlp) ResolveItunesDir(string path)
{
    string itunesDir = Directory.Exists(Path.Combine(path, "iPod_Control"))
        ? Path.GetDirectoryName(IpodDevice.Open(path).ItunesDbPath)!
        : path;
    string cdb = File.Exists(Path.Combine(itunesDir, "iTunesDB"))
        ? Path.Combine(itunesDir, "iTunesDB")
        : Path.Combine(itunesDir, "iTunesCDB");
    return (cdb, Path.Combine(itunesDir, "iTunes Library.itlp"));
}

static int ItlpDiffCmd(string[] rest)
{
    string? path = rest.FirstOrDefault(a => !a.StartsWith("--"));
    bool verbose = rest.Contains("--all");
    if (path is null) { Console.Error.WriteLine("usage: itlp-diff <ipod-root | itunes-dir> [--all]"); return 2; }
    var (cdbPath, itlp) = ResolveItunesDir(path);
    var cdb = ItunesDbReader.Read(File.ReadAllBytes(cdbPath));
    var diff = ItlpCompare.Compare(itlp, cdb);
    Console.WriteLine($"CDB     {cdbPath}  ({cdb.Tracks.Count} tracks, {cdb.Playlists.Count} playlists incl. master/smart)");
    Console.WriteLine($"SQLite  {itlp}");
    foreach (var (section, lines) in diff.Sections())
    {
        Console.WriteLine($"{section,-44} {lines.Count}");
        foreach (var l in verbose ? lines : lines.Take(15)) Console.WriteLine("    " + l);
        if (!verbose && lines.Count > 15) Console.WriteLine($"    ... {lines.Count - 15} more (--all)");
    }
    foreach (var n in diff.Notes) Console.WriteLine("note: " + n);
    Console.WriteLine(diff.InSync ? "IN SYNC" : $"OUT OF SYNC (playlists {(diff.PlaylistsInSync ? "in sync" : "differ")}, tracks {(diff.TracksInSync ? "in sync" : "differ")})");
    return diff.InSync ? 0 : 3;
}

static int AddTrackTestCmd(string[] rest)
{
    if (rest.Length < 2) { Console.Error.WriteLine("usage: addtrack-test <db-or-ipod> <audiofile>"); return 2; }
    string path = rest[0], audio = rest[1];
    string dbPath = Directory.Exists(path) ? IpodDevice.Open(path).ItunesDbPath : path;
    if (!File.Exists(dbPath)) { Console.Error.WriteLine($"No iTunesDB at {dbPath}"); return 1; }
    if (!File.Exists(audio)) { Console.Error.WriteLine($"No audio file at {audio}"); return 1; }

    var r = AddTrackRoundTrip.Run(File.ReadAllBytes(dbPath), audio);
    Console.WriteLine();
    Console.WriteLine($"file  {dbPath}");
    Console.WriteLine($"audio {audio}");
    Console.WriteLine();
    Console.WriteLine(r.Operation);
    Console.WriteLine($"  {r.Detail}");
    Console.WriteLine($"  semantic check    {(r.SemanticOk ? "PASS" : "FAIL")}");
    Console.WriteLine($"  idempotent check  {(r.IdempotentOk ? "PASS" : "FAIL")}");
    foreach (var p in r.Problems) Console.WriteLine($"    - {p}");
    Console.WriteLine();
    Console.WriteLine(r.Passed ? "ADD-TRACK ROUND-TRIP: PASS" : "ADD-TRACK ROUND-TRIP: FAIL");
    return r.Passed ? 0 : 1;
}

static int ApplyEditsCmd(string[] rest)
{
    string? path = null, changesPath = null, backupRoot = null;
    bool commit = false;
    for (int i = 0; i < rest.Length; i++)
    {
        string a = rest[i];
        if (a == "--changes" && i + 1 < rest.Length) changesPath = rest[++i];
        else if (a == "--backup-root" && i + 1 < rest.Length) backupRoot = rest[++i];
        else if (a is "--yes" or "--commit") commit = true;
        else if (a == "--dry-run") commit = false;
        else path ??= a;
    }

    if (changesPath is null) { Console.Error.WriteLine("apply-edits needs --changes <file.json>"); return 2; }
    if (!File.Exists(changesPath)) { Console.Error.WriteLine($"change-set not found: {changesPath}"); return 1; }

    if (path is null)
    {
        var devices = IpodDevice.Detect();
        if (devices.Count == 0) { Console.Error.WriteLine("No iPod found. Pass a path explicitly."); return 1; }
        path = devices[0].RootPath;
        Console.WriteLine($"Using {path}");
    }

    string dbPath = Directory.Exists(path) ? IpodDevice.Open(path).ItunesDbPath : path;
    if (!File.Exists(dbPath)) { Console.Error.WriteLine($"No iTunesDB at {dbPath}"); return 1; }

    ChangeSet? cs;
    try
    {
        cs = JsonSerializer.Deserialize<ChangeSet>(File.ReadAllText(changesPath), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
    }
    catch (JsonException ex) { Console.Error.WriteLine($"invalid change-set JSON: {ex.Message}"); return 1; }

    if (cs?.Ops is null || cs.Ops.Count == 0) { Console.Error.WriteLine("change-set has no ops."); return 1; }

    return WritePipeline.Run(new WritePipeline.Options
    {
        Root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(dbPath)))!,
        Changes = cs, Commit = commit, Label = "applyedits",
        BackupRoot = backupRoot ?? Path.Combine(Directory.GetCurrentDirectory(), "ipod-backups"),
    });
}

static void CopyDir(string src, string dst)
{
    Directory.CreateDirectory(dst);
    foreach (var file in Directory.EnumerateFiles(src))
        File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite: true);
    foreach (var dir in Directory.EnumerateDirectories(src))
        CopyDir(dir, Path.Combine(dst, Path.GetFileName(dir)));
}

static string Gb(long bytes) => $"{bytes / 1024.0 / 1024 / 1024:F1} GB";
