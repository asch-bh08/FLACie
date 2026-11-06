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
    public static List<ArtistChange> Plan(IReadOnlyList<Track> songs, Func<string, string, bool?>? catalogSays = null, Func<string, string?>? spelling = null)
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
            if (Split(artist, byArtist.ContainsKey, songs, t.AlbumArtist, spelling) is { Count: > 0 } parts && !parts.SequenceEqual([artist]))
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

    /// <summary>The names whose capitals only the catalog can settle (asked once each, kept by the caller): a name the library writes more than one way,
    /// one with a capital inside a word ("Lady GaGa", "OutKast", "DJ BoBo"), one all in small letters ("train", "a-ha") and one with a capital small word
    /// ("Tyler, The Creator"). Without the catalog's answer
    /// such a name is left as it is.</summary>
    public static List<string> SpellingQuestions(IReadOnlyList<Track> songs)
    {
        var all = songs.Select(x => x.Artist).Where(n => n.Length > 0)
            .SelectMany(n => Regex.Split(Feat().Replace(n, "$1, $2"), @"\s*(?:[;/,&]|\band\b)\s*", RegexOptions.IgnoreCase))
            .Select(n => n.Trim()).Where(n => n.Count(char.IsLetter) >= 2).Distinct().ToList();
        var variants = all.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).SelectMany(g => g);
        var odd = all.Where(n => Regex.IsMatch(n, @"\p{Ll}\p{Lu}") || (n.Any(char.IsLetter) && !n.Any(char.IsUpper)) || SmallWords(n) != n);
        return variants.Concat(odd).Distinct().ToList();
    }

    /// <summary>What the library knows once per scan, not once per song (planning 12,000 songs took 6 minutes when this was worked out for each).</summary>
    sealed class Names(IReadOnlyList<Track> songs)
    {
        public readonly HashSet<string> Spelled = songs.Select(x => x.Artist.Trim()).Where(n => n.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        /// <summary>Every name exactly as the library writes it.</summary>
        public readonly HashSet<string> Exact = songs.Select(x => x.Artist.Trim()).Concat(songs.Select(x => x.AlbumArtist.Trim())).Where(n => n.Length > 0).ToHashSet(StringComparer.Ordinal);
        public readonly Dictionary<string, int> AlbumArtistSongs = songs.GroupBy(x => Key(x.AlbumArtist)).ToDictionary(g => g.Key, g => g.Count());
    }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IReadOnlyList<Track>, Names> namesOf = new();

    /// <summary>
    /// The separate artists in one artist tag, or null to leave the tag alone. "/" and ";" always part names, and after feat. so do "," and " x ";
    /// "&amp;", "and" (and "," in the lead) part names only when the lead is an artist with songs of their own here and the others are not "the ...", so
    /// bands stay whole (Diana Ross &amp; the Supremes, Earth, Wind &amp; Fire, KC And Jojo); the last "&amp;" of a list with commas is part of the list
    /// (Jax Jones, Martin Solveig &amp; Madison Beer) unless that pair is an act here (Sly &amp; Robbie). Capitals: the catalog's way of writing a name
    /// when [spelling] knows it (Lady Gaga, OutKast, DJ BoBo, a-ha), else only small words made small ("Kings Of Leon" is Kings of Leon); nothing is
    /// copied from another way the library writes it. Web codes are decoded ("&amp;amp;"), a record database's number is dropped ("WizKid (4)"), and a
    /// tag with a broken character or a cut-off part ("feat. Th") is null, for the owner to look at.
    /// </summary>
    public static List<string>? Split(string tag, Func<string, bool> isArtist, IReadOnlyList<Track> songs, string? albumArtist = null, Func<string, string?>? spelling = null)
    {
        var s = Discogs().Replace(System.Net.WebUtility.HtmlDecode(tag), "").Trim();
        // a broken character, or a song's title in the artist tag ("The Way You Move - Outkast Featuring Sleepy Brown"): for the owner to look at
        if (s.Contains('\uFFFD') || s.Contains(" - ")) return null;
        var names = namesOf.GetValue(songs, l => new Names(l));
        string Clean(string n)
        {
            n = n.Trim();
            if (spelling?.Invoke(n) is { Length: > 0 } c && string.Equals(c, n, StringComparison.OrdinalIgnoreCase)) return c;
            // small words made small only when the library already writes the name so ("Kings of Leon" beside "Kings Of Leon"): never on a guess
            // ("Lady A" is not "Lady a", "Tyler, The Creator" keeps its capital, an album's name in the artist tag is left alone; owner, 6 Nov)
            var small = SmallWords(n);
            return small != n && names.Exact.Contains(small) ? small : n;
        }
        // an artist here by the name as written (capitals aside): by its letters alone "Bob" was B.o.B
        bool Known(string n) => names.Spelled.Contains(n.Trim()) && isArtist(Key(n));
        bool NoThe(IEnumerable<string> rest) => rest.All(x => !Key(x).StartsWith("the "));
        List<string> Pair(string n) => Regex.Split(n, @"\s*(?:&|\band\b)\s*", RegexOptions.IgnoreCase) is var p && p.Length > 1 && NoThe(p.Skip(1)) && Known(p[0]) ? [.. p] : [n];
        // the last of a list: "Martin Solveig & Madison Beer" is two, "Sly & Robbie" (an act here) one
        // ("Sly & Robbie", one-word names nobody here knows apart, stays one act)
        List<string> Last(string n) => Known(n) ? [n] : Regex.Split(n, @"\s*(?:&|\band\b)\s*", RegexOptions.IgnoreCase) is var p && p.Length > 1 && NoThe(p.Skip(1))
            && p.Any(x => x.Trim().Contains(' ') || Known(x)) ? [.. p] : [n];
        List<string> List(string[] items) => items.Length < 2 ? Pair(items[0]) : [.. items[..^1].SelectMany(Pair), .. Last(items[^1])];
        List<string> Commas(string n) => Regex.Split(n, @"\s*,\s*") is var p && p.Length > 1 && Known(p[0]) ? List(p) : Pair(n);
        var (lead, rest) = Feat().Match(s) is { Success: true } f ? (f.Groups[1].Value, f.Groups[2].Value) : (s, "");
        var leads = Slashes(lead).SelectMany(x => Regex.Split(x, @"\s*;\s*")).SelectMany(Commas);
        var featured = rest.Length == 0 ? [] : Slashes(rest).SelectMany(x => List(Regex.Split(x, @"\s*(?:[;,]|\s+x\s+|\b(?:feat\.?|ft\.?|featuring)\s)\s*", RegexOptions.IgnoreCase)));
        // one artist written two ways in one tag ("JAŸ-Z; JAY-Z", "Ms. Lauryn Hill; Lauryn Hill") is one artist
        var parts = leads.Concat(featured).Select(Clean).Where(x => x.Length > 0).DistinctBy(x => Key(Fold(Honorific().Replace(x, "")))).ToList();
        if (parts.Any(x => x.Count(char.IsLetterOrDigit) < 3 && !Known(x))) return null;
        // an act whose album is credited to exactly this name ("Eric B &amp; Rakim" on Paid In Full, "Panda Bear and Sonic Boom" on Reset) is one name
        // (owner, 6 Nov); a list with ";" or "/" or "," or "feat." is never a name
        if (albumArtist is { Length: > 0 } aa && Key(aa) == Key(s) && !Regex.IsMatch(s, "[;/,]") && !Feat().IsMatch(s)
            && names.AlbumArtistSongs.GetValueOrDefault(Key(s)) >= 3) return [Clean(s)];
        return parts;
    }

    [GeneratedRegex(@"^(?:ms|mr|mrs|miss)\.?\s+", RegexOptions.IgnoreCase)] private static partial Regex Honorific();

    /// <summary>Small words made small after the first word ("Kings Of Leon" is Kings of Leon, "Panic! At The Disco" is Panic! at the Disco); every
    /// other word kept exactly as it is written.</summary>
    static string SmallWords(string name)
    {
        var words = name.Split(' ');
        var seen = false;
        for (var i = 0; i < words.Length; i++)
        {
            if (words[i].Length == 0) continue;
            if (seen && Small.Contains(words[i].ToLowerInvariant())) words[i] = words[i].ToLowerInvariant();
            seen = true;
        }
        return string.Join(' ', words);
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

    static readonly HashSet<string> Small = ["of", "the", "at", "for", "and", "in", "a", "an", "to", "on", "by", "with", "from", "or", "but", "nor", "vs"];

}
