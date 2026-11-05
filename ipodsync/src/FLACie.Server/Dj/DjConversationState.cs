using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using FLACie.Core;

namespace FLACie.Server.Dj;

/// <summary>
/// F12: the question the DJ is waiting on, per account, for <see cref="Life"/>: "are you sure?" before a removal ([OnYes] is done only after a yes),
/// or "want a different name?" after a playlist was made ([Rename]: the playlist's name; the next words are the new name).
/// </summary>
public sealed partial class DjConversationState(Func<DateTime> now)
{
    public static readonly TimeSpan Life = TimeSpan.FromMinutes(1);

    public sealed record Pending(DateTime At, DjAskAnswer? OnYes = null, string? Rename = null);
    readonly ConcurrentDictionary<string, Pending> pending = new();

    public void Confirm(string account, DjAskAnswer onYes) => pending[account] = new(now(), onYes);
    public void OfferRename(string account, string playlist) => pending[account] = new(now(), Rename: playlist);

    [GeneratedRegex(@"^(?:yes|yeah|yep|yup|sure|ok|okay|go on|go ahead|do it|please|definitely|absolutely|correct|right)\b", RegexOptions.IgnoreCase)] private static partial Regex Yes();
    [GeneratedRegex(@"^(?:no|nope|nah|dont|don't|leave it|cancel|keep it|never mind|not really|it's fine|its fine|fine)\b", RegexOptions.IgnoreCase)] private static partial Regex No();
    [GeneratedRegex(@"^(?:(?:yes|yeah|ok|okay)[, ]+)?(?:call it|name it|rename it(?: to)?|make it|change it to)\s+(.+?)[.!]?$", RegexOptions.IgnoreCase)] private static partial Regex NewName();

    /// <summary>The answer to [text] when a question is waiting on it; null when none is (or it's over a minute old), and the request is read as usual.</summary>
    public DjAskAnswer? Answer(string account, string text)
    {
        if (!pending.TryRemove(account, out var p) || now() - p.At > Life) return null;
        var t = text.Trim();
        if (p.OnYes is { } yes)
        {
            if (Yes().IsMatch(t)) return yes;
            if (No().IsMatch(t)) return DjAskAnswer.Said("Okay, I'll leave it.", "Confirm");
            return null;
        }
        if (p.Rename is not { } old) return null;
        if (NewName().Match(t) is { Success: true } m) return Renamed(old, m.Groups[1].Value.Trim());
        if (No().IsMatch(t)) return DjAskAnswer.Said($"{old} it is.", "Rename");
        if (Yes().IsMatch(t)) { OfferRename(account, old); return new(DjAskDo.None, "What should I call it?", Intent: "Clarify") { Steps = [DjAskStep.Question("What should I call it?")] }; }
        return null;
    }

    static DjAskAnswer Renamed(string old, string name) =>
        new(DjAskDo.None, $"Done, it's called {name} now.", Intent: "Rename") { Steps = [new(DjAskDo.RenamePlaylist, Title: name, Playlist: old)] };
}
