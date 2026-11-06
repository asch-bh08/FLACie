using System.Text.RegularExpressions;

namespace FLACie.Core;

/// <summary>One proposed fix of a song's artists in Jellyfin (F24): its old and new Artists and AlbumArtist, and why.</summary>
public sealed record ArtistChange(string ItemId, string Path, string Title, string Album, IReadOnlyList<string> OldArtists, string OldAlbumArtist,
    IReadOnlyList<string> NewArtists, string NewAlbumArtist, string Why);

/// <summary>
/// F24: artist names fixed per song, never by merging names. A song tagged "X" goes to "The X" when The X also has its album or its title in the
/// library, or when the catalog says the song is The X's; everything else stays with X ("Breathe" on The Prodigy's album goes to The Prodigy;
/// H.N.I.C. stays with Prodigy). "X of Y" joins "X" when the library has an artist X that is not "The X" ("Prodigy of Mobb Deep" joins Prodigy).
/// "Eminem feat. Dido" (and "Eminem &amp; Dido", "Eminem, Dido" when Eminem is an artist of his own in the library) becomes artist Eminem with Dido
/// featured; the album artist stays the lead. Pure: the plan is a dry-run list; nothing is written until the owner approves it.
/// </summary>
public static partial class ArtistResolve
{
    [GeneratedRegex(@"^(.+?)\s+(?:feat\.?|ft\.?|featuring|with)\s+(.+)$", RegexOptions.IgnoreCase)] private static partial Regex Feat();
    [GeneratedRegex(@"^(.+?)\s*(?:,|&|\band\b)\s*(.+)$", RegexOptions.IgnoreCase)] private static partial Regex Joined();
    [GeneratedRegex(@"^(.+?)\s+of\s+(.+)$", RegexOptions.IgnoreCase)] private static partial Regex OfGroup();

    static string Key(string s) => string.Join(' ', Matching.SearchWords(s));

    /// <summary>The catalog questions the plan needs ("is [title] by The X?"): songs tagged X whose album and title The X does not share here.</summary>
    public static List<(string Title, string Artist)> CatalogQuestions(IReadOnlyList<Track> songs)
    {
        var names = songs.Select(t => t.Artist).Where(n => n.Length > 0).GroupBy(Key).ToDictionary(g => g.Key, g => g.First());
        var byArtist = songs.GroupBy(t => Key(t.Artist)).ToDictionary(g => g.Key, g => g.ToList());
        var asks = new List<(string, string)>();
        foreach (var t in songs)
        {
            var k = Key(t.Artist);
            if (k.StartsWith("the ") || !byArtist.TryGetValue("the " + k, out var theirs) || !names.TryGetValue("the " + k, out var theName)) continue;
            var album = Matching.NormTitle(WebCatalog.BaseTitle(t.Album)); var title = Matching.NormTitle(WebCatalog.BaseTitle(t.Title));
            if ((album.Length > 0 && theirs.Any(x => Matching.NormTitle(WebCatalog.BaseTitle(x.Album)) == album)) || theirs.Any(x => Matching.NormTitle(WebCatalog.BaseTitle(x.Title)) == title)) continue;
            asks.Add((t.Title, theName));
        }
        return asks.Distinct().ToList();
    }

    /// <summary>The changes, one per song that needs one. [catalogSays] answers whether the catalog has [title] by [artist] (cached by the caller; null when it
    /// could not be asked, which counts as no).</summary>
    public static List<ArtistChange> Plan(IReadOnlyList<Track> songs, Func<string, string, bool?>? catalogSays = null)
    {
        // every artist name in the library as written, by its words
        var names = songs.Select(t => t.Artist).Concat(songs.Select(t => t.AlbumArtist)).Where(n => n.Length > 0)
            .GroupBy(Key).ToDictionary(g => g.Key, g => g.GroupBy(n => n).OrderByDescending(x => x.Count()).First().Key);
        var byArtist = songs.GroupBy(t => Key(t.Artist)).ToDictionary(g => g.Key, g => g.ToList());
        var changes = new List<ArtistChange>();
        foreach (var t in songs.Where(t => (t.JellyfinId ?? "").Length > 0))
        {
            var artist = t.Artist.Trim(); var k = Key(artist);
            var albumArtist = t.AlbumArtist.Length > 0 ? t.AlbumArtist : artist;
            // 1. "X" that is The X's song
            if (!k.StartsWith("the ") && names.TryGetValue("the " + k, out var theName) && byArtist.TryGetValue("the " + k, out var theirs))
            {
                var album = Matching.NormTitle(WebCatalog.BaseTitle(t.Album)); var title = Matching.NormTitle(WebCatalog.BaseTitle(t.Title));
                var why = album.Length > 0 && theirs.Any(x => Matching.NormTitle(WebCatalog.BaseTitle(x.Album)) == album) ? $"{theName} also has the album {t.Album}"
                    : theirs.Any(x => Matching.NormTitle(WebCatalog.BaseTitle(x.Title)) == title) ? $"{theName} also has {t.Title}"
                    : catalogSays?.Invoke(t.Title, theName) == true ? $"the catalog lists {t.Title} by {theName}" : null;
                if (why is not null)
                {
                    changes.Add(new(t.JellyfinId!, t.Path, t.Title, t.Album, [artist], albumArtist, [theName], Key(albumArtist) == k ? theName : albumArtist, why));
                    continue;
                }
            }
            // 2. "X of Y": the same person as X (when X is an artist of its own here, and not The X)
            if (OfGroup().Match(artist) is { Success: true } of && Key(of.Groups[1].Value) is var ok && ok.Length > 0 && !ok.StartsWith("the ") && names.TryGetValue(ok, out var person) && ok != k)
            {
                changes.Add(new(t.JellyfinId!, t.Path, t.Title, t.Album, [artist], albumArtist, [person], Key(albumArtist) == k ? person : albumArtist, $"\"{artist}\" is {person}"));
                continue;
            }
            // 3. "Eminem feat. Dido": the lead, with the others featured
            if (Split(artist, byArtist.ContainsKey, songs) is { Count: > 0 } parts && !parts.SequenceEqual([artist]))
            {
                // the album artist leads when the tag names them ("Free; Pras; Queen; Wyclef Jean" on Queen's Greatest Hits III is Queen's; owner, 6 Nov)
                if (parts.FindIndex(x => Key(x) == Key(albumArtist)) is > 0 and var ai) { var a = parts[ai]; parts.RemoveAt(ai); parts.Insert(0, a); }
                var leadName = parts[0];
                var why = parts.Count == 1 ? $"the tag \"{artist}\" is written \"{leadName}\""
                    : Feat().IsMatch(artist) ? $"{leadName} with {string.Join(", ", parts.Skip(1))} featured"
                    : $"the tag lists {parts.Count} artists: {string.Join(", ", parts)}";
                changes.Add(new(t.JellyfinId!, t.Path, t.Title, t.Album, [artist], albumArtist, parts, Key(albumArtist) == k ? leadName : albumArtist, why));
            }
        }
        return changes;
    }

    /// <summary>
    /// The separate artists in one artist tag, or null to leave the tag alone. "/" and ";" always part names, and after feat. so do "," and " x ";
    /// "&amp;", "and" (and "," in the lead) part names only when every part is an artist with songs of their own here, so bands stay whole (Diana Ross
    /// &amp; the Supremes, Earth, Wind &amp; Fire, KC And Jojo). Names keep their spelling (P!nk, Eric B.; "Bob" is not B.o.B): only capitals follow
    /// the library's own way of writing the same name. Web codes are decoded ("&amp;amp;") and a record database's number is dropped ("WizKid (4)"). A
    /// part under 3 letters that is no artist here is a cut-off tag ("feat. Th"): null, for the owner to look at.
    /// </summary>
    public static List<string>? Split(string tag, Func<string, bool> isArtist, IReadOnlyList<Track> songs)
    {
        var s = Discogs().Replace(System.Net.WebUtility.HtmlDecode(tag), "").Trim();
        var spelled = songs.Select(x => x.Artist).Where(n => n.Length > 0).GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.GroupBy(n => n).OrderByDescending(x => x.Count()).First().Key, StringComparer.OrdinalIgnoreCase);
        var ways = songs.Select(x => x.Artist).Where(n => n.Length > 0).Distinct().GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        // another way of writing the name (capitals only) is taken only when it is better by Capitals: "Lady GaGa" is Lady Gaga, "Tears For Fears" is
        // Tears for Fears, but "Kings of Leon" never becomes "Kings Of Leon", nor JME "Jme" (owner, 6 Nov)
        string Clean(string n)
        {
            n = n.Trim();
            if (!ways.TryGetValue(n, out var all)) return n;
            var best = all.OrderByDescending(Capitals).First();
            return Capitals(best) > Capitals(n) ? best : n;
        }
        // an artist here by the name as written (capitals aside): by its letters alone "Bob" was B.o.B
        bool Known(string n) => spelled.ContainsKey(n.Trim()) && isArtist(Key(n));
        // "a & b" parts only when each side is an artist here; a name that is itself an artist here is never parted
        // (or the lead is one and the others are not "the ...": Eminem & Dr. Dre parts, Diana Ross & the Supremes does not). The whole tag is not
        // asked: the song being looked at makes it "an artist here" by itself
        List<string> By(string n, string sep) => Regex.Split(n, sep, RegexOptions.IgnoreCase) is var p && p.Length > 1
            && p.Skip(1).All(x => !Key(x).StartsWith("the ")) && (p.All(x => Known(x)) || Known(p[0])) ? [.. p] : [n];
        List<string> Pair(string n) => By(n, @"\s*(?:&|\band\b)\s*");
        var (lead, rest) = Feat().Match(s) is { Success: true } f ? (f.Groups[1].Value, f.Groups[2].Value) : (s, "");
        var leads = Slashes(lead).SelectMany(x => Regex.Split(x, @"\s*;\s*")).SelectMany(x => By(x, @"\s*,\s*")).SelectMany(Pair);
        var featured = rest.Length == 0 ? [] : Slashes(rest).SelectMany(x => Regex.Split(x, @"\s*(?:[;,]|\s+x\s+|\b(?:feat\.?|ft\.?|featuring)\s)\s*", RegexOptions.IgnoreCase)).SelectMany(Pair);
        // one artist written two ways in one tag ("JAŸ-Z; JAY-Z") is one artist
        var parts = leads.Concat(featured).Select(Clean).Where(x => x.Length > 0).DistinctBy(x => Key(Fold(x))).ToList();
        if (parts.Any(x => x.Count(char.IsLetterOrDigit) < 3 && !Known(x))) return null;
        return parts;
    }

    [GeneratedRegex(@"\s*\(\d+\)(?=\s|$|[,&;/])")] private static partial Regex Discogs();

    /// <summary>A tag parted at "/", except where a side is one or two letters: that "/" is in the name (HUNTR/X, AC/DC; owner, 6 Nov).</summary>
    static List<string> Slashes(string s)
    {
        var bits = s.Split('/');
        var res = new List<string>();
        foreach (var b in bits)
        {
            static bool Short(string x) => x.Trim().TakeWhile(c => c != ',' && c != ';').Count(char.IsLetterOrDigit) < 3 || x.TrimEnd().Reverse().TakeWhile(c => c != ',' && c != ';' && c != ' ').Count(char.IsLetterOrDigit) < 3;
            if (res.Count > 0 && (Short(b) || Short(res[^1]))) res[^1] += "/" + b; else res.Add(b);
        }
        return res.Select(x => x.Trim()).ToList();
    }

    static string Fold(string s) => new(s.Normalize(System.Text.NormalizationForm.FormD).Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());

    static readonly HashSet<string> Small = ["of", "the", "at", "for", "and", "in", "a", "an", "to", "on", "by", "with", "from", "or"];

    /// <summary>How well a name is written: small words ("of", "the", "for") small after the first word, no capital inside a word after small letters
    /// ("GaGa", "DeeJay"), a one-word name begun with a capital. Ways that score the same are equally good: the tag is left as it is.</summary>
    static int Capitals(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var score = 0;
        for (var i = 0; i < words.Length; i++)
        {
            var w = words[i];
            if (i > 0 && Small.Contains(w.ToLowerInvariant())) score += w == w.ToLowerInvariant() ? 1 : -1;
            if (Regex.IsMatch(w, @"\p{Ll}\p{Lu}")) score--;
        }
        if (words.Length == 1 && words[0].Length > 0 && char.IsLower(words[0][0]) && words[0] == words[0].ToLowerInvariant()) score--;
        return score;
    }
}
