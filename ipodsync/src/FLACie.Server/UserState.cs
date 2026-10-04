using System.Collections.Concurrent;
using System.Text.Json;
using FLACie.Core;

namespace FLACie.Server;

/// <summary>Where this server keeps its files (the folder mounted as the Docker volume).</summary>
public sealed record DataPaths(string Root);

public sealed class ImportItem
{
    public string Artist { get; set; } = "";
    public string Title { get; set; } = "";
    public string Album { get; set; } = "";
    /// <summary>pending, owned (already in the library), done (downloaded), requested (Lidarr has it, it arrives with the next scan), failed.</summary>
    public string State { get; set; } = "pending";
    public string? Message { get; set; }
}

public sealed class ImportList
{
    public string Name { get; set; } = "";
    public string? PlaylistId { get; set; }
    public List<ImportItem> Items { get; set; } = [];
}

public sealed class ImportJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string File { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.UtcNow;
    /// <summary>running, waiting (for disk space), done, cancelled</summary>
    public string State { get; set; } = "running";
    public string? Note { get; set; }
    public List<ImportList> Lists { get; set; } = [];
    public int Total => Lists.Sum(l => l.Items.Count);
    public int Finished => Lists.Sum(l => l.Items.Count(i => i.State is not "pending"));
    public int Failed => Lists.Sum(l => l.Items.Count(i => i.State == "failed"));
}

public sealed class ChartSettings
{
    public bool Enabled { get; set; }
    /// <summary>Deezer chart ids: 0 overall, 132 Pop, 116 Rap/Hip Hop, 152 Rock, 113 Dance, 165 R&amp;B.</summary>
    public List<int> Lists { get; set; } = [0];
    public int PerList { get; set; } = 10;
    public string LastRun { get; set; } = "";
    public string LastNote { get; set; } = "";
}

/// <summary>What this server remembers about one account besides the shared profile: background downloads and their limits.</summary>
public sealed class UserState
{
    /// <summary>Fetch the songs Autoplay is about to need, so they are there when the queue gets to them.</summary>
    public bool Prefetch { get; set; } = true;
    /// <summary>Background downloads (autoplay, imports, charts) stop when the music storage has less than this much free.</summary>
    public int MinFreeGb { get; set; } = 10;
    public ChartSettings Charts { get; set; } = new();
    public List<ImportJob> Imports { get; set; } = [];
    public HashSet<string> ChartSeen { get; set; } = [];
}

public sealed class UserStateStore(DataPaths paths)
{
    readonly ConcurrentDictionary<string, UserState> cache = new();
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    string FileOf(UserSession s) => Path.Combine(Directory.CreateDirectory(Path.Combine(paths.Root, "users")).FullName, s.Id + ".json");

    public UserState For(UserSession s) => cache.GetOrAdd(s.Id, _ =>
    {
        try { var f = FileOf(s); if (File.Exists(f)) return JsonSerializer.Deserialize<UserState>(File.ReadAllText(f)) ?? new(); } catch (Exception) { }
        return new UserState();
    });

    readonly object gate = new();
    public void Save(UserSession s)
    {
        var st = For(s);
        lock (gate) { try { File.WriteAllText(FileOf(s), JsonSerializer.Serialize(st, Json)); } catch (Exception) { } }
    }
}

/// <summary>Is there room for more downloads? Asks the music storage (the NAS share, over SMB) and remembers the answer for a couple of minutes.</summary>
public sealed class StorageGuard(UserStateStore states)
{
    readonly ConcurrentDictionary<string, (DateTime At, (long Free, long Total)? Space)> cache = new();

    public (long Free, long Total)? Space(UserSession s)
    {
        if (s.Nas is not { } nas) return null;
        var key = s.Id;
        if (cache.TryGetValue(key, out var c) && DateTime.UtcNow - c.At < TimeSpan.FromMinutes(2)) return c.Space;
        var sp = NasClient.ShareSpace(nas);
        cache[key] = (DateTime.UtcNow, sp);
        return sp;
    }

    /// <summary>True when downloads may go on. If the free space can't be read it is assumed to be fine: the file mover has its own limits.</summary>
    public bool Ok(UserSession s) => Space(s) is not { } sp || sp.Free > states.For(s).MinFreeGb * 1024L * 1024 * 1024;
}
