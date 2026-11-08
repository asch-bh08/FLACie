namespace FLACie.Core;

/// <summary>The person's playlists for a search: those named like it first, then those holding a song whose title or artist matches it, the most matching
/// songs first (owner, 7 Nov: searching "dreamer", "with me", "eminem" or "aura" should list the playlists the song or artist is in). Pure.</summary>
public static class PlaylistHits
{
    /// <summary>A playlist found, and what it holds of the search for its card ("Has “Dreamer”", "2 songs by Eminem"); null when its name matched.</summary>
    public sealed record Hit(Playlist List, string? Holds);

    public static IReadOnlyList<Hit> Find(IEnumerable<Playlist> lists, IReadOnlyList<string> words, int max = 12)
    {
        if (words.Count == 0) return [];
        var named = new List<Hit>();
        var holding = new List<(Hit Hit, int Count)>();
        foreach (var p in lists)
        {
            if (Matching.SearchHit(words, p.Name)) { named.Add(new Hit(p, null)); continue; }
            // the title alone or the artist alone: "with me" is a song, "eminem" an artist; the two run together would match far too much
            var byTitle = p.Entries.Where(e => Matching.SearchHit(words, e.Title)).ToList();
            var byArtist = p.Entries.Where(e => Matching.SearchHit(words, e.Artist)).ToList();
            var all = byTitle.Union(byArtist).ToList();
            if (all.Count == 0) continue;
            holding.Add((new Hit(p, Label(byTitle, byArtist, all)), all.Count));
        }
        return named.Concat(holding.OrderByDescending(h => h.Count).Select(h => h.Hit)).Take(max).ToList();
    }

    static string Label(List<PlaylistEntry> byTitle, List<PlaylistEntry> byArtist, List<PlaylistEntry> all)
    {
        if (byTitle.Count > 0 && byTitle.Select(e => e.Title).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1 && all.Count == byTitle.Count)
            return $"Has “{byTitle[0].Title}”";
        if (byArtist.Count == all.Count) return all.Count == 1 ? $"Has “{all[0].Title}”" : $"{all.Count} songs by {byArtist[0].Artist}";
        return $"Has {all.Count} of these songs";
    }
}
