namespace FLACie.Core;

/// <summary>
/// F6: the artist a search means, for the top result. Only artists named like the search count ("prodigy" is The Prodigy or Prodigy, never Prodigy of
/// Mobb Deep); among them the one played most, then the one with the most songs, then the exact name.
/// </summary>
public static class SearchRankTop
{
    public static Group? Artist(IEnumerable<Group> artists, string query, IReadOnlyList<JfPlay> plays)
    {
        var q = Key(query);
        if (q.Length == 0) return null;
        var byId = new Dictionary<string, int>();
        foreach (var p in plays) byId[p.Id] = p.Plays;
        int Played(Group g) => g.Tracks.Sum(t => t.JellyfinId is { } id ? byId.GetValueOrDefault(id) : 0);
        return artists.Where(g => Key(g.Name) == q)
            .OrderByDescending(Played).ThenByDescending(g => g.Tracks.Count).ThenByDescending(g => Words(g.Name) == Words(query))
            .FirstOrDefault();
    }

    static string Words(string s) => string.Join(' ', Matching.SearchWords(s));
    /// <summary>The name without a leading "the" ("" when that is all there is).</summary>
    static string Key(string s) { var w = Words(s); return w.StartsWith("the ") ? w[4..] : w == "the" ? "" : w; }
}

/// <summary>
/// F6.2: what the search page shows for an artist: their top 5 songs beside the Top result, then Popular (the next most played), Albums and Singles &amp; EPs
/// (each newest first). Songs are ranked by plays (Jellyfin's), then the newest; a release of three songs or fewer, or one named EP or Single, is a single.
/// </summary>
public sealed record ArtistPage(IReadOnlyList<Track> Top, IReadOnlyList<Track> Popular, IReadOnlyList<Group> Albums, IReadOnlyList<Group> Singles)
{
    public const int TopCount = 5, PopularCount = 10;

    public static ArtistPage Of(Group artist, IEnumerable<Group> albums, IReadOnlyList<JfPlay> plays)
    {
        var byId = new Dictionary<string, int>();
        foreach (var p in plays) byId[p.Id] = p.Plays;
        // a song on several of their albums (the album and a best-of) is one of their hits: after real plays it comes first, before the newest year,
        // which alone filled the top with the newest album (owner, 5 Nov: all No Tourists)
        var copies = artist.Tracks.GroupBy(Matching.MatchKey).ToDictionary(g => g.Key, g => g.Select(t => t.Album).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var ranked = artist.Tracks.OrderByDescending(t => t.JellyfinId is { } id ? byId.GetValueOrDefault(id) : 0).ThenByDescending(t => copies[Matching.MatchKey(t)])
            .ThenByDescending(t => t.Year).ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase).DistinctBy(t => Matching.MatchKey(t)).ToList();
        // no album gives more than 2 songs before every album has had its turn: the rest follow in the same order
        var perAlbum = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var spread = new List<Track>(); var later = new List<Track>();
        foreach (var t in ranked) ((perAlbum[t.Album] = perAlbum.GetValueOrDefault(t.Album) + 1) <= 2 ? spread : later).Add(t);
        var songs = spread.Concat(later).ToList();
        var paths = artist.Tracks.Select(t => t.Path).ToHashSet();
        var theirs = albums.Where(g => g.Name.Length > 0 && g.Tracks.Any(t => paths.Contains(t.Path)))
            .OrderByDescending(g => g.Tracks.Max(t => t.Year)).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return new(songs.Take(TopCount).ToList(), songs.Skip(TopCount).Take(PopularCount).ToList(), theirs.Where(g => !IsSingle(g)).ToList(), theirs.Where(IsSingle).ToList());
    }

    public static bool IsSingle(Group album) =>
        album.Tracks.Count <= 3 || System.Text.RegularExpressions.Regex.IsMatch(album.Name, @"[(\[\s-](?:ep|single)[)\]]?\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
}
