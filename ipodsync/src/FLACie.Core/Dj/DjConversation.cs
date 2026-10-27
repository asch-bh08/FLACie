using System.Text.RegularExpressions;

namespace FLACie.Core;

/// <summary>What happens after one turn of "talk to the DJ": listen again, or end the conversation (and whether the paused song carries on).</summary>
public enum DjTurn { ListenAgain, End, EndMusicChosen }

/// <summary>
/// Conversation mode for "talk to the DJ" (owner, 27 Oct): a press of the mic pauses the music (the mic heard the song otherwise) and starts a
/// conversation; after each answer the mic listens again once the DJ has stopped talking, until "that's all" (or thanks, bye, never mind), a press,
/// or nothing said. An answer that starts music (play, next, resume) or pauses it ends it too, so the music asked for is not paused again; otherwise
/// the song paused at the start carries on at the end. Pure; the phone has the same rules in Kotlin (playback/DjConversation.kt), keep the two the same.
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

    /// <summary>After the DJ answered [a] on turn [turn] (1 = the first): listen again, or end.</summary>
    public static DjTurn After(DjAskDo a, int turn) => a switch
    {
        DjAskDo.Play or DjAskDo.Next or DjAskDo.Resume or DjAskDo.Pause => DjTurn.EndMusicChosen,
        _ => turn >= MaxTurns ? DjTurn.End : DjTurn.ListenAgain,
    };

    /// <summary>At the end: the song paused by the mic carries on, unless the conversation chose what the music does.</summary>
    public static bool ResumeAtEnd(bool pausedByMic, DjTurn how) => pausedByMic && how != DjTurn.EndMusicChosen;
}
