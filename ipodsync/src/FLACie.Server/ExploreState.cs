using FLACie.Core;

namespace FLACie.Server;

/// <summary>
/// The filters on Explore (Songs, Albums, Artists, Genres share them, so a choice carries from one tab to the next): a text filter,
/// the first letter, genre, decade, lossless or lossy, and how the list is sorted. Kept per browser tab.
/// </summary>
public sealed class ExploreState
{
    public string Query = "", Letter = "", Genre = "", Decade = "", Format = "", Depth = "", Rate = "", Codec = "", Kbps = "", Sort = "az";
    /// <summary>True when the last list found nothing exactly for the text and is showing close matches instead (typos).</summary>
    public bool Fuzzy { get; private set; }
    public int Version { get; private set; }
    public event Action? Changed;

    public bool Filtering => Query.Length > 0 || Letter.Length > 0 || Genre.Length > 0 || Decade.Length > 0 || Format.Length > 0 || Depth.Length > 0 || Rate.Length > 0 || Codec.Length > 0 || Kbps.Length > 0;
    public void Touch() { Version++; Changed?.Invoke(); }
    public void Clear() { Query = Letter = Genre = Decade = Format = Depth = Rate = Codec = Kbps = ""; Touch(); }

    /// <summary>Bit depths and sample-rate bands to filter by (from the real format of each file, see <see cref="FormatIndex"/>).</summary>
    public static readonly (string, string)[] DepthChoices = [("16", "16-bit"), ("24", "24-bit"), ("32", "32-bit")];
    public static readonly (string, string)[] RateChoices = [("lo", "Below 44.1 kHz"), ("44", "44.1 kHz"), ("48", "48 kHz"), ("88", "88.2 kHz"), ("96", "96 kHz"), ("176", "176.4 kHz"), ("192", "192 kHz and up")];
    static bool RateBand(int hz, string band) => band switch
    {
        "lo" => hz > 0 && hz < 44_000, "44" => hz is >= 44_000 and <= 44_200, "48" => hz is >= 47_900 and <= 48_100, "88" => hz is >= 88_000 and <= 88_300,
        "96" => hz is >= 95_900 and <= 96_100, "176" => hz is >= 176_000 and <= 176_500, "192" => hz >= 191_900, _ => true,
    };

    /// <summary>Audio codecs to filter by (the real codec when it has been read, else the file extension) and bit-rate bands (kbps, from the real format).</summary>
    public static readonly (string, string)[] CodecChoices = [("flac", "FLAC"), ("alac", "ALAC (Apple Lossless)"), ("wav", "WAV / PCM"), ("aiff", "AIFF"), ("ape", "APE"), ("wv", "WavPack"), ("dsd", "DSD"), ("mp3", "MP3"), ("aac", "AAC / M4A"), ("ogg", "Ogg Vorbis"), ("opus", "Opus"), ("wma", "WMA")];
    public static readonly (string, string)[] KbpsChoices = [("128", "128 kbps or lower"), ("192", "129 to 192 kbps"), ("256", "193 to 256 kbps"), ("320", "257 to 320 kbps"), ("hi", "Above 320 kbps")];
    static bool KbpsBand(int k, string band) => k > 0 && band switch { "128" => k <= 128, "192" => k is > 128 and <= 192, "256" => k is > 192 and <= 256, "320" => k is > 256 and <= 320, "hi" => k > 320, _ => true };
    /// <summary>The codec family of a song: "flac", "mp3", "aac" ... ("" when unknown).</summary>
    public static string CodecFamily(Track t)
    {
        var raw = FormatIndex.Current?.Get(t) is { Codec.Length: > 0 } f ? f.Codec : InfoService.QuickFormat(t);
        if (raw.Equals("hi-res", StringComparison.OrdinalIgnoreCase)) raw = System.IO.Path.GetExtension(t.Path ?? "").TrimStart('.');
        var c = raw.ToLowerInvariant();
        if (c.Contains("alac")) return "alac";
        if (c.Contains("flac")) return "flac";
        if (c.Contains("mp3") || c.Contains("mpeg") || c == "mp2") return "mp3";
        if (c.Contains("aac") || c.Contains("m4a") || c.Contains("mp4")) return "aac";
        if (c.Contains("opus")) return "opus";
        if (c.Contains("vorbis") || c.Contains("ogg") || c == "oga") return "ogg";
        if (c.Contains("wma") || c.Contains("asf")) return "wma";
        if (c.Contains("wavpack") || c == "wv") return "wv";
        if (c.Contains("pcm") || c.Contains("wav")) return "wav";
        if (c.Contains("aif")) return "aiff";
        if (c.Contains("ape") || c.Contains("monkey")) return "ape";
        if (c.Contains("dsd") || c.Contains("dsf") || c.Contains("dff")) return "dsd";
        return "";
    }

    /// <summary>"A".."Z", or "#" for anything that doesn't start with a letter.</summary>
    public static string LetterOf(string name)
    {
        var s = Matching.SortKey(name);
        var c = s.Length > 0 ? char.ToUpperInvariant(s[0]) : '#';
        return c is >= 'A' and <= 'Z' ? c.ToString() : "#";
    }

    // the real codec when it has been read, else the file extension
    static bool Lossless(Track t) => FormatIndex.Current?.Get(t) is { } f ? f.Lossless : MediaInfo.LosslessCodecs.Contains(InfoService.QuickFormat(t).ToLowerInvariant());
    static bool DepthOk(Track t, string d) => FormatIndex.Current?.Get(t) is { Depth: > 0 } f && f.Depth.ToString() == d;
    static bool KbpsOk(Track t, string b) => FormatIndex.Current?.Get(t) is { Kbps: > 0 } f && KbpsBand(f.Kbps, b);
    static bool RateOk(Track t, string b) => FormatIndex.Current?.Get(t) is { Rate: > 0 } f && RateBand(f.Rate, b);
    static string DecadeOf(int year) => year >= 1900 ? (year / 10 * 10) + "s" : "";

    /// <summary>Keeps the items whose text matches the search words: exactly (punctuation and accents ignored) or, only when nothing at all matches that way, within a few typos.</summary>
    IEnumerable<T> Text<T>(IEnumerable<T> src, Func<T, string[]> fields)
    {
        var words = Matching.SearchWords(Query);
        var all = src.ToList();
        Fuzzy = false;
        if (words.Count == 0) return all;
        var exact = all.Where(i => Matching.SearchHit(words, fields(i))).ToList();
        Fuzzy = exact.Count == 0 && all.Count > 0;
        return Fuzzy ? all.Where(i => Matching.FuzzyHit(words, fields(i))).ToList() : exact;
    }

    bool Match(Track t) =>
        (Genre.Length == 0 || t.Genre.Trim().Equals(Genre, StringComparison.OrdinalIgnoreCase))
        && (Decade.Length == 0 || DecadeOf(t.Year) == Decade)
        && (Format.Length == 0 || (Format == "hires" ? InfoService.QuickFormat(t) == "HI-RES" : (Format == "lossless") == Lossless(t)))
        && (Depth.Length == 0 || DepthOk(t, Depth))
        && (Rate.Length == 0 || RateOk(t, Rate))
        && (Codec.Length == 0 || CodecFamily(t) == Codec)
        && (Kbps.Length == 0 || KbpsOk(t, Kbps));

    public List<Track> Songs(Library lib)
    {
        var l = Text(lib.Songs.Where(t => (Letter.Length == 0 || LetterOf(t.Title) == Letter) && Match(t)), t => [t.Title, t.Artist, t.Album]);
        return (Sort switch
        {
            "za" => l.OrderByDescending(t => Matching.SortKey(t.Title)),
            "artist" => l.OrderBy(t => Matching.SortKey(t.Artist)).ThenBy(t => t.Album).ThenBy(t => t.DiscNo).ThenBy(t => t.TrackNo),
            "new" => l.OrderByDescending(t => t.AddedMs),
            "year" => l.OrderByDescending(t => t.Year),
            "oldest" => l.Where(t => t.Year > 0).OrderBy(t => t.Year),
            "long" => l.OrderByDescending(t => t.DurationMs),
            _ => l,
        }).ToList();
    }

    public List<Group> Albums(Library lib)
    {
        var l = Text(lib.Albums.Where(g => (Letter.Length == 0 || LetterOf(g.Name) == Letter) && g.Tracks.Any(Match)),
            g => [g.Name, g.Tracks[0].AlbumArtist.Length > 0 ? g.Tracks[0].AlbumArtist : g.Tracks[0].Artist]);
        return (Sort switch
        {
            "za" => l.OrderByDescending(g => Matching.SortKey(g.Name)),
            "artist" => l.OrderBy(g => Matching.SortKey(g.Tracks[0].AlbumArtist.Length > 0 ? g.Tracks[0].AlbumArtist : g.Tracks[0].Artist)).ThenBy(g => g.Tracks.Max(t => t.Year)),
            "new" => l.OrderByDescending(g => g.Tracks.Max(t => t.AddedMs)),
            "year" => l.OrderByDescending(g => g.Tracks.Max(t => t.Year)),
            "oldest" => l.Where(g => g.Tracks.Any(t => t.Year > 0)).OrderBy(g => g.Tracks.Where(t => t.Year > 0).Min(t => t.Year)),
            "long" => l.OrderByDescending(g => g.Tracks.Count),
            _ => l,
        }).ToList();
    }

    public List<Group> Artists(Library lib)
    {
        var l = Text(lib.Artists.Where(g => (Letter.Length == 0 || LetterOf(g.Name) == Letter) && g.Tracks.Any(Match)), g => [g.Name]);
        return (Sort switch
        {
            "za" => l.OrderByDescending(g => Matching.SortKey(g.Name)),
            "long" or "new" => l.OrderByDescending(g => g.Tracks.Count),
            _ => l,
        }).ToList();
    }

    public List<Group> Genres(Library lib)
    {
        var l = Text(lib.Songs.Where(t => t.Genre.Length > 0 && Match(t) && (Letter.Length == 0 || LetterOf(t.Genre) == Letter)), t => [t.Genre])
            .GroupBy(t => t.Genre.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new Group(g.Key, g.ToList(), g.Select(t => t.ArtKey).FirstOrDefault(k => k is not null)));
        return (Sort == "az" ? l.OrderBy(g => Matching.SortKey(g.Name)) : Sort == "za" ? l.OrderByDescending(g => Matching.SortKey(g.Name)) : l.OrderByDescending(g => g.Tracks.Count).ThenBy(g => g.Name)).ToList();
    }

    /// <summary>Genres and decades to offer in the pickers, from what the library actually holds.</summary>
    public static (List<string> Genres, List<string> Decades) Choices(Library lib) => (
        lib.Songs.Where(t => t.Genre.Length > 0).GroupBy(t => t.Genre.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() >= 3).OrderBy(g => g.Key).Select(g => g.Key).ToList(),
        lib.Songs.Select(t => DecadeOf(t.Year)).Where(d => d.Length > 0).Distinct().OrderByDescending(d => d).ToList());
}
