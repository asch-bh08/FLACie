using System.Security.Claims;
using FLACie.Core;

namespace FLACie.Server;

// What the Retro page asks of whatever hosts it. FLACie.Server answers with its own services (SessionStore, AutoplayPlanner, InfoService);
// the Android app (FLACie.Hybrid) answers in-process, with no server.

/// <summary>Connect and the Jam of a Jellyfin account.</summary>
public sealed record Live(Connect Connect, Jam Jam);

/// <summary>Who is signed in: their session (library, playlists, history) and, for a Jellyfin account, Connect and Jams.</summary>
public interface ISessionStore
{
    UserSession For(ClaimsPrincipal p);
    Live? LiveFor(UserSession s);
}

/// <summary>What plays when the queue runs out: suggestions for a song, and fetching the ones the library lacks.</summary>
public interface IAutoplayPlanner
{
    Task<List<CatalogSong>> RelatedAsync(Track seed, CancellationToken ct = default);
    IReadOnlyList<CatalogSong> Fetching(UserSession s, IEnumerable<CatalogSong> suggested);
    void Prefetch(UserSession s, IEnumerable<CatalogSong> wanted, int max, Action onEach);
}

/// <summary>What a file really is (codec, rate, depth), for the desk's Now Playing card and the scenes' displays.</summary>
public interface ITrackInfo
{
    bool IsCached(Track t);
    Task<MediaInfo> GetAsync(UserSession s, Track t, bool retry = true, CancellationToken ct = default);
}

/// <summary>Small hooks into the host that plain code reaches without injection.</summary>
public static class RetroHost
{
    /// <summary>The codec a song is known to be ("alac"...), when the host has read it; null when unknown.</summary>
    public static Func<Track, string?>? KnownCodec { get; set; }
    /// <summary>This build's version, for the iPods' About screens.</summary>
    public static string Version { get; set; } = "";
    /// <summary>Called on every tick of the song playing (the server holds back background work while music plays).</summary>
    public static Action? Heartbeat { get; set; }
    /// <summary>Where the players are for the DJ's "Where" ticks: the Windows app runs its server with FLACIE_PARENT_PID set (as ChartsService.InDesktopShell reads it), anything else is the web.</summary>
    public static DjSurface DjSurface { get; set; } = Environment.GetEnvironmentVariable("FLACIE_PARENT_PID") is { Length: > 0 } ? DjSurface.Windows : DjSurface.Web;
    /// <summary>Tells the account's listening log what the person did (played a song, hearted it, thumbed it down); where there is no log this does nothing.</summary>
    public static Action<UserSession, ListenEvent>? Listen { get; set; }
    /// <summary>The player's ask to the DJ failed (the server logs it; the listener just hears no line).</summary>
    public static Action<Exception>? DjFailed { get; set; }
    /// <summary>What the recommendation engine would play next for this person, given the paths played lately (the last is playing now); null when the host has no engine or it failed.</summary>
    public static Func<UserSession, IReadOnlyList<string>, int, IReadOnlyList<(Track Track, string Why)>?>? RecommendNext { get; set; }
    /// <summary>"Talk to the DJ": the answer to the recognised words (the song playing, the last few played); null when the host has no DJ.</summary>
    public static Func<UserSession, string, string?, IReadOnlyList<string>, CancellationToken, Task<DjAskAnswer>>? DjAsk { get; set; }
    /// <summary>The path of a song fetched for a request once it can be played (artist, title), else null.</summary>
    public static Func<UserSession, string, string, string?>? DjLanded { get; set; }
    /// <summary>Whether words heard by a mic are a request to the DJ ("play my pop playlist", "next") rather than something to search for.</summary>
    public static Func<string, bool>? DjIsRequest { get; set; }
}
