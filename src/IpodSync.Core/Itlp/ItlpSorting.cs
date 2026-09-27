using System.Globalization;
using System.Text;

namespace IpodSync.Core.Itlp;

/// <summary>
/// Sort names and sort ranks for the SQLite library, derived from the rows iTunes
/// wrote on the real device (and checked against all of them by
/// <c>itlp-orders-check</c> before being trusted):
///
/// - <c>sort_*</c> text: leading punctuation/symbols stripped
///   ("(Don't Fear) The Reaper" → "Don't Fear) The Reaper", "...Baby" → "Baby",
///   "•KS34•" → "KS34•"), then one leading English article, case-insensitively
///   ("THE GOAT" → "GOAT").
/// - <c>*_order</c> ranks: 100 × position among distinct sort keys, compared with
///   Unicode (invariant, case-insensitive) collation, except keys that begin with a
///   digit sort after all letters (the device's "#" section).
/// - A rank for a new key is placed between its neighbours' existing ranks, so no
///   existing row has to be renumbered.
/// </summary>
public static class ItlpSorting
{
    public const long UnknownOrder = 4294967295;

    public static string? SortName(string? s)
    {
        if (s is null) return null;
        int i = 0;
        while (i < s.Length && !char.IsLetterOrDigit(s[i])) i++;
        string t = i < s.Length ? s[i..] : s;
        foreach (var article in new[] { "The ", "A ", "An " })
            if (t.Length > article.Length && t.StartsWith(article, StringComparison.OrdinalIgnoreCase))
                return t[article.Length..];
        return t;
    }

    /// <summary>
    /// Character-class collation matching the device's ranks: apostrophes and hyphens
    /// are ignored; then space &lt; other punctuation/symbols (by code point) &lt;
    /// digits &lt; letters (case- and accent-insensitive). A key whose first
    /// character is a digit sorts after every key that starts with a letter.
    /// </summary>
    public static readonly IComparer<string> Collation = Comparer<string>.Create(CompareKeys);

    public static bool SameKey(string a, string b) => CompareKeys(a, b) == 0;

    private static int CompareKeys(string a, string b)
    {
        var ka = Key(a);
        var kb = Key(b);
        bool da = ka.Count > 0 && ka[0].Class == 2, db = kb.Count > 0 && kb[0].Class == 2;
        if (da != db) return da ? 1 : -1;
        for (int i = 0; i < Math.Min(ka.Count, kb.Count); i++)
        {
            int c = ka[i].Class.CompareTo(kb[i].Class);
            if (c == 0) c = ka[i].Value.CompareTo(kb[i].Value);
            if (c != 0) return c;
        }
        return ka.Count.CompareTo(kb.Count);
    }

    private static List<(int Class, int Value)> Key(string s)
    {
        var key = new List<(int, int)>(s.Length);
        foreach (char raw in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(raw) == UnicodeCategory.NonSpacingMark) continue;
            if (raw is '\'' or '’' or '‘' or '-') continue;
            char ch = char.ToLowerInvariant(raw);
            if (char.IsWhiteSpace(ch)) key.Add((0, 0));
            else if (char.IsLetter(ch)) key.Add((3, ch));
            else if (char.IsDigit(ch)) key.Add((2, ch));
            else key.Add((1, ch));
        }
        return key;
    }

    /// <summary>Rank for <paramref name="key"/> among existing (key, rank) pairs:
    /// an equal key's rank if one exists, otherwise the midpoint between the
    /// neighbouring ranks (or +100 past the end / half of the first rank).</summary>
    public static long NeighbourRank(IEnumerable<(string Key, long Rank)> existing, string key)
    {
        long below = 0, above = long.MaxValue;
        foreach (var (k, r) in existing)
        {
            if (r == UnknownOrder || r <= 0) continue;
            int c = Collation.Compare(k, key);
            if (c == 0 || SameKey(k, key)) return r;
            if (c < 0 && r > below) below = r;
            if (c > 0 && r < above) above = r;
        }
        if (above == long.MaxValue) return below + 100;
        if (above - below > 1) return below + (above - below) / 2;
        return below > 0 ? below : above;   // no gap left: tie with a neighbour (sorting stays correct enough)
    }
}
