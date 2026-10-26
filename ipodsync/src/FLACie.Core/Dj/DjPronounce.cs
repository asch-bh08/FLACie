using System.Text.RegularExpressions;

namespace FLACie.Core;

/// <summary>
/// How odd names are said (Appendix A.5): a built-in starter list plus the server's own <c>dj-pronounce.tsv</c>, one name per line: written, kind (artist, title or word),
/// say (a respelling) and an optional ipa, separated by tabs. Only the spoken line uses them; captions keep the real spelling. A file entry replaces a built-in one.
/// </summary>
public sealed class DjPronounce
{
    public enum Kind { Artist, Title, Word }
    public sealed record Entry(string Written, Kind Kind, string Say, string? Ipa = null);

    /// <summary>Starting points, to be tuned by ear.</summary>
    public static readonly IReadOnlyList<Entry> StarterList =
    [
        new("Sigur Rós", Kind.Artist, "Siggur Rose"),
        new("Beyoncé", Kind.Artist, "Bee-yon-say", "biˈjɒnseɪ"),
        new("Mötley Crüe", Kind.Artist, "Motley Crew"),
        new("CHVRCHES", Kind.Artist, "Churches"),
        new("MØ", Kind.Artist, "Mur"),
        new("deadmau5", Kind.Artist, "dead mouse"),
        new("Sia", Kind.Artist, "See-a", "ˈsiːə"),
        new("Dvořák", Kind.Word, "Dvor-zhak"),
        new("SOS", Kind.Title, "S O S"),
        new("FLACie", Kind.Word, "Flacky"),   // as the Alexa skill says it
    ];

    public static DjPronounce Default { get; } = new([]);

    readonly Dictionary<string, Entry> artists = new(StringComparer.OrdinalIgnoreCase), titles = new(StringComparer.OrdinalIgnoreCase);
    readonly List<(Regex Find, string Say)> phrases = [];
    public IReadOnlyList<Entry> Entries { get; }

    public DjPronounce(IEnumerable<Entry> extra)
    {
        var all = new Dictionary<(string, Kind), Entry>();
        foreach (var e in StarterList.Concat(extra)) all[(e.Written.ToLowerInvariant(), e.Kind)] = e;
        Entries = all.Values.ToList();
        foreach (var e in Entries)
        {
            if (e.Kind == Kind.Artist) artists[e.Written] = e;
            else if (e.Kind == Kind.Title) titles[e.Written] = e;
        }
        // inside a longer name or line, any entry is matched as a whole word or phrase; longest first, so "Sigur Rós" goes before a shorter entry inside it
        foreach (var e in Entries.OrderByDescending(e => e.Written.Length))
            phrases.Add((new Regex(@"(?<![\p{L}\p{N}])" + Regex.Escape(e.Written) + @"(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), e.Say));
    }

    /// <summary>The respelling for an artist name exactly as tagged, or null.</summary>
    public string? Artist(string written) => artists.TryGetValue(DjText.Tidy(written), out var e) ? e.Say : null;

    /// <summary>The respelling for a title exactly as tagged, or null.</summary>
    public string? Title(string written) => titles.TryGetValue(DjText.Tidy(written), out var e) ? e.Say : null;

    /// <summary>Every entry respelt wherever it appears as a whole word or phrase.</summary>
    public string Phrases(string s)
    {
        if (s.Length == 0) return s;
        foreach (var (find, say) in phrases) s = find.Replace(s, say.Replace("$", "$$"));
        return s;
    }

    /// <summary>Reads a tab-separated file (two or more spaces also separate columns; "#" starts a comment). Lines it cannot read are skipped.</summary>
    public static List<Entry> Parse(string text)
    {
        var res = new List<Entry>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.TrimStart().StartsWith('#') || line.Trim().Length == 0) continue;
            var cols = line.Contains('\t') ? line.Split('\t') : Regex.Split(line.Trim(), @"\s{2,}");
            cols = cols.Select(c => c.Trim()).Where(c => c.Length > 0).ToArray();
            if (cols.Length < 3) continue;
            Kind? kind = cols[1].ToLowerInvariant() switch { "artist" => Kind.Artist, "title" => Kind.Title, "word" => Kind.Word, _ => null };
            if (kind is null || cols[0].Length > 200 || cols[2].Length > 200) continue;
            res.Add(new(cols[0], kind.Value, cols[2], cols.Length > 3 ? cols[3] : null));
        }
        return res;
    }

    /// <summary>The starter list plus the file at [path] when there is one.</summary>
    public static DjPronounce Load(string? path)
    {
        try { if (path is not null && File.Exists(path)) return new DjPronounce(Parse(File.ReadAllText(path))); } catch (Exception) { }
        return Default;
    }
}
