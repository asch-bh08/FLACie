using System.Text.RegularExpressions;
using FLACie.Core;

namespace FLACie.Server.Dj;

/// <summary>
/// F22: "play my favourite Prodigy songs": the person's real favourites among some songs (an artist, an album, a genre or a decade), without them having
/// hearted anything (F26.4: <see cref="Pick"/>).
/// </summary>
public static partial class DjFavourites
{
    public const int Fill = 15;

    /// <summary>The favourites and how many of them come from the person's own listening (the rest are hits filling up to <see cref="Fill"/>).</summary>
    public sealed record Picked(List<Track> Songs, int Played);

    public static List<Track> Rank(IDjAskUser u, IEnumerable<Track> scope, DateTime nowUtc) => Pick(u, scope, nowUtc).Songs;

    /// <summary>
    /// F26.4 (owner: "it gave songs played once or never"): plays are the larger of FLACie's own real plays (F21) and Jellyfin's count (every app,
    /// from before the log began); hearted songs and songs played twice or more first, then songs played once; then hits to fill to
    /// <see cref="Fill"/>: their popularity, then songs on several of their albums (a hit is on the album and the best-of), then the newest. A song
    /// skipped early more than it was played through is left out.
    /// </summary>
    public static Picked Pick(IDjAskUser u, IEnumerable<Track> scope, DateTime nowUtc)
    {
        var stats = PlayCounts.Scores(u.Listens, new DateTimeOffset(nowUtc).ToUnixTimeMilliseconds());
        var hearts = u.Favourites.Select(Matching.MatchKey).ToHashSet();
        var all = scope.ToList();
        var albums = all.GroupBy(Matching.MatchKey).ToDictionary(g => g.Key, g => g.Select(t => t.Album).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var songs = all.GroupBy(Matching.MatchKey).Select(g =>
        {
            var t = g.OrderByDescending(x => u.JellyfinPlays(x)).First(); var k = g.Key;
            var s = stats.GetValueOrDefault(k) ?? new(0, 0, 0); var h = hearts.Contains(k);
            var plays = Math.Max(s.Real, g.Sum(u.JellyfinPlays));
            var skipped = !h && s.Skips > s.Real && s.Score < 0 && g.Sum(u.JellyfinPlays) < 2;
            var tier = skipped ? 3 : h || plays >= 2 ? 0 : plays == 1 ? 1 : 2;
            return (Track: t, Tier: tier, Heart: h, Plays: plays, s.Score, Pop: u.Popularity(t), Albums: albums.GetValueOrDefault(k));
        }).ToList();
        var loved = songs.Where(x => x.Tier < 2).OrderBy(x => x.Tier).ThenByDescending(x => x.Heart).ThenByDescending(x => x.Plays).ThenByDescending(x => x.Score).ThenByDescending(x => x.Pop)
            .Select(x => x.Track).ToList();
        var hits = songs.Where(x => x.Tier == 2).OrderByDescending(x => x.Pop).ThenByDescending(x => x.Albums).ThenByDescending(x => x.Track.Year)
            .Select(x => x.Track).Take(Math.Max(0, Fill - loved.Count));
        return new([.. loved, .. hits], loved.Count);
    }

    /// <summary>What the DJ says it did: "You've played 6 of their songs a lot; then their biggest hits."</summary>
    public static string Say(Picked p, string name, bool byArtist)
    {
        var whose = byArtist ? "their" : name;
        var filled = p.Songs.Count > p.Played;
        return p.Played switch
        {
            0 => byArtist ? $"You haven't played much {name} yet, so here are their biggest hits." : $"You haven't played much {name} yet, so here are the biggest hits.",
            1 => filled ? $"Your favourite {name} song first, then {whose} biggest hits." : $"Your favourite {name} song.",
            var n => filled ? $"You've played {n} of {whose} songs a lot; then {whose} biggest hits." : $"The {n} {name} songs you play most.",
        };
    }

    [GeneratedRegex(@"^(?:(?:19|20)?(\d)0s|(twenties|thirties|forties|fifties|sixties|seventies|eighties|nineties|noughties))$")] private static partial Regex DecadeRx();
    static readonly string[] Words = ["twenties", "thirties", "forties", "fifties", "sixties", "seventies", "eighties", "nineties"];

    /// <summary>The first year of a decade said ("90s", "1990s", "nineties", "2000s", "noughties"), or null.</summary>
    public static int? Decade(string what)
    {
        var m = DecadeRx().Match(what.Trim().ToLowerInvariant());
        if (!m.Success) return null;
        if (m.Groups[2].Success) return m.Groups[2].Value == "noughties" ? 2000 : 1920 + Array.IndexOf(Words, m.Groups[2].Value) * 10;
        var d = int.Parse(m.Groups[1].Value);
        var full = what.Trim();
        return full.StartsWith("20") ? 2000 + d * 10 : full.StartsWith("19") ? 1900 + d * 10 : d <= 2 ? 2000 + d * 10 : 1900 + d * 10;
    }
}
