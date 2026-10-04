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
        if (SearchRank.Words(q).Length == 0) return ([], [], []);
        songDocs ??= Songs.Select(t => new SearchRank.Doc(t.Title, t.Artist, t.Album)).ToArray();
        albumDocs ??= Albums.Select(g => new SearchRank.Doc(g.Name, g.Tracks[0].AlbumArtist.Length > 0 ? g.Tracks[0].AlbumArtist : g.Tracks[0].Artist, "")).ToArray();
        artistDocs ??= Artists.Select(g => new SearchRank.Doc(g.Name, "", "")).ToArray();
        var si = Enumerable.Range(0, Songs.Count).ToList(); var ai = Enumerable.Range(0, Albums.Count).ToList(); var ri = Enumerable.Range(0, Artists.Count).ToList();
        return (
            SearchRank.Rank(si, i => songDocs[i], q, 100).Select(i => Songs[i]).ToList(),
            SearchRank.Rank(ai, i => albumDocs[i], q, 24).Select(i => Albums[i]).ToList(),
            SearchRank.Rank(ri, i => artistDocs[i], q, 12).Select(i => Artists[i]).ToList());
    }
    SearchRank.Doc[]? songDocs, albumDocs, artistDocs;

    /// <summary>This library's copy of a song by title and artist (the same song on another release counts), or null.</summary>
    public Track? Find(string title, string artist) => byMatch.GetValueOrDefault(Matching.MatchKey(title, artist));

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
