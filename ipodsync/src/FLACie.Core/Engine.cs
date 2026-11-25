using System.Text.RegularExpressions;

namespace FLACie.Core;

/// <summary>One suggestion: a library song (<see cref="Track"/>) or a song not in the library yet (<see cref="New"/>), never both.</summary>
public sealed record Pick(Track? Track, CatalogSong? New, double Score, string Why);

/// <summary>[NewShare] is the part of the picks that are new to the person (not in the library, or never played); [Explicit] false leaves explicit songs out.</summary>
public sealed record EngineOptions(int Take = 10, bool Explicit = true, double NewShare = 0.5, long Seed = 0);

/// <summary>
/// What plays next, and what fills Home. Each candidate gets
///   0.35 x fit (how close it feels to the last three songs, by vibe; its genre's scene and artist only stand in when there is no vibe to go on)
/// + 0.30 x taste (how much this person loves the song or plays its artist; a best-known song of a played artist a little more)
/// + 0.25 x together (fans of the song playing also play it, or this person plays it near those songs themselves)
/// + 0.10 x scene (the same genre tag, or a tag of the same scene: Indie Rock and Dance-Punk) + 0.10 x era (released near the songs playing)
/// - 0.60 x penalty (skips, "less like this") - a little for a song heard in the last day.
/// A song must feel close enough (fit 0.6 or more): a liked song may then cross genres (The Prodigy into David Guetta), but a song never heard also
/// needs a tie to what is playing (its artist, a related artist, its scene, or this person playing it with those songs), so a stranger only comes in
/// through something familiar. Then the rails: never a thumbs-down, never an explicit song when explicit is off, never a song heard in the last four
/// hours, never the artist that just played, never another version of a song already in the session, and no remix, live take or other recording unless
/// the session is mostly those (owner, 25 Nov: "don't add the same song just remixed into the same queue unless it's a full remix queue"). Only when that
/// leaves nothing do the rails give way, the four-hour rule first, the artist rule second. The picks are half songs the person knows and half new ones,
/// taken in turn, and spread: no artist twice in a row and at most two by one artist in any ten (owner, 25 Nov: Heads Will Roll, then a run of Yeah Yeah Yeahs).
/// </summary>
public static class Engine
{
    const double Gate = 0.6;
    /// <summary>At most this many songs by one artist in any <see cref="Window"/> picks in a row.</summary>
    public const int PerWindow = 2, Window = 10;
    static readonly Regex ExplicitTag = new(@"[(\[]\s*explicit\s*[)\]]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Why is worked out only for the songs picked: building it for every song in a big library took most of the time
    // (the reason's parts are kept as numbers: a closure for every song in the library cost more than the rest of the scoring)
    sealed record Cand(Track? Track, CatalogSong? New, string Artist, double Score, WhyParts Why, bool Fresh, bool Recent, bool SameArtist, bool Tied = true);
    readonly record struct WhyParts(string Artist, double Fit, double Love, bool Liked, bool ArtistPlayed, double Together);

    /// <summary>Another recording of a song: a remix, a live take, an acoustic or instrumental one, another vocal (a radio edit or a remaster is the song itself).</summary>
    public static bool IsOtherVersion(string title) => Matching.Recording(title).Length > 0 || RemixMatch.Remix(title) is not null;
    /// <summary>The same for a library song, worked out once per song.</summary>
    public static bool IsOtherVersion(Track t) => versions.GetValue(t, x => IsOtherVersion(x.Title) ? Yes : No) == Yes;
    static readonly object Yes = new(), No = new();
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Track, object> versions = new();

    /// <summary>Half the session or more is remixes and other recordings: a remix queue, which carries on with them.</summary>
    public static bool IsRemixQueue(IReadOnlyList<Track> session) => session.Count > 0 && session.Count(IsOtherVersion) * 2 >= session.Count;

    public static List<Pick> Next(Library lib, TasteProfile taste, Func<Track, Vibe?> vibe, IReadOnlyList<Track> session, IReadOnlyList<CatalogSong> related,
        DateTime utcNow, EngineOptions o)
    {
        var seed = session.Count > 0 ? session[^1] : null;
        var last = session.TakeLast(3).ToList();
        var lastKeys = last.Select(Matching.MatchKey).ToList();
        var lastArtists = last.Select(t => Matching.PrimaryArtist(t.Artist)).Where(a => a.Length > 0).Distinct().ToList();
        var mood = Mean(last.Select(vibe).OfType<Vibe>().ToList());
        var inSession = session.Select(Matching.MatchKey).ToHashSet();
        // any version of a song in the session is that song (owner, 12 Nov: "Dreamer", then its other vocal)
        var sessionSongs = session.Select(Matching.SongKey).ToHashSet();
        var remixes = IsRemixQueue(session);
        var artists = session.Select(t => Matching.PrimaryArtist(t.Artist)).Where(a => a.Length > 0).ToHashSet();
        var genres = last.Select(t => t.Genre.Trim()).Where(g => g.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var years = last.Select(t => t.Year).Where(y => y >= 1900).ToList();
        var year = years.Count > 0 ? years.Average() : 0;
        var prevArtist = seed is null ? "" : Matching.PrimaryArtist(seed.Artist);
        var relatedSongs = related.Select(s => Matching.MatchKey(s.Title, s.Artist)).ToHashSet();
        var relatedArtists = related.Select(s => Matching.PrimaryArtist(s.Artist)).ToHashSet();
        var rnd = new Random((int)(o.Seed % int.MaxValue));
        var nowMs = new DateTimeOffset(utcNow).ToUnixTimeMilliseconds();
        // a library has few genres and artists: each is worked out once per ask
        var scenes = new Dictionary<string, double>(StringComparer.Ordinal); var coArtists = new Dictionary<string, double>(StringComparer.Ordinal);
        double Scene(string g)
        {
            if (scenes.TryGetValue(g, out var have)) return have;
            var best = 0.0; foreach (var x in genres) best = Math.Max(best, GenreFamily.Near(x, g));
            return scenes[g] = best;
        }
        double Era(int y) => year <= 0 || y < 1900 ? 0 : Math.Max(0, 1 - Math.Abs(y - year) / 12);
        double CoArtist(string a)
        {
            if (coArtists.TryGetValue(a, out var have)) return have;
            var best = 0.0; foreach (var x in lastArtists) if (x != a) best = Math.Max(best, taste.ArtistsTogether(x, a));
            return coArtists[a] = best;
        }

        var cands = new List<Cand>();
        foreach (var t in lib.Songs)
        {
            // the song's key and lead artist once each: everything below is asked of every song in the library
            var key = Matching.MatchKey(t);
            if (inSession.Contains(key) || taste.Disliked(key)) continue;
            if (!o.Explicit && IsExplicit(t.Title, t.Album)) continue;
            // (Home's picks, with nothing playing, keep every version: the rule is about a queue)
            var version = seed is not null && IsOtherVersion(t);
            if (version && !remixes) continue;
            if (sessionSongs.Count > 0 && sessionSongs.Contains(Matching.SongKey(t))) continue;
            var artist = Matching.PrimaryArtist(t.Artist);
            var scene = seed is null ? 0 : Scene(t.Genre);
            var coSong = 0.0; foreach (var k in lastKeys) coSong = Math.Max(coSong, taste.PlayedTogether(k, key));
            var coArtist = seed is null ? 0 : CoArtist(artist);
            var link = artists.Contains(artist) || relatedArtists.Contains(artist) || scene > 0 || coSong > 0 || coArtist >= 0.3;
            // with a vibe on both sides it decides; without one, a song tied to what is playing passes and anything else does not
            var v = vibe(t);
            var tasteFit = taste.Fit(t, artist);
            var fit = seed is null ? tasteFit : mood is not null && v is not null ? 1 - VibeMath.Distance(mood, v) : link ? 0.6 + 0.1 * scene + 0.1 * Math.Max(coSong, coArtist) : 0.3;
            var liked = taste.Likes(key); var heard = taste.Known(key) > 0;
            if (seed is not null && (fit < Gate || (!heard && !liked && !link))) continue;
            // one of the best-known songs of an artist this person plays gets a little more
            var love = (liked ? 1 : Math.Max(taste.Love(key), tasteFit)) + (taste.IsHit(key) ? 0.2 : 0);
            // fans of what is playing play it, this person plays it near those songs, or it sits on a playlist they play from with one of the last three
            var onList = false; foreach (var s in last) if (taste.SharesPlayedPlaylist(s, t)) { onList = true; break; }
            var together = relatedSongs.Contains(key) || onList ? 1
                : Math.Max(coSong, Math.Max(relatedArtists.Contains(artist) ? 0.5 : 0, 0.6 * coArtist));
            var score = 0.35 * fit + 0.3 * love + 0.25 * together + 0.1 * scene + 0.1 * Era(t.Year) + (version ? 0.3 : 0)
                - 0.6 * taste.Penalty(key, artist) - (taste.PlayedWithin(key, nowMs, 24) ? 0.05 : 0) + rnd.NextDouble() * 0.01;
            cands.Add(new(t, null, artist, score, new(t.Artist, fit, love, liked, taste.ArtistFit(t) >= 0.5, together), !heard && !liked,
                taste.PlayedWithin(key, nowMs, 4), artist == prevArtist && artist.Length > 0, seed is null || link || together > 0));
        }
        // songs fans of this one play that are not in the library yet: tied to it by being on its list, so no vibe is needed to let them in
        foreach (var s in related)
        {
            if (lib.Find(s.Title, s.Artist) is not null || (!o.Explicit && IsExplicit(s.Title, s.Album))) continue;
            if ((IsOtherVersion(s.Title) && !remixes) || sessionSongs.Contains(Matching.SongKey(s.Title, s.Artist))) continue;
            var artist = Matching.PrimaryArtist(s.Artist);
            var stand = new Track("", s.Title, s.Artist, s.Album, s.Artist, 0, 0, 0, 0, null, 0, TrackSource.Cloud);
            if (taste.Disliked(stand)) continue;
            var love = taste.ArtistFit(stand);
            var score = 0.35 * 0.7 + 0.3 * love + 0.25 - 0.6 * taste.Penalty(stand) + rnd.NextDouble() * 0.01;
            cands.Add(new(null, s, artist, score, new(s.Artist, 0.7, love, false, love >= 0.5, 1), true, false, artist == prevArtist && artist.Length > 0));
        }
        // one copy of each song: the best scored (the album copy and a compilation's are one song)
        cands = cands.OrderByDescending(c => c.Score).DistinctBy(c => c.Track is { } t ? Matching.SongKey(t) : Matching.SongKey(c.New!.Title, c.New.Artist)).ToList();

        // a song tied to what played (its artist, a related artist or song, its genre, a playlist it is played from) before one that only has a similar
        // vibe and is loved: Voodoo People from search was followed by Party In The USA (owner, 12 Nov). Untied ones come in only when too few are tied
        if (cands.Count(c => c.Tied) >= o.Take) cands = cands.Where(c => c.Tied).ToList();
        // the rails, given way one at a time only when they would leave nothing
        var ok = cands.Where(c => !c.Recent && !c.SameArtist).ToList();
        if (ok.Count == 0) ok = cands.Where(c => !c.SameArtist).ToList();
        if (ok.Count == 0) ok = cands;
        return Blend(ok, o, prevArtist, seed);
    }

    /// <summary>Half known, half new (by NewShare), one per artist while there is enough to choose from, taken in turn so the list alternates; then
    /// spread so no artist plays twice in a row or more than <see cref="PerWindow"/> times in any <see cref="Window"/>. Only a pool of one or two
    /// artists may break that (there is nothing else to play).</summary>
    static List<Pick> Blend(List<Cand> cands, EngineOptions o, string prevArtist, Track? seed)
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
        var flood = sorted.Select(c => c.Artist).Distinct().Count() <= 2;
        var most = flood ? int.MaxValue : PerWindow * (int)Math.Ceiling(o.Take / (double)Window);
        var known = Take(false, o.Take - wantNew, 1); var fresh = Take(true, wantNew, 1);
        // short of one kind: more of the other, then the same artist again (up to the spread's limit)
        foreach (var cap in new[] { 1, Math.Min(most, PerWindow), most })
        {
            if (known.Count + fresh.Count < o.Take) fresh.AddRange(Take(true, o.Take - known.Count - fresh.Count, cap));
            if (known.Count + fresh.Count < o.Take) known.AddRange(Take(false, o.Take - known.Count - fresh.Count, cap));
        }
        var order = new List<Cand>();
        for (var i = 0; i < Math.Max(known.Count, fresh.Count); i++)
            foreach (var list in new[] { known, fresh })
                if (i < list.Count) order.Add(list[i]);
        return Spread(order, prevArtist, flood).Select(c => new Pick(c.Track, c.New, c.Score, Why(seed, c.Why.Artist, c.Why.Fit, c.Why.Love, c.Why.Liked, c.Why.ArtistPlayed, c.Why.Together))).ToList();
    }

    /// <summary>The picks in their order, each next one the first that is not by the artist just before it and whose artist has fewer than
    /// <see cref="PerWindow"/> songs in the last <see cref="Window"/>. Songs that never fit are left out, unless [flood] (one or two artists is all there is).</summary>
    static List<Cand> Spread(List<Cand> order, string prevArtist, bool flood)
    {
        var res = new List<Cand>(); var left = order.ToList();
        while (left.Count > 0)
        {
            var before = res.Count > 0 ? res[^1].Artist : prevArtist;
            var recent = res.Skip(Math.Max(0, res.Count - (Window - 1))).ToList();
            var i = left.FindIndex(c => c.Artist != before && recent.Count(r => r.Artist == c.Artist) < PerWindow);
            if (i < 0 && flood) i = left.FindIndex(c => c.Artist != before) is var j && j >= 0 ? j : 0;
            if (i < 0) break;
            res.Add(left[i]); left.RemoveAt(i);
        }
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
