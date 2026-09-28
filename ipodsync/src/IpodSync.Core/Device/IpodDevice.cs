namespace IpodSync.Core.Device;

/// <summary>
/// A mounted iPod: any volume with an <c>iPod_Control</c> directory — or an <c>iTunes_Control</c>
/// one, which is what an iOS device (iPod touch) calls the same thing, so a touch whose media
/// partition is mounted (ifuse, or a jailbroken device) is handled by the same code.
/// </summary>
public sealed class IpodDevice
{
    public required string RootPath { get; init; }
    public string? VolumeLabel { get; init; }
    public string? FileSystem { get; init; }
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }

    /// <summary>Raw key/value pairs from iPod_Control/Device/SysInfo.</summary>
    public IReadOnlyDictionary<string, string> SysInfo { get; init; }
        = new Dictionary<string, string>();

    public string ControlPath => Path.Combine(RootPath, ControlFolder(RootPath));

    /// <summary>"iPod_Control" on an iPod, "iTunes_Control" on an iOS device. Picked by where the
    /// database actually is, so a root that happens to have both folders still resolves.</summary>
    public static string ControlFolder(string root)
    {
        foreach (var name in new[] { "iPod_Control", "iTunes_Control" })
        {
            string dir = Path.Combine(root, name, "iTunes");
            if (File.Exists(Path.Combine(dir, "iTunesDB")) || File.Exists(Path.Combine(dir, "iTunesCDB")) || File.Exists(Path.Combine(dir, "iTunesSD")))
                return name;
        }
        return Directory.Exists(Path.Combine(root, "iPod_Control")) ? "iPod_Control" : "iTunes_Control";
    }

    public static bool LooksLikeIpod(string root) =>
        Directory.Exists(Path.Combine(root, "iPod_Control")) || Directory.Exists(Path.Combine(root, "iTunes_Control"));
    /// <summary>Path to the music database. Later iPods write a zlib-compressed
    /// iTunesCDB in place of the plain iTunesDB; the reader handles either.</summary>
    public string ItunesDbPath
    {
        get
        {
            string dir = Path.Combine(ControlPath, "iTunes");
            string plain = Path.Combine(dir, "iTunesDB");
            string packed = Path.Combine(dir, "iTunesCDB");
            return File.Exists(plain) ? plain : File.Exists(packed) ? packed : plain;
        }
    }

    public bool IsCompressedDatabase =>
        Path.GetFileName(ItunesDbPath) == "iTunesCDB";
    public string MusicPath => Path.Combine(ControlPath, "Music");
    public string DevicePath => Path.Combine(ControlPath, "Device");

    /// <summary>Present once iTunes has talked to the device. Required to compute
    /// the hashAB signature on Nano 6G/7G and Shuffle 4G.</summary>
    public string SysInfoExtendedPath => Path.Combine(DevicePath, "SysInfoExtended");

    public bool HasDatabase => File.Exists(ItunesDbPath);
    public bool HasSysInfoExtended => File.Exists(SysInfoExtendedPath);

    public string? Serial => Get("pszSerialNumber") ?? Get("SerialNumber");
    public string? ModelNumber => Get("ModelNumStr");

    /// <summary>Needed as an input to both hash58 and hashAB signing.</summary>
    public string? FirewireGuid => Get("FirewireGuid");

    /// <summary>
    /// HFS+ (Mac-formatted) iPods cannot be reached from Android at all, and only
    /// read-only on Windows without extra drivers. Worth surfacing early.
    /// </summary>
    public bool IsFat32 =>
        FileSystem?.Equals("FAT32", StringComparison.OrdinalIgnoreCase) == true;

    private string? Get(string key) =>
        SysInfo.TryGetValue(key, out var v) ? v : null;

    /// <summary>Scan all ready drives for an iPod_Control directory.</summary>
    public static List<IpodDevice> Detect()
    {
        var found = new List<IpodDevice>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            string root;
            string? label, fs;
            long total, free;
            try
            {
                if (!drive.IsReady) continue;
                root = drive.RootDirectory.FullName;
                if (!LooksLikeIpod(root)) continue;
                label = drive.VolumeLabel;
                fs = drive.DriveFormat;
                total = drive.TotalSize;
                free = drive.AvailableFreeSpace;
            }
            catch (IOException) { continue; }            // drive vanished mid-scan
            catch (UnauthorizedAccessException) { continue; }

            found.Add(Open(root, label, fs, total, free));
        }
        return found;
    }

    /// <summary>Open a specific path as an iPod (a real volume, or a copied-off backup).</summary>
    public static IpodDevice Open(string root) => Open(root, null, null, 0, 0);

    private static IpodDevice Open(string root, string? label, string? fs, long total, long free) => new()
    {
        RootPath = root,
        VolumeLabel = label,
        FileSystem = fs,
        TotalBytes = total,
        FreeBytes = free,
        SysInfo = ReadSysInfo(Path.Combine(root, ControlFolder(root), "Device", "SysInfo")),
    };

    /// <summary>SysInfo is plain text, one "Key: value" per line.</summary>
    private static Dictionary<string, string> ReadSysInfo(string path)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return map;

        try
        {
            foreach (var line in File.ReadAllLines(path))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                map[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }
        }
        catch (IOException) { /* unreadable SysInfo is not fatal */ }
        return map;
    }
}
