using System.Collections.Concurrent;

namespace FLACie.Core;

/// <summary>The iPod desk's disc pictures (the release's own "Medium" image from the Cover Art Archive), one lookup per album.
/// The page reads on its render thread while lookups finish on the thread pool, so the store is a concurrent one.</summary>
public sealed class DeskDiscArts
{
    readonly ConcurrentDictionary<string, string?> known = new();
    readonly ConcurrentBag<Task> pending = [];

    /// <summary>The album's disc picture when one was found; null while it is looked up, or when there is none (the desk prints one from the cover).
    /// The first ask for an album starts <paramref name="lookup"/>; <paramref name="found"/> runs when it brings back a usable picture.</summary>
    public string? Get(string key, Func<Task<string?>> lookup, Func<Task> found)
    {
        if (known.TryGetValue(key, out var url)) return url;
        if (!known.TryAdd(key, null)) return known.GetValueOrDefault(key);
        pending.Add(Task.Run(async () =>
        {
            try
            {
                if (await lookup() is { } got && Usable(got)) { known[key] = got; await found(); }
            }
            catch (Exception) { }   // no picture: the desk prints one from the cover
        }));
        return null;
    }

    /// <summary>Every lookup started so far has finished (tests).</summary>
    public Task Settled() => Task.WhenAll(pending.ToArray());

    /// <summary>The picture goes into a CSS url('…'): only a plain https address, nothing that could close the quote or the url.</summary>
    static bool Usable(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
        && !url.Any(c => c is '\'' or '"' or '(' or ')' or '\\' || char.IsWhiteSpace(c) || char.IsControl(c));
}
