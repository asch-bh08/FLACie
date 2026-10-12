namespace FLACie.Server;

/// <summary>What the server keeps on disk so that it need not ask Jellyfin again, by kind (bytes).</summary>
public sealed record CacheReport(long Covers, int CoverFiles, long Library, long Lyrics, long FormatIndex, long Free, long MinFree)
{
    public long Total => Covers + Library + Lyrics + FormatIndex;
}

/// <summary>
/// The server's caches as one thing: the covers (<see cref="CoverStore"/>), the library listings kept per account (so a restart shows the library at once and only asks
/// for what changed), the lyrics and the format index (every song's real audio facts). There is no size limit: everything is kept while the disk has room. Only when
/// the free space of the data volume falls below the minimum are the least recently used covers (then lyrics) deleted, until there is room again. The Clear cache
/// button empties the covers, the lyrics and the saved library listings; they fill up again as you browse (covers one by one, the library with one listing). The format index
/// is small and costs Jellyfin requests to rebuild, so it is counted but kept.
/// </summary>
public sealed class CacheService(DataPaths paths, CoverStore covers)
{
    string Lib => Path.Combine(paths.Root, "library");
    string Lyr => Path.Combine(paths.Root, "lyrics");

    static long DirBytes(string dir)
    {
        try { return Directory.Exists(dir) ? new DirectoryInfo(dir).EnumerateFiles().Sum(f => f.Length) : 0; } catch (Exception) { return 0; }
    }

    public CacheReport Report()
    {
        var (n, covBytes) = covers.Size();
        var (free, _) = covers.Space();
        long idx = 0;
        foreach (var f in new[] { "audiofacts.json", "audiofacts-misses.json" }) try { var p = Path.Combine(paths.Root, f); if (File.Exists(p)) idx += new FileInfo(p).Length; } catch (Exception) { }
        return new CacheReport(covBytes, n, DirBytes(Lib), DirBytes(Lyr), idx, free, covers.MinFreeBytes);
    }

    /// <summary>Deletes the covers, the lyrics and the saved library listings (not the format index). Returns the bytes given back.</summary>
    public long Clear()
    {
        var before = Report().Total;
        covers.Clear();
        foreach (var dir in new[] { Lib, Lyr })
            try { if (Directory.Exists(dir)) foreach (var f in new DirectoryInfo(dir).EnumerateFiles().ToList()) try { f.Delete(); } catch (Exception) { } } catch (Exception) { }
        return Math.Max(0, before - Report().Total);
    }

    /// <summary>Frees room when the volume is nearly full: the least recently used covers go first, then the least recently used lyrics, and only as much as it takes.
    /// Nothing is deleted while there is room. Returns how many files went.</summary>
    public int EvictIfLow()
    {
        var n = covers.EvictIfLow();
        var (free, _) = covers.Space();
        var min = covers.MinFreeBytes;
        if (free >= min) return n;
        var want = (long)(min * 1.25) - free; long freed = 0;
        try
        {
            if (!Directory.Exists(Lyr)) return n;
            foreach (var f in new DirectoryInfo(Lyr).EnumerateFiles().OrderBy(f => f.LastAccessTimeUtc).ThenBy(f => f.LastWriteTimeUtc).ToList())
            {
                if (freed >= want) break;
                try { var len = f.Length; f.Delete(); freed += len; n++; } catch (Exception) { }
            }
        }
        catch (Exception) { }
        return n;
    }
}

/// <summary>Looks at the free disk space every ten minutes and frees cache room only when it is low (see <see cref="CacheService.EvictIfLow"/>).</summary>
public sealed class CacheJanitor(CacheService cache, ILogger<CacheJanitor> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromMinutes(10), ct); } catch (OperationCanceledException) { break; }
            try { var n = cache.EvictIfLow(); if (n > 0) log.LogInformation("disk space is low: {n} cached files (least recently used) removed", n); }
            catch (Exception e) { log.LogDebug(e, "cache check failed"); }
        }
    }
}
