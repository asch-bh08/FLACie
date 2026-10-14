namespace FLACie.Core;

/// <summary>One tape or CD on the iPod desk's shelf: something that can be played from the start.</summary>
public sealed record DeskSource(string Id, string Label, string Sub, string? ArtKey, IReadOnlyList<Track> Tracks);

/// <summary>What the iPod desk's shelf holds: Up Next (while something plays), Favourites, then each playlist; anything that resolves to no songs is left off. Pure and unit-tested.</summary>
public static class DeskSources
{
    /// <summary>The play context a shelf pick starts with ("desk:" + the source id), so the shelf can light the source that is playing.</summary>
    public const string ContextPrefix = "desk:";

    public static List<DeskSource> Build(IReadOnlyList<Track> queue, int index, IReadOnlyList<PlaylistEntry> favorites, IReadOnlyList<Playlist> playlists,
        Func<IEnumerable<PlaylistEntry>, IReadOnlyList<Track>> resolve)
    {
        var all = new List<DeskSource>();
        if (index >= 0 && index < queue.Count) all.Add(Make("upnext", "Up Next", queue.Skip(index).ToList(), queue[index].ArtKey));
        if (favorites.Count > 0 && resolve(favorites) is { Count: > 0 } fav) all.Add(Make("favorites", "Favourites", fav));
        foreach (var p in playlists) if (resolve(p.Entries) is { Count: > 0 } ts) all.Add(Make("pl:" + p.Id, p.Name, ts));
        return all;
    }

    /// <summary>The shelf item that is playing: the desk source the play context names, else Up Next while anything plays.</summary>
    public static string? ActiveId(string? context, bool playing) =>
        !playing ? null : context is not null && context.StartsWith(ContextPrefix, StringComparison.Ordinal) ? context[ContextPrefix.Length..] : "upnext";

    static DeskSource Make(string id, string label, IReadOnlyList<Track> ts, string? art = null) =>
        new(id, label, ts.Count == 1 ? "1 song" : $"{ts.Count} songs", art ?? ts.Select(t => t.ArtKey).FirstOrDefault(a => a is not null), ts);
}
