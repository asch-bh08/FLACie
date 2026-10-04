using Microsoft.AspNetCore.DataProtection;
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
    /// <summary>Playlists that already exist (same name, ignoring a quality tag such as "(FLAC)") are renamed to the file's name and made to hold exactly the file's songs.</summary>
    public bool Replace { get; set; }
    /// <summary>Replace mode has already merged the same-named playlists for this job.</summary>
    public bool Consolidated { get; set; }
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
    /// <summary>Stored as "turned off" so that a fresh or older file means on (the daily download is on unless switched off).</summary>
    public bool Off { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public bool Enabled { get => !Off; set => Off = !value; }
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
    /// <summary>The Jellyfin sign-in, encrypted with the server's keys, so daily jobs can run after a restart before anyone opens the page.</summary>
    public string? Account { get; set; }
}

public sealed class UserStateStore(DataPaths paths, Microsoft.AspNetCore.DataProtection.IDataProtectionProvider dp)
{
    readonly ConcurrentDictionary<string, UserState> cache = new();
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    string FileOf(UserSession s) => Path.Combine(Directory.CreateDirectory(Path.Combine(paths.Root, "users")).FullName, s.Id + ".json");

    public UserState For(UserSession s) => cache.GetOrAdd(s.Id, _ =>
    {
        try { var f = FileOf(s); if (File.Exists(f)) return JsonSerializer.Deserialize<UserState>(File.ReadAllText(f)) ?? new(); } catch (Exception) { }
        return new UserState();
    });


    /// <summary>Keeps this account's Jellyfin sign-in (encrypted) so the daily chart download can run after a restart.</summary>
    public void Remember(UserSession s)
    {
        if (s.Jellyfin is not { } j) return;
        var st = For(s);
        var protectedJson = dp.CreateProtector("FLACie.Account").Protect(JsonSerializer.Serialize(new[] { j.Server, j.UserId, j.UserName, j.Token }));
        if (st.Account == protectedJson) return;
        st.Account = protectedJson; Save(s);
    }

    /// <summary>The Jellyfin accounts remembered by earlier runs.</summary>
    public List<JellyfinAccount> Remembered()
    {
        var res = new List<JellyfinAccount>();
        var dir = Path.Combine(paths.Root, "users");
        if (!Directory.Exists(dir)) return res;
        foreach (var f in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                var st = JsonSerializer.Deserialize<UserState>(File.ReadAllText(f));
                if (st?.Account is null) continue;
                var a = JsonSerializer.Deserialize<string[]>(dp.CreateProtector("FLACie.Account").Unprotect(st.Account));
                if (a is { Length: 4 }) res.Add(new JellyfinAccount(a[0], a[1], a[2], a[3]));
            }
            catch (Exception) { }
        }
        return res;
    }
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
