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
    void Song(UserSession s, CatalogSong song, bool hiRes = false, bool fallbackToNormal = true);
    void Album(UserSession s, CatalogAlbum album);
    Task FetchSongAsync(UserSession s, string kind, string artist, string title, string album, string? artUrl, long durationMs, CancellationToken ct = default);
}

/// <summary>The player windows open on this account (the server lists them on the dashboard).</summary>
public interface IPlayerClients
{
    void Add(string id, UserSession s, PlayerState player, string ip, string ua, string browser, string os, string screen, string language);
    void Remove(string id);
}

/// <summary>Small hooks into the host and what it offers.</summary>
public static class AppHost
{
    /// <summary>True on the server: downloads, import, Connect, Jams, the dashboard and the charts are offered. The phone app shows only what it can do itself.</summary>
    public static bool Server { get; set; } = true;
    /// <summary>The Connect device picker in the player bar (the server's, which knows Alexa); none when null.</summary>
    public static Type? DevicePicker { get; set; }
    /// <summary>The album a song on a compilation really belongs to, looked up online; null when unknown.</summary>
    public static Func<string, string, CancellationToken, Task<string?>>? OnlineAlbum { get; set; }
    /// <summary>How many songs are still waiting to have their real format read.</summary>
    public static Func<int> ProbePending { get; set; } = () => 0;
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
