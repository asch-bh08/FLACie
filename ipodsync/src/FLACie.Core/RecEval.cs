namespace FLACie.Core;

/// <summary>
/// The offline measure for recommendations: listening replayed song by song, and how often the song that really played next is in the
/// top ten a recommender offers for what had played so far (hit rate at 10).
/// </summary>
public static class RecEval
{
    /// <summary>The part of [cases] whose actual next song (same song by title and artist) is in the first ten [recommender] gives for its session.</summary>
    public static double HitRateAt10(Func<IReadOnlyList<Track>, List<Track>> recommender, IReadOnlyList<(IReadOnlyList<Track> session, Track actualNext)> cases)
    {
        if (cases.Count == 0) return 0;
        var hits = 0;
        foreach (var (session, next) in cases)
        {
            var key = Matching.MatchKey(next);
            if (recommender(session).Take(10).Any(t => Matching.MatchKey(t) == key)) hits++;
        }
        return (double)hits / cases.Count;
    }

    /// <summary>
    /// Plays (oldest first or not) cut into listening sessions: a gap of [gapMinutes] between two starts begins a new one, and the same song started
    /// again within a minute is one play. Every song after the first of a session is a case, with the songs before it in that session.
    /// </summary>
    public static List<(IReadOnlyList<Track> Session, Track Next, long At)> Cases(IEnumerable<(Track Track, long At)> plays, int gapMinutes = 30)
    {
        var res = new List<(IReadOnlyList<Track>, Track, long)>();
        var session = new List<Track>(); long lastAt = long.MinValue;
        foreach (var (t, at) in plays.OrderBy(p => p.At))
        {
            var same = session.Count > 0 && Matching.MatchKey(session[^1]) == Matching.MatchKey(t);
            if (same && at - lastAt < 60_000) { lastAt = at; continue; }
            if (session.Count > 0 && at - lastAt > gapMinutes * 60_000L) session = [];
            if (session.Count > 0 && !same) res.Add((session.ToList(), t, at));
            session.Add(t); lastAt = at;
        }
        return res;
    }
}
