using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FLACie.Core;

public sealed record ImportedTrack(string Artist, string Title, string Album = "");
public sealed record ImportedPlaylist(string Name, List<ImportedTrack> Tracks);

/// <summary>
/// Reads a playlist or library file the user drops in: an iTunes/Music <c>Library.xml</c>, an M3U/M3U8, a Spotify-export CSV (Exportify and
/// similar), or a plain text list. In text, a line "## Name (12 tracks)" starts a playlist and "Artist - Title" lines are its songs (several artists
/// are separated by ";"); without headings the whole file is one playlist named after it.
/// </summary>
public static class PlaylistImport
{
    public static List<ImportedPlaylist> Parse(string fileName, byte[] bytes)
    {
        var text = Decode(bytes);
        var name = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var head = text.TrimStart().Length > 200 ? text.TrimStart()[..200] : text.TrimStart();
        List<ImportedPlaylist> res;
        if (ext == ".xml" || head.StartsWith("<?xml") || head.StartsWith("<plist")) res = Itunes(text);
        else if (ext is ".m3u" or ".m3u8" || head.StartsWith("#EXTM3U")) res = M3u(text, name);
        else if (ext == ".csv" || LooksLikeCsv(text)) res = Csv(text, name);
        else res = Plain(text, name);
        // drop empties and exact duplicates inside a playlist
        return res.Select(p => p with { Tracks = p.Tracks.Where(t => t.Title.Length > 0).DistinctBy(t => Matching.MatchKey(t.Title, t.Artist)).ToList() })
            .Where(p => p.Tracks.Count > 0).ToList();
    }

    static string Decode(byte[] b)
    {
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return Encoding.UTF8.GetString(b, 3, b.Length - 3);
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return Encoding.Unicode.GetString(b, 2, b.Length - 2);
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
        try { return new UTF8Encoding(false, true).GetString(b); } catch (Exception) { return Encoding.Latin1.GetString(b); }
    }

    // ---- plain text ----

    static readonly Regex Heading = new(@"^\s*#{1,3}\s*(?<n>.+?)\s*(\(\s*\d+\s*(tracks?|songs?)?\s*\))?\s*$", RegexOptions.IgnoreCase);

    static List<ImportedPlaylist> Plain(string text, string fallback)
    {
        var lists = new List<ImportedPlaylist>(); ImportedPlaylist? cur = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim('\r', ' ', '\t');
            if (line.Length == 0) continue;
            if (line.StartsWith('#'))
            {
                var m = Heading.Match(line);
                if (m.Success && !line.StartsWith("#EXT")) { cur = new(m.Groups["n"].Value.Trim(), []); lists.Add(cur); }
                continue;
            }
            if (cur is null) { cur = new(fallback, []); lists.Add(cur); }
            if (SplitLine(line) is { } t) cur.Tracks.Add(t);
        }
        return lists;
    }

    /// <summary>"Artist - Title" (the artist may list several people with ";"), or "Title by Artist"; a bare line is a title.</summary>
    public static ImportedTrack? SplitLine(string line)
    {
        line = Regex.Replace(line, @"^\s*(\d{1,3}[.)]\s+|[-*•]\s+)", "").Trim();
        if (line.Length == 0) return null;
        var i = line.IndexOf(" - ", StringComparison.Ordinal);
        if (i > 0) return new(line[..i].Trim().Replace("; ", ", ").Replace(";", ","), line[(i + 3)..].Trim());
        var by = Regex.Match(line, @"^(?<t>.+?)\s+by\s+(?<a>.+)$", RegexOptions.IgnoreCase);
        if (by.Success) return new(by.Groups["a"].Value.Trim(), by.Groups["t"].Value.Trim());
        return new("", line);
    }

    // ---- M3U ----

    static List<ImportedPlaylist> M3u(string text, string name)
    {
        var tracks = new List<ImportedTrack>(); string? info = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim('\r', ' ');
            if (line.Length == 0) continue;
            if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase)) { info = line[(line.IndexOf(',') + 1)..].Trim(); continue; }
            if (line.StartsWith('#')) continue;
            var t = info is { Length: > 0 } ? SplitLine(info) : FromFileName(line);
            if (t is not null) tracks.Add(t);
            info = null;
        }
        return [new(name, tracks)];
    }

    static ImportedTrack? FromFileName(string path)
    {
        var n = Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));
        n = Regex.Replace(n, @"^\d{1,3}\s*[-._ ]\s*", "");
        return SplitLine(n);
    }

    // ---- CSV (Spotify exports) ----

    static bool LooksLikeCsv(string text)
    {
        var first = text.Split('\n').FirstOrDefault()?.ToLowerInvariant() ?? "";
        return first.Contains(',') && (first.Contains("track name") || first.Contains("artist name") || first.Contains("\"title\"") || first.StartsWith("title,") || first.Contains("song"));
    }

    static List<ImportedPlaylist> Csv(string text, string name)
    {
        var rows = CsvRows(text);
        if (rows.Count == 0) return [];
        var head = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Col(params string[] names) => head.FindIndex(h => names.Any(n => h == n || h.StartsWith(n)));
        var ti = Col("track name", "title", "song", "name"); var ai = Col("artist name", "artist"); var li = Col("album name", "album");
        if (ti < 0) return [];
        var pl = Col("playlist");
        var groups = new Dictionary<string, List<ImportedTrack>>();
        foreach (var r in rows.Skip(1))
        {
            string C(int i) => i >= 0 && i < r.Count ? r[i].Trim() : "";
            var key = pl >= 0 && C(pl).Length > 0 ? C(pl) : name;
            if (!groups.TryGetValue(key, out var l)) groups[key] = l = [];
            l.Add(new(C(ai).Replace(";", ", "), C(ti), C(li)));
        }
        return groups.Select(g => new ImportedPlaylist(g.Key, g.Value)).ToList();
    }

    static List<List<string>> CsvRows(string text)
    {
        var rows = new List<List<string>>(); var row = new List<string>(); var sb = new StringBuilder(); var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted) { if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; } else quoted = false; } else sb.Append(c); }
            else if (c == '"') quoted = true;
            else if (c == ',') { row.Add(sb.ToString()); sb.Clear(); }
            else if (c == '\n') { row.Add(sb.ToString().TrimEnd('\r')); sb.Clear(); if (row.Any(x => x.Length > 0)) rows.Add(row); row = []; }
            else sb.Append(c);
        }
        if (sb.Length > 0 || row.Count > 0) { row.Add(sb.ToString()); if (row.Any(x => x.Length > 0)) rows.Add(row); }
        return rows;
    }

    // ---- iTunes / Music Library.xml (an Apple property list) ----

    static List<ImportedPlaylist> Itunes(string xml)
    {
        var root = XDocument.Parse(xml, LoadOptions.None).Root?.Element("dict");
        if (root is null) return [];
        var top = Dict(root);
        var tracks = new Dictionary<string, ImportedTrack>();
        if (top.GetValueOrDefault("Tracks") is { } td)
            foreach (var (id, node) in Dict(td))
            {
                var t = Dict(node);
                if (t.TryGetValue("Podcast", out var pc) && pc.Name == "true") continue;
                if (t.TryGetValue("Movie", out var mv) && mv.Name == "true") continue;
                tracks[id] = new(Val(t, "Artist"), Val(t, "Name"), Val(t, "Album"));
            }
        var lists = new List<ImportedPlaylist>();
        var skip = new[] { "Library", "Music", "Movies", "TV Shows", "Podcasts", "Audiobooks", "Downloaded", "Purchased", "Voice Memos", "iTunes U", "Genius", "Books", "Tagged", "Home Videos" };
        foreach (var pl in top.GetValueOrDefault("Playlists")?.Elements("dict") ?? [])
        {
            var d = Dict(pl);
            var n = Val(d, "Name");
            if (n.Length == 0 || d.ContainsKey("Master") || d.ContainsKey("Distinguished Kind") || skip.Contains(n)) continue;
            var items = d.GetValueOrDefault("Playlist Items")?.Elements("dict")
                .Select(i => Val(Dict(i), "Track ID")).Where(tracks.ContainsKey).Select(i => tracks[i]).ToList() ?? [];
            if (items.Count > 0) lists.Add(new(n, items));
        }
        return lists;
    }

    static Dictionary<string, XElement> Dict(XElement dict)
    {
        var res = new Dictionary<string, XElement>(); string? key = null;
        foreach (var e in dict.Elements())
        {
            if (e.Name == "key") key = e.Value;
            else if (key is not null) { res[key] = e; key = null; }
        }
        return res;
    }

    static string Val(Dictionary<string, XElement> d, string k) => d.TryGetValue(k, out var e) ? e.Value.Trim() : "";
}
