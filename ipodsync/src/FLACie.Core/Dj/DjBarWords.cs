using System.Text.RegularExpressions;

namespace FLACie.Core;

/// <summary>F26.5: the words in the DJ bar while it works on what it heard ("Finding your favourites…", "Looking for Firestarter…", "Thinking…").</summary>
public static partial class DjBarWords
{
    [GeneratedRegex(@"\bfav(?:ou?rite)?s?\b|\bfaves?\b|\bsongs i love\b", RegexOptions.IgnoreCase)] private static partial Regex Favourites();
    [GeneratedRegex(@"^(?:play|put on)\s+(.{2,60}?)(?:\s+(?:please|for me|now))?$", RegexOptions.IgnoreCase)] private static partial Regex PlaySomething();
    [GeneratedRegex(@"\b(?:make|create|start)\b.*\bplaylist\b", RegexOptions.IgnoreCase)] private static partial Regex MakeList();

    public static string Doing(string heard)
    {
        var t = (heard ?? "").Trim().TrimEnd('.', '!', '?');
        if (Favourites().IsMatch(t)) return "Finding your favourites…";
        if (MakeList().IsMatch(t)) return "Making your playlist…";
        if (PlaySomething().Match(t) is { Success: true } m && !m.Groups[1].Value.StartsWith("some", StringComparison.OrdinalIgnoreCase)) return $"Looking for {m.Groups[1].Value}…";
        return "Thinking…";
    }
}
