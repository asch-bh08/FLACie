using IpodSync.Core.Device;
using IpodSync.Core.ItunesDb;

string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "detect";

try
{
    switch (cmd)
    {
        case "detect": return Detect();
        case "dump":   return Dump(args.Skip(1).ToArray());
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

static string Gb(long bytes) => $"{bytes / 1024.0 / 1024 / 1024:F1} GB";
