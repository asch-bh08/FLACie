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
            var (lead, rest) = Feat().Match(artist) is { Success: true } f ? (f.Groups[1].Value.Trim(), f.Groups[2].Value)
                : Joined().Match(artist) is { Success: true } j && names.ContainsKey(Key(j.Groups[1].Value)) && byArtist.ContainsKey(Key(j.Groups[1].Value)) ? (j.Groups[1].Value.Trim(), j.Groups[2].Value)
                : (null, null);
            if (lead is not null && rest is not null && Key(lead).Length > 0)
            {
                var featured = Regex.Split(rest, @"\s*(?:,|&|\band\b|feat\.?|ft\.?)\s*", RegexOptions.IgnoreCase).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                var leadName = names.GetValueOrDefault(Key(lead), lead);
                changes.Add(new(t.JellyfinId!, t.Path, t.Title, t.Album, [artist], albumArtist, [leadName, .. featured], Key(albumArtist) == k ? leadName : albumArtist,
                    $"{leadName} with {string.Join(", ", featured)} featured"));
            }
        }
        return changes;
    }
}
