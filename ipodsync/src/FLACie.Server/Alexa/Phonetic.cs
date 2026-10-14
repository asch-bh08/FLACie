using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FLACie.Server.Alexa;

/// <summary>A name folded for comparison: <see cref="Fold"/> is the glued, accent-free text, <see cref="Key"/> its consonant skeleton (what a speech recogniser can confuse: "beetles" and "beatles").</summary>
public readonly record struct Form(string Fold, string Key);

/// <summary>
/// Folding of names the way people say them and speech recognition writes them. "x t c" = XTC, "a c d c" = AC/DC, "P!nk" = pink, "the beetles" = The Beatles,
/// "2Pac" = "two pac", "Hey Jude - Remastered 2015" = "hey jude". Deterministic, no I/O.
/// </summary>
public static partial class Phonetic
{
    [GeneratedRegex(@"\s*[\(\[][^\)\]]*\b(?:live|remaster(?:ed)?|mono|stereo|deluxe|radio edit|single version|album version|feat|ft|featuring|version|edit|bonus track|explicit|clean)\b[^\)\]]*[\)\]]", RegexOptions.IgnoreCase)] private static partial Regex TagBracket();
    [GeneratedRegex(@"\s[-–—]\s.*\b(?:remaster(?:ed)?|live|mono|stereo|deluxe|edit|version|bonus|single|acoustic|demo)\b.*$", RegexOptions.IgnoreCase)] private static partial Regex TagDash();
    [GeneratedRegex(@"\s+(?:feat\.?|ft\.?|featuring)\s.*$", RegexOptions.IgnoreCase)] private static partial Regex FeatTail();
    [GeneratedRegex(@"\s*[\(\[][^\)\]]*[\)\]]")] private static partial Regex AnyBracket();
    [GeneratedRegex(@"(?<=\d)(?=\p{L})|(?<=\p{L})(?=\d)")] private static partial Regex DigitEdge();
    [GeneratedRegex(@"[^\p{L}\p{N}]+")] private static partial Regex NonAlnum();

    static readonly string[] DigitWords = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine"];

    /// <summary>Drops version tags: "(Live)", "[2011 Remaster]", "- Remastered 2015", "(feat. X)", " feat. X".</summary>
    public static string StripTags(string s)
    {
        s = TagBracket().Replace(s, "");
        s = TagDash().Replace(s, "");
        return FeatTail().Replace(s, "").Trim();
    }

    /// <summary>The title as it is spoken back: no version tags.</summary>
    public static string CleanTitle(string s) => StripTags(s);

    /// <summary>The same with every bracket group dropped as well: "(I Can't Get No) Satisfaction" = "Satisfaction".</summary>
    public static string StripBrackets(string s) => AnyBracket().Replace(StripTags(s), "").Trim();

    static string Accents(string s)
    {
        if (s.All(c => c < 128)) return s;
        s = s.Replace("ß", "ss").Replace("æ", "ae").Replace("œ", "oe").Replace("ø", "o").Replace("ł", "l").Replace("đ", "d");
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Words of a name, folded: lower-case, no accents, "&amp;"/"+"/"n"/"plus" = "and", numbers spelled out, "to"/"too" = "two", "for" = "four", a leading "the" dropped.</summary>
    public static List<string> Words(string s, bool tags = true)
    {
        if (string.IsNullOrWhiteSpace(s)) return [];
        s = Accents((tags ? StripTags(s) : s).ToLowerInvariant());
        var sb = new StringBuilder(s.Length + 8);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '!' && i > 0 && i + 1 < s.Length && char.IsLetter(s[i - 1]) && char.IsLetter(s[i + 1])) sb.Append('i');   // P!nk
            else if (c is '&' or '+') sb.Append(" and ");
            else if (c is '\'' or '’' or '`' or '´') { }
            else sb.Append(c);
        }
        var tokens = new List<string>();
        foreach (var t0 in NonAlnum().Replace(sb.ToString(), " ").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            foreach (var t in DigitEdge().Split(t0))
            {
                if (t.Length == 0) continue;
                if (t is "n" or "plus") { tokens.Add("and"); continue; }
                if (t is "to" or "too") { tokens.Add("two"); continue; }
                if (t == "for") { tokens.Add("four"); continue; }
                if (char.IsDigit(t[0])) { foreach (var d in t) tokens.Add(DigitWords[d - '0']); continue; }
                tokens.Add(t.Length > 4 && t.EndsWith("ing", StringComparison.Ordinal) ? t[..^1] : t);   // believing = believin'
            }
        if (tokens.Count > 1 && tokens[0] == "the") tokens.RemoveAt(0);
        return tokens;
    }

    /// <summary>The folded words glued together ("radio head" and "Radiohead" meet here).</summary>
    public static string Fold(string s) => string.Concat(Words(s));

    /// <summary>A consonant skeleton of the glued text: vowels dropped (except a leading one), c/k/q = k, ph = f, x = ks, z = s, doubles collapsed.</summary>
    public static string Skeleton(string fold)
    {
        if (fold.Length == 0) return "";
        var s = fold.Replace("ph", "f").Replace("ck", "k").Replace("kn", "n").Replace("wr", "r").Replace("x", "ks").Replace("q", "k").Replace("z", "s");
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == 'c') c = (i + 1 < s.Length && (s[i + 1] is 'e' or 'i' or 'y')) ? 's' : 'k';
            if (c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y') { if (i == 0) sb.Append('a'); continue; }
            if (c == 'h' && i > 0 && sb.Length > 0 && sb[^1] is 's' or 'c' or 'g' or 'p' or 't') continue;
            if (sb.Length > 0 && sb[^1] == c) continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>A phonetic key: equal for names that sound alike once folded.</summary>
    public static string Key(string s) => Skeleton(Fold(s));

    public static Form FormOf(string s) { var f = Fold(s); return new(f, Skeleton(f)); }

    /// <summary>0..1: how alike two spoken names are (edit distance on the folded text, or on the consonant skeleton when that fits better). Symmetric.</summary>
    public static double Similarity(string a, string b) => Similarity(FormOf(a), FormOf(b));

    public static double Similarity(Form a, Form b)
    {
        if (a.Fold.Length == 0 || b.Fold.Length == 0) return 0;
        if (a.Fold == b.Fold) return 1;
        var lev = Ratio(a.Fold, b.Fold);
        if (a.Key.Length >= 3 && b.Key.Length >= 3)
        {
            var kr = a.Key == b.Key ? 0.9 : Ratio(a.Key, b.Key) * 0.8;
            if (kr > lev) lev = kr;
        }
        return lev;
    }

    static double Ratio(string a, string b)
    {
        var max = Math.Max(a.Length, b.Length);
        if (Math.Abs(a.Length - b.Length) * 2 > max) return Math.Max(0, 1.0 - (double)Math.Abs(a.Length - b.Length) / max) * 0.5;
        return 1.0 - (double)Distance(a, b) / max;
    }

    static int Distance(string a, string b)
    {
        if (a.Length > 80) a = a[..80];
        if (b.Length > 80) b = b[..80];
        var prev = new int[b.Length + 1]; var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    /// <summary>Makes text safe to speak (SSML): "&amp;" becomes "and", markup characters go.</summary>
    public static string SpeechSafe(string s)
    {
        s = s.Replace("&", " and ");
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) if (c is not ('<' or '>' or '"' or '\\') && !char.IsControl(c)) sb.Append(c);
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }
}
