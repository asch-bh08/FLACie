using System.Text;
using IpodSync.Core.ItunesDb;

namespace IpodSync.Core.LocalLibrary;

/// <summary>
/// Reads playlist files — iTunes "Export Playlist" text (UTF-16, tab-separated,
/// Name/Artist/Album/Time/Location columns) and M3U/M3U8 — and matches each entry to a
/// track already on the device with the same rules folder sync uses (normalised title +
/// first artist + duration, then the artist-in-title fallback). Read-only.
/// </summary>
public static class PlaylistImport
{
    public sealed record Entry(int Line, string? Title, string? Artist, string? Album, double Seconds, string? Location);

    public sealed class Result
    {
        public List<(Entry Entry, Track Track)> Matched { get; } = [];
        public List<Entry> Unmatched { get; } = [];
    }

    public static List<Entry> Read(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        string text = Decode(bytes);
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".m3u" or ".m3u8" ? ReadM3u(text) : ReadItunesText(text);
    }

    private static string Decode(byte[] b)
    {
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return Encoding.Unicode.GetString(b, 2, b.Length - 2);
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return Encoding.UTF8.GetString(b, 3, b.Length - 3);
        return Encoding.UTF8.GetString(b);
    }

    private static List<Entry> ReadItunesText(string text)
    {
        var lines = text.Split(["\r\n", "\n"], StringSplitOptions.None);
        if (lines.Length == 0) return [];
        var header = lines[0].Split('\t');
        int Col(string name) => Array.FindIndex(header, h => h.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
        int name = Col("Name"), artist = Col("Artist"), album = Col("Album"), time = Col("Time"), loc = Col("Location");
        if (name < 0) throw new InvalidDataException("not an iTunes playlist export (no Name column)");
        var list = new List<Entry>();
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var f = lines[i].Split('\t');
            string? Get(int c) => c >= 0 && c < f.Length && !string.IsNullOrWhiteSpace(f[c]) ? f[c].Trim() : null;
            double.TryParse(Get(time), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double secs);
            list.Add(new Entry(i + 1, Get(name), Get(artist), Get(album), secs, Get(loc)));
        }
        return list;
    }

    private static List<Entry> ReadM3u(string text)
    {
        var list = new List<Entry>();
        string? title = null, artist = null;
        double secs = 0;
        var lines = text.Split(["\r\n", "\n"], StringSplitOptions.None);
        for (int i = 0; i < lines.Length; i++)
        {
            string l = lines[i].Trim();
            if (l.Length == 0) continue;
            if (l.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                string rest = l[8..];
                int comma = rest.IndexOf(',');
                double.TryParse(comma > 0 ? rest[..comma] : rest, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out secs);
                string display = comma > 0 ? rest[(comma + 1)..] : "";
                int dash = display.IndexOf(" - ", StringComparison.Ordinal);
                (artist, title) = dash > 0 ? (display[..dash], display[(dash + 3)..]) : (null, display);
                continue;
            }
            if (l.StartsWith('#')) continue;
            title ??= Path.GetFileNameWithoutExtension(l);
            list.Add(new Entry(i + 1, title, artist, null, secs, l));
            title = null; artist = null; secs = 0;
        }
        return list;
    }

    /// <param name="prefer">Track ids to prefer when the library holds several copies of a
    /// song (e.g. the tracks already in the target playlist: a "(LAC)" playlist holds the
    /// lossless copies, while an AAC copy of the same song may be closer in duration).</param>
    public static Result Match(IReadOnlyList<Entry> entries, ItunesDatabase device, IReadOnlySet<uint>? prefer = null)
    {
        var result = new Result();
        var byKey = device.Tracks.GroupBy(t => FolderSync.Key(t.Title, t.Artist)).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var e in entries)
        {
            Track? match = null;
            if (byKey.TryGetValue(FolderSync.Key(e.Title, e.Artist), out var candidates))
                match = candidates.OrderBy(t => prefer?.Contains(t.Id) == true ? 0 : 1).ThenBy(t => Math.Abs(t.LengthMs / 1000.0 - e.Seconds))
                    .FirstOrDefault(t => e.Seconds <= 0 || Math.Abs(t.LengthMs / 1000.0 - e.Seconds) <= 2.5);
            if (match is null && e.Seconds > 0)
            {
                var asFile = new FolderSync.SourceFile(e.Location ?? e.Title ?? "", e.Location ?? "", 0, 0, e.Title, e.Artist, e.Album, e.Seconds);
                match = device.Tracks.Where(t => Math.Abs(t.LengthMs / 1000.0 - e.Seconds) <= 1.5 && FolderSync.LooseMatch(asFile, t))
                    .OrderBy(t => prefer?.Contains(t.Id) == true ? 0 : 1).FirstOrDefault();
            }
            if (match is not null) result.Matched.Add((e, match));
            else result.Unmatched.Add(e);
        }
        return result;
    }
}
