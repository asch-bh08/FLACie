namespace FLACie.Core;

/// <summary>
/// What this account likes, learned from what it does: every play (newer plays count more: the weight halves every 30 days), every
/// favourite and every playlist a song is in. The signals are spread over the song's artist, genre, decade and album, so a song never
/// played can still score well because it sounds like what is played. Nothing leaves the server and nothing is sent anywhere.
/// </summary>
public sealed class TasteProfile
{
    readonly Dictionary<string, double> artist = new(), genre = new(), decade = new(), album = new();
    readonly Dictionary<string, double> songs = new();
    readonly Dictionary<string, long> lastPlayed = new();
    double artistMax = 1, genreMax = 1, decadeMax = 1, albumMax = 1;

    public bool Empty => songs.Count == 0;

    public TasteProfile(Library lib, IReadOnlyList<PlayedEntry> history, IReadOnlyList<PlaylistEntry> favourites, IReadOnlyList<Playlist> playlists, DateTime utcNow)
    {
        var now = new DateTimeOffset(utcNow).ToUnixTimeMilliseconds();
        foreach (var h in history)
        {
            if (lib.Resolve(h.Path, h.Title, h.Artist) is not { } t) continue;
            var days = Math.Max(0, (now - h.At) / 86_400_000.0);
            Add(t, 2.0 * Math.Pow(0.5, days / 30));
            var k = Matching.MatchKey(t); lastPlayed[k] = Math.Max(lastPlayed.GetValueOrDefault(k), h.At);
        }
        foreach (var f in favourites) if (lib.Resolve(f.Path, f.Title, f.Artist) is { } t) Add(t, 3);
        foreach (var e in playlists.SelectMany(p => p.Entries)) if (lib.Resolve(e.Path, e.Title, e.Artist) is { } t) Add(t, 0.7);
        artistMax = Max(artist); genreMax = Max(genre); decadeMax = Max(decade); albumMax = Max(album);
    }

    static double Max(Dictionary<string, double> d) => d.Count == 0 ? 1 : Math.Max(1, d.Values.Max());
    static string Dec(Track t) => t.Year >= 1900 ? (t.Year / 10 * 10).ToString() : "";
    static string Gen(Track t) => t.Genre.Trim().ToLowerInvariant();

    void Add(Track t, double w)
    {
        Bump(songs, Matching.MatchKey(t), w);
        Bump(artist, Matching.PrimaryArtist(t.Artist), w);
        if (Gen(t).Length > 0) Bump(genre, Gen(t), w);
        if (Dec(t).Length > 0) Bump(decade, Dec(t), w);
        if (Matching.AlbumNorm(t.Album).Length > 0) Bump(album, t.AlbumKey, w);
    }
    static void Bump(Dictionary<string, double> d, string k, double w) { if (k.Length > 0) d[k] = d.GetValueOrDefault(k) + w; }

    /// <summary>How much this account has played, saved or listed this exact song (0 = never).</summary>
    public double Known(Track t) => songs.GetValueOrDefault(Matching.MatchKey(t));
    public double Artist(Track t) => artist.GetValueOrDefault(Matching.PrimaryArtist(t.Artist));

    /// <summary>0..1: how well the song fits the taste, from its artist, genre, decade and album (unplayed songs can score too).</summary>
    public double Fit(Track t)
    {
        var a = artist.GetValueOrDefault(Matching.PrimaryArtist(t.Artist)) / artistMax;
        var g = Gen(t).Length > 0 ? genre.GetValueOrDefault(Gen(t)) / genreMax : 0;
        var d = Dec(t).Length > 0 ? decade.GetValueOrDefault(Dec(t)) / decadeMax : 0;
        var al = Matching.AlbumNorm(t.Album).Length > 0 ? album.GetValueOrDefault(t.AlbumKey) / albumMax : 0;
        return (a * 0.45 + g * 0.30 + d * 0.10 + al * 0.15);
    }

    /// <summary>True when the song was played in the last [hours] hours (so it is not suggested again straight away).</summary>
    public bool PlayedWithin(Track t, DateTime utcNow, double hours) =>
        lastPlayed.TryGetValue(Matching.MatchKey(t), out var at) && new DateTimeOffset(utcNow).ToUnixTimeMilliseconds() - at < hours * 3_600_000;
}

/// <summary>
/// The recommendation algorithm behind Quick picks and the "recommended" rows. Each song in the library gets a score:
///   fit to the taste profile (artist 45 %, genre 30 %, album 15 %, decade 10 %)
///   + a liking for songs already played a lot, saved or listed (so favourites come round again, but not at once)
///   + a bonus for songs never played, so the mix keeps surfacing music in the library that has not been heard yet
///   - songs played in the last two days, which are left out
///   + a small random part seeded by the time slot, so the list changes every hour but is steady within it.
/// The best are then spread so no artist fills the row. With no history yet it falls back to recent additions and a shuffle.
/// </summary>
public static class Recommender
{
    public static List<Track> Recommend(Library lib, TasteProfile taste, DateTime utcNow, int take = 20, long seed = 0, int perArtist = 2, IEnumerable<string>? exclude = null)
    {
        var skip = exclude?.ToHashSet() ?? [];
        var rnd = new Random((int)((seed + HomeBuilder.Slot(utcNow)) % int.MaxValue));
        var now = new DateTimeOffset(utcNow).ToUnixTimeMilliseconds();
        var scored = new List<(Track T, double S)>();
        foreach (var t in lib.Songs)
        {
            if (t.DurationMs is > 0 and < 40_000) continue;
            var key = Matching.MatchKey(t);
            if (skip.Contains(key) || taste.PlayedWithin(t, utcNow, 48)) continue;
            var known = taste.Known(t);
            double s = taste.Fit(t) * 3
                + Math.Min(known, 6) * 0.12
                + (known == 0 ? 0.35 : 0)
                + (t.AddedMs > 0 && now - t.AddedMs < 14L * 86_400_000 ? 0.25 : 0)
                + rnd.NextDouble() * 0.6;
            scored.Add((t, s));
        }
        var ranked = scored.OrderByDescending(x => x.S).Select(x => x.T);
        var res = new List<Track>(); var per = new Dictionary<string, int>();
        foreach (var t in ranked)
        {
            var a = Matching.PrimaryArtist(t.Artist);
            per[a] = per.GetValueOrDefault(a) + 1;
            if (per[a] > perArtist) continue;
            res.Add(t);
            if (res.Count >= take) break;
        }
        return res;
    }
}
