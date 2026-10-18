using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using FLACie.Core;
using FLACie.Server;
using Microsoft.AspNetCore.Components.Authorization;

namespace FLACie.Hybrid;

/// <summary>The one person signed in on this phone: their Jellyfin account (kept in the app's secure storage) and their session.</summary>
public sealed class HybridAccount(JellyfinClient jf)
{
    const string Key = "flacie.account";
    public UserSession? Session { get; private set; }
    public event Action? Changed;

    static string Dir => FileSystem.AppDataDirectory;

    /// <summary>Signs in with a server address, user name and password; the library loads in the background.</summary>
    public async Task SignInAsync(string server, string user, string password, CancellationToken ct = default)
    {
        var a = await jf.SignInAsync(JellyfinClient.Normalise(server), user, password, ct);
        await SecureStorage.Default.SetAsync(Key, JsonSerializer.Serialize(a));
        Start(a);
    }

    /// <summary>The account saved last time, if there is one: signed in again without asking.</summary>
    public async Task<bool> RestoreAsync()
    {
        if (Session is not null) return true;
        try
        {
            if (await SecureStorage.Default.GetAsync(Key) is { Length: > 0 } json && JsonSerializer.Deserialize<JellyfinAccount>(json) is { } a) { Start(a); return true; }
        }
        catch (Exception) { }
        return false;
    }

    public void SignOut()
    {
        SecureStorage.Default.Remove(Key);
        Session = null; Changed?.Invoke();
    }

    void Start(JellyfinAccount a)
    {
        var id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"jf|{a.Server}|{a.UserId}")))[..32];
        Directory.CreateDirectory(Path.Combine(Dir, "library"));
        var s = new UserSession("jellyfin", a, null) { Id = id, CachePath = Path.Combine(Dir, "library", id + ".v2.json") };
        Session = s;
        _ = Task.Run(async () => { try { await s.LoadAsync(jf); } catch (Exception e) { System.Diagnostics.Debug.WriteLine("FLACie: library load failed: " + e); } });
        Changed?.Invoke();
    }

    public ClaimsPrincipal Principal => Session?.Jellyfin is { } a
        ? new ClaimsPrincipal(new ClaimsIdentity([new Claim("kind", "jellyfin"), new Claim("jf.server", a.Server), new Claim("jf.user", a.UserId), new Claim("jf.name", a.UserName), new Claim(ClaimTypes.Name, a.UserName)], "flacie"))
        : new ClaimsPrincipal(new ClaimsIdentity());
}

/// <summary>Signed in once there is a session.</summary>
public sealed class HybridAuth : AuthenticationStateProvider
{
    readonly HybridAccount account;
    public HybridAuth(HybridAccount a) { account = a; a.Changed += () => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync()); }
    public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(account.Principal));
}

/// <summary>The Retro page's sessions: the phone's one account. No Connect or Jams in this prototype.</summary>
public sealed class HybridSessions(HybridAccount account) : ISessionStore
{
    public UserSession For(ClaimsPrincipal p) => account.Session ?? throw new InvalidOperationException("Not signed in");
    public Live? LiveFor(UserSession s) => null;
}

/// <summary>Autoplay's suggestions from the web catalogue, as the server asks it; nothing is downloaded (the phone has no download manager).</summary>
public sealed class HybridAutoplay(WebCatalog catalog) : IAutoplayPlanner
{
    readonly ConcurrentDictionary<string, List<CatalogSong>> cache = new();
    public async Task<List<CatalogSong>> RelatedAsync(Track seed, CancellationToken ct = default)
    {
        var key = Matching.MatchKey(seed);
        if (cache.TryGetValue(key, out var c)) return c;
        List<CatalogSong> songs;
        try { songs = await catalog.RelatedAsync(seed.Artist, seed.Title, 24, ct); } catch (OperationCanceledException) { throw; } catch (Exception) { songs = []; }
        return cache[key] = songs;
    }
    public IReadOnlyList<CatalogSong> Fetching(UserSession s, IEnumerable<CatalogSong> suggested) => [];
    public void Prefetch(UserSession s, IEnumerable<CatalogSong> wanted, int max, Action onEach) { }
}

/// <summary>What a file is, as Jellyfin read it (kept for the session).</summary>
public sealed class HybridTrackInfo(JellyfinClient jf) : ITrackInfo
{
    readonly ConcurrentDictionary<string, MediaInfo> cache = new();
    public bool IsCached(Track t) => cache.ContainsKey(t.Path);
    public async Task<MediaInfo> GetAsync(UserSession s, Track t, bool retry = true, CancellationToken ct = default)
    {
        if (cache.TryGetValue(t.Path, out var have)) return have;
        MediaInfo? info = null;
        if (s.Jellyfin is { } a && t.JellyfinId is { } id) try { info = await jf.MediaInfoAsync(a, id, ct); } catch (OperationCanceledException) { throw; } catch (Exception) { }
        var ext = MediaInfo.FromExtension(t.FilePath ?? t.Path);
        info ??= new MediaInfo(ext, ext, 0, 0, 0, 0, 0, t.DurationMs, t.FilePath ?? "", "Jellyfin");
        if (info.Codec.Length == 0 && info.Container.Length == 0) info = info with { Codec = ext };
        if (info.Known) cache[t.Path] = info;
        return info with { Origin = "library" };
    }
}

/// <summary>The phone fetches nothing the library lacks (downloads are the server's): every song is simply not being downloaded.</summary>
public sealed class NoDownloads : IDownloads
{
    public IReadOnlyList<DownloadJob> JobsOf(UserSession s) => [];
    public void Subscribe(UserSession s, Action a) { }
    public void Unsubscribe(UserSession s, Action a) { }
    public void DismissFinished(UserSession s) { }
    public DownloadJob? JobFor(UserSession s, string artist, string title) => null;
    public DownloadTagStatus StatusOfKey(UserSession s, string key, long cloudAddedMs = 0, long? nowMs = null) => DownloadTagStatus.None;
    public void Song(UserSession s, CatalogSong song, bool hiRes = false, bool fallbackToNormal = true) { }
    public void Album(UserSession s, CatalogAlbum album) { }
    public Task FetchSongAsync(UserSession s, string kind, string artist, string title, string album, string? artUrl, long durationMs, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>There is one player on the phone and no dashboard to list it on.</summary>
public sealed class NoClients : IPlayerClients
{
    public void Add(string id, UserSession s, PlayerState player, string ip, string ua, string browser, string os, string screen, string language) { }
    public void Remove(string id) { }
}

/// <summary>How this build plays music.</summary>
public static class PlayerHost
{
    /// <summary>True when songs play in the app's media service (Android) rather than in the page.</summary>
    public static bool Native { get; set; }
}
