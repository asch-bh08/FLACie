namespace FLACie.Core;

/// <summary>
/// "Recommended songs" under one of the person's own playlists: songs from their library that are not in it, chosen by how close they sit to the
/// playlist's songs, the way <see cref="Engine.Next"/> judges closeness: by feel (the songs' <see cref="Vibe"/>) where both sides have one, and by
/// artist, genre and decade in common. A big playlist is judged by a fixed sample of its songs (chosen by their keys, not at random), so the same
/// playlist always gives the same list; [skip] steps through it for Refresh (past the end it starts again). No song twice, two per artist at most,
/// music only (nothing under 40 seconds, no audiobooks or podcasts, no songs that are only online).
/// </summary>
public static class PlaylistRecs
{
    public const int MaxSeeds = 60, PerArtist = 2;

    public static List<Track> Pick(Library lib, IReadOnlyList<Track> playlist, Func<Track, Vibe?>? vibe, int n, int skip = 0)
    {
        if (playlist.Count == 0 || n <= 0) return [];
        var ranked = Ranked(lib, playlist, vibe);
        if (ranked.Count == 0) return [];
        // Refresh goes round: past the end it starts again from the top
        skip = Math.Max(0, skip);
        if (skip >= ranked.Count) skip %= ranked.Count;
        return ranked.Skip(skip).Take(Math.Clamp(n, 1, 50)).ToList();
    }

    /// <summary>Every candidate, best first, with the per-artist cap already applied.</summary>
    public static List<Track> Ranked(Library lib, IReadOnlyList<Track> playlist, Func<Track, Vibe?>? vibe)
    {
        var inList = playlist.Select(Matching.MatchKey).ToHashSet();
        var paths = playlist.Select(t => t.Path).ToHashSet();
        // the sample: every song of a small playlist, a fixed spread of a big one
        var seeds = playlist.DistinctBy(Matching.MatchKey).OrderBy(t => Stable(Matching.MatchKey(t))).Take(MaxSeeds).ToList();
        var artists = Shares(seeds, t => Matching.PrimaryArtist(t.Artist));
        var genres = Shares(seeds, Genre);
        var decades = Shares(seeds, t => t.Year >= 1900 ? (t.Year / 10 * 10).ToString() : "");
        var vibes = vibe is null ? [] : seeds.Select(vibe).OfType<Vibe>().ToList();
        Vibe? mood = vibes.Count == 0 ? null : new(vibes.Average(v => v.Energy), vibes.Average(v => v.Loudness),
            vibes.Any(v => v.Bpm > 0) ? vibes.Where(v => v.Bpm > 0).Average(v => v.Bpm) : 0, vibes.Average(v => v.Dance));
        double aMax = Max(artists), gMax = Max(genres), dMax = Max(decades);

        var cands = new List<(Track T, double S, string Key)>();
        foreach (var t in lib.Songs)
        {
            if (!IsMusic(t) || paths.Contains(t.Path)) continue;
            var key = Matching.MatchKey(t);
            if (inList.Contains(key)) continue;
            var a = artists.GetValueOrDefault(Matching.PrimaryArtist(t.Artist)) / aMax;
            var g = genres.GetValueOrDefault(Genre(t)) / gMax;
            var d = t.Year >= 1900 ? decades.GetValueOrDefault((t.Year / 10 * 10).ToString()) / dMax : 0;
            var link = a > 0 || g > 0;
            var v = mood is not null && vibe?.Invoke(t) is { } tv ? 1 - VibeMath.Distance(mood, tv) : (double?)null;
            // as in the engine: with a vibe on both sides it must feel close; without one, a song must share an artist or a genre with the playlist
            if (v is { } fit ? fit < 0.6 && !link : !link) continue;
            var s = 0.45 * a + 0.30 * g + 0.10 * d + 0.35 * (v ?? (link ? 0.7 : 0.3));
            cands.Add((t, s, key));
        }
        var res = new List<Track>(); var per = new Dictionary<string, int>(); var used = new HashSet<string>();
        foreach (var c in cands.OrderByDescending(c => c.S).ThenBy(c => Stable(c.Key)).ThenBy(c => c.T.Path, StringComparer.Ordinal))
        {
            var ar = Matching.PrimaryArtist(c.T.Artist);
            if (!used.Add(c.Key) || per.GetValueOrDefault(ar) >= PerArtist) continue;
            per[ar] = per.GetValueOrDefault(ar) + 1;
            res.Add(c.T);
        }
        return res;
    }

    static readonly string[] NotMusic = ["audiobook", "audio book", "podcast", "spoken word", "speech"];
    /// <summary>A song to recommend: in the library for real (not only online), longer than 40 seconds, not an audiobook or a podcast.</summary>
    public static bool IsMusic(Track t) => t.Source != TrackSource.Cloud && !(t.DurationMs is > 0 and < 40_000) && !NotMusic.Any(Genre(t).Contains);

    static string Genre(Track t) => t.Genre.Trim().ToLowerInvariant();
    static Dictionary<string, double> Shares(List<Track> seeds, Func<Track, string> of)
    {
        var d = new Dictionary<string, double>();
        foreach (var t in seeds) { var k = of(t); if (k.Length > 0) d[k] = d.GetValueOrDefault(k) + 1; }
        return d;
    }
    static double Max(Dictionary<string, double> d) => d.Count == 0 ? 1 : d.Values.Max();

    /// <summary>A hash that is the same in every run (string.GetHashCode is not), for the fixed sample and the order of ties.</summary>
    public static uint Stable(string s)
    {
        var h = 2166136261u;
        foreach (var ch in s) { h ^= ch; h *= 16777619u; }
        return h;
    }
}
