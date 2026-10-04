using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FLACie.Core;

public enum TrackSource { Jellyfin, Nas, Local, Cloud }

/// <summary>One song from one source. <see cref="Path"/> is the same string the Android app stores in playlists and
/// favourites (a Jellyfin stream URL, an smb:// URL or a local path), so the shared profile matches across devices.</summary>
public sealed record Track(
    string Path, string Title, string Artist, string Album, string AlbumArtist, int TrackNo, int DiscNo,
    long DurationMs, int Year, string? ArtKey, long AddedMs, TrackSource Source, string? FilePath = null, string? JellyfinId = null, string Genre = "", long Size = 0)
{
    public string AlbumKey => Matching.PrimaryArtist(AlbumArtist.Length > 0 ? AlbumArtist : Artist) + "|" + Matching.AlbumNorm(Album);
}

public sealed record Group(string Name, IReadOnlyList<Track> Tracks, string? ArtKey);

/// <summary>Ported from the Android app (library/Track.kt, Library.kt) so both merge and search the same way.</summary>
public static partial class Matching
{
    [GeneratedRegex(@"\s*[(\[](feat\.?|ft\.?|featuring|with)\s[^)\]]*[)\]]", RegexOptions.IgnoreCase)] private static partial Regex FeatParen();
    [GeneratedRegex(@"\s+(feat\.?|ft\.?|featuring)\s.*$", RegexOptions.IgnoreCase)] private static partial Regex FeatTail();
    [GeneratedRegex(@"[^\p{L}\p{N}]+")] private static partial Regex NonAlnum();
    [GeneratedRegex(@"\s*(;|,|&|/|\s+x\s+|\s+(feat\.?|ft\.?|featuring|with)\s+)\s*", RegexOptions.IgnoreCase)] private static partial Regex ArtistSplit();
    [GeneratedRegex(@"[(\[][^)\]]*[)\]]")] private static partial Regex Edition();
    [GeneratedRegex(@"/Audio/([0-9a-fA-F]{32})/")] private static partial Regex JellyfinItem();

    public static string NormTitle(string s) => NonAlnum().Replace(FeatTail().Replace(FeatParen().Replace(s, ""), "").ToLowerInvariant(), " ").Trim();

    /// <summary>Just the lead artist, lower-case, with a leading "The" dropped ("The Black Eyed Peas" = "Black Eyed Peas").</summary>
    public static string PrimaryArtist(string s)
    {
        var first = ArtistSplit().Split(s).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? "";
        var a = string.Join(' ', NonAlnum().Replace(first.ToLowerInvariant(), " ").Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return a.StartsWith("the ") && a.Length > 4 ? a[4..] : a;
    }

    public static string MatchKey(string title, string artist) => NormTitle(title) + "|" + PrimaryArtist(artist);
    public static string MatchKey(Track t) => MatchKey(t.Title, t.Artist);
    public static string AlbumNorm(string album) => new(Edition().Replace(album.ToLowerInvariant(), "").Where(char.IsLetterOrDigit).ToArray());
    public static string MergeKey(Track t) => MatchKey(t) + "|" + AlbumNorm(t.Album);

    public static string? JellyfinIdIn(string path) { var m = JellyfinItem().Match(path); return m.Success ? m.Groups[1].Value : null; }

    /// <summary>The same file seen through different sources (the NAS share and Jellyfin's view of it): last three path
    /// segments, decoded and lower-cased.</summary>
    public static string? FileKey(Track t)
    {
        var p = t.Source switch { TrackSource.Jellyfin => t.FilePath, TrackSource.Nas => t.Path, _ => null };
        if (string.IsNullOrWhiteSpace(p)) return null;
        string decoded;
        try { decoded = Uri.UnescapeDataString(p); } catch { decoded = p; }
        var segs = decoded.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segs.Length < 2 ? null : string.Join('/', segs.TakeLast(3)).ToLowerInvariant();
    }

    // ---- search: "scream and shout will i am" finds "Scream & Shout" by will.i.am ----

    public static IReadOnlyList<string> SearchWords(string query) =>
        Fold(query).Split(' ').Select(w => new string(w.Where(char.IsLetterOrDigit).ToArray())).Where(w => w.Length > 0).ToList();

    public static bool SearchHit(IReadOnlyList<string> words, params string[] fields)
    {
        if (words.Count == 0) return false;
        var hay = new string(Fold(string.Join(' ', fields)).Where(char.IsLetterOrDigit).ToArray());
        return words.All(hay.Contains);
    }

    private static string Fold(string s)
    {
        var d = s.ToLowerInvariant().Replace("&", " and ").Replace("+", " and ").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString();
    }

    public static string SortKey(string s)
    {
        var t = s.Trim();
        foreach (var a in new[] { "the ", "a ", "an " }) if (t.StartsWith(a, StringComparison.OrdinalIgnoreCase)) return t[a.Length..].ToLowerInvariant();
        return t.ToLowerInvariant();
    }
}
