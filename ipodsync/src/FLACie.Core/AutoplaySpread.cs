namespace FLACie.Core;

/// <summary>
/// Autoplay never floods one artist (owner, 12 Nov: "I asked it to play Dreamer and then about ten songs by Livin' Joy, not ones I know, just because I
/// asked for one artist's song"). Of the next songs, at most [maxSeed] are by the artist of the song asked for (songs the person loves do not count
/// towards that), and no artist plays twice in a row while something else can go between. The order is kept otherwise.
/// </summary>
public static class AutoplaySpread
{
    public const int MaxSeed = 2;

    public static List<T> Spread<T>(IReadOnlyList<T> songs, Func<T, string> artistOf, string seedArtist, Func<T, bool>? loved = null, int maxSeed = MaxSeed)
    {
        var seed = Matching.PrimaryArtist(seedArtist);
        string A(T t) => Matching.PrimaryArtist(artistOf(t));
        // the seed artist's songs past the cap go (loved ones stay)
        var kept = new List<T>(); var seedCount = 0;
        foreach (var t in songs)
        {
            if (A(t) == seed && !(loved?.Invoke(t) ?? false)) { if (seedCount >= maxSeed) continue; seedCount++; }
            kept.Add(t);
        }
        // no artist twice in a row: a song that would follow its own artist waits for the next one that does not
        var output = new List<T>(); var waiting = new List<T>();
        foreach (var t in kept)
        {
            if (output.Count > 0 && A(output[^1]) == A(t)) { waiting.Add(t); continue; }
            output.Add(t);
            for (var i = 0; i < waiting.Count; i++)
                if (A(output[^1]) != A(waiting[i])) { output.Add(waiting[i]); waiting.RemoveAt(i); i = -1; }
        }
        output.AddRange(waiting);   // nothing else could go between: they come last
        return output;
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
