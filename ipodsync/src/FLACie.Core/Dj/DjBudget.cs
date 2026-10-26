namespace FLACie.Core;

/// <summary>A Workers AI text model and Cloudflare's price for it, in neurons per million tokens (developers.cloudflare.com/workers-ai/platform/pricing).</summary>
public sealed record DjTextModel(string Id, double InNeurons, double OutNeurons)
{
    /// <summary>The neurons of one call.</summary>
    public double Cost(double tokensIn, double tokensOut) => tokensIn * InNeurons / 1e6 + tokensOut * OutNeurons / 1e6;
}

/// <summary>The text models the DJ can write with. <see cref="Primary"/> first; <see cref="Fallback"/> only when it could not answer at all; then the templates.</summary>
public static class DjModels
{
    /// <summary>Llama 3.3 70B (fp8, fast): by far the best short copy of the models on the free plan, and still about 22 neurons a line.</summary>
    public static readonly DjTextModel Primary = new("@cf/meta/llama-3.3-70b-instruct-fp8-fast", 26_668, 204_805);
    /// <summary>Llama 3.1 8B, the model of the last round.</summary>
    public static readonly DjTextModel Fallback = new("@cf/meta/llama-3.1-8b-instruct", 25_608, 75_147);

    /// <summary>The candidates that were weighed (for the report and the tests): prices as published on 2026-10-26.</summary>
    public static readonly IReadOnlyList<DjTextModel> Weighed =
    [
        Primary, Fallback,
        new("@cf/mistralai/mistral-small-3.1-24b-instruct", 31_876, 50_488),
        new("@cf/google/gemma-3-12b-it", 31_371, 50_560),
        new("@cf/meta/llama-4-scout-17b-16e-instruct", 24_545, 77_273),
        new("@cf/qwen/qwq-32b", 60_000, 90_909),
    ];
}

/// <summary>
/// How the free allowance is shared out over a day. Aura-1 costs by the character, so a whole line (about 100 characters, about 136 neurons) costs three times
/// a pooled line: a phrase made once and kept ("Coming up,") followed by a short clip of the song's name ("Smack That, by Akon with Eminem."), which is itself
/// kept, so a song heard again costs nothing. Whole lines (the model's, or a template's) are said while there is room for them above a reserve kept for
/// pooled lines for the rest of the day; that reserve shrinks as the listener's day goes on, so an evening that has budget left gets the best lines.
/// </summary>
public static class DjBudget
{
    public const double FreeNeurons = 10_000;
    /// <summary>Aura-1: 1,363.64 neurons per 1,000 characters.</summary>
    public const double AuraPerChar = 1.36364;
    /// <summary>The pooled lines a day the reserve is kept for: what "well over 100 Aura lines a day" needs at the 50% cap.</summary>
    public const int Floor = 120;
    /// <summary>The listening day the reserve is spread over (06:00 to midnight where the listener is).</summary>
    public const int DayStart = 6, DayHours = 18;
    /// <summary>A song's name clip, as an average: "Smack That, by Akon with Em-in-em." is 34 characters.</summary>
    public const int NameChars = 32;
    /// <summary>A whole line, as an average (the model's lines are 60 to 130 characters).</summary>
    public const int WholeChars = 100;
    /// <summary>The model's prompt and answer, as an average (the answer is capped at <see cref="MaxTokens"/>).</summary>
    public const int PromptTokens = 550, AnswerTokens = 35, MaxTokens = 60;

    public static double Aura(int chars) => chars * AuraPerChar;
    /// <summary>A pooled line once its phrase is made: the name clip only.</summary>
    public static double PooledCost => Aura(NameChars);
    /// <summary>A whole Aura line written by [model].</summary>
    public static double WholeCost(DjTextModel model) => Aura(WholeChars) + model.Cost(PromptTokens, AnswerTokens);

    /// <summary>The pooled lines still to keep room for: what is left of <see cref="Floor"/>, in proportion to what is left of the listener's day
    /// (taken as midday when their time is not known).</summary>
    public static int FloorLeft(int linesToday, int? localHour)
    {
        var left = Math.Max(0, Floor - linesToday);
        var h = localHour ?? 12;
        var hoursLeft = h < DayStart ? DayHours : Math.Max(0, DayStart + DayHours - h);
        return Math.Min(left, (int)Math.Ceiling(Floor * hoursLeft / (double)DayHours));
    }

    /// <summary>A whole line costing [cost] may be said: it leaves room for the reserve of pooled lines.</summary>
    public static bool WholeAllowed(double cap, double used, int linesToday, int? localHour, double cost) =>
        used + cost + FloorLeft(linesToday, localHour) * PooledCost <= cap;

    /// <summary>A rough "lines left today" for the dashboard: what is left under the cap, in pooled lines.</summary>
    public static int LinesLeft(double cap, double used) => Math.Max(0, (int)Math.Floor((cap - used) / PooledCost));

    /// <summary>Songs an hour, as an average (about 3.5 minutes a song): what one listener on "Every song" needs in lines an hour. "Often" is about a third of it.</summary>
    public const int SongsPerHour = 17;

    /// <summary>How long [linesLeft] last one listener on "Every song", in hours.</summary>
    public static double HoursOfEverySong(int linesLeft) => Math.Max(0, linesLeft) / (double)SongsPerHour;

    /// <summary>Lines a day at [cap] when every line is whole (the last round: an 8B line said whole by Aura, every time).</summary>
    public static double LinesPerDayWhole(double cap) => cap / (Aura(WholeChars) + DjModels.Fallback.Cost(375, AnswerTokens));

    /// <summary>
    /// Lines a day at [cap] for a family that listens all day, with [repeat] the share of name clips already made (a song heard before): the reserve's lines,
    /// pooled, and whole model lines in whatever the reserve leaves. (A pooled phrase is made once per voice, so it is left out.)
    /// </summary>
    public static (double Pooled, double Whole) LinesPerDay(double cap, double repeat)
    {
        var pooled = PooledCost * (1 - Math.Clamp(repeat, 0, 1));
        if (pooled <= 0) return (Floor, cap / WholeCost(DjModels.Primary));
        var reserve = Floor * pooled;
        if (reserve >= cap) return (cap / pooled, 0);
        return (Floor, (cap - reserve) / WholeCost(DjModels.Primary));
    }
}

/// <summary>The song's feel in a few plain words, from its <see cref="Vibe"/> (and the previous song's): only clear cases, "" otherwise.</summary>
public static class DjFeel
{
    public static string Of(Vibe? now, Vibe? prev)
    {
        if (now is null) return "";
        if (prev is not null)
        {
            var slower = prev.Bpm > 0 && now.Bpm > 0 && now.Bpm < prev.Bpm * 0.8;
            var faster = prev.Bpm > 0 && now.Bpm > 0 && now.Bpm > prev.Bpm * 1.25;
            if ((slower && now.Energy <= prev.Energy) || now.Energy < prev.Energy - 0.25) return "slowing it down";
            if ((faster && now.Energy >= prev.Energy) || now.Energy > prev.Energy + 0.25) return "picking up the pace";
        }
        if (now.Energy >= 0.7 && now.Loudness >= 0.75) return "a big one";
        if (now.Energy <= 0.25) return "a gentle one";
        return "";
    }
}

/// <summary>The listener's first name for "Evening, Ashley": the first word of the account's name when it looks like a name; null otherwise
/// (an e-mail address, "admin", "guest", digits).</summary>
public static class DjNames
{
    static readonly HashSet<string> NotNames = new(StringComparer.OrdinalIgnoreCase)
        { "admin", "administrator", "root", "guest", "user", "test", "family", "home", "media", "music", "jellyfin", "flacie", "kids", "shared", "owner", "server" };

    public static string? FirstName(string? accountName)
    {
        var w = (accountName ?? "").Trim().Split([' ', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (w.Length is < 2 or > 20 || !w.All(char.IsLetter) || NotNames.Contains(w) || (accountName ?? "").Contains('@')) return null;
        return w.All(char.IsLower) || w.All(char.IsUpper) ? char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant() : w;
    }
}

/// <summary>Where the DJ's facts about a song come from on the server: the listener's taste and the MusicBrainz cache. Pure.</summary>
public static class DjFactsOf
{
    /// <summary>Loved (hearted, or finished three times lately), a new find (never played by this account, once it has played anything), or not known.</summary>
    public static DjBond Bond(TasteProfile taste, Track t) => taste.Likes(t) ? DjBond.Loved : !taste.Empty && taste.Known(t) <= 0 ? DjBond.NewFind : DjBond.Unknown;

    /// <summary>
    /// What MusicBrainz says for certain about [t]: only from a candidate that matches it closely (title, artist and album, or title, artist and length);
    /// the first year is the earliest year of the close matches when it is before the song's own tag; the label is not in the search answer, so it stays blank.
    /// </summary>
    public static DjExtra? Brainz(Track t, IReadOnlyList<MbCandidate>? hits, Vibe? vibe = null, Vibe? prevVibe = null)
    {
        var close = (hits ?? []).Select(c => (c, s: MbMatching.Score(t, c))).Where(x => x.s >= 85).Select(x => x.c).ToList();
        var type = close.FirstOrDefault()?.ReleaseType is { Length: > 0 } rt
            ? string.Join(' ', rt.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w == "ep" ? "EP" : w)) : "";
        if (type == "other") type = "";
        var first = close.Where(c => c.Year is >= 1900).Select(c => c.Year!.Value).DefaultIfEmpty(0).Min();
        if (first > 0 && t.Year > 0 && first >= t.Year) first = 0;
        if (type.Length == 0 && first == 0 && vibe is null) return null;
        return new DjExtra(type, first, "", vibe, prevVibe);
    }
}
