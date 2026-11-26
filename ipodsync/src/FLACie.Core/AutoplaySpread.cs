namespace FLACie.Core;

/// <summary>
/// Autoplay never floods one artist (owner, 12 Nov: "I asked it to play Dreamer and then about ten songs by Livin' Joy, not ones I know, just because I
/// asked for one artist's song"). Of the next songs, at most [maxSeed] are by the artist of the song asked for (songs the person loves do not count
/// towards that), and no artist plays twice in a row while something else can go between. The order is kept otherwise.
/// </summary>
public static class AutoplaySpread
{
    public const int MaxSeed = 2;
    /// <summary>Songs the person loves may come more often when they fit (owner, 26 Nov: "I don't mind them all being from the same artist as long as
    /// the song is good and I like it and it matches the vibe"), but never a flood: at most [MaxRun] by one artist in a row and [MaxWindow] in any [Window].</summary>
    public const int MaxRun = 3, MaxWindow = 4, Window = 10, MaxLoved = 4;

    /// <summary>[already]: the seed artist's songs Autoplay has queued or played so far in this run. The cap is for the whole run, not each refill (owner,
    /// 25 Nov: Heads Will Roll, then 10-20 Yeah Yeah Yeahs songs). Songs by the seed artist that the person loves have their own, higher cap ([maxLoved]);
    /// songs they do not know stay at [maxSeed]. A loved song may follow its own artist (up to [MaxRun] in a row); an unknown one never does.</summary>
    public static List<T> Spread<T>(IReadOnlyList<T> songs, Func<T, string> artistOf, string seedArtist, Func<T, bool>? loved = null, int maxSeed = MaxSeed,
        int already = 0, int maxLoved = MaxLoved)
    {
        var seed = Matching.PrimaryArtist(seedArtist);
        string A(T t) => Matching.PrimaryArtist(artistOf(t));
        bool L(T t) => loved?.Invoke(t) ?? false;
        var kept = new List<T>(); var seedCount = already; var lovedCount = 0;
        foreach (var t in songs)
        {
            if (A(t) == seed)
            {
                if (L(t)) { if (lovedCount >= maxLoved) continue; lovedCount++; }
                else { if (seedCount >= maxSeed) continue; seedCount++; }
            }
            kept.Add(t);
        }
        // no artist twice in a row (a loved song may make a run of up to MaxRun): a song that would break that waits for the next one that does not
        int Run(List<T> o) { var n = 0; for (var i = o.Count - 1; i >= 0 && A(o[i]) == A(o[^1]); i--) n++; return n; }
        bool Fits(List<T> o, T t) => o.Count == 0 || A(o[^1]) != A(t) || (L(t) && L(o[^1]) && Run(o) < MaxRun);
        var output = new List<T>(); var waiting = new List<T>();
        foreach (var t in kept)
        {
            if (!Fits(output, t)) { waiting.Add(t); continue; }
            output.Add(t);
            for (var i = 0; i < waiting.Count; i++)
                if (Fits(output, waiting[i])) { output.Add(waiting[i]); waiting.RemoveAt(i); i = -1; }
        }
        output.AddRange(waiting);   // nothing else could go between: they come last
        // hard rule: no artist more than MaxWindow times in any Window songs in a row
        var final = new List<T>();
        foreach (var t in output)
            if (final.Skip(Math.Max(0, final.Count - (Window - 1))).Count(x => A(x) == A(t)) < MaxWindow) final.Add(t);
        return final;
    }

    /// <summary>One version of each song, and none of a song already heard or queued ([heard] holds their <see cref="Matching.SongKey"/>s): Autoplay played
    /// "Dreamer", its 7-inch Mix and its Janice Robinson Vocal one after another (owner, 12 Nov). The order is kept.</summary>
    public static List<T> OneVersion<T>(IEnumerable<T> songs, Func<T, string> titleOf, Func<T, string> artistOf, IEnumerable<string> heard)
    {
        var seen = heard.ToHashSet();
        return songs.Where(t => seen.Add(Matching.SongKey(titleOf(t), artistOf(t)))).ToList();
    }

    /// <summary>How many of the chosen songs Autoplay goes by.</summary>
    public const int SeedCount = 8;

    /// <summary>The songs Autoplay goes by, oldest first (owner, 12 Nov: "based on the songs at the end of a playlist, or the song I asked for or clicked
    /// play on"). While the person's own queue plays: the song playing and the few before it. Once Autoplay has taken over ([autoplayFrom] is where its
    /// songs start): the last songs of the person's own queue, never Autoplay's own picks, so it does not drift away from what they chose.</summary>
    public static List<T> Seeds<T>(IReadOnlyList<T> queue, int autoplayFrom, int index)
    {
        var end = autoplayFrom > 0 && index >= autoplayFrom ? autoplayFrom : Math.Min(index + 1, queue.Count);
        return queue.Take(Math.Max(0, end)).TakeLast(SeedCount).ToList();
    }

    /// <summary>The chosen song the related-songs lookup starts from: the song playing in the person's own queue; under Autoplay, each Autoplay song
    /// takes the next of <see cref="Seeds"/> from the end back (one song asked for stays that song).</summary>
    public static T? Lead<T>(IReadOnlyList<T> queue, int autoplayFrom, int index)
    {
        var seeds = Seeds(queue, autoplayFrom, index);
        if (seeds.Count == 0) return default;
        if (!(autoplayFrom > 0 && index >= autoplayFrom)) return seeds[^1];
        return seeds[seeds.Count - 1 - (index - autoplayFrom) % seeds.Count];
    }
}
