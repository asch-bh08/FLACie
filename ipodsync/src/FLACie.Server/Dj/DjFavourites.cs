using System.Text.RegularExpressions;
using FLACie.Core;

namespace FLACie.Server.Dj;

/// <summary>
/// F22: "play my favourite Prodigy songs": the person's real favourites among some songs (an artist, an album, a genre or a decade), without them having
/// hearted anything. Hearted songs and songs played through more than once come first, then songs played through once, by real plays (F21's rules:
/// 60 % or 4 minutes, newer plays weigh more); then their most popular songs to fill to <see cref="Fill"/>; songs skipped early more than played last.
/// </summary>
public static partial class DjFavourites
{
    public const int Fill = 15;

    public static List<Track> Rank(IDjAskUser u, IEnumerable<Track> scope, DateTime nowUtc)
    {
        var stats = PlayCounts.Scores(u.Listens, new DateTimeOffset(nowUtc).ToUnixTimeMilliseconds());
        var hearts = u.Favourites.Select(Matching.MatchKey).ToHashSet();
        var songs = scope.DistinctBy(Matching.MatchKey).Select(t =>
        {
            var k = Matching.MatchKey(t); var s = stats.GetValueOrDefault(k) ?? new(0, 0, 0); var h = hearts.Contains(k);
            var tier = s.Score < 0 && !h ? 3 : h || s.Real >= 2 ? 0 : s.Real == 1 ? 1 : 2;
            return (Track: t, Tier: tier, Heart: h, s.Score, Pop: u.Popularity(t));
        }).ToList();
        var loved = songs.Where(x => x.Tier < 2).OrderBy(x => x.Tier).ThenByDescending(x => x.Heart).ThenByDescending(x => x.Score).ThenByDescending(x => x.Pop).Select(x => x.Track).ToList();
        var fill = songs.Where(x => x.Tier == 2).OrderByDescending(x => x.Pop).ThenBy(x => x.Track.Title, StringComparer.OrdinalIgnoreCase).Select(x => x.Track);
        var skipped = songs.Where(x => x.Tier == 3).OrderByDescending(x => x.Score).Select(x => x.Track);
        return [.. loved, .. fill.Take(Math.Max(0, Fill - loved.Count)), .. skipped];
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
