using System.Globalization;
using System.Text;

namespace FLACie.Core;

/// <summary>
/// Search that ranks. The old matcher glued title, artist and album into one string and looked for each typed word inside it, so
/// "no ma" matched "Bruno Mars" (across the join), a one-letter word matched almost everything, nothing was ranked beyond the
/// title, and one typo meant no results. This one works on whole words:
///   exact word > word start > inside a word > glued across a dotted name (will.i.am, P!nk) > one or two typos,
/// weighted by where it hit (title, then artist, then album), with a bonus when the whole query is the title or "artist title".
/// Every typed word has to hit something. Typos only count when nothing matches cleanly. Ported to Kotlin (library/Track.kt) so
/// the phone ranks the same way.
/// </summary>
public static class SearchRank
{
    public sealed class Doc
    {
        public readonly string[] Title, Artist, Album;
        public readonly string TitleGlue, ArtistGlue, AlbumGlue, All;
        public Doc(string title, string artist, string album)
        {
            Title = Words(title); Artist = Words(artist); Album = Words(album);
            TitleGlue = string.Concat(Title); ArtistGlue = string.Concat(Artist); AlbumGlue = string.Concat(Album);
            All = TitleGlue + ArtistGlue + AlbumGlue;
        }
    }

    public sealed class Query
    {
        public readonly string[] Tokens;
        public readonly string Glued, Spaced;
        public Query(string q) { Tokens = Words(q); Glued = string.Concat(Tokens); Spaced = string.Join(' ', Tokens); }
        public bool Empty => Tokens.Length == 0;
    }

    /// <summary>Lower-case words with accents dropped; "&amp;" and "+" read as "and"; the dots and apostrophes inside a name are joiners
    /// ("will.i.am" is one word, "don't" is "dont").</summary>
    public static string[] Words(string s)
    {
        if (string.IsNullOrEmpty(s)) return [];
        var d = s.ToLowerInvariant().Replace("&", " and ").Replace("+", " and ").Normalize(NormalizationForm.FormD);
        var words = new List<string>(); var sb = new StringBuilder();
        void Flush() { if (sb.Length > 0) { words.Add(sb.ToString()); sb.Clear(); } }
        foreach (var c in d)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (c is '.' or '\'' or '’' or '`') continue;
            else Flush();
        }
        Flush();
        return [.. words];
    }

    /// <summary>0 = not a match. Higher is better. [fuzzy] also accepts a typo or two.</summary>
    public static int Score(Query q, Doc d, bool fuzzy)
    {
        if (q.Empty) return 0;
        var total = 0;
        foreach (var t in q.Tokens)
        {
            var best = Best(t, d.Title, d.TitleGlue, 10, fuzzy);
            best = Math.Max(best, Best(t, d.Artist, d.ArtistGlue, 8, fuzzy));
            best = Math.Max(best, Best(t, d.Album, d.AlbumGlue, 4, fuzzy));
            if (best == 0) { total = -1; break; }
            total += best;
        }
        // "will i am": no single word, but the typed letters run on through a dotted name
        if (total < 0 && q.Glued.Length >= 4 && (Runs(d.Artist, q.Glued) || Runs(d.Title, q.Glued) || Runs(d.Album, q.Glued))) total = 6 * q.Tokens.Length;
        if (total <= 0) return 0;
        var title = d.TitleGlue; var both = d.ArtistGlue + d.TitleGlue; var both2 = d.TitleGlue + d.ArtistGlue;
        if (title == q.Glued) total += 60;
        else if (both == q.Glued || both2 == q.Glued) total += 50;
        else if (title.StartsWith(q.Glued, StringComparison.Ordinal)) total += 25;
        else if (d.ArtistGlue == q.Glued) total += 20;
        return total;
    }

    /// <summary>Some run of whole words, written together, spells [glued] ("will" "i" "am" against the words of "Will I Am").</summary>
    static bool Runs(string[] words, string glued)
    {
        for (var i = 0; i < words.Length; i++)
        {
            var acc = "";
            for (var j = i; j < words.Length; j++)
            {
                acc += words[j];
                if (acc == glued) return true;
                if (!glued.StartsWith(acc, StringComparison.Ordinal)) break;
            }
        }
        return false;
    }

    static int Best(string t, string[] words, string glue, int weight, bool fuzzy)
    {
        var best = 0;
        foreach (var w in words)
        {
            int s;
            if (w == t) s = weight * 3;
            else if (t.Length >= 2 && w.StartsWith(t, StringComparison.Ordinal)) s = weight * 2;
            else if (t.Length >= 3 && w.Contains(t, StringComparison.Ordinal)) s = weight;
            else if (fuzzy && t.Length >= 4 && Near(t, w)) s = Math.Max(1, weight / 2);
            else continue;
            if (s > best) best = s;
        }
        return best;
    }

    /// <summary>Within one typo for short words, two for long ones; also a typo in a word the user is still typing (a prefix).</summary>
    static bool Near(string a, string b)
    {
        if (a[0] != b[0]) return false; // slips of the finger rarely change the first letter
        var max = a.Length >= 9 ? 2 : 1;
        if (Math.Abs(a.Length - b.Length) <= max && Distance(a, b, max) <= max) return true;
        return b.Length > a.Length && Distance(a, b[..a.Length], max) <= max;
    }

    /// <summary>Damerau-Levenshtein distance, giving up once it passes [limit].</summary>
    public static int Distance(string a, string b, int limit)
    {
        if (Math.Abs(a.Length - b.Length) > limit) return limit + 1;
        var prev2 = new int[b.Length + 1]; var prev = new int[b.Length + 1]; var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i; var rowMin = cur[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                var v = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) v = Math.Min(v, prev2[j - 2] + 1);
                cur[j] = v; if (v < rowMin) rowMin = v;
            }
            if (rowMin > limit) return limit + 1;
            (prev2, prev, cur) = (prev, cur, prev2);
        }
        return prev[b.Length];
    }

    /// <summary>The items that match, best first. Typos are tried only when fewer than [enough] match cleanly.</summary>
    public static List<T> Rank<T>(IEnumerable<T> items, Func<T, Doc> doc, string query, int take, int enough = 4)
    {
        var q = new Query(query);
        if (q.Empty) return [];
        var list = items as IList<T> ?? items.ToList();
        var hits = Run(list, doc, q, false);
        if (hits.Count < enough) hits = Run(list, doc, q, true);
        return hits.OrderByDescending(h => h.score).Take(take).Select(h => h.item).ToList();
    }

    static List<(T item, int score)> Run<T>(IList<T> list, Func<T, Doc> doc, Query q, bool fuzzy)
    {
        var res = new List<(T, int)>();
        foreach (var it in list) { var s = Score(q, doc(it), fuzzy); if (s > 0) res.Add((it, s)); }
        return res;
    }

    /// <summary>True when every typed word hits, with typos allowed: for filtering online catalog results.</summary>
    public static bool Matches(string query, string title, string artist, string album = "") =>
        Score(new Query(query), new Doc(title, artist, album), true) > 0;
}
