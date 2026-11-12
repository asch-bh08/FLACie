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
}
