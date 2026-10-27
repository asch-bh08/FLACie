using System.Text.RegularExpressions;

namespace FLACie.Core;

/// <summary>A learned request: the phrase key someone used, and when (ms). The plays after it (until the next one, or two hours) count for the phrase.</summary>
public sealed record PhraseAsk(string Key, long At);

/// <summary>
/// What one person plays in a situation (in the car, at night, in the morning) and after a phrase of their own ("sunday cleaning"): a score per song path,
/// weighted as the taste profile is (a finish counts for, a skip against, anything else a little), fading with a 90-day half-life.
/// </summary>
public static class SituationTaste
{
    public static readonly string[] Known = ["car", "night", "morning", "workout", "chill"];
    public static readonly TimeSpan PhraseWindow = TimeSpan.FromHours(2);
    const double HalfLifeDays = 90;

    public static bool In(ListenEvent e, string situation) => situation switch
    {
        "car" => e.Surface == "car",
        "night" => e.Hour is >= 22 or < 5,
        "morning" => e.Hour is >= 5 and < 11,
        _ => false,
    };

    static double Weight(ListenEvent e, long nowMs)
    {
        var d = ListenClass.IsFinish(e) ? 1 : ListenClass.IsSkip(e) ? -1 : 0.3;
        return d * Math.Pow(0.5, Math.Max(0, nowMs - e.At) / 86_400_000.0 / HalfLifeDays);
    }

    public static Dictionary<string, double> Score(IEnumerable<ListenEvent> events, Func<ListenEvent, bool> keep, long nowMs)
    {
        var res = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var e in events)
            if (e.Kind == ListenKind.Play && keep(e)) res[e.Path] = res.GetValueOrDefault(e.Path) + Weight(e, nowMs);
        return res;
    }

    /// <summary>The plays after each request with [key], up to the next request or <see cref="PhraseWindow"/>.</summary>
    public static Dictionary<string, double> Phrase(IReadOnlyList<ListenEvent> events, IReadOnlyList<PhraseAsk> asks, string key, long nowMs)
    {
        var sorted = asks.OrderBy(a => a.At).ToList();
        var windows = new List<(long From, long To)>();
        for (var i = 0; i < sorted.Count; i++)
        {
            if (sorted[i].Key != key) continue;
            var to = sorted[i].At + (long)PhraseWindow.TotalMilliseconds;
            if (i + 1 < sorted.Count) to = Math.Min(to, sorted[i + 1].At);
            windows.Add((sorted[i].At, to));
        }
        return windows.Count == 0 ? [] : Score(events, e => windows.Any(w => e.At >= w.From && e.At < w.To), nowMs);
    }
}

/// <summary>A request's phrase reduced to its words that matter ("play some B-road music" is "b road"), and matched to one already learned.</summary>
public static class PhraseKeys
{
    static readonly HashSet<string> Filler = ["play", "put", "on", "some", "me", "us", "music", "songs", "song", "tunes", "tune", "stuff", "something", "for", "the", "a", "an",
        "give", "i", "want", "need", "to", "of", "my", "please", "mix", "bit", "any", "anything", "playlist", "tracks", "track"];

    static string Stem(string w) => w.Length > 3 && w.EndsWith('s') && !w.EndsWith("ss") ? w[..^1] : w;

    public static string Of(string text)
    {
        var s = Regex.Replace((text ?? "").ToLowerInvariant().Replace("'", "").Replace("’", ""), @"[^\p{L}\p{N}]+", " ");
        var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !Filler.Contains(w)).Select(Stem).ToList();
        return string.Join(' ', words).Trim();
    }

    /// <summary>The learned key with (nearly) the same words as [key], else [key] itself.</summary>
    public static string Closest(string key, IEnumerable<string> known)
    {
        var mine = Of(key).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        string best = key; var bestScore = 0.0;
        foreach (var k in known.Distinct())
        {
            if (k == key) return k;
            var theirs = k.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            if (mine.Count == 0 || theirs.Count == 0) continue;
            var j = (double)mine.Intersect(theirs).Count() / mine.Union(theirs).Count();
            if (j >= 0.5 && j > bestScore) { best = k; bestScore = j; }
        }
        return best;
    }
}
