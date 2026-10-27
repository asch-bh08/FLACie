namespace FLACie.Core;

/// <summary>
/// The DJ's words on screen while it talks: a short line is one caption for the whole clip; a long one is split into two or three phrases at its natural
/// breaks (a full stop, a comma, a dash), each shown for its share of the clip by length. The last phrase stays up a little after the voice ends, then fades.
/// The Android app mirrors this in Kotlin (playback/DjCaptions.kt); keep the two the same.
/// </summary>
public static class DjCaptions
{
    public sealed record Phrase(string Text, int AtMs, int UntilMs);

    /// <summary>A line up to this many characters is one caption.</summary>
    public const int OneLine = 70;
    /// <summary>How long the last phrase stays after the clip ends.</summary>
    public const int LingerMs = 600;
    /// <summary>How long the cue sound plays before the voice (the players hold the clip back by this much when it is on).</summary>
    public const int CueLeadMs = 250;

    /// <summary>[text] as 1-3 phrases over a clip of [clipMs]; the last one ends <see cref="LingerMs"/> after the clip. Empty for an empty line.</summary>
    public static List<Phrase> Plan(string text, int clipMs)
    {
        var t = string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (t.Length == 0) return [];
        clipMs = Math.Max(1000, clipMs);
        var parts = Split(t);
        var total = parts.Sum(p => p.Length);
        var res = new List<Phrase>(); var at = 0;
        for (var i = 0; i < parts.Count; i++)
        {
            var end = i == parts.Count - 1 ? clipMs : at + (int)Math.Round(clipMs * (double)parts[i].Length / total);
            res.Add(new(parts[i], at, i == parts.Count - 1 ? end + LingerMs : end));
            at = end;
        }
        return res;
    }

    /// <summary>The phrases of a line: itself when short, else two or three pieces cut at the breaks nearest to even lengths (at a space when it has none).</summary>
    public static List<string> Split(string t)
    {
        if (t.Length <= OneLine) return [t];
        var n = t.Length > OneLine * 2 ? 3 : 2;
        var cuts = new List<int>();
        for (var k = 1; k < n; k++)
        {
            var want = t.Length * k / n;
            var cut = Best(t, want, cuts.LastOrDefault());
            if (cut > 0 && cut < t.Length) cuts.Add(cut);
        }
        var res = new List<string>(); var from = 0;
        foreach (var c in cuts.Append(t.Length)) { var p = t[from..c].Trim(); if (p.Length > 0) res.Add(p); from = c; }
        return res;
    }

    /// <summary>The place to cut nearest [want]: just after a break (". ", ", ", "; ", " - ") within a third of a phrase of it, else at the nearest space.</summary>
    static int Best(string t, int want, int after)
    {
        var reach = Math.Max(12, t.Length / 6);
        int best = -1, bestDist = int.MaxValue;
        for (var i = Math.Max(after + 1, want - reach); i < Math.Min(t.Length - 1, want + reach); i++)
            if (t[i] == ' ' && i > 0 && t[i - 1] is '.' or ',' or ';' or '!' or '?' or ':' or '-' or '—' && Math.Abs(i - want) < bestDist) { best = i; bestDist = Math.Abs(i - want); }
        if (best > 0) return best;
        for (var d = 0; d < t.Length; d++)
        {
            if (want + d < t.Length && want + d > after && t[want + d] == ' ') return want + d;
            if (want - d > after && want - d > 0 && t[want - d] == ' ') return want - d;
        }
        return -1;
    }

    /// <summary>The phrase showing [ms] after the clip started, or null (before it, or after the last one is gone).</summary>
    public static string? At(IReadOnlyList<Phrase> plan, int ms) => plan.FirstOrDefault(p => ms >= p.AtMs && ms < p.UntilMs)?.Text;
}
