namespace FLACie.Core;

/// <summary>One clip of a pooled line: [Text] spoken, [Caption] written. [Personal]: it holds the listener's first name (a hello), so its clip is kept for that
/// person only; every other clip is shared by the family (it holds no personal data).</summary>
public sealed record DjPart(string Text, string Caption, bool Personal = false);

/// <summary>
/// The reusable phrases of pooled lines, per style: each is made once per voice and kept, and a line is one of them followed by a short clip of the song's name
/// ("Coming up," + "Smack That, by Akon with Eminem."). Only where that sounds natural: before a name, and "That was..." / "...and this is" around two names.
/// </summary>
public static class DjPool
{
    public static IReadOnlyList<string> Intros(DjStyle s) => s switch
    {
        DjStyle.Chill => ["Here's", "Now then,", "Next, nice and easy,", "This is"],
        DjStyle.Hype => ["Coming up,", "Here we go,", "Right, it's", "Okay,"],
        _ => ["Coming up,", "Here's", "Alright,", "This is", "Okay,"],
    };

    public static IReadOnlyList<string> Openers(DjStyle s) => s switch
    {
        DjStyle.Chill => ["Easing in with", "First up,"],
        DjStyle.Hype => ["Let's get going with", "Kicking off with"],
        _ => ["First up,", "Starting with", "So, to start,"],
    };

    public const string ThatWas = "That was...", AndThisIs = "...and this is";

    /// <summary>Every phrase of [style] with no name in it, hellos included: what is made once per voice ahead of time.</summary>
    public static IEnumerable<string> All(DjStyle s)
    {
        foreach (var p in Intros(s)) yield return p;
        foreach (var p in Openers(s)) yield return p;
        yield return ThatWas; yield return AndThisIs;
        foreach (var h in new[] { 8, 14, 20 })
            foreach (var d in new[] { 1, 3, 6, 0 })
                if (DjPlanner.Greeting(h, d, s, null) is { } g) yield return g.Say;
    }

    /// <summary>The song's name clip: "Smack That, by Akon with Em-in-em." (spoken) and "Smack That, by Akon with Eminem." (written).</summary>
    public static DjPart Name(string titleSay, string titleWritten, string artistSay, string artistWritten) =>
        new(DjTemplates.Finish(titleSay + ", by " + artistSay + "."), DjTemplates.Finish(titleWritten + ", by " + artistWritten + "."));

    /// <summary>The parts said one after the other, as one line of text (what the device's own voice says when there is no clip).</summary>
    public static string Join(IReadOnlyList<DjPart> parts) => DjText.Tidy(string.Join(' ', parts.Select(p => p.Text)));
}
