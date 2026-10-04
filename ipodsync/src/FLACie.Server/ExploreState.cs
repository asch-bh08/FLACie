using FLACie.Core;

namespace FLACie.Server;

/// <summary>
/// The filters on Explore (Songs, Albums, Artists, Genres share them, so a choice carries from one tab to the next): a text filter,
/// the first letter, genre, decade, lossless or lossy, and how the list is sorted. Kept per browser tab.
/// </summary>
public sealed class ExploreState
{
    public string Query = "", Letter = "", Genre = "", Decade = "", Format = "", Sort = "az";
    public int Version { get; private set; }
    public event Action? Changed;

    public bool Filtering => Query.Length > 0 || Letter.Length > 0 || Genre.Length > 0 || Decade.Length > 0 || Format.Length > 0;
    public void Touch() { Version++; Changed?.Invoke(); }
    public void Clear() { Query = Letter = Genre = Decade = Format = ""; Touch(); }

    /// <summary>"A".."Z", or "#" for anything that doesn't start with a letter.</summary>
    public static string LetterOf(string name)
    {
        var s = Matching.SortKey(name);
        var c = s.Length > 0 ? char.ToUpperInvariant(s[0]) : '#';
        return c is >= 'A' and <= 'Z' ? c.ToString() : "#";
    }

    static bool Lossless(Track t) => MediaInfo.LosslessCodecs.Contains(InfoService.QuickFormat(t).ToLowerInvariant());
    static string DecadeOf(int year) => year >= 1900 ? (year / 10 * 10) + "s" : "";

    IReadOnlyList<string> words = [];
    string wordsFor = "\0";
    bool Words(params string[] fields)
    {
        if (wordsFor != Query) { wordsFor = Query; words = Matching.SearchWords(Query); }
        return words.Count == 0 || Matching.SearchHit(words, fields);
    }

    bool Match(Track t) =>
        (Genre.Length == 0 || t.Genre.Trim().Equals(Genre, StringComparison.OrdinalIgnoreCase))
        && (Decade.Length == 0 || DecadeOf(t.Year) == Decade)
        && (Format.Length == 0 || (Format == "lossless") == Lossless(t));

    public List<Track> Songs(Library lib)
    {
        var l = lib.Songs.Where(t => (Letter.Length == 0 || LetterOf(t.Title) == Letter) && Match(t) && Words(t.Title, t.Artist, t.Album));
        return (Sort switch
        {
            "za" => l.OrderByDescending(t => Matching.SortKey(t.Title)),
            "artist" => l.OrderBy(t => Matching.SortKey(t.Artist)).ThenBy(t => t.Album).ThenBy(t => t.DiscNo).ThenBy(t => t.TrackNo),
            "new" => l.OrderByDescending(t => t.AddedMs),
            "year" => l.OrderByDescending(t => t.Year),
            "oldest" => l.Where(t => t.Year > 0).OrderBy(t => t.Year),
            "long" => l.OrderByDescending(t => t.DurationMs),
            _ => l,
        }).ToList();
    }

    public List<Group> Albums(Library lib)
    {
        var l = lib.Albums.Where(g => (Letter.Length == 0 || LetterOf(g.Name) == Letter)
            && Words(g.Name, g.Tracks[0].AlbumArtist.Length > 0 ? g.Tracks[0].AlbumArtist : g.Tracks[0].Artist) && g.Tracks.Any(Match));
        return (Sort switch
        {
            "za" => l.OrderByDescending(g => Matching.SortKey(g.Name)),
            "artist" => l.OrderBy(g => Matching.SortKey(g.Tracks[0].AlbumArtist.Length > 0 ? g.Tracks[0].AlbumArtist : g.Tracks[0].Artist)).ThenBy(g => g.Tracks.Max(t => t.Year)),
            "new" => l.OrderByDescending(g => g.Tracks.Max(t => t.AddedMs)),
            "year" => l.OrderByDescending(g => g.Tracks.Max(t => t.Year)),
            "oldest" => l.Where(g => g.Tracks.Any(t => t.Year > 0)).OrderBy(g => g.Tracks.Where(t => t.Year > 0).Min(t => t.Year)),
            "long" => l.OrderByDescending(g => g.Tracks.Count),
            _ => l,
        }).ToList();
    }

    public List<Group> Artists(Library lib)
    {
        var l = lib.Artists.Where(g => (Letter.Length == 0 || LetterOf(g.Name) == Letter) && Words(g.Name) && g.Tracks.Any(Match));
        return (Sort switch
        {
            "za" => l.OrderByDescending(g => Matching.SortKey(g.Name)),
            "long" or "new" => l.OrderByDescending(g => g.Tracks.Count),
            _ => l,
        }).ToList();
    }

    public List<Group> Genres(Library lib)
    {
        var l = lib.Songs.Where(t => t.Genre.Length > 0 && Match(t) && (Words(t.Genre)) && (Letter.Length == 0 || LetterOf(t.Genre) == Letter))
            .GroupBy(t => t.Genre.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new Group(g.Key, g.ToList(), g.Select(t => t.ArtKey).FirstOrDefault(k => k is not null)));
        return (Sort == "az" ? l.OrderBy(g => Matching.SortKey(g.Name)) : Sort == "za" ? l.OrderByDescending(g => Matching.SortKey(g.Name)) : l.OrderByDescending(g => g.Tracks.Count).ThenBy(g => g.Name)).ToList();
    }

    /// <summary>Genres and decades to offer in the pickers, from what the library actually holds.</summary>
    public static (List<string> Genres, List<string> Decades) Choices(Library lib) => (
        lib.Songs.Where(t => t.Genre.Length > 0).GroupBy(t => t.Genre.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() >= 3).OrderBy(g => g.Key).Select(g => g.Key).ToList(),
        lib.Songs.Select(t => DecadeOf(t.Year)).Where(d => d.Length > 0).Distinct().OrderByDescending(d => d).ToList());
}
