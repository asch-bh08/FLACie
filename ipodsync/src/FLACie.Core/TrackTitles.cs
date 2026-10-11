using System.Text.RegularExpressions;

namespace FLACie.Core;

/// <summary>
/// Cleans the title of a song whose file has no real tags. Jellyfin names such an item after its file ("06 - Jason Mraz -
/// Everything Is Sound"), with no artist. This reads the artist and title out of that name, but only when the title really is
/// the file name (a tagged title is never touched, so "99 Problems", "1979" or "7 Rings" stay as they are).
/// The same rules, with the same test table, live in the Android app (library/TrackTitles.kt): keep the two in step.
/// </summary>
public static partial class TrackTitles
{
    public readonly record struct Result(string Title, string Artist, bool Cleaned);

    const string Sep = "[-–—]";
    [GeneratedRegex(@"\s+[-–—]\s+")] private static partial Regex DashSplit();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
    [GeneratedRegex(@"^\(?\s*(\d{1,2})[-.](\d{2,3})\s*\)?(?:\s*[-.]\s+|\s+|\.\s*)(?=\p{L})")] private static partial Regex DiscTrack();
    [GeneratedRegex(@"^(\d{1,3})\s*(?:[-–—]|\.|\))\s+(?=\S)")] private static partial Regex Spaced();
    [GeneratedRegex(@"^(\d{2})[-_.](?=\p{L})")] private static partial Regex Tight();
    [GeneratedRegex(@"^(\d{1,3})\s+(?=\p{L})")] private static partial Regex Bare();
    [GeneratedRegex(@"^\d{1,3}$")] private static partial Regex OnlyNumber();
    [GeneratedRegex(@"\s*[\[(]\s*hi-?res\s*[\])]\s*$", RegexOptions.IgnoreCase)] private static partial Regex HiRes();
    [GeneratedRegex(@"\s*[\[(]\s*(?:https?://)?(?:www\.)?[\p{L}\p{N}\-]+(?:\.[\p{L}\p{N}\-]+)*\.(?:com|net|org|ru|info|me|fm|to|cc|biz|co|uk|io|tv|ws|su|ua|pl|de|xyz|club|site|top)\s*[\])]\s*$", RegexOptions.IgnoreCase)] private static partial Regex SpamBracket();
    [GeneratedRegex(@"\s*(?:[-–—]\s*)?(?:https?://)?www\.[\p{L}\p{N}\-.]+\.[a-z]{2,6}\s*$", RegexOptions.IgnoreCase)] private static partial Regex SpamPlain();
    [GeneratedRegex(@"\s*\(\d{4}\)\s*$")] private static partial Regex YearTail();
    [GeneratedRegex(@"^(various( artists)?|va|unknown( artist)?|<unknown>)$", RegexOptions.IgnoreCase)] private static partial Regex Various();

    /// <summary>File name without folders and extension; "" for a path with no file part.</summary>
    public static string BaseName(string path)
    {
        var p = path.Split('?')[0].Replace('\\', '/').TrimEnd('/');
        var f = p[(p.LastIndexOf('/') + 1)..];
        var dot = f.LastIndexOf('.');
        return dot > 0 && f.Length - dot - 1 is >= 1 and <= 5 && f[(dot + 1)..].All(char.IsLetterOrDigit) ? f[..dot] : f;
    }

    static List<string> Folders(string path)
    {
        var segs = path.Split('?')[0].Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (segs.Count > 0) segs.RemoveAt(segs.Count - 1);
        return segs.TakeLast(2).ToList();
    }

    static string Fold(string s) => Spaces().Replace(s.Replace('_', ' '), " ").Trim().ToLowerInvariant();
    static bool Usable(string artist) => !string.IsNullOrWhiteSpace(artist) && !Various().IsMatch(artist.Trim());

    /// <summary>Whether the item's title is just its file name: the one sign that Jellyfin found no title tag.</summary>
    public static bool IsFileName(string name, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(name)) return false;
        var b = BaseName(path);
        return b.Length > 0 && Fold(name) == Fold(b);
    }

    /// <summary>Whether a name starts like a track number ("06 - ", "06. ", "06 ", "1-06 "), for deciding if a folder's files are numbered.</summary>
    public static bool HasNumberPrefix(string name)
    {
        var t = name.Replace('_', ' ').Trim();
        return DiscTrack().IsMatch(t) || Spaced().IsMatch(t) || Tight().IsMatch(t) || Bare().IsMatch(t);
    }

    static string StripNoise(string s)
    {
        var t = s;
        for (var i = 0; i < 3; i++) t = SpamPlain().Replace(SpamBracket().Replace(HiRes().Replace(t, ""), ""), "").Trim();
        return t;
    }

    static bool LooksLikeName(string s) => s.Trim().Length is > 0 and <= 60 && !OnlyNumber().IsMatch(s) && s.Any(char.IsLetter);

    /// <param name="name">the item's title as Jellyfin gives it</param>
    /// <param name="path">the file's path on the server ("" when unknown: then nothing is changed)</param>
    /// <param name="artist">the item's artist ("" or "Unknown" when there is none)</param>
    /// <param name="album">the item's album, used to drop an "Album - " part</param>
    /// <param name="siblingsNumbered">other files in the same folder also start with track numbers (makes "12 Title" safe to read)</param>
    /// <param name="knownArtists">primary artist names (see <see cref="Matching.PrimaryArtist"/>) known from properly tagged songs, to tell which side of "X - Y" is the artist</param>
    public static Result Clean(string name, string? path, string artist, string album = "", bool siblingsNumbered = false, IReadOnlySet<string>? knownArtists = null)
    {
        var same = new Result(name, artist, false);
        if (!IsFileName(name, path)) return same;
        var t = StripNoise(Spaces().Replace(name.Replace('_', ' '), " ").Trim());
        if (t.Length == 0) return same;

        var dirs = Folders(path!).Select(Fold).ToList();
        var artistMissing = !Usable(artist) || dirs.Contains(Fold(artist));
        var albumMissing = string.IsNullOrWhiteSpace(album) || dirs.Contains(Fold(album));
        var known = knownArtists ?? new HashSet<string>();
        bool IsKnown(string s) => known.Contains(Matching.PrimaryArtist(s));

        // the track number
        var numbered = false;
        if (DiscTrack().Match(t) is { Success: true } d) { t = t[(d.Index + d.Length)..]; numbered = true; }
        if (!numbered)
        {
            var m = Spaced().Match(t);
            if (!m.Success) m = Tight().Match(t);
            if (m.Success && t.Length > m.Index + m.Length) { t = t[(m.Index + m.Length)..]; numbered = true; }
        }
        if (!numbered && Bare().Match(t) is { Success: true } b)
        {
            var padded = b.Groups[1].Value.StartsWith('0');
            // "50 Cent - In Da Club": when the whole front part is a known artist, the number is part of the name
            if (!IsKnown(DashSplit().Split(t)[0]) && (siblingsNumbered || (padded && (artistMissing || albumMissing)))) { t = t[(b.Index + b.Length)..]; numbered = true; }
        }
        t = t.Trim().TrimStart('-', '–', '—', '.', ' ').TrimEnd('-', '–', '—', ' ');
        if (t.Length == 0) return same;

        var title = t;
        var credit = artist;
        var parts = DashSplit().Split(t).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        var pa = Usable(artist) ? Matching.PrimaryArtist(artist) : "";

        if (parts.Count >= 3)
        {
            var numAt = Enumerable.Range(1, parts.Count - 2).Cast<int?>().FirstOrDefault(i => OnlyNumber().IsMatch(parts[i!.Value]));
            if (numAt is { } n)
            {
                // "Artist - Album - 06 - Title"
                title = string.Join(" - ", parts.Skip(n + 1));
                if (artistMissing || !Usable(artist)) credit = parts[0];
            }
            else if (album.Trim().Length > 0 && string.Equals(YearTail().Replace(parts[1], ""), YearTail().Replace(album, "").Trim(), StringComparison.OrdinalIgnoreCase))
            {
                title = string.Join(" - ", parts.Skip(2));
                if (artistMissing) credit = parts[0];
            }
            else if ((pa.Length > 0 && Matching.PrimaryArtist(parts[0]) == pa) || IsKnown(parts[0]) || (!Usable(artist) && LooksLikeName(parts[0])))
            {
                title = string.Join(" - ", parts.Skip(1));
                if (!Usable(artist)) credit = parts[0];
            }
        }
        else if (parts.Count == 2)
        {
            var a = parts[0]; var bb = parts[1];
            var aKnown = IsKnown(a); var bKnown = IsKnown(bb);
            if (pa.Length > 0 && Matching.PrimaryArtist(a) == pa) title = bb;
            else if (pa.Length > 0 && Matching.PrimaryArtist(bb) == pa) title = a;
            else if (pa.Length > 0 && !(aKnown || bKnown)) title = t;   // credited elsewhere and nothing says which side is which: leave it
            else if (bKnown && !aKnown) { title = a; if (!Usable(artist)) credit = bb; }
            else if (LooksLikeName(a)) { title = bb; if (!Usable(artist)) credit = a; }
            else title = t;
            // a short bare number as the whole title ("Artist - 22") is more likely a misread than a song, unless the artist is a known one
            if (OnlyNumber().IsMatch(title) && !(aKnown || (pa.Length > 0 && Matching.PrimaryArtist(a) == pa))) { title = t; credit = artist; }
        }
        title = title.Trim();
        if (title.Length == 0) return same;
        return new Result(title, credit, title != name || credit != artist);
    }

    /// <summary>Primary artist names of Jellyfin songs that do have real tags, in the form <see cref="Clean"/> compares with.</summary>
    public static HashSet<string> KnownArtistsOf(IEnumerable<Track> tracks)
    {
        var o = new HashSet<string>();
        foreach (var t in tracks)
        {
            if (t.Source != TrackSource.Jellyfin || IsFileName(t.Title, t.FilePath)) continue;
            foreach (var a in new[] { t.Artist, t.AlbumArtist })
                if (Usable(a) && Matching.PrimaryArtist(a) is { Length: > 0 } p) o.Add(p);
        }
        return o;
    }

    static string Dir(Track t) { var p = (t.FilePath ?? "").Replace('\\', '/'); var i = p.LastIndexOf('/'); return i < 0 ? "" : p[..i]; }

    /// <summary>
    /// <see cref="Clean"/> for a whole Jellyfin library: knows which artists exist and which folders hold numbered files. Other sources pass through.
    /// A cleaned song keeps its file name title in <see cref="Track.OrigTitle"/>, so a playlist entry saved under it still resolves.
    /// </summary>
    public static List<Track> CleanAll(IReadOnlyList<Track> tracks, IReadOnlySet<string>? extraKnown = null)
    {
        var known = KnownArtistsOf(tracks);
        if (extraKnown is not null) known.UnionWith(extraKnown);
        var numbered = new Dictionary<string, HashSet<string>>();
        foreach (var t in tracks)
            if (t.Source == TrackSource.Jellyfin && IsFileName(t.Title, t.FilePath) && HasNumberPrefix(t.Title))
            {
                var no = new string(t.Title.Trim().TakeWhile(c => char.IsDigit(c) || c is '-' or '.').ToArray());
                (numbered.TryGetValue(Dir(t), out var set) ? set : numbered[Dir(t)] = []).Add(no.Length > 0 ? no : t.Title);
            }
        var res = new List<Track>(tracks.Count);
        foreach (var t in tracks)
        {
            if (t.Source != TrackSource.Jellyfin || !string.IsNullOrEmpty(t.OrigTitle)) { res.Add(t); continue; }
            var sib = numbered.TryGetValue(Dir(t), out var s) && s.Count >= 2;
            var r = Clean(t.Title, t.FilePath, t.Artist, t.Album, sib, known);
            if (!r.Cleaned) { res.Add(t); continue; }
            var aa = t.AlbumArtist.Length == 0 || t.AlbumArtist == t.Artist || !Usable(t.AlbumArtist) ? r.Artist : t.AlbumArtist;
            // a song that had no usable credit has no catalog art key yet either: look it up under the name it now has
            var art = t.ArtKey is null ? Art.LookupKey(r.Artist, t.Album, r.Title) : t.ArtKey;
            res.Add(t with { Title = r.Title, Artist = r.Artist, AlbumArtist = aa, OrigTitle = t.Title, ArtKey = art });
        }
        return res;
    }
}
