namespace FLACie.Core;

/// <summary>What a search was for, which decides how many playlists it shows (F23).</summary>
public enum PlaylistQueryKind { Playlists, Artist, Song }

/// <summary>
/// F23: which outside playlists a search shows. One list per purpose (an artist's best-of, a decade, a genre) from the best source: Spotify, then Deezer,
/// then SoundCloud, then FLACie's own. SoundCloud lists are ranked against the others found for the same search (likes, plays and reposts per day since
/// posted; the artist's or label's verified account first) and one under 1,000 likes or in the bottom half is never shown. A playlist, genre or decade
/// search leads with a full row; an artist search shows their best-of and a few close lists; a song search one or none; each needs a relevance near the
/// best one's. The Playlists filter shows them all, best first, and a genre narrows them.
/// </summary>
public static class WebPlaylistPick
{
    public static readonly string[] SourceOrder = ["spotify", "deezer", "soundcloud", "own"];
    public const int FullRow = 12, ArtistRow = 8, SongRow = 1, MinLikes = 1000;
    /// <summary>A list shows only with a relevance this share of the best one's or more.</summary>
    public const double Near = 0.6;

    static int Rank(string source) { var i = Array.IndexOf(SourceOrder, source); return i < 0 ? SourceOrder.Length : i; }

    public static List<WebPlaylist> OnePerPurpose(IEnumerable<WebPlaylist> lists) =>
        lists.GroupBy(l => l.Purpose).Select(g => g.OrderBy(l => Rank(l.Source)).First()).ToList();

    /// <summary>SoundCloud's lists found for one search, ranked; the weak ones (under 1,000 likes, or the bottom half) left out, the verified kept.</summary>
    public static List<WebPlaylist> SoundCloudShown(IEnumerable<WebPlaylist> found, DateTime nowUtc)
    {
        var sc = found.Where(l => l.Source == "soundcloud").ToList();
        double Pull(WebPlaylist l) { var days = Math.Max(1, (nowUtc - (l.PostedUtc ?? nowUtc.AddDays(-365))).TotalDays); return (l.Likes + l.Plays / 100.0 + l.Reposts * 2.0) / days; }
        var ranked = sc.OrderByDescending(l => l.Verified).ThenByDescending(Pull).ToList();
        var keep = ranked.Take((ranked.Count + 1) / 2).ToHashSet();
        return ranked.Where(l => l.Verified || (l.Likes >= MinLikes && keep.Contains(l))).ToList();
    }

    static readonly HashSet<string> Filler = ["playlist", "playlists", "songs", "song", "music", "the", "best", "of", "mix", "top", "hits", "tracks", "a", "my", "some"];
    static readonly string[] PlaylistWords = ["playlist", "playlists", "mix", "workout", "gym", "party", "chill", "running", "study", "sleep", "top hits", "charts", "chart"];

    /// <summary>What [query] is after: playlists (it says so, or names a genre or a decade), an artist in the library, or else a song.</summary>
    public static PlaylistQueryKind Classify(string query, IEnumerable<string> artists, IEnumerable<string> genres)
    {
        var q = string.Join(' ', Matching.SearchWords(query));
        if (PlaylistWords.Any(w => (" " + q + " ").Contains(" " + w + " ")) || Decade(q) is not null || genres.Any(g => string.Join(' ', Matching.SearchWords(g)) == q))
            return PlaylistQueryKind.Playlists;
        static string Key(string s) { var w = string.Join(' ', Matching.SearchWords(s)); return w.StartsWith("the ") ? w[4..] : w; }
        return artists.Any(a => Key(a) == Key(query)) ? PlaylistQueryKind.Artist : PlaylistQueryKind.Song;
    }

    /// <summary>"90s", "1990s", "2010s": the decade's first year; null for anything else.</summary>
    public static int? Decade(string q)
    {
        var m = System.Text.RegularExpressions.Regex.Match(q.Trim().ToLowerInvariant(), @"^(?:(19|20)(\d)0s?|(\d)0s)$");
        if (!m.Success) return null;
        return m.Groups[1].Success ? int.Parse(m.Groups[1].Value) * 100 + int.Parse(m.Groups[2].Value) * 10 : int.Parse(m.Groups[3].Value) is var d && d <= 2 ? 2000 + d * 10 : 1900 + d * 10;
    }

    /// <summary>How well a list answers the search, 0 to 1.5: the share of the search's words in its name and subject, more for its subject exactly.</summary>
    public static double Relevance(WebPlaylist l, string query)
    {
        var words = Matching.SearchWords(query).Where(w => !Filler.Contains(w)).ToList();
        if (words.Count == 0) return 0;
        var hay = Matching.SearchWords(l.Name + " " + l.Subject).ToHashSet();
        var share = words.Count(hay.Contains) / (double)words.Count;
        static string Key(string s) { var w = string.Join(' ', Matching.SearchWords(s)); return w.StartsWith("the ") ? w[4..] : w; }
        var subject = Key(l.Subject) == Key(string.Join(' ', words)) || (Decade(query) is { } d && l.Kind == WebPlaylistKind.Decade && l.Subject.StartsWith(d.ToString()));
        return share + (subject ? 0.5 : 0);
    }

    /// <summary>
    /// For an artist search, a list that holds their songs (All Out 90s with Firestarter, our own Electronic mix): 0.95 for one song, up to 1.2 for six
    /// or more, so it shows beside the best-of (which scores 1.5 on its name). The artist is matched exactly, "The" included: The Prodigy is not
    /// Prodigy of Mobb Deep. A featuring credit after the name still counts.
    /// </summary>
    public static double Holds(WebPlaylist l, string artist)
    {
        var name = Matching.SearchWords(artist);
        if (name.Count == 0) return 0;
        var n = l.Tracks.Count(t => Matching.SearchWords(t.Artist) is var w && w.Count >= name.Count && w.Take(name.Count).SequenceEqual(name)
            && (w.Count == name.Count || w[name.Count] is "feat" or "ft" or "featuring" or "with" or "x" or "and"));
        return n == 0 ? 0 : 0.9 + 0.05 * Math.Min(n, 6);
    }

    /// <summary>The All view ([artist]: the artist an artist search found, for <see cref="Holds"/>): as many lists as the search is about, each near the best one's relevance, one per purpose, strongest first.</summary>
    public static List<WebPlaylist> ForAll(IEnumerable<WebPlaylist> found, string query, PlaylistQueryKind kind, DateTime nowUtc, string? artist = null)
    {
        var scored = Candidates(found, nowUtc).Select(l => (l, r: kind == PlaylistQueryKind.Artist ? Math.Max(Relevance(l, query), Holds(l, artist ?? query)) : Relevance(l, query))).Where(x => x.r > 0).ToList();
        if (scored.Count == 0) return [];
        var best = scored.Max(x => x.r);
        var take = kind switch { PlaylistQueryKind.Playlists => FullRow, PlaylistQueryKind.Artist => ArtistRow, _ => SongRow };
        // a song search shows a list only when it is clearly about it (its subject)
        var floor = kind == PlaylistQueryKind.Song ? Math.Max(1.0, best * Near) : best * Near;
        return scored.Where(x => x.r >= floor).OrderByDescending(x => x.r).ThenBy(x => Rank(x.l.Source)).Select(x => x.l).Take(take).ToList();
    }

    /// <summary>The Playlists filter: every list that matches at all, best first; [genre] narrows them to that genre's lists and lists with it in their name.</summary>
    public static List<WebPlaylist> ForFilter(IEnumerable<WebPlaylist> found, string query, string? genre, DateTime nowUtc)
    {
        var all = Candidates(found, nowUtc);
        if (genre is { Length: > 0 } g)
        {
            var gw = string.Join(' ', Matching.SearchWords(g));
            all = all.Where(l => (l.Kind == WebPlaylistKind.Genre && string.Join(' ', Matching.SearchWords(l.Subject)) == gw) || (" " + string.Join(' ', Matching.SearchWords(l.Name)) + " ").Contains(" " + gw + " ")).ToList();
        }
        return all.Select(l => (l, r: query.Trim().Length == 0 ? 1 : Relevance(l, query))).Where(x => x.r > 0).OrderByDescending(x => x.r).ThenBy(x => Rank(x.l.Source)).Select(x => x.l).ToList();
    }

    /// <summary>The lists that may show at all: SoundCloud's strong ones, then one per purpose.</summary>
    static List<WebPlaylist> Candidates(IEnumerable<WebPlaylist> found, DateTime nowUtc)
    {
        var list = found.ToList();
        var sc = SoundCloudShown(list, nowUtc).ToHashSet();
        return OnePerPurpose(list.Where(l => l.Source != "soundcloud" || sc.Contains(l)));
    }
}
