using System.Globalization;
using System.Text;

namespace FLACie.Core;

/// <summary>
/// Checking a name by ear: the voice says the plain name and the respelling, a speech-to-text model writes down what it heard, and whichever comes back
/// nearer the written name wins. When the plain name is already heard right, the respelling is dropped (it can only make things worse).
/// </summary>
public static class DjHearing
{
    public enum Verdict { Plain, Respelt }

    /// <summary>A transcript this close to the written name (0-1) counts as heard right.</summary>
    public const double Right = 0.85;

    /// <summary>For comparing: lower case, accents off, "&amp;" as "and", "$" as "s", only letters and digits (spaces, dots and hyphens don't count).</summary>
    public static string Norm(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var t = s.Replace("&", " and ").Replace("$", "s").Replace("ø", "o").Replace("Ø", "o").Normalize(NormalizationForm.FormD);
        var b = new StringBuilder(t.Length);
        foreach (var c in t)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) b.Append(char.ToLowerInvariant(c));
        }
        return b.ToString();
    }

    /// <summary>How near [heard] is to [written], 0-1 (1 - edit distance / the longer length, after <see cref="Norm"/>). A transcript that has the name
    /// inside a little more ("the prodigy." or "it's the prodigy") is scored on its best-matching stretch.</summary>
    public static double Score(string written, string? heard)
    {
        var a = Norm(written); var h = Norm(heard);
        if (a.Length == 0 || h.Length == 0) return 0;
        var best = Ratio(a, h);
        if (h.Length > a.Length)
            for (var i = 0; i + a.Length <= h.Length; i++) best = Math.Max(best, Ratio(a, h.Substring(i, a.Length)) - 0.02);
        return Math.Round(best, 3);
    }

    static double Ratio(string a, string b) => 1.0 - (double)Distance(a, b) / Math.Max(a.Length, b.Length);

    static int Distance(string a, string b)
    {
        var prev = new int[b.Length + 1]; var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++) cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    /// <summary>Which to keep: the plain name when it is heard right, or when the respelling is heard no better; else the respelling.</summary>
    public static Verdict Choose(double plain, double respelt) => plain >= Right || respelt <= plain ? Verdict.Plain : Verdict.Respelt;
}
