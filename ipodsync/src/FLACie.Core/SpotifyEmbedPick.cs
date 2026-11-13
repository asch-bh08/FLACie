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
    public static readonly string[] SourceOrder = ["spotify", "deezer", "soundcloud", "youtube", "own"];
    public const int FullRow = 12, ArtistRow = 15, SongRow = 1, MinLikes = 1000;
    /// <summary>F26.1: an artist search shows our own made lists only when it found fewer real lists than this (and always after them).</summary>
    public const int RealEnough = 4, MinSize = 8;
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

    /// <summary>F26.1: the list's name holds the artist (exact words, "The" kept: "The Prodigy Mix", "Best of The Prodigy"; not "Prodigy Mixtape").</summary>
    public static bool TitleHolds(string title, string artist)
    {
        var name = Matching.SearchWords(artist); var t = Matching.SearchWords(title);
        if (name.Count == 0) return false;
        for (var i = 0; i + name.Count <= t.Count; i++)
            if (t.Skip(i).Take(name.Count).SequenceEqual(name) && (i == 0 || t[i - 1] != "the" || name[0] == "the")) return true;
        return false;
    }

    /// <summary>How many of the artist's songs a list holds (as <see cref="Holds"/> matches them).</summary>
    public static int HoldsCount(WebPlaylist l, string artist)
    {
        var name = Matching.SearchWords(artist);
        return name.Count == 0 ? 0 : l.Tracks.Count(t => Matching.SearchWords(t.Artist) is var w && w.Count >= name.Count && w.Take(name.Count).SequenceEqual(name)
            && (w.Count == name.Count || w[name.Count] is "feat" or "ft" or "featuring" or "with" or "x" or "and"));
    }

    /// <summary>
    /// F26.1 (owner: "this is how many playlists we should have, and the made-for-you ones should be a last resort"): an artist search's lists.
    /// Real lists (Spotify's, Deezer's fans' and editors', SoundCloud's) that name the artist or hold several of their songs, with at least
    /// <see cref="MinSize"/> songs; the editors' first, then by how many of the artist's songs, then by size and fans; near-duplicate names once;
    /// one best-of per artist (Spotify, then Deezer, then SoundCloud). Our own lists only when fewer than <see cref="RealEnough"/> real ones were
    /// found, and after them. At most <see cref="ArtistRow"/>.
    /// </summary>
    public static List<WebPlaylist> ForArtist(IEnumerable<WebPlaylist> found, string artist, DateTime nowUtc)
    {
        var all = Candidates(found, nowUtc);
        int Size(WebPlaylist l) => Math.Max(l.Size, l.Tracks.Count);
        bool Relevant(WebPlaylist l) => TitleHolds(l.Name, artist) || TitleHolds(l.Subject, artist) || HoldsCount(l, artist) >= 2;
        // a SoundCloud set of the artist's own account is theirs whatever its name; its songs are read only when it is opened
        bool Big(WebPlaylist l) => Size(l) >= MinSize || (l.Source == "soundcloud" && l.Verified && l.Tracks.Count == 0);
        // the same words in any order and case ("the prodigy all songs" / "THE PRODIGY ALL SONGS", "Best of The Prodigy" / "The Prodigy - Best Of")
        static string Dup(string name) => string.Join(' ', Matching.SearchWords(name).Distinct().Order(StringComparer.Ordinal));
        var real = all.Where(l => l.Source != "own" && (Relevant(l) || (l.Source == "soundcloud" && l.Verified)) && Big(l))
            .OrderByDescending(l => l.Editor || l.Source == "spotify")
            .ThenByDescending(l => l.Kind == WebPlaylistKind.Artist)
            .ThenByDescending(l => HoldsCount(l, artist))
            .ThenByDescending(l => Size(l) + l.Likes / 100)
            .DistinctBy(l => Dup(l.Name)).ToList();
        List<WebPlaylist> ours = real.Count >= RealEnough ? [] : all.Where(l => l.Source == "own" && Relevant(l)).OrderByDescending(l => l.Kind == WebPlaylistKind.Artist).ThenByDescending(l => HoldsCount(l, artist)).ToList();
        return [.. real.Take(ArtistRow), .. ours.Take(Math.Max(0, ArtistRow - real.Count))];
    }

    /// <summary>
    /// F26.2 "Lists with &lt;artist&gt; in": lists already read (Spotify's, Deezer's opened or best-ofs, SoundCloud sets) that hold the artist's songs but
    /// are not about the artist (those are in the Playlists row), the ones holding most of their songs first; each with the songs it holds, in its order.
    /// </summary>
    public static List<(WebPlaylist List, IReadOnlyList<string> Songs)> WithArtistIn(IEnumerable<WebPlaylist> known, string artist, int take = 12)
    {
        var name = Matching.SearchWords(artist);
        if (name.Count == 0) return [];
        bool Theirs(WebTrack t) => Matching.SearchWords(t.Artist) is var w && w.Count >= name.Count && w.Take(name.Count).SequenceEqual(name)
            && (w.Count == name.Count || w[name.Count] is "feat" or "ft" or "featuring" or "with" or "x" or "and");
        return known.Where(l => l.Source != "own" && l.Tracks.Count > 0 && !TitleHolds(l.Name, artist) && !(l.Kind == WebPlaylistKind.Artist && TitleHolds(l.Subject, artist)))
            .Select(l => (List: l, Songs: (IReadOnlyList<string>)l.Tracks.Where(Theirs).Select(t => t.Title).Distinct(StringComparer.OrdinalIgnoreCase).ToList()))
            .Where(x => x.Songs.Count > 0)
            .DistinctBy(x => x.List.Source + ":" + x.List.Id)
            .OrderByDescending(x => x.Songs.Count).ThenBy(x => Rank(x.List.Source)).ThenBy(x => x.List.Name, StringComparer.OrdinalIgnoreCase)
            .Take(take).ToList();
    }

    /// <summary>Which of their songs a list holds, for its card: "Firestarter, Breathe + 2".</summary>
    public static string SongsLabel(IReadOnlyList<string> songs) => songs.Count switch
    {
        0 => "", 1 => songs[0], 2 => songs[0] + ", " + songs[1],
        _ => $"{songs[0]}, {songs[1]} + {songs.Count - 2}",
    };

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
    public static List<WebPlaylist> ForFilter(IEnumerable<WebPlaylist> found, string query, string? genre, DateTime nowUtc, string? decade = null)
    {
        var all = Candidates(found, nowUtc);
        // F26.3: the decade picked in the Filters panel ("1990s"): that decade's lists, and lists naming it ("90s", "1990s")
        if (decade is { Length: >= 4 } d && int.TryParse(d[..4], out var start))
        {
            string[] says = [$"{start}s", $"{start % 100:00}s", $"{start}"];
            all = all.Where(l => (l.Kind == WebPlaylistKind.Decade && l.Subject.StartsWith(start.ToString())) || Matching.SearchWords(l.Name).Any(w => says.Contains(w))).ToList();
        }
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
