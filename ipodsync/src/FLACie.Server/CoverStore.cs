using System.Security.Cryptography;
using System.Text;

namespace FLACie.Server;

/// <summary>
/// The covers kept on disk, one file per (cover key, variant). A Jellyfin cover's key carries its image tag, so a cover is fetched once and never again unless
/// Jellyfin's tag for it changes (a new key). There is no fixed cap: everything stays while the data volume has room, and only when its free space falls below
/// <see cref="MinFreeBytes"/> (the larger of 2 GB and 10 % of the volume) are the least recently used covers deleted until there is room again.
/// </summary>
public sealed class CoverStore
{
    public const string Row = "s240", Full = "full", Other = "v3";
    static readonly string[] Exts = [".webp", ".jpg", ".png", ".gif", ".img"];
    readonly string dir;
    int saves;

    /// <summary>Free and total bytes of the volume the covers are on (replaceable for tests).</summary>
    public Func<(long Free, long Total)> Space { get; set; }
    public CoverStore(string dir)
    {
        this.dir = Directory.CreateDirectory(dir).FullName;
        Space = () => { try { var d = new DriveInfo(Path.GetPathRoot(this.dir)!); return (d.AvailableFreeSpace, d.TotalSize); } catch (Exception) { return (long.MaxValue, long.MaxValue); } };
    }

    /// <summary>The least free space worth keeping on the volume (2 GB): below it or below 10 % of the volume, the least recently used covers are deleted.</summary>
    public long MinFloorBytes { get; set; } = 2L << 30;
    public long MinFreeBytes { get { var (_, total) = Space(); return Math.Max(MinFloorBytes, total / 10); } }

    static string Name(string key) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)));
    static string Ext(string contentType) => contentType switch { "image/webp" => ".webp", "image/jpeg" => ".jpg", "image/png" => ".png", "image/gif" => ".gif", _ => ".img" };
    public static string TypeOf(string path) => Path.GetExtension(path) switch { ".webp" => "image/webp", ".png" => "image/png", ".gif" => "image/gif", _ => "image/jpeg" };

    /// <summary>The saved cover, if there is one (its use is noted, so the least recently used can be told apart).</summary>
    public string? Find(string key, string variant)
    {
        var stem = Path.Combine(dir, Name(key) + "." + variant);
        foreach (var e in Exts)
        {
            var f = stem + e;
            if (!File.Exists(f)) continue;
            try { if (DateTime.UtcNow - File.GetLastAccessTimeUtc(f) > TimeSpan.FromMinutes(10)) File.SetLastAccessTimeUtc(f, DateTime.UtcNow); } catch (Exception) { }
            return f;
        }
        return null;
    }

    public async Task<string> SaveAsync(string key, string variant, byte[] bytes, string contentType)
    {
        var file = Path.Combine(dir, Name(key) + "." + variant + Ext(contentType));
        var tmp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllBytesAsync(tmp, bytes);
        File.Move(tmp, file, true);
        if (Interlocked.Increment(ref saves) % 25 == 0) EvictIfLow();
        return file;
    }

    IEnumerable<FileInfo> Files() => new DirectoryInfo(dir).EnumerateFiles().Where(f => !f.Name.EndsWith(".tmp", StringComparison.Ordinal));

    public (int Count, long Bytes) Size() { var n = 0; long b = 0; foreach (var f in Files()) { n++; b += f.Length; } return (n, b); }

    public int Clear() { var n = 0; foreach (var f in Files().ToList()) try { f.Delete(); n++; } catch (Exception) { } return n; }

    /// <summary>When the volume's free space is below the minimum, deletes the least recently used covers until it is 25 % above it. Returns how many went.</summary>
    public int EvictIfLow()
    {
        var (free, _) = Space();
        var min = MinFreeBytes;
        if (free >= min) return 0;
        var want = (long)(min * 1.25) - free;   // bytes to give back
        var n = 0; long freed = 0;
        foreach (var f in Files().OrderBy(f => f.LastAccessTimeUtc).ThenBy(f => f.LastWriteTimeUtc).ToList())
        {
            if (freed >= want) break;
            try { var len = f.Length; f.Delete(); freed += len; n++; } catch (Exception) { }
        }
        return n;
    }
}
