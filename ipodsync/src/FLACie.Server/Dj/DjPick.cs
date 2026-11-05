using System.Collections.Concurrent;
using FLACie.Core;

namespace FLACie.Server.Dj;

/// <summary>
/// F11: which of several songs that match is meant. Own plays first, then popularity, then how close the title is to what was said
/// (Corona's The Rhythm of the Night over DeBarge's Rhythm of the Night). The others are kept for <see cref="Life"/> so "not that one" plays the next.
/// </summary>
public sealed class DjPick(Func<DateTime> now)
{
    public static readonly TimeSpan Life = TimeSpan.FromMinutes(1);

    sealed record Left(DateTime At, IReadOnlyList<Track> Others);
    readonly ConcurrentDictionary<string, Left> left = new();

    /// <summary>The matches, best first.</summary>
    public static IReadOnlyList<Track> Rank(IDjAskUser u, IEnumerable<Track> matches, string said)
    {
        var top = u.MostPlayed(500);
        var plays = new Dictionary<string, int>();
        for (var i = 0; i < top.Count; i++) plays.TryAdd(top[i].Path, top.Count - i);
        var key = Matching.NormTitle(said);
        return matches.DistinctBy(t => t.Path)
            .OrderByDescending(t => plays.GetValueOrDefault(t.Path))
            .ThenByDescending(u.Popularity)
            .ThenBy(t => Distance(Matching.NormTitle(t.Title), key))
            .ToList();
    }

    /// <summary>Songs in the library that share the title (with or without "The"), for ranking alongside what the matcher found.</summary>
    public static IEnumerable<Track> SameTitle(IDjAskUser u, Track t)
    {
        var k = Core(t.Title);
        return u.Songs.Where(x => Core(x.Title) == k);
    }

    static string Core(string title) { var n = Matching.NormTitle(title); return n.StartsWith("the ") ? n[4..] : n; }

    /// <summary>Keeps the matches not played, for "the other one".</summary>
    public void Keep(string account, IReadOnlyList<Track> others)
    {
        if (others.Count == 0) left.TryRemove(account, out _);
        else left[account] = new(now(), others);
    }

    /// <summary>The next match after "not that one" ([artist]: "the Corona one"), or null when none is left or it's been over a minute.</summary>
    public Track? Other(string account, string? artist = null)
    {
        if (!left.TryGetValue(account, out var l) || now() - l.At > Life) return null;
        var pick = artist is { Length: > 0 } a ? l.Others.FirstOrDefault(t => Matching.NormTitle(t.Artist).Contains(Matching.NormTitle(a))) : l.Others.FirstOrDefault();
        if (pick is null) return null;
        Keep(account, l.Others.Where(t => t != pick).ToList());
        return pick;
    }

    static int Distance(string a, string b)
    {
        var d = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) d[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            var prev = d[0]; d[0] = i;
            for (var j = 1; j <= b.Length; j++) { var t = d[j]; d[j] = Math.Min(Math.Min(d[j] + 1, d[j - 1] + 1), prev + (a[i - 1] == b[j - 1] ? 0 : 1)); prev = t; }
        }
        return d[b.Length];
    }
}
