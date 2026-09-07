using IpodSync.Core.Device;
using IpodSync.Core.ItunesDb;

string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "detect";

try
{
    switch (cmd)
    {
        case "detect":    return Detect();
        case "dump":      return Dump(args.Skip(1).ToArray());
        case "roundtrip": return RoundTripCmd(args.Skip(1).ToArray());
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
        Console.WriteLine($"           {t.RelativePath ?? "(no location)"}");
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

static string Gb(long bytes) => $"{bytes / 1024.0 / 1024 / 1024:F1} GB";
