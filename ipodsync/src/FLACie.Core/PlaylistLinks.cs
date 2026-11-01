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
    public enum Service { Spotify, Deezer }
    public sealed record Link(Service Service, string Kind, string Id);

    [GeneratedRegex(@"open\.spotify\.com/(?:intl-[a-z]{2}/)?(?:embed/)?(playlist|album)/([A-Za-z0-9]{10,40})", RegexOptions.IgnoreCase)]
    private static partial Regex SpotifyUrl();
    [GeneratedRegex(@"deezer\.com/(?:[a-z]{2}/)?(playlist|album)/(\d{3,20})", RegexOptions.IgnoreCase)]
    private static partial Regex DeezerUrl();
    [GeneratedRegex(@"<script id=""__NEXT_DATA__"" type=""application/json"">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex NextData();

    /// <summary>What [url] points at, or null for a link this can't read.</summary>
    public static Link? Recognise(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (SpotifyUrl().Match(url) is { Success: true } s) return new(Service.Spotify, s.Groups[1].Value.ToLowerInvariant(), s.Groups[2].Value);
        if (DeezerUrl().Match(url) is { Success: true } d) return new(Service.Deezer, d.Groups[1].Value.ToLowerInvariant(), d.Groups[2].Value);
        return null;
    }

    /// <summary>The address to fetch for [link]: Spotify's embed page, or the first page of Deezer's track list.</summary>
    public static string FetchUrl(Link link) => link.Service == Service.Spotify
        ? $"https://open.spotify.com/embed/{link.Kind}/{link.Id}"
        : $"https://api.deezer.com/{link.Kind}/{link.Id}";

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
