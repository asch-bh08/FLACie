namespace FLACie.Core;

/// <summary>One level of the library as the iPod shows it: its title, rows and kind, plus the songs or groups the rows stand for (so an id like "t:3" can be resolved).</summary>
public sealed record IpodLevel(string Title, IReadOnlyList<IpodRow> Rows, IpodKind Kind, IReadOnlyList<Track>? Tracks = null, IReadOnlyList<Group>? Groups = null);

/// <summary>Something to play: this list from this index, or all of it shuffled.</summary>
public sealed record IpodPlay(IReadOnlyList<Track> List, int Index, bool Shuffle);

/// <summary>
/// The library side of the iPod menus (the wheel iPods and the touch iPods share it): Artists, Albums, Songs, Genres, Playlists, Cover Flow, and what a press on one of their rows
/// opens or plays. Pure and unit-tested. Row ids: "t:N" a song of the level (or "t:shuffle"), "a:N" an artist, "b:N" an album, "g:N" a genre, "p:N" a playlist, "c:N" a Cover Flow album.
/// </summary>
public static class IpodContent
{
    public static string Count(int n) => n == 1 ? "1 song" : n + " songs";

    public static string Time(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }

    public static IReadOnlyList<IpodRow> SongRows(IReadOnlyList<Track> ts)
    {
        var rows = new List<IpodRow>(ts.Count + 1);
        if (ts.Count > 1) rows.Add(new IpodRow("Shuffle", "t:shuffle", false, ArtKey: ts.Select(t => t.ArtKey).FirstOrDefault(a => a is not null), Detail: Count(ts.Count)));
        for (var i = 0; i < ts.Count; i++)
        {
            var t = ts[i];
            rows.Add(new IpodRow(t.Title, "t:" + i, false, ArtKey: t.ArtKey, Sub: t.Artist, Detail: string.Join("  ·  ", new[] { t.Album, Time(t.DurationMs / 1000.0) }.Where(x => x.Length > 0))));
        }
        return rows;
    }

    public static IReadOnlyList<IpodRow> GroupRows(IReadOnlyList<Group> gs, string prefix, bool artistSub) =>
        gs.Select((g, i) => new IpodRow(g.Name, prefix + i, ArtKey: g.ArtKey, Sub: artistSub ? g.Tracks.FirstOrDefault()?.Artist : null, Detail: Count(g.Tracks.Count))).ToList();

    public static IReadOnlyList<Group> Genres(Library lib) => lib.Songs.GroupBy(t => string.IsNullOrWhiteSpace(t.Genre) ? "Unknown" : t.Genre.Trim(), StringComparer.OrdinalIgnoreCase)
        .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase).Select(g => new Group(g.Key, g.ToList(), g.Select(t => t.ArtKey).FirstOrDefault(a => a is not null))).ToList();

    static IpodLevel Groups(string title, IReadOnlyList<Group> gs, string prefix, bool artistSub, IpodKind kind) =>
        new(title, gs.Count == 0 ? [new IpodRow("None", "m:none", false)] : GroupRows(gs, prefix, artistSub), gs.Count == 0 && kind == IpodKind.CoverFlow ? IpodKind.Menu : kind, null, gs);

    /// <summary>The level a library menu id opens ("m:songs", "m:artists", ...), or null when the id is not a library one.</summary>
    public static IpodLevel? Level(string id, Library lib, Func<IReadOnlyList<Group>> playlists) => id switch
    {
        "m:songs" => new IpodLevel("Songs", SongRows(lib.Songs), IpodKind.Songs, lib.Songs),
        "m:artists" => Groups("Artists", lib.Artists, "a:", false, IpodKind.Artists),
        "m:albums" => Groups("Albums", lib.Albums, "b:", true, IpodKind.Albums),
        "m:genres" => Groups("Genres", Genres(lib), "g:", false, IpodKind.Albums),
        "m:pl" => Groups("Playlists", playlists(), "p:", false, IpodKind.Albums),
        "m:cover" => Groups("Cover Flow", lib.Albums, "c:", true, IpodKind.CoverFlow),
        _ => null,
    };

    /// <summary>What a press on a row of <paramref name="current"/> does: a deeper level, or songs to play. Both null when the id means nothing here.</summary>
    public static (IpodLevel? Next, IpodPlay? Play) Open(string id, IpodLevel current)
    {
        if (id.StartsWith("t:") && current.Tracks is { } ts)
        {
            if (id == "t:shuffle") return (null, new IpodPlay(ts, 0, true));
            if (int.TryParse(id.AsSpan(2), out var i) && i >= 0 && i < ts.Count) return (null, new IpodPlay(ts, i, false));
            return (null, null);
        }
        if (id.Length > 2 && current.Groups is { } gs && int.TryParse(id.AsSpan(2), out var gi) && gi >= 0 && gi < gs.Count)
        {
            var g = gs[gi];
            if (id.StartsWith("a:"))
            {
                var albums = g.Tracks.GroupBy(t => t.Album).Select(x => new Group(string.IsNullOrEmpty(x.Key) ? "Unknown Album" : x.Key, x.ToList(), x.Select(t => t.ArtKey).FirstOrDefault(a => a is not null))).ToList();
                if (albums.Count <= 1) return (new IpodLevel(g.Name, SongRows(g.Tracks), IpodKind.Songs, g.Tracks), null);
                // "All Songs" first (the artist itself), then each album: ids b:0, b:1 ... line up with this list
                var list = new List<Group> { g }; list.AddRange(albums);
                var rows = new List<IpodRow> { new("All Songs", "b:0", ArtKey: g.ArtKey, Detail: Count(g.Tracks.Count)) };
                for (var k = 0; k < albums.Count; k++) rows.Add(new IpodRow(albums[k].Name, "b:" + (k + 1), ArtKey: albums[k].ArtKey, Detail: Count(albums[k].Tracks.Count)));
                return (new IpodLevel(g.Name, rows, IpodKind.Albums, null, list), null);
            }
            if (id.StartsWith("b:") || id.StartsWith("g:") || id.StartsWith("p:") || id.StartsWith("c:"))
                return (new IpodLevel(g.Name, SongRows(g.Tracks), IpodKind.Songs, g.Tracks), null);
        }
        return (null, null);
    }
}
