namespace FLACie.Core;

/// <summary>
/// How the search page lays out what it found (owner, 25 Nov: "heads will roll" showed playlists and downloads before the song the library has). The
/// song asked for comes first; the catalog ("More music to download") comes last, and when the library already has the song asked for, the catalog's
/// copies of it (remixes, live takes, karaoke, other singers' covers) are folded under one small line instead of filling rows.
/// The phone has the same rules in SearchLayout.kt.
/// </summary>
public static class SearchLayout
{
    /// <summary>The song whatever the version and whoever sings it: "Heads Will Roll", "Heads Will Roll (A-Trak Remix)" and
    /// "Heads Will Roll - Live" are one.</summary>
    public static string TitleCore(string title) => Matching.SongKey(title, "").Split('|')[0];

    /// <summary>The library's best song when it is the song asked for: every word of its name was typed (the artist may be typed too).
    /// "heads will roll" asks for Heads Will Roll; "heads" might mean another song, so nothing is folded for it.</summary>
    public static string? AskedSong(string query, string? bestTitle)
    {
        if (string.IsNullOrWhiteSpace(bestTitle)) return null;
        var typed = Matching.SearchWords(query).ToHashSet();
        var name = Matching.SearchWords(TitleCore(bestTitle));
        return name.Count > 0 && name.All(typed.Contains) ? bestTitle : null;
    }

    /// <summary>The catalog's songs split in two: the rows shown, and the copies of the song the library has (folded away).
    /// Without a song asked for, everything is shown.</summary>
    public static (List<T> Shown, List<T> Folded) FoldCopies<T>(IEnumerable<T> catalog, Func<T, string> titleOf, string? ownedTitle)
    {
        if (string.IsNullOrWhiteSpace(ownedTitle)) return (catalog.ToList(), []);
        var core = TitleCore(ownedTitle);
        List<T> shown = [], folded = [];
        foreach (var s in catalog) (TitleCore(titleOf(s)) == core ? folded : shown).Add(s);
        return (shown, folded);
    }

    /// <summary>What the Top result is: the best match of any kind (owner, 26 Nov: "that could be a playlist if I search for 2026").</summary>
    public enum TopKind { None, Artist, Album, Playlist, Song }

    /// <summary>Picks the Top result. [songExact]: the best song is named exactly what was typed, so it wins over everything. Otherwise an artist named like
    /// the search, then an album, then a playlist named so, then the best song.</summary>
    public static TopKind PickTop(bool artist, bool album, bool playlist, bool song, bool songExact)
    {
        if (songExact && song) return TopKind.Song;
        if (artist) return TopKind.Artist;
        if (album) return TopKind.Album;
        if (playlist) return TopKind.Playlist;
        return song ? TopKind.Song : TopKind.None;
    }

    /// <summary>The width from which the Top result and the Playlists sit side by side, half each, with equal weight (owner, 26 Nov: "50/50 ... on smaller
    /// aspect ratios or phone make the top result first"). Narrower than this, and on the phone, the Top result comes first, full width, then the rest.</summary>
    public const int SideBySideMinWidth = 900;
    public static bool SideBySide(int width) => width >= SideBySideMinWidth;
}
