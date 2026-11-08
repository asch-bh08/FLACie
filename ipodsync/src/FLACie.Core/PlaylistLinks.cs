using System.Text.Json;
using System.Text.RegularExpressions;

namespace FLACie.Core;

/// <summary>
/// A playlist or album link from another music service, read without an account or a developer key (owner, 1 Nov: "can we import a playlist from
/// another music service"). Spotify: its public embed page lists the songs (a playlist's first 100 at most). Deezer: its open API. Pure: the server
/// fetches (<c>PlaylistLinkReader</c>) and hands the answers here.
/// </summary>
public static partial class PlaylistLinks
{
    public enum Service { Spotify, Deezer, YouTube, SoundCloud, Apple }

    /// <summary>F25: what a pasted link may be, said when a link is not one of them.</summary>
    public const string Refusal = "FLACie can't read that link yet. It reads playlists and albums from Spotify, Deezer, Apple Music, YouTube, YouTube Music and SoundCloud.";

    /// <summary>The hosts flacie-ytdl may be asked about (its /playlist refuses any other), and how many songs it lists at most.</summary>
    public static readonly string[] YtdlHosts = ["youtube.com", "www.youtube.com", "m.youtube.com", "music.youtube.com", "youtu.be", "soundcloud.com", "www.soundcloud.com", "m.soundcloud.com"];
    public const int YtdlMax = 500;

    public static bool YtdlAllowed(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && YtdlHosts.Contains(u.Host.ToLowerInvariant());

    [GeneratedRegex(@"^https?://(?:www\.|m\.|music\.)?(?:youtube\.com|youtu\.be)/[^\s]*?[?&]list=([A-Za-z0-9_-]{10,64})", RegexOptions.IgnoreCase)]
    private static partial Regex YouTubeUrl();
    [GeneratedRegex(@"^https?://(?:www\.|m\.)?soundcloud\.com/([A-Za-z0-9_-]+)/sets/([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex SoundCloudUrl();
    [GeneratedRegex(@"^https?://(?:geo\.)?music\.apple\.com/([a-z]{2})/(playlist|album)/[^/?#]*/((?:pl\.)?[A-Za-z0-9.]{3,64})", RegexOptions.IgnoreCase)]
    private static partial Regex AppleUrl();
    [GeneratedRegex(@"\s*[(\[](?:official\s*(?:music\s*)?(?:video|audio|visuali[sz]er|lyric video)|lyrics?(?:\s*video)?|audio|hd|hq|4k|visuali[sz]er|video|music video|explicit|clip officiel)[)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex YouTubeNoise();
    [GeneratedRegex(@"\s+[-–—|]\s+")] private static partial Regex Dash();

    /// <summary>F25: a YouTube video's title and channel as artist and title: "Artist - Song (Official Video)" is split and cleaned; a title with no dash
    /// takes the channel as the artist ("Artist - Topic" and "ArtistVEVO" channels name the artist).</summary>
    public static (string Artist, string Title) CleanYouTube(string title, string channel)
    {
        var t = YouTubeNoise().Replace(title ?? "", "").Trim();
        var c = Regex.Replace(channel ?? "", @"\s*-\s*Topic$|VEVO$|\s*Official$", "", RegexOptions.IgnoreCase).Trim();
        if (Dash().Split(t, 2) is [var a, var song] && a.Trim().Length > 0 && song.Trim().Length > 0) return (a.Trim(), song.Trim());
        return (c, t);
    }
    public sealed record Link(Service Service, string Kind, string Id);

    [GeneratedRegex(@"open\.spotify\.com/(?:intl-[a-z]{2}/)?(?:embed/)?(playlist|album)/([A-Za-z0-9]{10,40})", RegexOptions.IgnoreCase)]
    private static partial Regex SpotifyUrl();
    [GeneratedRegex(@"deezer\.com/(?:[a-z]{2}/)?(playlist|album)/(\d{3,20})", RegexOptions.IgnoreCase)]
    private static partial Regex DeezerUrl();
    [GeneratedRegex(@"<script id=""__NEXT_DATA__"" type=""application/json"">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex NextData();

    /// <summary>What [url] points at, or null for a link this can't read.</summary>
    /// <summary>One song at a link (owner, 8 Nov): [Url] is the clean address the yt-dlp service fetches, [Service] "SoundCloud" or "YouTube".</summary>
    public sealed record Song(string Url, string Service);

    [GeneratedRegex(@"^https://(?:www\.|m\.)?soundcloud\.com/([A-Za-z0-9_-]+)/([A-Za-z0-9_-]+)/?(?:[?#].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex SoundCloudTrack();
    [GeneratedRegex(@"^https://(?:(?:www\.|m\.|music\.)?youtube\.com/watch\?(?:[^#]*&)?v=|youtu\.be/)([A-Za-z0-9_-]{11})(?:[?&#].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex YouTubeVideo();
    static readonly HashSet<string> NotTracks = new(StringComparer.OrdinalIgnoreCase) { "sets", "tracks", "albums", "reposts", "likes", "followers", "following", "popular-tracks", "comments" };

    /// <summary>A SoundCloud track or a YouTube video (not a set, a playlist or a profile), else null.</summary>
    public static Song? SongLink(string? text)
    {
        var u = (text ?? "").Trim();
        if (u.Length == 0 || u.Contains(' ')) return null;
        if (SoundCloudTrack().Match(u) is { Success: true } sc && !NotTracks.Contains(sc.Groups[2].Value) && !NotTracks.Contains(sc.Groups[1].Value)
            && sc.Groups[1].Value.ToLowerInvariant() is not ("discover" or "search" or "you" or "stream" or "charts"))
            return new($"https://soundcloud.com/{sc.Groups[1].Value}/{sc.Groups[2].Value}", "SoundCloud");
        if (YouTubeVideo().Match(u) is { Success: true } y && !u.Contains("list=", StringComparison.OrdinalIgnoreCase))
            return new($"https://www.youtube.com/watch?v={y.Groups[1].Value}", "YouTube");
        return null;
    }

    public static Link? Recognise(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (SpotifyUrl().Match(url) is { Success: true } s) return new(Service.Spotify, s.Groups[1].Value.ToLowerInvariant(), s.Groups[2].Value);
        if (DeezerUrl().Match(url) is { Success: true } d) return new(Service.Deezer, d.Groups[1].Value.ToLowerInvariant(), d.Groups[2].Value);
        var u = url.Trim();
        if (YouTubeUrl().Match(u) is { Success: true } y) return new(Service.YouTube, "playlist", y.Groups[1].Value);
        if (SoundCloudUrl().Match(u) is { Success: true } sc) return new(Service.SoundCloud, "set", sc.Groups[1].Value + "/sets/" + sc.Groups[2].Value);
        if (AppleUrl().Match(u) is { Success: true } ap) return new(Service.Apple, ap.Groups[2].Value.ToLowerInvariant(), ap.Groups[1].Value.ToLowerInvariant() + "/" + ap.Groups[3].Value);
        return null;
    }

    /// <summary>The address to fetch for [link]: Spotify's embed page, the first page of Deezer's track list, Apple Music's public page, or (YouTube,
    /// SoundCloud) the playlist's own address for flacie-ytdl.</summary>
    public static string FetchUrl(Link link) => link.Service switch
    {
        Service.Spotify => $"https://open.spotify.com/embed/{link.Kind}/{link.Id}",
        Service.Deezer => $"https://api.deezer.com/{link.Kind}/{link.Id}",
        Service.YouTube => $"https://www.youtube.com/playlist?list={link.Id}",
        Service.SoundCloud => $"https://soundcloud.com/{link.Id}",
        _ => $"https://music.apple.com/{link.Id.Split('/')[0]}/{link.Kind}/x/{link.Id.Split('/')[1]}",
    };

    /// <summary>F25: flacie-ytdl's /playlist answer ({name, tracks:[{title, artist, durationMs}]}, artist being the uploader) as a playlist: YouTube titles
    /// are cleaned into artist and title; at most <see cref="YtdlMax"/> songs.</summary>
    public static ImportedPlaylist? FromYtdl(string json, Service service)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (!r.TryGetProperty("tracks", out var list) || list.ValueKind != JsonValueKind.Array) return null;
        var tracks = new List<ImportedTrack>();
        foreach (var t in list.EnumerateArray().Take(YtdlMax))
        {
            var (artist, title) = service == Service.YouTube ? CleanYouTube(Str(t, "title") ?? "", Str(t, "artist") ?? "") : (Str(t, "artist") ?? "", Str(t, "title") ?? "");
            if (title.Length > 0 && artist.Length > 0) tracks.Add(new(artist.Trim(), title.Trim()));
        }
        var name = (Str(r, "name") ?? "").Trim();
        return tracks.Count == 0 ? null : new(name.Length > 0 ? name : service + " playlist", tracks);
    }

    /// <summary>The songs on a Spotify embed page (its name and each song's title and artists), or null when the page has none.</summary>
    public static ImportedPlaylist? FromSpotifyEmbed(string html)
    {
        var m = NextData().Match(html);
        if (!m.Success) return null;
        using var doc = JsonDocument.Parse(m.Groups[1].Value);
        if (!doc.RootElement.TryGetProperty("props", out var props) || !props.TryGetProperty("pageProps", out var pp) || !pp.TryGetProperty("state", out var st)
            || !st.TryGetProperty("data", out var data) || !data.TryGetProperty("entity", out var e)) return null;
        var name = Str(e, "name") is { Length: > 0 } n ? n : Str(e, "title") ?? "Spotify playlist";
        var album = Str(e, "type") == "album" ? name : "";
        var tracks = new List<ImportedTrack>();
        if (e.TryGetProperty("trackList", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var t in list.EnumerateArray())
            {
                var title = Str(t, "title") ?? "";
                // "subtitle" is the artists, joined with commas; the first is the lead (what a search matches best)
                var artist = (Str(t, "subtitle") ?? "").Split(',')[0].Trim();
                if (title.Length > 0 && artist.Length > 0) tracks.Add(new(artist, title, album));
            }
        return tracks.Count == 0 ? null : new(name.Trim(), tracks);
    }

    /// <summary>A Deezer playlist or album (its name, and the songs of its first page of tracks); [next] is the address of the next page, or null.</summary>
    public static ImportedPlaylist? FromDeezer(string json, out string? next)
    {
        next = null;
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (r.TryGetProperty("error", out _)) return null;
        var name = Str(r, "title") ?? "Deezer playlist";
        var albumName = r.TryGetProperty("record_type", out _) ? name : null;   // only albums carry a record type
        if (!r.TryGetProperty("tracks", out var tr)) return null;
        var tracks = DeezerTracks(tr, albumName, out next);
        return tracks.Count == 0 ? null : new(name.Trim(), tracks);
    }

    /// <summary>A further page of a Deezer track list ([json] is the answer from a [next] address).</summary>
    public static List<ImportedTrack> FromDeezerPage(string json, out string? next)
    {
        using var doc = JsonDocument.Parse(json);
        return DeezerTracks(doc.RootElement, null, out next);
    }

    static List<ImportedTrack> DeezerTracks(JsonElement page, string? albumName, out string? next)
    {
        next = Str(page, "next");
        var tracks = new List<ImportedTrack>();
        if (page.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            foreach (var t in data.EnumerateArray())
            {
                var title = Str(t, "title") ?? "";
                var artist = t.TryGetProperty("artist", out var a) ? Str(a, "name") ?? "" : "";
                var album = albumName ?? (t.TryGetProperty("album", out var al) ? Str(al, "title") ?? "" : "");
                if (title.Length > 0 && artist.Length > 0) tracks.Add(new(artist, title, album));
            }
        return tracks;
    }

    static string? Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
