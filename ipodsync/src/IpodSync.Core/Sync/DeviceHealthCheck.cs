using IpodSync.Core.Artwork;
using IpodSync.Core.Device;
using IpodSync.Core.ItunesDb;
using IpodSync.Core.Itlp;
using IpodSync.Core.Signing;

namespace IpodSync.Core.Sync;

/// <summary>Read-only health of a device, for hosts that show it as text (the Android app through the engine): are the databases consistent and signed,
/// is the artwork intact, what is this iPod and can it be written to. Same checks as the desktop app's Device health tab.</summary>
public sealed record HealthReport(
    int Tracks, int Playlists, long FreeBytes, long TotalBytes,
    bool? SignaturesValid, string SignatureDetail,
    bool? DatabasesInSync, string SyncDetail,
    bool? ArtworkOk, string ArtworkDetail,
    string? ModelName, string? ModelNumber, bool CanWrite, string ProfileSummary, List<string> Notes,
    string? ProvenFirewire);

/// <summary>One pre-write backup: <see cref="Kind"/> is the label it was made for, <see cref="Outcome"/> what its write log ended with.</summary>
public sealed record BackupEntry(string Name, string Kind, long CreatedMs, bool HasArtwork, string? Outcome);

public static class DeviceHealthCheck
{
    public static HealthReport Run(string deviceRoot, IReadOnlyList<string> firewireCandidates, string backupRoot, long freeBytes = 0, long totalBytes = 0)
    {
        var device = IpodDevice.Open(deviceRoot);
        string itunesDir = Path.GetDirectoryName(device.ItunesDbPath)!;
        byte[] cdbBytes = File.ReadAllBytes(device.ItunesDbPath);
        var cdb = ItunesDbReader.Read(cdbBytes);
        if (freeBytes == 0 && totalBytes == 0)
        {
            try { var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(deviceRoot))!); freeBytes = drive.AvailableFreeSpace; totalBytes = drive.TotalSize; } catch (Exception) { }
        }

        // signatures: hash72 must validate on the CDB and the Locations cbk; hash58 is valid when a FirewireGuid candidate reproduces it
        bool? sigOk = null;
        string sigDetail = "not signed (older iPod)";
        string? proven = null;
        if (DeviceSigning.RequiresSigning(cdbBytes))
        {
            var parts = new List<string>();
            bool h72 = Hash72.ExtractFromDatabase(cdbBytes) is not null;
            parts.Add(h72 ? "hash72 valid" : "hash72 INVALID");
            var problems = new List<string>();
            var signer = DeviceSigning.Resolve(itunesDir, Path.GetFileName(device.ItunesDbPath), firewireCandidates, SigningInputs.References(backupRoot, []), problems);
            bool h58 = signer is not null && signer.VerifyDatabase(cdbBytes).Count == 0;
            if (h58) proven = Convert.ToHexString(signer!.FirewireId);
            parts.Add(h58 ? "hash58 valid" : "hash58 not proven");
            bool cbkOk = true;
            string loc = Path.Combine(itunesDir, "iTunes Library.itlp", "Locations.itdb");
            if (File.Exists(loc) && File.Exists(loc + ".cbk"))
            {
                cbkOk = Hash72.VerifyCbk(File.ReadAllBytes(loc), File.ReadAllBytes(loc + ".cbk")).Problems.Count == 0;
                parts.Add(cbkOk ? "cbk valid" : "cbk INVALID");
            }
            sigOk = h72 && h58 && cbkOk;
            sigDetail = string.Join(", ", parts);
        }

        bool? inSync = null;
        string syncDetail = "no SQLite library on this iPod (classic database only)";
        string itlp = Path.Combine(itunesDir, "iTunes Library.itlp");
        if (File.Exists(Path.Combine(itlp, "Library.itdb")))
        {
            var diff = ItlpCompare.Compare(itlp, cdb);
            inSync = diff.InSync;
            syncDetail = diff.InSync ? "iTunesCDB and SQLite library agree"
                : $"playlists {(diff.PlaylistsInSync ? "agree" : "differ")}, tracks {(diff.TracksInSync ? "agree" : "differ")}";
        }

        bool? artOk = null;
        string artDetail = "no artwork database";
        string artDir = Path.Combine(device.ControlPath, "Artwork");
        if (File.Exists(Path.Combine(artDir, "ArtworkDB")))
        {
            var root = ArtworkDb.Parse(File.ReadAllBytes(Path.Combine(artDir, "ArtworkDB")));
            var lengths = ArtworkDb.Formats(root).Select(f => f.Format).Distinct()
                .ToDictionary(f => f, f => File.Exists(Path.Combine(artDir, $"F{f}_1.ithmb")) ? new FileInfo(Path.Combine(artDir, $"F{f}_1.ithmb")).Length : -1L);
            var problems = ArtworkDb.Check(root, cdb, lengths);
            artOk = problems.Count == 0;
            artDetail = artOk == true ? $"{ArtworkDb.Images(root).Count()} images, {cdb.Tracks.Count(t => t.HasArtwork)} tracks with art" : $"{problems.Count} problem(s): {problems[0]}";
        }

        var profile = IpodProfiler.Inspect(deviceRoot);
        return new HealthReport(cdb.Tracks.Count, cdb.Playlists.Count(p => !p.IsMaster), freeBytes, totalBytes, sigOk, sigDetail, inSync, syncDetail, artOk, artDetail,
            profile.ModelName, profile.ModelNumber, profile.CanWrite, profile.Summary, profile.Notes.ToList(), proven);
    }

    /// <summary>The backups made before writes, newest first (a backup is a folder with an iTunes folder and, usually, a write log).</summary>
    public static List<BackupEntry> ListBackups(string backupRoot)
    {
        var list = new List<BackupEntry>();
        if (!Directory.Exists(backupRoot)) return list;
        foreach (var dir in Directory.EnumerateDirectories(backupRoot))
        {
            if (!Directory.Exists(Path.Combine(dir, "iTunes"))) continue;
            string name = Path.GetFileName(dir);
            string logPath = Path.Combine(dir, "write-log.txt");
            string? outcome = null;
            if (File.Exists(logPath))
            {
                var lines = File.ReadAllLines(logPath);
                outcome = lines.LastOrDefault(l => l.StartsWith("WRITE VERIFIED") || l.StartsWith("Device restored") || l.StartsWith("RESTORE INCOMPLETE"))
                    ?? lines.LastOrDefault(l => l.Trim().Length > 0);
                if (outcome?.StartsWith("WRITE VERIFIED") == true) outcome = "Write verified";
                else if (outcome?.StartsWith("Device restored") == true) outcome = "Write failed verification; restored from this backup";
            }
            string kind = name.Contains('-') ? name[..name.IndexOf('-')] : name;
            list.Add(new BackupEntry(name, kind, new DateTimeOffset(Directory.GetCreationTime(dir)).ToUnixTimeMilliseconds(), Directory.Exists(Path.Combine(dir, "Artwork")), outcome));
        }
        return list.OrderByDescending(b => b.CreatedMs).ToList();
    }
}
