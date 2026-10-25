using System.Text.RegularExpressions;

namespace FLACie.Core;

/// <summary>One suggestion: a library song (<see cref="Track"/>) or a song not in the library yet (<see cref="New"/>), never both.</summary>
public sealed record Pick(Track? Track, CatalogSong? New, double Score, string Why);

/// <summary>[NewShare] is the part of the picks that are new to the person (not in the library, or never played); [Explicit] false leaves explicit songs out.</summary>
public sealed record EngineOptions(int Take = 10, bool Explicit = true, double NewShare = 0.5, long Seed = 0);

/// <summary>
/// What plays next, and what fills Home. Each candidate gets
///   0.35 x fit (how close it feels to the last three songs, by vibe; genre and artist only stand in when there is no vibe to go on)
/// + 0.30 x taste (how much this person loves the song or plays its artist)
/// + 0.25 x together (fans of the song playing also play it)
/// - 0.60 x penalty (skips, "less like this").
/// A song must feel close enough (fit 0.6 or more): a liked song may then cross genres (The Prodigy into David Guetta), but a song never heard also
/// needs a genre or an artist in common with what is playing, so a stranger only comes in through something familiar. Then the rails: never a
/// thumbs-down, never an explicit song when explicit is off, never a song heard in the last four hours, never the artist that just played. Only
/// when that leaves nothing do the rails give way, the four-hour rule first, the artist rule second. The picks are half songs the person knows and
/// half new ones, taken in turn.
/// </summary>
public static class Engine
{
    const double Gate = 0.6;
    static readonly Regex ExplicitTag = new(@"[(\[]\s*explicit\s*[)\]]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    sealed record Cand(Track? Track, CatalogSong? New, string Artist, double Score, string Why, bool Fresh, bool Recent, bool SameArtist);

    public static List<Pick> Next(Library lib, TasteProfile taste, Func<Track, Vibe?> vibe, IReadOnlyList<Track> session, IReadOnlyList<CatalogSong> related,
        DateTime utcNow, EngineOptions o)
    {
        var seed = session.Count > 0 ? session[^1] : null;
        var last = session.TakeLast(3).ToList();
        var mood = Mean(last.Select(vibe).OfType<Vibe>().ToList());
        var inSession = session.Select(Matching.MatchKey).ToHashSet();
        var artists = session.Select(t => Matching.PrimaryArtist(t.Artist)).Where(a => a.Length > 0).ToHashSet();
        var genres = session.Select(t => t.Genre.Trim().ToLowerInvariant()).Where(g => g.Length > 0).ToHashSet();
        var prevArtist = seed is null ? "" : Matching.PrimaryArtist(seed.Artist);
        var relatedSongs = related.Select(s => Matching.MatchKey(s.Title, s.Artist)).ToHashSet();
        var relatedArtists = related.Select(s => Matching.PrimaryArtist(s.Artist)).ToHashSet();
        var rnd = new Random((int)(o.Seed % int.MaxValue));

        var cands = new List<Cand>();
        foreach (var t in lib.Songs)
        {
            if (inSession.Contains(Matching.MatchKey(t)) || taste.Disliked(t)) continue;
            if (!o.Explicit && IsExplicit(t.Title, t.Album)) continue;
            var artist = Matching.PrimaryArtist(t.Artist);
            var link = artists.Contains(artist) || relatedArtists.Contains(artist) || genres.Contains(t.Genre.Trim().ToLowerInvariant());
            // with a vibe on both sides it decides; without one, a song tied to what is playing passes and anything else does not
            var v = vibe(t);
            var fit = seed is null ? taste.Fit(t) : mood is not null && v is not null ? 1 - VibeMath.Distance(mood, v) : link ? 0.7 : 0.3;
            var liked = taste.Likes(t); var heard = taste.Known(t) > 0;
            if (seed is not null && (fit < Gate || (!heard && !liked && !link))) continue;
            var love = liked ? 1 : Math.Max(taste.Love(t), taste.Fit(t));
            // fans of what is playing play it, or it sits on a playlist this person plays from with one of the last three songs
            var together = relatedSongs.Contains(Matching.MatchKey(t)) || last.Any(s => taste.SharesPlayedPlaylist(s, t)) ? 1 : relatedArtists.Contains(artist) ? 0.5 : 0;
            var score = 0.35 * fit + 0.3 * love + 0.25 * together - 0.6 * taste.Penalty(t) + rnd.NextDouble() * 0.01;
            cands.Add(new(t, null, artist, score, Why(seed, t.Artist, fit, love, liked, taste.ArtistFit(t) >= 0.5, together), !heard && !liked,
                taste.PlayedWithin(t, utcNow, 4), artist == prevArtist && artist.Length > 0));
        }
        // songs fans of this one play that are not in the library yet: tied to it by being on its list, so no vibe is needed to let them in
        foreach (var s in related)
        {
            if (lib.Find(s.Title, s.Artist) is not null || (!o.Explicit && IsExplicit(s.Title, s.Album))) continue;
            var artist = Matching.PrimaryArtist(s.Artist);
            var stand = new Track("", s.Title, s.Artist, s.Album, s.Artist, 0, 0, 0, 0, null, 0, TrackSource.Cloud);
            if (taste.Disliked(stand)) continue;
            var love = taste.ArtistFit(stand);
            var score = 0.35 * 0.7 + 0.3 * love + 0.25 - 0.6 * taste.Penalty(stand) + rnd.NextDouble() * 0.01;
            cands.Add(new(null, s, artist, score, Why(seed, s.Artist, 0.7, love, false, love >= 0.5, 1), true, false, artist == prevArtist && artist.Length > 0));
        }

        // the rails, given way one at a time only when they would leave nothing
        var ok = cands.Where(c => !c.Recent && !c.SameArtist).ToList();
        if (ok.Count == 0) ok = cands.Where(c => !c.SameArtist).ToList();
        if (ok.Count == 0) ok = cands;
        return Blend(ok, o);
    }

    /// <summary>Half known, half new (by NewShare), one per artist while there is enough to choose from, taken in turn so the list alternates.</summary>
    static List<Pick> Blend(List<Cand> cands, EngineOptions o)
    {
        var sorted = cands.OrderByDescending(c => c.Score).ToList();
        var wantNew = (int)Math.Round(o.Take * o.NewShare);
        var used = new HashSet<Cand>(); var per = new Dictionary<string, int>();
        List<Cand> Take(bool fresh, int n, int cap)
        {
            var got = new List<Cand>();
            foreach (var c in sorted)
            {
                if (got.Count >= n) break;
                if (c.Fresh != fresh || used.Contains(c) || per.GetValueOrDefault(c.Artist) >= cap) continue;
                used.Add(c); per[c.Artist] = per.GetValueOrDefault(c.Artist) + 1; got.Add(c);
            }
            return got;
        }
        var known = Take(false, o.Take - wantNew, 1); var fresh = Take(true, wantNew, 1);
        // short of one kind: more of the other, then the same artist again
        if (known.Count + fresh.Count < o.Take) fresh.AddRange(Take(true, o.Take - known.Count - fresh.Count, 1));
        if (known.Count + fresh.Count < o.Take) known.AddRange(Take(false, o.Take - known.Count - fresh.Count, 1));
        if (known.Count + fresh.Count < o.Take) known.AddRange(Take(false, o.Take - known.Count - fresh.Count, int.MaxValue));
        if (known.Count + fresh.Count < o.Take) fresh.AddRange(Take(true, o.Take - known.Count - fresh.Count, int.MaxValue));
        var res = new List<Pick>();
        for (var i = 0; i < Math.Max(known.Count, fresh.Count); i++)
            foreach (var list in new[] { known, fresh })
                if (i < list.Count) res.Add(new(list[i].Track, list[i].New, list[i].Score, list[i].Why));
        return res;
    }

    static bool IsExplicit(string title, string album) => ExplicitTag.IsMatch(title) || ExplicitTag.IsMatch(album);

    static Vibe? Mean(List<Vibe> vs) => vs.Count == 0 ? null : new(vs.Average(v => v.Energy), vs.Average(v => v.Loudness),
        vs.Any(v => v.Bpm > 0) ? vs.Where(v => v.Bpm > 0).Average(v => v.Bpm) : 0, vs.Average(v => v.Dance));

    /// <summary>The strongest part of the score first, then the next one if it counts: "Same energy as Out of Space · you play The Prodigy a lot".</summary>
    static string Why(Track? seed, string artist, double fit, double love, bool liked, bool artistPlayed, double together)
    {
        var parts = new List<(double W, string Text)>
        {
            (0.35 * fit, seed is null ? "Fits what you play" : $"Same energy as {seed.Title}"),
            (0.3 * love, liked ? "you love this one" : artistPlayed ? $"you play {artist} a lot" : "like what you play"),
            (0.25 * together, seed is null ? "goes with what you play" : $"fans of {seed.Artist} play this"),
        };
        var top = parts.Where(p => p.W >= 0.1).OrderByDescending(p => p.W).Take(2).Select(p => p.Text).ToList();
        if (top.Count == 0) return "Fits what you play";
        top[0] = char.ToUpperInvariant(top[0][0]) + top[0][1..];
        return string.Join(" · ", top);
    }
}
