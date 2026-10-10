namespace FLACie.Core;

/// <summary>A song as Jellyfin counts it for one user: how many times it was played (in any Jellyfin app, not only FLACie), when last, and whether it is a favourite there.</summary>
public sealed record JfPlay(string Id, int Plays, long LastMs, bool Favourite);

/// <summary>
/// What this account likes, learned from what it does: every play in FLACie (newer plays count more: the weight halves every 30 days), every play
/// Jellyfin has counted for this user in any app (so years of listening count from the first day, halving every 120 days but never below a quarter),
/// every favourite and every playlist a song is in. The signals are spread over the song's artist, genre, decade and album, so a song never
/// played can still score well because it sounds like what is played. [hits] are the best-known songs of the artists played most (from a public
/// catalog); a library song that is one of them scores for it. Nothing leaves the server except the artist names looked up in that catalog.
/// </summary>
public sealed class TasteProfile
{
    readonly Dictionary<string, double> artist = new(), genre = new(), decade = new(), album = new();
    readonly Dictionary<string, double> songs = new();
    readonly Dictionary<string, long> lastPlayed = new();
    readonly IReadOnlySet<string> hits;
    double artistMax = 1, genreMax = 1, decadeMax = 1, albumMax = 1, songMax = 1;

    public bool Empty => songs.Count == 0;

    public TasteProfile(Library lib, IReadOnlyList<PlayedEntry> history, IReadOnlyList<PlaylistEntry> favourites, IReadOnlyList<Playlist> playlists, DateTime utcNow,
        IReadOnlyList<JfPlay>? jellyfin = null, IReadOnlySet<string>? hits = null)
    {
        this.hits = hits ?? new HashSet<string>();
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
        // Jellyfin's own count for this user: the whole listening history, from every app
        foreach (var p in jellyfin ?? [])
        {
            if (lib.ByJellyfinId(p.Id) is not { } t) continue;
            var days = p.LastMs > 0 ? Math.Max(0, (now - p.LastMs) / 86_400_000.0) : 365;
            var w = Math.Log(1 + Math.Max(0, p.Plays)) * 1.3 * Math.Max(0.25, Math.Pow(0.5, days / 120)) + (p.Favourite ? 2 : 0);
            if (w > 0) Add(t, w);
            if (p.LastMs > 0) { var k = Matching.MatchKey(t); lastPlayed[k] = Math.Max(lastPlayed.GetValueOrDefault(k), p.LastMs); }
        }
        artistMax = Max(artist); genreMax = Max(genre); decadeMax = Max(decade); albumMax = Max(album); songMax = Max(songs);
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

    /// <summary>0..1, how much this song is loved: its own plays, on a curve (the tenth play counts less than the second).</summary>
    public double Love(Track t) => Math.Log(1 + Known(t)) / Math.Log(1 + songMax);
    /// <summary>0..1, how much this artist is played compared with the most played.</summary>
    public double ArtistFit(Track t) => artist.GetValueOrDefault(Matching.PrimaryArtist(t.Artist)) / artistMax;
    /// <summary>0..1, how much songs of this song's album are played (the other songs of an album you play).</summary>
    public double AlbumFit(Track t) => Matching.AlbumNorm(t.Album).Length > 0 ? album.GetValueOrDefault(t.AlbumKey) / albumMax : 0;
    public double GenreFit(Track t) => Gen(t).Length > 0 ? genre.GetValueOrDefault(Gen(t)) / genreMax : 0;
    /// <summary>The song is one of the best known of an artist this account plays a lot.</summary>
    public bool IsHit(Track t) => hits.Contains(Matching.MatchKey(t));

    /// <summary>0..1: how well the song fits the taste, from its artist, genre, decade and album (unplayed songs can score too).</summary>
    public double Fit(Track t)
    {
        var d = Dec(t).Length > 0 ? decade.GetValueOrDefault(Dec(t)) / decadeMax : 0;
        return ArtistFit(t) * 0.45 + GenreFit(t) * 0.30 + d * 0.10 + AlbumFit(t) * 0.15;
    }

    /// <summary>The lead artists played most, with their names as they appear in the library (to look their best-known songs up).</summary>
    public List<string> TopArtists(Library lib, int n)
    {
        var names = lib.Songs.GroupBy(t => Matching.PrimaryArtist(t.Artist)).Where(g => g.Key.Length > 0 && artist.GetValueOrDefault(g.Key) > 0)
            .OrderByDescending(g => artist[g.Key]).Take(n).Select(g => g.First().Artist).ToList();
        return names;
    }

    /// <summary>True when the song was played in the last [hours] hours (so it is not suggested again straight away).</summary>
    public bool PlayedWithin(Track t, DateTime utcNow, double hours) =>
        lastPlayed.TryGetValue(Matching.MatchKey(t), out var at) && new DateTimeOffset(utcNow).ToUnixTimeMilliseconds() - at < hours * 3_600_000;
}

/// <summary>
/// The recommendation algorithm behind Quick picks and the "recommended" rows. Each song in the library gets a score:
///   + how much the song itself is loved (plays here and in Jellyfin, favourites, playlists)          weight 2.2
///   + how much its artist is played                                                                   weight 1.2
///   + how much its album is played (so the other songs of albums you play come up)                    weight 0.9
///   + its genre                                                                                        weight 0.35
///   + being one of the best-known songs of an artist you play (more for artists you play most)        up to 1.4
///   - a song played in the last six hours is left out, in the last day it is held back
///   + a small random part seeded by the time slot, so the list changes every hour but is steady within it.
/// A song you have never played only counts when it is tied to something you play (its artist, its album or one of that artist's hits): a song
/// that only shares a genre is no longer enough. The row is then built from two kinds, about three in five songs you already love and two in five
/// that are new to you but related, mixed in turn, so it feels like your own music with a few good finds. With no history yet it falls back to
/// recent additions and a shuffle.
/// </summary>
public static class Recommender
{
    public static List<Track> Recommend(Library lib, TasteProfile taste, DateTime utcNow, int take = 20, long seed = 0, int perArtist = 2, IEnumerable<string>? exclude = null)
    {
        var skip = exclude?.ToHashSet() ?? [];
        var rnd = new Random((int)((seed + HomeBuilder.Slot(utcNow)) % int.MaxValue));
        var now = new DateTimeOffset(utcNow).ToUnixTimeMilliseconds();
        var loved = new List<(Track T, double S)>(); var fresh = new List<(Track T, double S)>(); var other = new List<(Track T, double S)>();
        foreach (var t in lib.Songs)
        {
            if (t.DurationMs is > 0 and < 40_000) continue;
            var key = Matching.MatchKey(t);
            if (skip.Contains(key) || taste.PlayedWithin(t, utcNow, 6)) continue;
            var known = taste.Known(t);
            var a = taste.ArtistFit(t); var al = taste.AlbumFit(t); var hit = taste.IsHit(t);
            double s = taste.Love(t) * 2.2 + a * 1.2 + al * 0.9 + taste.GenreFit(t) * 0.35
                + (hit ? 0.6 + 0.8 * a : 0)
                + (t.AddedMs > 0 && now - t.AddedMs < 14L * 86_400_000 ? 0.15 : 0)
                + rnd.NextDouble() * 0.25
                - (taste.PlayedWithin(t, utcNow, 24) ? 0.6 : 0);
            if (known > 0) loved.Add((t, s));
            else if (a > 0.12 || al > 0.12 || hit) fresh.Add((t, s));
            else other.Add((t, s));
        }
        var res = new List<Track>(); var per = new Dictionary<string, int>(); var used = new HashSet<string>();
        List<Track> Take(List<(Track T, double S)> src, int n, int cap)
        {
            var got = new List<Track>();
            foreach (var x in src.OrderByDescending(x => x.S))
            {
                var ar = Matching.PrimaryArtist(x.T.Artist);
                if (per.GetValueOrDefault(ar) >= cap || used.Contains(Matching.MatchKey(x.T))) continue;
                used.Add(Matching.MatchKey(x.T));
                per[ar] = per.GetValueOrDefault(ar) + 1; got.Add(x.T);
                if (got.Count >= n) break;
            }
            return got;
        }
        if (loved.Count + fresh.Count == 0)
        {
            // no taste yet: recent additions, then a shuffle
            var any = lib.Songs.Where(t => !(t.DurationMs is > 0 and < 40_000) && !skip.Contains(Matching.MatchKey(t)))
                .OrderByDescending(t => t.AddedMs > 0 && now - t.AddedMs < 14L * 86_400_000 ? 1 : 0).ThenBy(_ => rnd.Next()).Select(t => (t, rnd.NextDouble())).ToList();
            return Take(any, take, perArtist);
        }
        var wantLoved = (int)Math.Round(take * 0.6);
        // songs you love: two of one artist at most; new ones may bring an artist to three (their best-known songs are what you want from artists you play)
        var a1 = Take(loved, wantLoved, perArtist); var b1 = Take(fresh, take - a1.Count, perArtist + 1);
        // short of songs (a small history or library): more of what you love, then more new ones with a looser limit, and only then songs that merely share a taste
        if (a1.Count + b1.Count < take) a1.AddRange(Take(loved, take - a1.Count - b1.Count, perArtist + 2));
        if (a1.Count + b1.Count < take) b1.AddRange(Take(fresh, take - a1.Count - b1.Count, perArtist + 2));
        if (a1.Count + b1.Count < take) b1.AddRange(Take(other, take - a1.Count - b1.Count, perArtist + 1));
        if (a1.Count + b1.Count < take) b1.AddRange(Take(other, take - a1.Count - b1.Count, 99));   // a library with only a few artists: no limit left
        // mix them in turn: two you love, one new, and so on
        int i = 0, j = 0;
        while (res.Count < take && (i < a1.Count || j < b1.Count))
        {
            for (var k = 0; k < 2 && i < a1.Count && res.Count < take; k++) res.Add(a1[i++]);
            if (j < b1.Count && res.Count < take) res.Add(b1[j++]);
            else if (i >= a1.Count && j < b1.Count) res.Add(b1[j++]);
        }
        return res;
    }
}
