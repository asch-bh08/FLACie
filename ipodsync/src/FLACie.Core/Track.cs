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

    /// <summary>
    /// Typo-tolerant version of <see cref="SearchHit"/>: every query word must be inside the text, or within a couple of typos of a word in it
    /// ("beyonse" finds Beyoncé, "metalica" Metallica, "daft pnuk" Daft Punk; transposed letters count as one typo). Short words (3 letters or fewer)
    /// stay exact, 4 to 7 letters allow one typo, 8 or more allow two. Meant as the fallback when nothing matches exactly.
    /// </summary>
    public static bool FuzzyHit(IReadOnlyList<string> words, params string[] fields)
    {
        if (words.Count == 0) return false;
        var folded = Fold(string.Join(' ', fields));
        var hay = new string(folded.Where(char.IsLetterOrDigit).ToArray());
        List<string>? tokens = null;
        foreach (var w in words)
        {
            if (hay.Contains(w)) continue;
            var tol = w.Length <= 3 ? 0 : w.Length <= 7 ? 1 : 2;
            if (tol == 0) return false;
            tokens ??= folded.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => new string(x.Where(char.IsLetterOrDigit).ToArray())).Where(x => x.Length > 0).ToList();
            var ok = false;
            foreach (var t in tokens)
            {
                if (Math.Abs(t.Length - w.Length) > tol + 2) continue;
                // the whole word, or its start (a word still being typed: "metalic" against "metallica")
                if (Typos(w, t, tol) <= tol || (t.Length > w.Length && Typos(w, t[..w.Length], tol) <= tol)) { ok = true; break; }
            }
            if (!ok) return false;
        }
        return true;
    }

    /// <summary>
    /// "Did you mean ...?": for a search that only matched with typos, the words of the results that are closest to what was typed
    /// (the most common spelling wins). Returns the corrected words joined, or "" when no word needed correcting.
    /// </summary>
    public static string Suggest(IReadOnlyList<string> words, IEnumerable<string[]> results)
    {
        var sample = results.Take(300).Select(f => Fold(string.Join(' ', f)).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => new string(x.Where(char.IsLetterOrDigit).ToArray())).Where(x => x.Length > 0).Distinct().ToList()).ToList();
        var outWords = new List<string>(words.Count); var changed = false;
        foreach (var w in words)
        {
            var tol = w.Length <= 3 ? 0 : w.Length <= 7 ? 1 : 2;
            if (tol == 0 || sample.Any(toks => toks.Contains(w))) { outWords.Add(w); continue; }
            var best = ""; var bestCount = 0; var bestDist = int.MaxValue;
            var counts = new Dictionary<string, int>();
            foreach (var toks in sample)
                foreach (var t in toks)
                {
                    if (Math.Abs(t.Length - w.Length) > tol + 2) continue;
                    var d = Typos(w, t, tol);
                    if (d > tol && t.Length > w.Length) d = Typos(w, t[..w.Length], tol);
                    if (d > tol) continue;
                    counts[t] = counts.GetValueOrDefault(t) + 1;
                    var c = counts[t];
                    if (c > bestCount || (c == bestCount && d < bestDist)) { best = t; bestCount = c; bestDist = d; }
                }
            if (best.Length > 0) { outWords.Add(best); changed = true; } else outWords.Add(w);
        }
        return changed ? string.Join(' ', outWords) : "";
    }

    /// <summary>Edit distance counting a swap of two neighbouring letters as one change; gives up (returns more than <paramref name="max"/>) early.</summary>
    private static int Typos(string a, string b, int max)
    {
        if (Math.Abs(a.Length - b.Length) > max) return max + 1;
        var prev2 = new int[b.Length + 1]; var prev = new int[b.Length + 1]; var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i; var best = cur[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                var v = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) v = Math.Min(v, prev2[j - 2] + 1);
                cur[j] = v; if (v < best) best = v;
            }
            if (best > max) return max + 1;
            (prev2, prev, cur) = (prev, cur, prev2);
        }
        return prev[b.Length];
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

/// <summary>How a song's source is named on screen. A song that was just downloaded is played straight from the user's server (the file mover) while
/// Jellyfin has not scanned it yet; that is called "Streaming", and it turns into a normal Jellyfin song at the next library scan.</summary>
public static class SourceLabel
{
    public static string Short(TrackSource s) => s switch { TrackSource.Nas => "NAS", TrackSource.Cloud => "Streaming", _ => s.ToString() };
    public const string StreamingHint = "A song you just downloaded, played straight from your server while it is filed into the library. After the next Jellyfin scan it becomes a normal library song.";
    /// <summary>"Playing from ..." in the full-screen player.</summary>
    public static string PlayingFrom(TrackSource s) => s == TrackSource.Cloud ? "your server (new download)" : Short(s);
}
