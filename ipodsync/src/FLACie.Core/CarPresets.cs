namespace FLACie.Core;

/// <summary>The car stereo's preset keys (iPod mode › Scene › Car): radio "stations" made from the library's biggest genres, each with a short capital
/// name that fits a key (ROCK, POP, DANCE). A press shuffles that genre's songs.</summary>
public static class CarPresets
{
    public sealed record Station(string Label, string Genre, IReadOnlyList<Track> Tracks);

    static readonly char[] Splits = [';', '/', ',', '|'];
    static readonly Dictionary<string, string> Short = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Alternative"] = "ALT", ["Electronic"] = "ELECTRO", ["Electronica"] = "ELECTRO", ["Classical"] = "CLASSIC", ["Soundtrack"] = "OST",
        ["Instrumental"] = "INSTR", ["Christmas"] = "XMAS", ["Children's Music"] = "KIDS",
    };

    /// <summary>The genre a song files under: the first of a compound tag ("Pop; Dance", "Pop/Rock"), trimmed; null when it has none.</summary>
    public static string? GenreOf(Track t)
    {
        var g = (t.Genre ?? "").Split(Splits, 2)[0].Trim();
        return g.Length == 0 || g.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? null : g;
    }

    /// <summary>The biggest genres (most songs first, ties by name), at most <paramref name="max"/>; spellings that differ only in case are one genre.</summary>
    public static IReadOnlyList<Station> Stations(IEnumerable<Track> songs, int max = 6) =>
        songs.Select(t => (Genre: GenreOf(t), Track: t)).Where(x => x.Genre is not null)
            .GroupBy(x => x.Genre!, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Name: g.GroupBy(x => x.Genre!).OrderByDescending(n => n.Count()).ThenBy(n => n.Key, StringComparer.Ordinal).First().Key, Tracks: g.Select(x => x.Track).ToList()))
            .OrderByDescending(g => g.Tracks.Count).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(g => new Station(Label(g.Name), g.Name, g.Tracks))
            .ToList();

    /// <summary>A genre's name on a preset key: in capitals, at most 8 letters (a known short form, else its first word, else cut).</summary>
    public static string Label(string genre)
    {
        var g = genre.Trim();
        if (g.Length <= 8) return g.ToUpperInvariant();
        if (Short.TryGetValue(g, out var s)) return s;
        var first = g.Split([' ', '-'], 2, StringSplitOptions.RemoveEmptyEntries)[0];
        return (first.Length <= 8 ? first : first[..8]).ToUpperInvariant();
    }
}
