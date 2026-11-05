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
