namespace FLACie.Core;

/// <summary>A user's merged library, built the same way as the Android app (Library.rebuild): Jellyfin first, then NAS
/// songs Jellyfin doesn't already have (by file, or the same song on the same album), with no duplicates.</summary>
public sealed class Library
{
    public IReadOnlyList<Track> Songs { get; }
    public IReadOnlyList<Group> Albums { get; }
    public IReadOnlyList<Group> Artists { get; }
    readonly Dictionary<string, Track> byPath;
    readonly Dictionary<string, Track> byMatch;
    readonly Dictionary<string, Track> byJellyfinId;

    public static readonly Library Empty = new([]);

    public Library(IReadOnlyList<Track> tracks)
    {
        Songs = tracks.OrderBy(t => Matching.SortKey(t.Title)).ToList();
        byPath = new(); byMatch = new(); byJellyfinId = new(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tracks)
        {
            byPath.TryAdd(t.Path, t);
            byMatch.TryAdd(Matching.MatchKey(t), t);
            if (t.JellyfinId is { } id) byJellyfinId.TryAdd(id, t);
        }
        Albums = tracks.GroupBy(t => t.AlbumKey)
            .Select(g => { var l = g.OrderBy(t => t.DiscNo).ThenBy(t => t.TrackNo).ThenBy(t => Matching.SortKey(t.Title)).ToList(); return new Group(l[0].Album.Length > 0 ? l[0].Album : "Unknown Album", l, l.Select(t => t.ArtKey).FirstOrDefault(k => k is not null)); })
            .OrderBy(g => Matching.SortKey(g.Name)).ToList();
        Artists = tracks.GroupBy(t => (t.AlbumArtist.Length > 0 ? t.AlbumArtist : t.Artist) is { Length: > 0 } a ? a : "Unknown Artist")
            .Select(g => new Group(g.Key, g.OrderBy(t => t.Album).ThenBy(t => t.DiscNo).ThenBy(t => t.TrackNo).ToList(), g.Select(t => t.ArtKey).FirstOrDefault(k => k is not null)))
            .OrderBy(g => Matching.SortKey(g.Name)).ToList();
    }

    public Track? ByPath(string path) => byPath.GetValueOrDefault(path);
    public Track? ByJellyfinId(string id) => byJellyfinId.GetValueOrDefault(id);

    /// <summary>A playlist or favourite entry: the exact file, else this library's copy of the same song.</summary>
    public Track? Resolve(string path, string title, string artist) =>
        ByPath(path) ?? (Matching.JellyfinIdIn(path) is { } id ? ByJellyfinId(id) : null) ?? byMatch.GetValueOrDefault(Matching.MatchKey(title, artist));

    public IReadOnlyList<Group> RecentAlbums(int n = 24) => Albums.OrderByDescending(g => g.Tracks.Max(t => t.AddedMs)).Take(n).ToList();

    /// <summary>Songs matching every word of the query (punctuation-blind), best first.</summary>
    public (IReadOnlyList<Track> Songs, IReadOnlyList<Group> Albums, IReadOnlyList<Group> Artists) Search(string q)
    {
        var w = Matching.SearchWords(q);
        if (w.Count == 0) return ([], [], []);
        var qn = string.Concat(w);
        int Score(string title) { var n = string.Concat(Matching.SearchWords(title)); return n == qn ? 0 : n.StartsWith(qn) ? 1 : n.Contains(qn) ? 2 : 3; }
        return (
            Songs.Where(t => Matching.SearchHit(w, t.Title, t.Artist, t.Album)).OrderBy(t => Score(t.Title)).Take(100).ToList(),
            Albums.Where(g => Matching.SearchHit(w, g.Name, g.Tracks[0].AlbumArtist)).Take(24).ToList(),
            Artists.Where(g => Matching.SearchHit(w, g.Name)).Take(12).ToList());
    }

    /// <summary>Jellyfin songs, then NAS songs Jellyfin doesn't have, de-duplicated.</summary>
    public static Library Merge(IReadOnlyList<Track> jellyfin, IReadOnlyList<Track> nas)
    {
        var acc = new List<Track>();
        var seen = new HashSet<string>();
        var anySong = new HashSet<string>();
        foreach (var t in jellyfin)
            if (seen.Add(Matching.MergeKey(t))) { acc.Add(t); anySong.Add(Matching.MatchKey(t)); }
        // NAS rows are checked against every Jellyfin file, not just the rows that survived Jellyfin's own de-dup
        var jfFiles = jellyfin.Select(Matching.FileKey).OfType<string>().ToHashSet();
        foreach (var t in nas)
        {
            if (Matching.FileKey(t) is { } fk && jfFiles.Contains(fk)) continue;
            var mk = Matching.MergeKey(t);
            if (seen.Contains(mk)) continue;
            if (Matching.AlbumNorm(t.Album).Length == 0 && anySong.Contains(Matching.MatchKey(t))) continue; // untagged copy of a song already here
            seen.Add(mk); anySong.Add(Matching.MatchKey(t)); acc.Add(t);
        }
        return new Library(acc);
    }
}
