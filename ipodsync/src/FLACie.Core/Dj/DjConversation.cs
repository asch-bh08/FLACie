using System.Text.RegularExpressions;

namespace FLACie.Core;

/// <summary>What happens after one turn of "talk to the DJ": listen again, or end the conversation (and whether the paused song carries on).</summary>
public enum DjTurn { ListenAgain, End, EndMusicChosen }

/// <summary>
/// "Talk to the DJ": a press of the mic pauses the music (the mic heard the song otherwise) and listens. One request, one answer, then it stops, as
/// soon as the person has finished speaking (owner, 28 Oct: no waiting for "thanks"); it listens again only when the DJ asked them something back
/// ("Do you mean your hearted songs, or the ones you play most?"), until it has its answer, a press, or nothing said. An answer that starts music
/// (play, next, resume) or pauses it leaves the music as chosen; otherwise the song paused at the start carries on. Pure; the phone has the same rules in Kotlin (playback/DjConversation.kt), keep the two the same.
/// </summary>
public static partial class DjConversation
{
    /// <summary>Turns at most, so a mic left lit in a noisy room stops by itself.</summary>
    public const int MaxTurns = 8;

    [GeneratedRegex(@"^(ok(ay)?[, ]+)?(that'?s (all|it|everything)|thanks?( you)?( dj)?|thank you( very much| so much)?|cheers|no,? thanks?( you)?|bye( bye)?|good ?bye|see (you|ya)|never ?mind|nothing( else)?|i'?m (good|done|fine)|all good|stop listening|that will be all|we'?re done|done)[.!]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GoodbyeRx();

    /// <summary>The person ended the conversation ("that's all", "thanks", "bye", "never mind"...): nothing is asked of the DJ.</summary>
    public static bool IsGoodbye(string? text) => text is not null && GoodbyeRx().IsMatch(text.Trim().Replace('’', '\''));

    /// <summary>After the DJ answered [a] on turn [turn] (1 = the first): listen again only when it [asked] the person something back, else end.</summary>
    public static DjTurn After(DjAskDo a, bool asked, int turn) => a switch
    {
        DjAskDo.Play or DjAskDo.Next or DjAskDo.Resume or DjAskDo.Pause => DjTurn.EndMusicChosen,
        _ => asked && turn < MaxTurns ? DjTurn.ListenAgain : DjTurn.End,
    };

    /// <summary>At the end: the song paused by the mic carries on, unless the conversation chose what the music does.</summary>
    public static bool ResumeAtEnd(bool pausedByMic, DjTurn how) => pausedByMic && how != DjTurn.EndMusicChosen;
}
