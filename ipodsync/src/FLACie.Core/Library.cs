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
    readonly Dictionary<string, Track> byOrigTitle = new();

    public static readonly Library Empty = new([]);

    public Library(IReadOnlyList<Track> tracks)
    {
        Songs = tracks.OrderBy(t => Matching.SortKey(t.Title)).ToList();
        byPath = new(); byMatch = new(); byJellyfinId = new(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tracks)
        {
            byPath.TryAdd(t.Path, t);
            // one copy per song for lookups by name: the album copy over a best-of's (Matching.Prefer)
            var mk = Matching.MatchKey(t);
            if (!byMatch.TryGetValue(mk, out var had) || Matching.Prefer(t, had)) byMatch[mk] = t;
            if (t.JellyfinId is { } id) byJellyfinId.TryAdd(id, t);
            if (t.OrigTitle.Length > 0) byOrigTitle.TryAdd(Matching.NormTitle(t.OrigTitle), t);
        }
        // the same recording twice (an album copy and a best-of's of the same length): one in the song lists, the main one
        foreach (var t in tracks) if (byMatch.TryGetValue(Matching.MatchKey(t), out var main) && main.Path != t.Path && SameRecording(t, main)) dupes.Add(t.Path);
        if (dupes.Count > 0) Songs = Songs.Where(t => !dupes.Contains(t.Path)).ToList();
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
        ByPath(path) ?? (Matching.JellyfinIdIn(path) is { } id ? ByJellyfinId(id) : null) ?? byMatch.GetValueOrDefault(Matching.MatchKey(title, artist))
        ?? (byOrigTitle.GetValueOrDefault(Matching.NormTitle(title)) is { } o && (artist.Length == 0 || Matching.PrimaryArtist(artist) == Matching.PrimaryArtist(o.Artist)) ? o : null); // saved under the file name it showed before TrackTitles

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
            OneVersion(OneCopy(SearchRank.Rank(si, i => songDocs[i], q, 100).Select(i => Songs[i]))),
            SearchRank.Rank(ai, i => albumDocs[i], q, 24).Select(i => Albums[i]).ToList(),
            SearchRank.Rank(ri, i => artistDocs[i], q, 12).Select(i => Artists[i]).ToList());
    }
    SearchRank.Doc[]? songDocs, albumDocs, artistDocs;

    /// <summary>Every other copy and version of the song (same song whatever the mix or edit, same lead artist: <see cref="Matching.SongKey"/>)
    /// besides [t]: "Dreamer - Re-Original 7-inch Mix" and "Dreamer (Janice Robinson Vocal)" are versions of "Dreamer".</summary>
    public IReadOnlyList<Track> OtherVersions(Track t)
    {
        copies ??= Songs.GroupBy(x => Matching.SongKey(x.Title, x.Artist)).Where(g => g.Count() > 1).ToDictionary(g => g.Key, g => g.ToList());
        return copies.TryGetValue(Matching.SongKey(t.Title, t.Artist), out var all) ? all.Where(x => x.Path != t.Path).ToList() : [];
    }
    Dictionary<string, List<Track>>? copies;

    /// <summary>What another copy of a song is, so two rows of "Voodoo People" can be told apart (owner, 13 Nov): its real name from the catalog
    /// (<see cref="VersionNames"/>), else its length ("3:40 edit", "8:00 version"); null for the main copy (<see cref="Find"/>), a song with one copy,
    /// or a second copy of the same recording (<see cref="IsDuplicate"/>).</summary>
    public string? VersionNote(Track t)
    {
        // (only copies with the very same title need a name; a mix or a vocal version already says what it is)
        if (!OtherVersions(t).Any(o => Matching.MatchKey(o) == Matching.MatchKey(t)) || Find(t.Title, t.Artist) is not { } main || main.Path == t.Path || SameRecording(t, main)) return null;
        if (VersionNames.Lookup?.Invoke(t.Path) is { Length: > 0 } name) return name;
        if (t.DurationMs <= 0 || main.DurationMs <= 0) return null;
        var len = TimeSpan.FromMilliseconds(t.DurationMs);
        return $"{(int)len.TotalMinutes}:{len.Seconds:00} {(t.DurationMs < main.DurationMs ? "edit" : "version")}";
    }

    /// <summary>The title to show: the song's own, with what version it is when the library has other copies ("Voodoo People (05 Edit)").</summary>
    public string DisplayTitle(Track t) => VersionNote(t) is { } v ? $"{t.Title} ({v})" : t.Title;

    /// <summary>A second copy of the main copy's recording (the same length within 5 s): hidden from the song lists and search (its album keeps it)
    /// (owner, 13 Nov: "if it's another copy, why keep it").</summary>
    public bool IsDuplicate(Track t) => dupes.Contains(t.Path);
    readonly HashSet<string> dupes = [];
    static bool SameRecording(Track a, Track b) => a.DurationMs > 0 && b.DurationMs > 0 && Math.Abs(a.DurationMs - b.DurationMs) <= 5000;

    /// <summary>A song's versions found by one search show as one row, the plain version (no mix or edit in its title) where the best ranked of them
    /// was; the others open under it (owner, 13 Nov: "show one main one then hide the different versions, like SoundCloud or Spotify").</summary>
    static List<Track> OneVersion(List<Track> ranked)
    {
        var groups = ranked.GroupBy(t => Matching.SongKey(t.Title, t.Artist)).ToDictionary(g => g.Key, g => g.ToList());
        var shown = new HashSet<string>(); var res = new List<Track>();
        foreach (var t in ranked)
        {
            var k = Matching.SongKey(t.Title, t.Artist);
            var g = groups[k];
            if (g.Count == 1) { res.Add(t); continue; }
            if (!shown.Add(k)) continue;
            res.Add(g.FirstOrDefault(x => Matching.Recording(x.Title).Length == 0 && Matching.NormTitle(x.Title) == k.Split('|')[0]) ?? g.FirstOrDefault(x => Matching.Recording(x.Title).Length == 0) ?? t);
        }
        return res;
    }

    /// <summary>A song that is both on its own album and on a compilation shows once, as the album copy, where the better ranked of the two was.</summary>
    List<Track> OneCopy(IEnumerable<Track> ranked)
    {
        var list = ranked.ToList();
        var shown = new HashSet<string>(); var res = new List<Track>();
        foreach (var t in list)
        {
            var k = Matching.MatchKey(t);
            var copies = list.Where(x => Matching.MatchKey(x) == k).ToList();
            if (copies.Count > 1 && copies.Any(c => !Matching.IsCompilation(c)) && copies.Any(Matching.IsCompilation))
            {
                if (!shown.Add(k)) continue;
                res.Add(byMatch.TryGetValue(k, out var pick) && copies.Contains(pick) ? pick : copies.First(c => !Matching.IsCompilation(c)));
            }
            else res.Add(t);
        }
        return res;
    }

    /// <summary>This library's copy of a song by title and artist (the same song on another release counts), or null.</summary>
    public Track? Find(string title, string artist) => byMatch.GetValueOrDefault(Matching.MatchKey(title, artist));

    /// <summary>A song with this title whose credit is missing or the same artist spelled another way (a file with no artist tag), when the exact match finds nothing.</summary>
    Dictionary<string, List<Track>>? byNormTitle;
    public Track? FindLoose(string title, string artist)
    {
        var nt = Matching.NormTitle(title); var pa = Matching.PrimaryArtist(artist);
        // the songs grouped by their cleaned title once, not every title cleaned again for each lookup: a playlist with songs not matched by path was slow
        // to open (owner, 1 Nov; the phone has the same index)
        byNormTitle ??= Songs.GroupBy(t => Matching.NormTitle(t.Title)).ToDictionary(g => g.Key, g => g.ToList());
        bool SameArtist(Track t)
        {
            var a = Matching.PrimaryArtist(t.Artist);
            return a.Length == 0 || a is "unknown artist" or "unknown" || (pa.Length > 0 && (a.Contains(pa) || pa.Contains(a) || Matching.FolderArtist(t) == pa));
        }
        if ((byNormTitle.GetValueOrDefault(nt) ?? []).FirstOrDefault(SameArtist) is { } hit) return hit;
        // then the song's own name on both sides: Spotify lists "More And More - Single Version", "Freed From Desire - Radio Edit", "Scatman
        // (ski-ba-bop-ba-dop-bop)" and the files downloaded for them are plain "More And More" (owner, 5 Nov: shown as not in the library after
        // downloading). A remix or live recording keeps its word, so it never stands in for the original
        var bt = Matching.NormTitle(WebCatalog.BaseTitle(title));
        byBaseTitle ??= Songs.GroupBy(t => Matching.NormTitle(WebCatalog.BaseTitle(t.Title))).ToDictionary(g => g.Key, g => g.ToList());
        return bt.Length == 0 ? null : (byBaseTitle.GetValueOrDefault(bt) ?? []).FirstOrDefault(SameArtist);
    }
    Dictionary<string, List<Track>>? byBaseTitle;

    /// <summary>Jellyfin songs, then NAS songs Jellyfin doesn't have, de-duplicated.</summary>
    public static Library Merge(IReadOnlyList<Track> jellyfin, IReadOnlyList<Track> nas)
    {
        jellyfin = CoverVetting.Vet(jellyfin);   // an album picture only for songs that really belong to that album (AlbumId is shared by unrelated albums on a real Jellyfin)
        // a song downloaded from search keeps the cover it was found with, not the one inside the file (owner, 6 Nov)
        if (DownloadCovers.Current is { } dc) jellyfin = dc.Apply(jellyfin);
        // a song filed under a playlist's name shows its own album and cover (AlbumFixes; owner, 13 Nov)
        jellyfin = AlbumFixes.Apply(jellyfin); nas = AlbumFixes.Apply(nas);
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
