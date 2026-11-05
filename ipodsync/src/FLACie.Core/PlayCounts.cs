namespace FLACie.Core;

/// <summary>
/// F21: the songs really listened to, for Home's "Frequently played" row (the phone has the same rules: library/PlayCounts.kt). A real play is heard to
/// 60 % of the song or 4 minutes; a skip (under 30 s heard, not a real play) counts against the song; a play in between counts for nothing. Newer plays
/// weigh more (half-life 30 days). A song needs <see cref="MinReal"/> real plays to show. One entry per song (its match key), best first.
/// </summary>
public static class PlayCounts
{
    public const int MinReal = 3;
    const double HalfLifeMs = 30.0 * 86_400_000.0;

    public static bool IsReal(double heard, double length) => heard >= 240 || (length > 0 && heard >= length * 0.6);
    public static bool IsSkip(double heard, double length) => !IsReal(heard, length) && heard < 30;

    /// <summary>The plays of the listening log, ranked: each song's newest play (for its path, title and artist), most listened first.</summary>
    public static List<ListenEvent> Rank(IEnumerable<ListenEvent> events, long nowMs)
    {
        Dictionary<string, double> score = []; Dictionary<string, int> real = []; Dictionary<string, ListenEvent> last = [];
        foreach (var e in events)
        {
            if (e.Kind != ListenKind.Play) continue;
            var key = Matching.MatchKey(e.Title, e.Artist);
            var w = Math.Pow(0.5, Math.Max(0, nowMs - e.At) / HalfLifeMs);
            if (IsReal(e.Heard, e.Length))
            {
                score[key] = score.GetValueOrDefault(key) + w; real[key] = real.GetValueOrDefault(key) + 1;
                if (!last.TryGetValue(key, out var l) || e.At > l.At) last[key] = e;
            }
            else if (IsSkip(e.Heard, e.Length)) score[key] = score.GetValueOrDefault(key) - w;
        }
        return real.Where(r => r.Value >= MinReal && score.GetValueOrDefault(r.Key) > 0).Select(r => r.Key)
            .OrderByDescending(k => score[k]).ThenByDescending(k => last[k].At).ThenBy(k => k, StringComparer.Ordinal)
            .Select(k => last[k]).ToList();
    }
}
