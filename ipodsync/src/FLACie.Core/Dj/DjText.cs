using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FLACie.Core;

/// <summary>Numbers as the DJ says them: "nineteen ninety-seven", "nineteen oh five", "two thousand and five", "twenty twelve", "about twelve minutes".</summary>
public static class DjNumbers
{
    static readonly string[] Ones = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"];
    static readonly string[] Tens = ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];

    /// <summary>A whole number in words, British style ("one hundred and five"). Beyond a million it stays in digits.</summary>
    public static string Words(int n)
    {
        if (n < 0) return "minus " + Words(-n);
        if (n < 20) return Ones[n];
        if (n < 100) return Tens[n / 10] + (n % 10 > 0 ? "-" + Ones[n % 10] : "");
        if (n < 1000) return Ones[n / 100] + " hundred" + (n % 100 > 0 ? " and " + Words(n % 100) : "");
        if (n < 1_000_000) return Words(n / 1000) + " thousand" + (n % 1000 == 0 ? "" : n % 1000 < 100 ? " and " + Words(n % 1000) : " " + Words(n % 1000));
        return n.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>A year as it is said: 1905 "nineteen oh five", 1997 "nineteen ninety-seven", 2000 "two thousand", 2005 "two thousand and five", 2012 "twenty twelve".</summary>
    public static string Year(int y)
    {
        if (y is < 1000 or > 9999) return Words(y);
        if (y is >= 2000 and < 2010) return y == 2000 ? "two thousand" : "two thousand and " + Ones[y - 2000];
        int hi = y / 100, lo = y % 100;
        if (lo == 0) return Words(hi) + " hundred";
        return Words(hi) + " " + (lo < 10 ? "oh " + Ones[lo] : Words(lo));
    }

    /// <summary>The decade of a year from 1950 to 1999 ("fifties" ... "nineties"); null for any other year.</summary>
    public static string? Decade(int y) => y is >= 1950 and <= 1999
        ? (y / 10 % 10) switch { 5 => "fifties", 6 => "sixties", 7 => "seventies", 8 => "eighties", _ => "nineties" }
        : null;
}

/// <summary>
/// Clean-up of song facts before the DJ uses them (Appendix A.3 of docs/research/ai-dj-free.md). The written form (for captions) keeps the real spelling minus version
/// tags; the spoken form is respelt for a voice: "&amp;" and "+" become "and", ALL CAPS words are tamed, symbols, quotes and emoji go.
/// Credits are never split on "&amp;" (Simon &amp; Garfunkel stays one act), so <c>Matching.ArtistSplit</c> is not used here.
/// </summary>
public static partial class DjText
{
    // version tags, as Alexa's spoken titles drop them (copied from FLACie.Server/Alexa/Phonetic.StripTags, so Core needs no server code)
    [GeneratedRegex(@"\s*[\(\[][^\)\]]*\b(?:live|remaster(?:ed)?|mono|stereo|deluxe|radio edit|single version|album version|feat|ft|featuring|version|edit|bonus track|explicit|clean)\b[^\)\]]*[\)\]]", RegexOptions.IgnoreCase)] private static partial Regex TagBracket();
    [GeneratedRegex(@"\s[-–—]\s.*\b(?:remaster(?:ed)?|live|mono|stereo|deluxe|edit|version|bonus|single|acoustic|demo)\b.*$", RegexOptions.IgnoreCase)] private static partial Regex TagDash();
    // feat. credits (the same patterns as Matching.FeatParen / FeatTail), with the guest captured
    [GeneratedRegex(@"\s*[(\[](?:feat\.?|ft\.?|featuring|with)\s+([^)\]]*)[)\]]", RegexOptions.IgnoreCase)] private static partial Regex FeatParen();
    [GeneratedRegex(@"\s+(?:feat\.?|ft\.?|featuring)\s+(.*)$", RegexOptions.IgnoreCase)] private static partial Regex FeatTail();
    // the same "no real artist" names as TrackTitles
    [GeneratedRegex(@"^(various( artists)?|va|unknown( artist)?|<unknown>)$", RegexOptions.IgnoreCase)] private static partial Regex NoArtist();
    [GeneratedRegex(@"^(unknown( album)?|<unknown>|various( artists)?)$", RegexOptions.IgnoreCase)] private static partial Regex NoAlbum();
    [GeneratedRegex(@"\s*[(\[][^)\]]*[)\]]")] private static partial Regex Bracket();
    [GeneratedRegex(@"soundtrack|\bost\b|music from the (?:motion picture|film|movie)", RegexOptions.IgnoreCase)] private static partial Regex SoundtrackWord();
    [GeneratedRegex(@"\s*[-–—:]?\s*(?:\(|\[)?(?:the\s+)?(?:original\s+)?(?:motion picture\s+|film\s+|movie\s+|television\s+|tv\s+|video game\s+)?(?:soundtrack|ost)(?:\s+album)?(?:\)|\])?\s*$", RegexOptions.IgnoreCase)] private static partial Regex SoundtrackTail();
    [GeneratedRegex(@"\s*[-–—]\s*(?:single|ep)\s*$", RegexOptions.IgnoreCase)] private static partial Regex SingleTail();
    [GeneratedRegex(@"greatest hits|best of|the very best|\bhits\b|collection|anthology|\bessentials?\b|now that'?s what", RegexOptions.IgnoreCase)] private static partial Regex Compilation();
    [GeneratedRegex(@"\s*(?:,|;|/|&|\s+and\s+|\s+x\s+|\s+with\s+)\s*", RegexOptions.IgnoreCase)] private static partial Regex LeadSplit();
    [GeneratedRegex(@"\p{L}+(?:['’]\p{L}+)*")] private static partial Regex Word();
    [GeneratedRegex(@"(?<=\p{L})!(?=\p{L})")] private static partial Regex BangInWord();
    [GeneratedRegex(@"(?<![\d+])\+(?![\d+])")] private static partial Regex Plus();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();

    static readonly HashSet<string> Roman = ["II", "III", "IV", "VI", "VII", "VIII", "IX", "XI", "XII", "XIII", "XIV", "XV", "XX"];

    /// <summary>Whitespace collapsed, control characters gone.</summary>
    public static string Tidy(string? s) => s is null ? "" : Spaces().Replace(new string(s.Where(c => !char.IsControl(c)).ToArray()), " ").Trim();

    public static int WordCount(string s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>Drops version tags: "(Live)", "[2011 Remaster]", "- Remastered 2015", "(Radio Edit)", "(feat. X)", " feat. X".</summary>
    public static string StripTags(string s)
    {
        s = TagBracket().Replace(s, "");
        s = TagDash().Replace(s, "");
        return FeatTail().Replace(s, "").Trim();
    }

    /// <summary>The title as written on screen: no "(feat. X)", "(Remastered 2011)", "[Deluxe]", "- 2009 Remaster", "[Live]" or "(Radio Edit)".</summary>
    public static string Title(string title)
    {
        var t = Tidy(title);
        if (t.Length == 0) return "";
        var s = Tidy(StripTags(WebCatalog.BaseTitle(t)));
        return s.TrimEnd(' ', '-', '–', '—', ':', ',', ';');
    }

    /// <summary>The title is just the file name (no title tag), so it is never spoken.</summary>
    public static bool IsFileName(string title, string? path) => TrackTitles.IsFileName(title, path);

    /// <summary>Blank, "Unknown Artist", "Various": not a name to say.</summary>
    public static bool NoName(string artist) => Tidy(artist) is var a && (a.Length == 0 || NoArtist().IsMatch(a));

    /// <summary>The credit as said: the lead act and a guest from "feat." (in the artist or the title), if any.</summary>
    public readonly record struct Credit(string Lead, string? Guest);

    public static Credit CreditOf(string artist, string title)
    {
        var a = Tidy(artist);
        string? guest = null;
        if (FeatParen().Match(a) is { Success: true } m) { guest = m.Groups[1].Value; a = a.Remove(m.Index, m.Length); }
        else if (FeatTail().Match(a) is { Success: true } m2) { guest = m2.Groups[1].Value; a = a[..m2.Index]; }
        a = Tidy(a);
        if (guest is null)
        {
            var t = Tidy(title);
            var tm = FeatParen().Match(t);
            if (!tm.Success) tm = FeatTail().Match(t);
            if (tm.Success) guest = tm.Groups[1].Value;
        }
        guest = Tidy(guest);
        // a guest that is the act itself, or already named in the credit ("Mark Ronson & Amy Winehouse"), is not added again
        if (guest.Length == 0 || NoArtist().IsMatch(guest) || a.Contains(guest, StringComparison.OrdinalIgnoreCase)) guest = null;
        // over 6 words: the lead act only
        if (WordCount(a) > 6 && LeadSplit().Split(a).FirstOrDefault(p => p.Trim().Length > 0) is { } lead) a = Tidy(lead);
        if (guest is not null && WordCount(a) + WordCount(guest) + 1 > 6) guest = null;
        return new(a, guest);
    }

    /// <summary>The album as written, without edition tags or " - Single"; [soundtrack] is set for "(Original Motion Picture Soundtrack)", "OST" and the like, which are dropped too.</summary>
    public static string Album(string album, out bool soundtrack)
    {
        var a = Tidy(album);
        soundtrack = SoundtrackWord().IsMatch(a);
        if (soundtrack) a = SoundtrackTail().Replace(a, "");
        a = Tidy(Bracket().Replace(a, " "));
        a = SingleTail().Replace(a, "");
        if (soundtrack) a = SoundtrackTail().Replace(a, "");
        a = Tidy(a).TrimEnd(' ', '-', '–', '—', ':', ',');
        return NoAlbum().IsMatch(a) ? "" : a;
    }

    /// <summary>A best-of or hits album: its year is the compilation's, not the song's.</summary>
    public static bool IsCompilation(string album, string albumArtist) => Compilation().IsMatch(album) || (Tidy(albumArtist) is { Length: > 0 } aa && NoArtist().IsMatch(aa));

    /// <summary>
    /// The spoken form of a written name or line: P!nk = Pink, "&amp;" and a lone "+" = "and", "/" and "_" = a space; "!", "?", quotes, brackets, other symbols and emoji go.
    /// ALL CAPS: a line that is all capitals is put in Title Case; otherwise words of 4+ capitals become Title Case (ABBA = Abba) and shorter ones are spelt out (SOS = S O S).
    /// </summary>
    public static string Speak(string s)
    {
        if (s.Length == 0) return "";
        s = BangInWord().Replace(s, "i");
        // capitals first, while "AC/DC" is still one word (spelt out, not "Ac Dc")
        s = Caps(Spaces().Replace(s, " ").Trim());
        s = s.Replace("&", " and ");
        s = Plus().Replace(s, " and ");
        s = s.Replace("@", " at ").Replace("%", " percent");
        var sb = new StringBuilder(s.Length);
        foreach (var r in s.EnumerateRunes())
        {
            var cat = Rune.GetUnicodeCategory(r);
            if (cat is UnicodeCategory.OtherSymbol or UnicodeCategory.ModifierSymbol or UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse or UnicodeCategory.Control) continue;
            if (r.Value == 0xFE0F || r.Value == 0xFE0E) continue;   // emoji presentation selectors
            var c = r.IsBmp ? (char)r.Value : '\0';
            // brackets left in a name are a short pause, not words run together ("Tonight (Dyia)"; owner, 30 Oct)
            if (c is '(' or ')' or '[' or ']' or '{' or '}') { sb.Append(", "); continue; }
            if (c is '!' or '?' or '"' or '“' or '”' or '„' or '«' or '»' or '*' or '#' or '~' or '^' or '<' or '>' or '\\' or '|' or '=') { sb.Append(' '); continue; }
            if (c is '/' or '_') { sb.Append(' '); continue; }
            sb.Append(r.ToString());
        }
        var t = Regex.Replace(Spaces().Replace(sb.ToString(), " "), @"\s*,(\s*,)*\s*", ", ").Trim().Trim(',', ';', ':', '-', '–', '—').Trim();
        // a name ending in a full stop ("HUMBLE.", "DAMN.") would make the voice stop mid-line; an abbreviation ("Mr.") or "..." keeps it
        if (t.EndsWith('.') && !t.EndsWith("..", StringComparison.Ordinal) && !Abbreviations.Contains(t[..^1].Split(' ')[^1])) t = t[..^1].TrimEnd();
        return t;
    }

    static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase) { "Mr", "Mrs", "Ms", "Dr", "St", "Jr", "Sr", "Vol", "No", "vs", "Pt" };

    /// <summary>The capitals rule of <see cref="Speak"/> on its own.</summary>
    public static string Caps(string s)
    {
        var letters = s.Where(char.IsLetter).ToList();
        if (!letters.Any(char.IsUpper)) return s;
        var words = Word().Matches(s);
        // a whole line in capitals ("ALL CAPS TITLE", "HUMBLE.") is shouting, not initials: Title Case all of it; a lone short word (SOS, DNA) is initials
        var allCaps = !letters.Any(char.IsLower) && (s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2 || words.Any(w => w.Value.Count(char.IsLetter) >= 4));
        return Word().Replace(s, m =>
        {
            var w = m.Value;
            var ls = w.Where(char.IsLetter).ToList();
            if (ls.Count < 2 || !ls.All(char.IsUpper)) return w;
            if (Roman.Contains(w)) return w;
            if (allCaps || ls.Count >= 4) return char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant();
            var spelt = string.Join(' ', w.Where(char.IsLetter));
            var before = m.Index > 0 && char.IsLetterOrDigit(s[m.Index - 1]);
            var after = m.Index + m.Length < s.Length && char.IsLetterOrDigit(s[m.Index + m.Length]);
            return (before ? " " : "") + spelt + (after ? " " : "");
        });
    }

    /// <summary>Genres the DJ will name, with how each is said and written. "Pop;Rock" uses the first.</summary>
    static readonly Dictionary<string, (string Say, string Write)> Genres = new(StringComparer.OrdinalIgnoreCase)
    {
        ["rock"] = ("rock", "rock"), ["pop"] = ("pop", "pop"), ["jazz"] = ("jazz", "jazz"), ["soul"] = ("soul", "soul"), ["disco"] = ("disco", "disco"), ["funk"] = ("funk", "funk"),
        ["blues"] = ("blues", "blues"), ["reggae"] = ("reggae", "reggae"), ["country"] = ("country", "country"), ["folk"] = ("folk", "folk"), ["hip hop"] = ("hip hop", "hip hop"),
        ["hiphop"] = ("hip hop", "hip hop"), ["rap"] = ("rap", "rap"), ["r&b"] = ("R and B", "R&B"), ["rnb"] = ("R and B", "R&B"), ["rhythm and blues"] = ("rhythm and blues", "rhythm and blues"),
        ["electronic"] = ("electronic", "electronic"), ["electronica"] = ("electronica", "electronica"), ["dance"] = ("dance", "dance"), ["house"] = ("house", "house"), ["techno"] = ("techno", "techno"),
        ["trance"] = ("trance", "trance"), ["indie"] = ("indie", "indie"), ["indie rock"] = ("indie rock", "indie rock"), ["indie pop"] = ("indie pop", "indie pop"), ["alternative"] = ("alternative", "alternative"),
        ["alternative rock"] = ("alternative rock", "alternative rock"), ["metal"] = ("metal", "metal"), ["heavy metal"] = ("heavy metal", "heavy metal"), ["punk"] = ("punk", "punk"), ["punk rock"] = ("punk rock", "punk rock"),
        ["hard rock"] = ("hard rock", "hard rock"), ["soft rock"] = ("soft rock", "soft rock"), ["classic rock"] = ("classic rock", "classic rock"), ["classical"] = ("classical", "classical"), ["gospel"] = ("gospel", "gospel"),
        ["ska"] = ("ska", "ska"), ["grunge"] = ("grunge", "grunge"), ["ambient"] = ("ambient", "ambient"), ["drum and bass"] = ("drum and bass", "drum and bass"), ["drum & bass"] = ("drum and bass", "drum and bass"),
        ["garage"] = ("garage", "garage"), ["swing"] = ("swing", "swing"), ["motown"] = ("Motown", "Motown"), ["new wave"] = ("new wave", "new wave"), ["synthpop"] = ("synth pop", "synth-pop"),
        ["synth pop"] = ("synth pop", "synth-pop"), ["britpop"] = ("Britpop", "Britpop"), ["dubstep"] = ("dubstep", "dubstep"), ["latin"] = ("Latin", "Latin"), ["k pop"] = ("K pop", "K-pop"),
        ["afrobeats"] = ("Afrobeats", "Afrobeats"), ["bluegrass"] = ("bluegrass", "bluegrass"), ["rock and roll"] = ("rock and roll", "rock and roll"), ["rock & roll"] = ("rock and roll", "rock and roll"),
        ["rock n roll"] = ("rock and roll", "rock and roll"), ["singer songwriter"] = ("singer-songwriter", "singer-songwriter"), ["easy listening"] = ("easy listening", "easy listening"), ["lounge"] = ("lounge", "lounge"),
    };

    static string GenreKey(string genre)
    {
        var first = genre.Split([';', '/', ',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        return Tidy(first.Replace('-', ' ').Replace('_', ' ')).ToLowerInvariant();
    }

    /// <summary>The genre to name (said, written), or null when it is not on the list ("Other", "Unknown", blank).</summary>
    public static (string Say, string Write)? Genre(string genre) => Genres.TryGetValue(GenreKey(genre), out var g) ? g : null;

    /// <summary>Spoken word (audiobooks, podcasts, comedy): the DJ stays out of it.</summary>
    public static bool IsSpokenWord(string genre) => GenreKey(genre) is var k && (k.Contains("spoken") || k.Contains("audiobook") || k.Contains("audio book") || k.Contains("podcast") || k is "comedy" or "speech" or "books");
}
