using FLACie.Core;

namespace FLACie.Server;

// What the shared FLACie pages (library, search, playlists, the player bar and full-screen player) ask of whatever hosts them.
// FLACie.Server answers with its own services; the Android app (FLACie.Hybrid) answers on the phone, with no server.

/// <summary>Where the app keeps its files (the server's data folder, or the phone's app data).</summary>
public sealed record DataPaths(string Root);

/// <summary>Fetching songs and albums the library lacks, and how far each fetch has got (DownloadManager on the server).</summary>
public interface IDownloads
{
    IReadOnlyList<DownloadJob> JobsOf(UserSession s);
    void Subscribe(UserSession s, Action a);
    void Unsubscribe(UserSession s, Action a);
    void DismissFinished(UserSession s);
    DownloadJob? JobFor(UserSession s, string artist, string title);
    DownloadTagStatus StatusOfKey(UserSession s, string key, long cloudAddedMs = 0, long? nowMs = null);
    /// <summary>[kind] "Playlist": asked for as interest shows (F23), not by the person: kept out of Search's Downloads panel and the done digest.</summary>
    void Song(UserSession s, CatalogSong song, bool hiRes = false, bool fallbackToNormal = true, string kind = "Search");
    void Album(UserSession s, CatalogAlbum album);
    Task FetchSongAsync(UserSession s, string kind, string artist, string title, string album, string? artUrl, long durationMs, CancellationToken ct = default);
    /// <summary>This server downloads music at all (the Dashboard's "Allow downloads", see <see cref="DownloadPolicy"/>). When false the pages show no
    /// download buttons and no "set up downloads" hints: to the person using it, FLACie is simply a player of the music they have.</summary>
    bool Allowed { get; }
    /// <summary>Downloads are allowed and this person has download services set up, so a download button does something.</summary>
    bool Ready(UserSession s);
    /// <summary>This person may use "Download all" and import playlist files (administrators, or everyone when the Dashboard says so).</summary>
    bool MayBulk(UserSession s);
}

/// <summary>The player windows open on this account (the server lists them on the dashboard).</summary>
public interface IPlayerClients
{
    void Add(string id, UserSession s, PlayerState player, string ip, string ua, string browser, string os, string screen, string language);
    void Remove(string id);
}

/// <summary>Small hooks into the host and what it offers.</summary>
/// <summary>F23: playlists from outside (Spotify, Deezer, SoundCloud) and FLACie's own, for Search, and their songs downloaded as interest shows.</summary>
public interface IWebPlaylists
{
    /// <summary>The All view's lists for a search (fetched as needed, politely), best first.</summary>
    Task<IReadOnlyList<WebPlaylist>> ForSearchAsync(UserSession s, string query, CancellationToken ct);
    /// <summary>The Playlists filter: every list that matches, best first; [genre] narrows them.</summary>
    Task<IReadOnlyList<WebPlaylist>> ForFilterAsync(UserSession s, string query, string? genre, CancellationToken ct);
    /// <summary>A list shown before (kept for 30 days since it was last used), or null.</summary>
    WebPlaylist? Find(string source, string id);
    /// <summary>Someone showed interest (opened it, played from it at [from], searched its artist, or it was shown as a popular list): its missing songs
    /// are queued for download by <see cref="PlaylistFetchPolicy"/>.</summary>
    void Interest(UserSession s, WebPlaylist list, FetchTrigger trigger, int from = 0);
    /// <summary>The list with its songs: a list found by name only (a SoundCloud set) has them read now, once (F25.3).</summary>
    Task<WebPlaylist> FillAsync(UserSession s, WebPlaylist list, CancellationToken ct);
}

public static class AppHost
{
    /// <summary>F23: outside playlists (the server sets it; null on the phone app and in tests, where only FLACie's own lists show).</summary>
    public static IWebPlaylists? WebPlaylists { get; set; }
    /// <summary>True on the server: downloads, import, Connect, Jams, the dashboard and the charts are offered. The phone app shows only what it can do itself.</summary>
    public static bool Server { get; set; } = true;
    /// <summary>The Connect device picker in the player bar (the server's, which knows Alexa); none when null.</summary>
    public static Type? DevicePicker { get; set; }
    /// <summary>The album a song on a compilation really belongs to, looked up online; null when unknown.</summary>
    public static Func<string, string, CancellationToken, Task<string?>>? OnlineAlbum { get; set; }
    /// <summary>How many songs are still waiting to have their real format read.</summary>
    public static Func<int> ProbePending { get; set; } = () => 0;
    /// <summary>The account's listening log, or null while it has none (Home's picks then learn from the history and favourites).</summary>
    public static Func<UserSession, IReadOnlyList<ListenEvent>?> ListenLog { get; set; } = _ => null;
    /// <summary>How a song feels, where it has been analysed.</summary>
    public static Func<Track, Vibe?> Vibe { get; set; } = _ => null;
}

/// <summary>The short format badge on a song row ("FLAC", "MP3", "HI-RES").</summary>
public static class QuickFormats
{
    public static string Of(Track t)
    {
        var src = t.FilePath ?? (t.Source == TrackSource.Jellyfin ? "" : t.Path);
        if (t.Source == TrackSource.Cloud && src.IndexOf("path=", StringComparison.Ordinal) is var at and >= 0) src = Uri.UnescapeDataString(src[(at + 5)..]);
        // only the real facts (ffprobe / Jellyfin / TagLib) say Hi-Res: never the file name, bit rate or extension. Until they are known the plain extension shows.
        if (FormatIndex.Current?.Get(t) is { HiRes: true }) return "HI-RES";
        var e = MediaInfo.FromExtension(src).ToUpperInvariant();
        return e switch { "" => "", "MPEG" => "MP3", "M4A" => "M4A", _ => e };
    }
}
