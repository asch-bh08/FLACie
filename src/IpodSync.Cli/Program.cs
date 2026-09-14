using System.Text.Json;
using IpodSync.Core.Device;
using IpodSync.Core.ItunesDb;
using IpodSync.Core.Itlp;
using IpodSync.Core.LocalLibrary;

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
        case "hash72-verify":  return Hash72VerifyCmd(args.Skip(1).ToArray());
        case "hash58-verify":  return Hash58VerifyCmd(args.Skip(1).ToArray());
        case "itlp-orders-check": return ItlpOrdersCheckCmd(args.Skip(1).ToArray());
        case "art-check":      return ArtCheckCmd(args.Skip(1).ToArray());
        case "sync-folder":    return SyncFolderCmd(args.Skip(1).ToArray());
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
          itlp-sync <root> [--yes] [--resign]
                              bring the SQLite bundle's playlists into line with the CDB.
                              Dry run by default (works on a staged copy); --yes backs up,
                              writes, re-verifies from the device, restores on failure.
                              --resign re-signs the CDB (hash72 + hash58) even if unchanged.
          hash72-verify / hash58-verify   read-only signature checks (see OVERNIGHT-STATUS.md)
        Writes to signed databases (nano 5G) are signed automatically once the device's key
        material is proven against an existing iTunes signature; --firewire-guid,
        --signing-reference and --allow-unsigned override discovery.
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
    string? path = rest.FirstOrDefault(a => !a.StartsWith("--") && !IsOptionValue(rest, a));
    if (path is null) { Console.Error.WriteLine("usage: itlp-sync <ipod-root> [--yes] [--resign] [--backup-root dir] [--firewire-guid hex] [--signing-reference file] [--allow-unsigned]"); return 2; }
    return WritePipeline.Run(PipelineOptions(path, rest, null, "itlpsync"));
}

static bool IsOptionValue(string[] rest, string a)
{
    int i = Array.IndexOf(rest, a);
    return i > 0 && rest[i - 1] is "--backup-root" or "--firewire-guid" or "--signing-reference" or "--changes";
}

static WritePipeline.Options PipelineOptions(string root, string[] rest, ChangeSet? changes, string label)
{
    string? backupRoot = null;
    var fw = new List<string>();
    var refs = new List<string>();
    for (int i = 0; i < rest.Length - 1; i++)
    {
        if (rest[i] == "--backup-root") backupRoot = rest[i + 1];
        else if (rest[i] == "--firewire-guid") fw.Add(rest[i + 1]);
        else if (rest[i] == "--signing-reference") refs.Add(rest[i + 1]);
    }
    backupRoot ??= Path.Combine(Directory.GetCurrentDirectory(), "ipod-backups");
    return new WritePipeline.Options
    {
        Root = root, Changes = changes, Label = label, BackupRoot = backupRoot,
        Commit = rest.Contains("--yes") || rest.Contains("--commit"),
        ResignCdb = rest.Contains("--resign"),
        AllowUnsigned = rest.Contains("--allow-unsigned"),
        FirewireCandidates = SigningInputs.FirewireCandidates(fw),
        SigningReferences = SigningInputs.References(backupRoot, refs),
    };
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

// Read-only: validates the hash72 implementation against signatures iTunes wrote.
static int Hash72VerifyCmd(string[] rest)
{
    if (rest.Length == 0) { Console.Error.WriteLine("usage: hash72-verify <ipod-root | itunes-dir> [more itunes-dirs or db files...]"); return 2; }
    var keys = new List<(string Source, IpodSync.Core.Signing.Hash72.DeviceKey Key)>();
    int bad = 0;
    foreach (var arg in rest)
    {
        if (File.Exists(arg))
        {
            var k = IpodSync.Core.Signing.Hash72.ExtractFromDatabase(File.ReadAllBytes(arg));
            Console.WriteLine($"{arg}: header hash72 {(k is null ? "does NOT validate for this content" : "valid")}");
            if (k is not null) keys.Add((arg, k));
            continue;
        }
        var (cdbPath, itlp) = ResolveItunesDir(arg);
        if (File.Exists(cdbPath))
        {
            var k = IpodSync.Core.Signing.Hash72.ExtractFromDatabase(File.ReadAllBytes(cdbPath));
            Console.WriteLine($"{cdbPath}: header hash72 {(k is null ? "does NOT validate for this content" : "valid")}");
            if (k is not null) keys.Add((cdbPath, k));
        }
        string loc = Path.Combine(itlp, "Locations.itdb"), cbkPath = loc + ".cbk";
        if (File.Exists(loc) && File.Exists(cbkPath))
        {
            byte[] locations = File.ReadAllBytes(loc), cbk = File.ReadAllBytes(cbkPath);
            var (k, problems) = IpodSync.Core.Signing.Hash72.VerifyCbk(locations, cbk);
            Console.WriteLine($"{cbkPath}: {(problems.Count == 0 ? "checksums + signature valid" : string.Join("; ", problems))}");
            if (k is not null)
            {
                keys.Add((cbkPath, k));
                bool regen = IpodSync.Core.Signing.Hash72.BuildCbk(locations, k).AsSpan().SequenceEqual(cbk);
                Console.WriteLine($"  regenerate cbk from Locations.itdb + recovered key: {(regen ? "byte-identical PASS" : "DIFFERS FAIL")}");
                if (!regen) bad++;
            }
            bad += problems.Count;
        }
    }
    for (int i = 1; i < keys.Count; i++)
    {
        bool same = keys[i].Key.SameAs(keys[0].Key);
        Console.WriteLine($"device key from {Path.GetFileName(keys[i].Source)} == key from {Path.GetFileName(keys[0].Source)}: {(same ? "yes" : "NO")}");
    }
    if (keys.Count > 0) Console.WriteLine($"device key iv {Convert.ToHexString(keys[0].Key.Iv)[..8]}.. (from {keys[0].Source})");
    return bad == 0 && keys.Count > 0 ? 0 : 1;
}

// Read-only: checks the hash58 implementation against a database iTunes signed.
static int Hash58VerifyCmd(string[] rest)
{
    if (rest.Length < 2) { Console.Error.WriteLine("usage: hash58-verify <iTunesCDB|iTunesDB file> <FirewireGuid hex>"); return 2; }
    Console.WriteLine($"S-box self-test     {(IpodSync.Core.Signing.Hash58.SelfTest() ? "PASS" : "FAIL")}");
    byte[] file = File.ReadAllBytes(rest[0]);
    byte[] fw = IpodSync.Core.Signing.Hash58.ParseFirewireGuid(rest[1]);
    bool ok = IpodSync.Core.Signing.Hash58.Verify(fw, file);
    Console.WriteLine($"stored hash58       {Convert.ToHexString(file.AsSpan(0x58, 20))}");
    Console.WriteLine($"computed (file, db id/0x32/hash58 zeroed): {(ok ? "MATCH" : "no match")}");
    if (!ok)
    {
        // Research variants, reported but never used for signing.
        byte[] z = IpodSync.Core.Signing.Hash58.ZeroedForHash(file);
        byte[] z72 = (byte[])z.Clone(); Array.Clear(z72, 0x72, 46);
        Console.WriteLine($"variant hash72 also zeroed: {(IpodSync.Core.Signing.Hash58.Compute(fw, z72).AsSpan().SequenceEqual(file.AsSpan(0x58, 20)) ? "MATCH" : "no match")}");
    }
    return ok ? 0 : 1;
}

// Read-only: checks ItlpSorting's sort-name rule and collation against every row iTunes wrote.
static int ItlpOrdersCheckCmd(string[] rest)
{
    if (rest.Length == 0) { Console.Error.WriteLine("usage: itlp-orders-check <ipod-root | itunes-dir>"); return 2; }
    var (_, itlp) = ResolveItunesDir(rest[0]);
    using var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(itlp, "Library.itdb")};Mode=ReadOnly;Pooling=False");
    db.Open();
    List<object?[]> Rows(string sql)
    {
        using var c = db.CreateCommand(); c.CommandText = sql;
        using var r = c.ExecuteReader(); var list = new List<object?[]>();
        while (r.Read()) { var row = new object?[r.FieldCount]; for (int i = 0; i < r.FieldCount; i++) row[i] = r.IsDBNull(i) ? null : r.GetValue(i); list.Add(row); }
        return list;
    }
    int failures = 0;
    foreach (var col in new[] { "title", "artist", "album", "album_artist", "composer" })
    {
        var bad = Rows($"SELECT {col}, sort_{col} FROM item").Where(r => IpodSync.Core.Itlp.ItlpSorting.SortName((string?)r[0]) != (string?)r[1]).ToList();
        Console.WriteLine($"sort_{col,-13} rule mismatches {bad.Count}");
        foreach (var b in bad.Take(5)) Console.WriteLine($"    '{b[0]}' -> device '{b[1]}' rule '{IpodSync.Core.Itlp.ItlpSorting.SortName((string?)b[0])}'");
        failures += bad.Count;
    }
    foreach (var (label, sql) in new[] {
        ("item.title_order", "SELECT sort_title, title_order FROM item"),
        ("item.artist_order", "SELECT sort_artist, artist_order FROM item"),
        ("item.album_order", "SELECT sort_album, album_order FROM item WHERE album IS NOT NULL"),
        ("item.album_artist_order", "SELECT sort_album_artist, album_artist_order FROM item WHERE album_artist IS NOT NULL"),
        ("album.name_order", "SELECT sort_name, name_order FROM album WHERE is_unknown = 0"),
        ("artist.name_order", "SELECT sort_name, name_order FROM artist WHERE is_unknown = 0"),
        ("track_artist.name_order", "SELECT sort_name, name_order FROM track_artist WHERE is_unknown = 0"),
        ("composer.name_order", "SELECT sort_name, name_order FROM composer WHERE is_unknown = 0"),
    })
    {
        var rows = Rows(sql).Where(r => r[0] is not null && r[1] is not null).Select(r => ((string)r[0]!, Convert.ToInt64(r[1]))).ToList();
        var sorted = rows.OrderBy(r => r.Item2).ToList();
        // Pairs of rank-adjacent distinct ranks whose keys the collation orders the other way.
        var byRank = sorted.GroupBy(r => r.Item2).Select(g => (Rank: g.Key, Key: g.First().Item1)).ToList();
        var inversions = new List<string>();
        for (int i = 0; i + 1 < byRank.Count; i++)
            if (IpodSync.Core.Itlp.ItlpSorting.Collation.Compare(byRank[i].Key, byRank[i + 1].Key) > 0)
                inversions.Add($"'{byRank[i].Key}' ({byRank[i].Rank}) > '{byRank[i + 1].Key}' ({byRank[i + 1].Rank})");
        // Leave-one-out: does NeighbourRank put each key back between its true neighbours?
        int misplaced = 0;
        for (int i = 0; i < byRank.Count; i++)
        {
            var others = byRank.Where((_, j) => j != i).Select(x => (x.Key, x.Rank));
            long r = IpodSync.Core.Itlp.ItlpSorting.NeighbourRank(others, byRank[i].Key);
            long lo = i > 0 ? byRank[i - 1].Rank : 0, hi = i + 1 < byRank.Count ? byRank[i + 1].Rank : long.MaxValue;
            if (!(r > lo && r < hi)) misplaced++;
        }
        Console.WriteLine($"{label,-24} {byRank.Count} ranks, adjacent inversions {inversions.Count}, leave-one-out misplaced {misplaced}");
        foreach (var x in inversions.Take(6)) Console.WriteLine("    " + x);
    }
    return failures == 0 ? 0 : 1;
}

// Read-only: ArtworkDB round-trip + structural checks against the ithmb files and the CDB.
static int ArtCheckCmd(string[] rest)
{
    if (rest.Length == 0) { Console.Error.WriteLine("usage: art-check <ipod-root | Artwork dir>"); return 2; }
    string artDir = Directory.Exists(Path.Combine(rest[0], "iPod_Control")) ? Path.Combine(rest[0], "iPod_Control", "Artwork") : rest[0];
    byte[] bytes = File.ReadAllBytes(Path.Combine(artDir, "ArtworkDB"));
    var root = IpodSync.Core.Artwork.ArtworkDb.Parse(bytes);
    bool identical = root.Serialize().AsSpan().SequenceEqual(bytes);
    Console.WriteLine($"ArtworkDB round-trip   {(identical ? "byte-identical PASS" : "DIFFERS FAIL")} ({bytes.Length:N0} bytes)");
    var images = IpodSync.Core.Artwork.ArtworkDb.Images(root).ToList();
    var formats = IpodSync.Core.Artwork.ArtworkDb.Formats(root);
    Console.WriteLine($"images {images.Count}, next id {IpodSync.Core.Artwork.ArtworkDb.NextImageId(root)}, formats {string.Join(", ", formats.Select(f => $"{f.Format}:{f.Size}"))}");
    int bad = identical ? 0 : 1;
    foreach (var (fmt, size) in formats)
    {
        string ithmb = Path.Combine(artDir, $"F{fmt}_1.ithmb");
        long len = File.Exists(ithmb) ? new FileInfo(ithmb).Length : -1;
        var thumbs = images.SelectMany(IpodSync.Core.Artwork.ArtworkDb.Thumbs).Where(t => t.Format == fmt).ToList();
        int outOfRange = thumbs.Count(t => t.Offset < 0 || t.Offset + t.Size > len || t.Size != size);
        int maxEnd = thumbs.Count == 0 ? 0 : thumbs.Max(t => t.Offset + t.Size);
        Console.WriteLine($"  F{fmt}_1.ithmb {len:N0} bytes, {thumbs.Count} thumbs, out of range/bad size {outOfRange}, highest end {maxEnd:N0}");
        bad += outOfRange;
    }
    string cdbDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(artDir))!, "iTunes");
    string cdbPath = Path.Combine(cdbDir, File.Exists(Path.Combine(cdbDir, "iTunesDB")) ? "iTunesDB" : "iTunesCDB");
    if (File.Exists(cdbPath))
    {
        var cdb = ItunesDbReader.Read(File.ReadAllBytes(cdbPath));
        var ids = images.ToDictionary(IpodSync.Core.Artwork.ArtworkDb.ImageId);
        var withArt = cdb.Tracks.Where(t => t.HasArtwork).ToList();
        int dangling = withArt.Count(t => !ids.ContainsKey((int)t.ArtworkId));
        int refMismatch = ids.Values.Count(m => IpodSync.Core.Artwork.ArtworkDb.RefCount(m) != withArt.Count(t => t.ArtworkId == IpodSync.Core.Artwork.ArtworkDb.ImageId(m)));
        Console.WriteLine($"CDB: {withArt.Count} tracks with artwork, dangling image links {dangling}, images whose reference count != referencing tracks {refMismatch}");
        bad += dangling + refMismatch;
    }
    return bad == 0 ? 0 : 1;
}

static int SyncFolderCmd(string[] rest)
{
    var positional = rest.Where(a => !a.StartsWith("--") && !IsOptionValue(rest, a) && !IsSyncOptionValue(rest, a)).ToList();
    if (positional.Count < 2)
    {
        Console.Error.WriteLine("usage: sync-folder <ipod-root> <music-folder> [--yes] [--batch N] [--limit N] [--playlist name] [--remove-missing] [--backup-root dir]");
        return 2;
    }
    string root = positional[0], folder = Path.GetFullPath(positional[1]);
    int batch = IntOpt(rest, "--batch", 20), limit = IntOpt(rest, "--limit", int.MaxValue);
    string? playlist = StrOpt(rest, "--playlist");
    bool commit = rest.Contains("--yes"), removeMissing = rest.Contains("--remove-missing");

    var device = IpodDevice.Open(root);
    var cdb = ItunesDbReader.Read(File.ReadAllBytes(device.ItunesDbPath));
    var manifest = FolderSync.Manifest.Load(cdb.LibraryPersistentId, folder);
    Console.WriteLine($"device      {root}  library 0x{cdb.LibraryPersistentId:X16}, {cdb.Tracks.Count} tracks");
    Console.WriteLine($"source      {folder}");
    Console.WriteLine($"manifest    {FolderSync.Manifest.PathFor(cdb.LibraryPersistentId)} ({manifest.Entries.Count} entries)");

    var files = FolderSync.Scan(folder);
    Console.WriteLine($"scanned     {files.Count} audio files");
    var plan = FolderSync.MakePlan(files, cdb, manifest, removeMissing);

    Console.WriteLine($"in sync     {plan.Unchanged.Count}");
    Console.WriteLine($"on iPod already (adopt, no copy) {plan.Adopt.Count}");
    foreach (var (f, t) in plan.Adopt.Take(10)) Console.WriteLine($"    = {f.RelativePath}  ->  #{t.Id} {t.Artist} - {t.Title}");
    if (plan.Adopt.Count > 10) Console.WriteLine($"    ... {plan.Adopt.Count - 10} more");
    Console.WriteLine($"duplicates in source (skipped) {plan.SourceDuplicates.Count}");
    foreach (var (skip, keep) in plan.SourceDuplicates.Take(5)) Console.WriteLine($"    - {skip.RelativePath}  (keeping {keep.RelativePath})");
    var toAdd = plan.Add.Take(limit).ToList();
    Console.WriteLine($"to add      {plan.Add.Count} ({plan.AddBytes / 1048576.0:F1} MB source){(toAdd.Count < plan.Add.Count ? $", limited to {toAdd.Count}" : "")}");
    foreach (var f in toAdd.Take(25)) Console.WriteLine($"    + {f.RelativePath}  [{f.Artist} - {f.Title}, {f.Seconds:F0}s]");
    if (toAdd.Count > 25) Console.WriteLine($"    ... {toAdd.Count - 25} more");
    var removals = plan.RemoveCandidates.Where(r => r.Entry.Origin == "added").ToList();
    if (removeMissing) Console.WriteLine($"to remove   {removals.Count} (source file gone; only tracks this sync added)");

    // Space: lossless sources become ALAC (about their own size), lossy ones 256k AAC.
    long estimate = toAdd.Sum(f =>
    {
        string ext = Path.GetExtension(f.Path).ToLowerInvariant();
        if (ext is ".flac" or ".wav" or ".ape" or ".wv" or ".aif" or ".aiff") return (long)(f.Size * 1.1);
        if (ext is ".ogg" or ".oga" or ".opus" or ".wma") return Math.Max(f.Size, (long)(f.Seconds * 32000));
        return f.Size;
    });
    var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!);
    const long reserve = 200L * 1024 * 1024;
    Console.WriteLine($"space       need ~{estimate / 1048576.0:F1} MB, device free {drive.AvailableFreeSpace / 1048576.0:F1} MB (keeping {reserve / 1048576} MB spare)");
    if (estimate > drive.AvailableFreeSpace - reserve) { Console.Error.WriteLine("refusing: not enough free space on the device for this sync (use --limit)."); return 1; }

    if (!commit)
    {
        // Prove the first batch end to end (transcode, CDB, SQLite, artwork, signing) without writing.
        if (toAdd.Count > 0)
        {
            var cs = BatchChangeSet(toAdd.Take(batch), playlist);
            Console.WriteLine();
            Console.WriteLine($"DRY RUN of the first batch ({cs.Ops!.Count} op(s)):");
            int rc = WritePipeline.Run(PipelineOptions(root, rest, cs, "syncfolder"));
            if (rc != 0) return rc;
        }
        Console.WriteLine("DRY RUN: nothing written. Re-run with --yes to adopt matches into the manifest and add files in verified batches.");
        return 0;
    }

    foreach (var (f, t) in plan.Adopt)
        manifest.Entries.Add(new FolderSync.ManifestEntry { RelativePath = f.RelativePath, Size = f.Size, MtimeTicks = f.MtimeTicks, PersistentId = t.PersistentId, Origin = "adopted" });
    manifest.Save();
    Console.WriteLine($"manifest    adopted {plan.Adopt.Count} existing track(s)");

    int done = 0;
    foreach (var chunk in toAdd.Chunk(batch))
    {
        var cs = BatchChangeSet(chunk, playlist);
        Console.WriteLine();
        Console.WriteLine($"=== batch {done / batch + 1}: {chunk.Length} file(s)");
        var bySource = chunk.ToDictionary(f => Path.GetFullPath(f.Path), StringComparer.OrdinalIgnoreCase);
        int rc = WritePipeline.Run(PipelineOptions(root, rest, cs, "syncfolder").WithCallback(report =>
        {
            foreach (var (src, pid) in report?.AddedTracks ?? [])
                if (bySource.TryGetValue(Path.GetFullPath(src), out var f))
                    manifest.Entries.Add(new FolderSync.ManifestEntry { RelativePath = f.RelativePath, Size = f.Size, MtimeTicks = f.MtimeTicks, PersistentId = pid, Origin = "added" });
            manifest.Save();
        }));
        if (rc != 0) { Console.Error.WriteLine($"batch failed (exit {rc}); stopping. {done} file(s) added before it."); return rc; }
        done += chunk.Length;
    }

    if (removeMissing && removals.Count > 0)
    {
        var cs = new ChangeSet { Ops = removals.Select(r => new EditOp { Op = "removeTrack", TrackId = r.Track.Id }).ToList() };
        Console.WriteLine();
        Console.WriteLine($"=== removing {removals.Count} track(s) whose source file is gone");
        var gone = removals.Select(r => r.Entry).ToHashSet();
        int rc = WritePipeline.Run(PipelineOptions(root, rest, cs, "syncfolder").WithCallback(_ =>
        {
            manifest.Entries.RemoveAll(gone.Contains);
            manifest.Save();
        }));
        if (rc != 0) return rc;
    }
    Console.WriteLine($"SYNC COMPLETE: {done} added, {plan.Adopt.Count} adopted, {plan.Unchanged.Count} already in sync.");
    return 0;
}

static ChangeSet BatchChangeSet(IEnumerable<FolderSync.SourceFile> files, string? playlist) => new()
{
    Ops = files.Select(f => new EditOp { Op = "addTrackFromFile", SourcePath = f.Path, Playlist = playlist }).ToList(),
};

static bool IsSyncOptionValue(string[] rest, string a)
{
    int i = Array.IndexOf(rest, a);
    return i > 0 && rest[i - 1] is "--batch" or "--limit" or "--playlist";
}

static int IntOpt(string[] rest, string name, int fallback)
{
    int i = Array.IndexOf(rest, name);
    return i >= 0 && i + 1 < rest.Length && int.TryParse(rest[i + 1], out int v) ? v : fallback;
}

static string? StrOpt(string[] rest, string name)
{
    int i = Array.IndexOf(rest, name);
    return i >= 0 && i + 1 < rest.Length ? rest[i + 1] : null;
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
    string? changesPath = null;
    for (int i = 0; i < rest.Length - 1; i++) if (rest[i] == "--changes") changesPath = rest[i + 1];
    string? path = rest.FirstOrDefault(a => !a.StartsWith("--") && !IsOptionValue(rest, a));

    if (changesPath is null) { Console.Error.WriteLine("apply-edits needs --changes <file.json>"); return 2; }
    if (!File.Exists(changesPath)) { Console.Error.WriteLine($"change-set not found: {changesPath}"); return 1; }
    if (path is null)
    {
        var devices = IpodDevice.Detect();
        if (devices.Count == 0) { Console.Error.WriteLine("No iPod found. Pass a path explicitly."); return 1; }
        path = devices[0].RootPath;
        Console.WriteLine($"Using {path}");
    }

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

    return WritePipeline.Run(PipelineOptions(path, rest, cs, "applyedits"));
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
