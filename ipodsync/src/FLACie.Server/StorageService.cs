using FLACie.Core;

namespace FLACie.Server;

public sealed record StorageReport(
    long DataBytes, IReadOnlyList<(string Name, long Bytes)> DataParts, long DiskFree, long DiskTotal,
    (long Free, long Total)? Music, int NasSongs, long NasBytes, int JellyfinSongs, long JellyfinBytes, string? Problem);

/// <summary>How much disk this server and the music library use: the server's own folder (cover cache, lyrics, keys, settings), the
/// disk it sits on, the music storage (the NAS share) with its free space, and the size of the library on each source.</summary>
public sealed class StorageService(DataPaths paths, StorageGuard guard, JellyfinClient jf)
{
    static long DirSize(string dir)
    {
        try { return Directory.Exists(dir) ? new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0; }
        catch (Exception) { return 0; }
    }

    /// <summary>The cheap numbers, instantly: the server's folder, its disk, the NAS free space and the NAS file sizes already read by the scan.</summary>
    public StorageReport Quick(UserSession s)
    {
        var parts = new[] { "art", "lyrics", "users", "keys" }.Select(n => (n, DirSize(Path.Combine(paths.Root, n)))).Where(p => p.Item2 > 0).ToList();
        var total = DirSize(paths.Root);
        var other = total - parts.Sum(p => p.Item2);
        if (other > 0) parts.Add(("other", other));
        long free = 0, size = 0;
        try { var d = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(paths.Root))!); free = d.AvailableFreeSpace; size = d.TotalSize; } catch (Exception) { }
        var nas = s.Library.Songs.Where(t => t.Source == TrackSource.Nas).ToList();
        return new(total, parts, free, size, guard.Space(s), nas.Count, nas.Sum(t => t.Size), 0, 0, null);
    }

    /// <summary>Adds the Jellyfin library's size, which Jellyfin only reports by listing every file's details (so it is a button, not automatic).</summary>
    public async Task<StorageReport> FullAsync(UserSession s, CancellationToken ct)
    {
        var q = Quick(s);
        if (s.Jellyfin is not { } a) return q;
        try { var (n, b) = await jf.LibrarySizeAsync(a, ct); return q with { JellyfinSongs = n, JellyfinBytes = b }; }
        catch (Exception e) { return q with { Problem = "Jellyfin: " + e.Message }; }
    }

    public static string Bytes(long b) =>
        b >= 1L << 40 ? $"{b / (double)(1L << 40):0.0} TB" : b >= 1L << 30 ? $"{b / (double)(1L << 30):0.0} GB" : b >= 1L << 20 ? $"{b / (double)(1L << 20):0} MB" : b >= 1L << 10 ? $"{b / 1024.0:0} KB" : $"{b} B";
}
