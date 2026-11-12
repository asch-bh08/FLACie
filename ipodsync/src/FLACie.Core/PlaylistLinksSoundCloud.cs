using System.Text.Json;
using System.Text.RegularExpressions;

namespace FLACie.Core;

/// <summary>
/// F25.3: an artist's own SoundCloud sets (albums, singles with remixes, remix and B-side collections), found quietly through flacie-ytdl's
/// /artist-sets ({account, sets:[{title, url}]}). Only the account named exactly like the artist counts (as Deezer's best-ofs, "The" included).
/// The sets become playlists like Spotify's: source "soundcloud", the artist's own (verified), their songs read only when one is opened or played.
/// Same name twice shows once; a set named like an album already in the library is left out. Remix and B-side sets are lists of their own, so they
/// show beside the artist's best-of; the others are the artist's lists (one per purpose: a Spotify or Deezer best-of wins).
/// </summary>
public static partial class SoundCloudSets
{
    [GeneratedRegex(@"\b(remix(?:es|ed)?|b-?sides?|bootlegs?|edits|rework(?:s|ed)?|vip|dubs?)\b", RegexOptions.IgnoreCase)] private static partial Regex RemixSet();
    [GeneratedRegex(@"^https://(?:www\.|m\.)?soundcloud\.com/([A-Za-z0-9_-]+/sets/[A-Za-z0-9_-]+)")] private static partial Regex SetUrl();

    /// <summary>An uploader named exactly like the artist ("The Prodigy" is not "Prodigy"; a featured artist after the name is allowed).</summary>
    public static bool IsArtistsOwn(string uploader, string artist)
    {
        var a = Matching.SearchWords(artist); var u = Matching.SearchWords(uploader);
        return u.Count > 0 && a.SequenceEqual(u);
    }

    /// <summary>Anyone's public sets found by name (flacie-ytdl /find-sets: {sets: [{title, url, uploader, tracks, likes, art}]}; owner, 12 Nov: "if I
    /// search for a playlist I saw on Spotify or my mate's one it should be in my library"). Lists of their own (Kind Other, the uploader as subject),
    /// their songs read only when one is opened or played, like the artists' sets. Only soundcloud.com set links count; the cover only from SoundCloud's
    /// own image host.</summary>
    public static List<WebPlaylist> FromSearch(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("sets", out var sets) || sets.ValueKind != JsonValueKind.Array) return [];
        var lists = new List<WebPlaylist>();
        foreach (var s in sets.EnumerateArray().Take(20))
        {
            string Str(string n) => s.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            long Num(string n) => s.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var x) ? x : 0;
            var title = SpotifyEmbed.Clean(Str("title"));
            if (title.Length == 0 || SetUrl().Match(Str("url")) is not { Success: true } m) continue;
            var art = Str("art");
            var picture = art.StartsWith("https://i1.sndcdn.com/", StringComparison.Ordinal) && art.Length <= 400 ? art.Replace("-large.", "-t500x500.") : null;
            lists.Add(new("soundcloud", m.Groups[1].Value, title, WebPlaylistKind.Other, SpotifyEmbed.Clean(Str("uploader")), [],
                Likes: Math.Max(0, Num("likes")), Size: (int)Math.Clamp(Num("tracks"), 0, 10000), Picture: picture));
        }
        return lists;
    }

    public static List<WebPlaylist> FromAnswer(string json, string artist, IEnumerable<string> libraryAlbums)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (!r.TryGetProperty("account", out var acc) || acc.ValueKind != JsonValueKind.String || !r.TryGetProperty("sets", out var sets) || sets.ValueKind != JsonValueKind.Array) return [];
        var owned = libraryAlbums.Select(a => Matching.NormTitle(WebCatalog.BaseTitle(a))).ToHashSet();
        var seen = new HashSet<string>();
        var lists = new List<WebPlaylist>();
        foreach (var s in sets.EnumerateArray().Take(50))
        {
            var title = SpotifyEmbed.Clean(s.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : "");
            var url = s.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() ?? "" : "";
            if (title.Length == 0 || SetUrl().Match(url) is not { Success: true } m) continue;
            var key = Matching.NormTitle(title);
            if (!seen.Add(key) || owned.Contains(Matching.NormTitle(WebCatalog.BaseTitle(title)))) continue;
            var remix = RemixSet().IsMatch(title);
            lists.Add(new("soundcloud", m.Groups[1].Value, title, remix ? WebPlaylistKind.Other : WebPlaylistKind.Artist, remix ? $"{artist} remixes" : artist, [], Verified: true));
        }
        return lists;
    }
}
