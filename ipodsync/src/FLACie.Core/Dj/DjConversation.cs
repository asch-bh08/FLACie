using System.Text.RegularExpressions;

namespace FLACie.Core;

/// <summary>What happens after one turn of "talk to the DJ": listen again, or end the conversation (and whether the paused song carries on).</summary>
public enum DjTurn { ListenAgain, End, EndMusicChosen }

/// <summary>
/// "Talk to the DJ": a press of the mic pauses the music (the mic heard the song otherwise) and listens, and from there it takes turns like a person would
/// (owner, 31 Oct). A plain control the DJ carried out (skip, pause, volume, shuffle, play something) ends it at once: nothing more is wanted. A normal
/// answer ("It's Firestarter.") leaves a short window, about four seconds, in which the mic listens again for a follow-up, and ends quietly if nobody speaks.
/// When the DJ asks something back ("Do you mean your hearted songs, or the ones you play most?") the window is longer, about eight seconds, as the person
/// has to think. "Thanks", "bye", "that's all" or "never mind" end it with a quick "Anytime." and the music comes back up. About 1.2 seconds of quiet after
/// speech ends the person's turn. An answer that starts music (play, next, resume) or pauses it leaves the music as chosen; otherwise the song paused at the
/// start carries on. Pure; the phone has the same rules in Kotlin (playback/DjConversation.kt), keep the two the same.
/// </summary>
public static partial class DjConversation
{
    /// <summary>Turns at most, so a mic left lit in a noisy room stops by itself.</summary>
    public const int MaxTurns = 8;

    /// <summary>After a normal answer: how long the mic waits for the person to start speaking again before it lets go quietly.</summary>
    public const int FollowUpMs = 4000;
    /// <summary>After the DJ asked something back: longer, as the person has to think about it.</summary>
    public const int AnswerWindowMs = 8000;
    /// <summary>Quiet this long after the person's speech means they have finished their turn (not a pause for breath).</summary>
    public const int EndOfSpeechMs = 1200;
    /// <summary>What the DJ says to "thanks" and its kind. A fixed line, made ahead in the voice (DjAskEngine.FixedReplies), so it is heard at once.</summary>
    public const string Anytime = "Anytime.";

    [GeneratedRegex(@"^(ok(ay)?[, ]+)?(that'?s (all|it|everything)|thanks?( you)?( dj)?|thank you( very much| so much)?|cheers|no,? thanks?( you)?|bye( bye)?|good ?bye|see (you|ya)|never ?mind|nothing( else)?|i'?m (good|done|fine)|all good|stop listening|that will be all|we'?re done|done)[.!]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GoodbyeRx();

    /// <summary>The person ended the conversation ("that's all", "thanks", "bye", "never mind"...): nothing is asked of the DJ.</summary>
    public static bool IsGoodbye(string? text) => text is not null && GoodbyeRx().IsMatch(text.Trim().Replace('’', '\''));

    /// <summary>How long the mic waits for the person to begin speaking on the next turn: longer when the DJ [asked] something back.</summary>
    public static int ListenWindowMs(bool asked) => asked ? AnswerWindowMs : FollowUpMs;

    /// <summary>
    /// After the DJ answered [a] on turn [turn] (1 = the first). [intent] is the server's name for what it understood ("Clarify", "Goodbye", "ThumbsDown"...).
    /// A control carried out ends it; a goodbye ends it; anything else (an answer, a question back, a line that didn't catch it) listens again until the
    /// last turn. How long it waits is <see cref="ListenWindowMs"/>.
    /// </summary>
    public static DjTurn After(DjAskDo a, bool asked, int turn, string? intent = null) => a switch
    {
        DjAskDo.Play or DjAskDo.Next or DjAskDo.Resume or DjAskDo.Pause => DjTurn.EndMusicChosen,
        DjAskDo.None when intent is not ("Goodbye" or "ThumbsDown") => turn < MaxTurns ? DjTurn.ListenAgain : DjTurn.End,
        _ => DjTurn.End,
    };

    /// <summary>At the end: the song paused by the mic carries on, unless the conversation chose what the music does.</summary>
    public static bool ResumeAtEnd(bool pausedByMic, DjTurn how) => pausedByMic && how != DjTurn.EndMusicChosen;
}
