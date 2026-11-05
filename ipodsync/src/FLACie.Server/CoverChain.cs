using FLACie.Core;

namespace FLACie.Server;

/// <summary>One place a cover is looked for (F1). [Value]: the Jellyfin picture key ("jf<item>~<tag>" or "jf<folder>"), the Jellyfin artist id, or the artist's name.</summary>
public sealed record CoverStep(CoverStep.Kinds Kind, string Value, string Album = "", string Title = "")
{
    public enum Kinds { Catalog, JellyfinPicture, JellyfinArtist, CatalogArtist }
}

/// <summary>
/// F1: the cover chain for a song without a picture of its own. The catalog (the song's own release, then its album), the folder's picture as Jellyfin has
/// it, the artist's photo on Jellyfin, then the catalog's photo of the artist; when every one comes up empty the made cover (<see cref="GeneratedCover"/>).
/// </summary>
public static class CoverChain
{
    /// <summary>The steps for a catalog lookup key, in order; empty for any other key.</summary>
    public static IReadOnlyList<CoverStep> Plan(string key)
    {
        if (Art.ParseLookupKey(key) is not var (artist, album, title)) return [];
        var steps = new List<CoverStep> { new(CoverStep.Kinds.Catalog, artist, album, title) };
        foreach (var f in Art.LookupFallbacks(key))
            steps.Add(f.StartsWith("ja", StringComparison.Ordinal) ? new(CoverStep.Kinds.JellyfinArtist, f[2..])
                : new(CoverStep.Kinds.JellyfinPicture, f.StartsWith("jd", StringComparison.Ordinal) ? "jf" + f[2..] : f));
        if (artist.Length > 0) steps.Add(new(CoverStep.Kinds.CatalogArtist, artist));
        return steps;
    }

    /// <summary>The first picture a step gives, and the step it came from; null with [Transient] when some step could not be asked (try again, not a miss).</summary>
    public static async Task<(byte[]? Bytes, CoverStep? From, bool Transient)> RunAsync(IReadOnlyList<CoverStep> steps,
        Func<CoverStep, CancellationToken, Task<(byte[]? Bytes, bool Transient)>> fetch, CancellationToken ct)
    {
        var transient = false;
        foreach (var step in steps)
        {
            var (bytes, t) = await fetch(step, ct);
            if (bytes is { Length: > 0 }) return (bytes, step, false);
            transient |= t;
        }
        return (null, null, transient);
    }
}
