using FLACie.Core;
using FLACie.Server.Dj;

namespace FLACie.Server.Alexa;

/// <summary>The DJ behind the skill: free-form words go to the same request reader as "talk to the DJ", and Alexa's plays go into the account's listening log.</summary>
public interface IAlexaBrain
{
    /// <summary>The DJ's answer to the words, for the linked person; null when the DJ can't be asked for them.</summary>
    Task<DjAskAnswer?> AskAsync(IAlexaUser user, string text, string? current, IReadOnlyList<string> recent, CancellationToken ct);
    /// <summary>A song played on Alexa: [heard] and [length] in seconds.</summary>
    void Listened(IAlexaUser user, Track t, double heard, double length);
}

/// <summary>The server's brain: the person's <see cref="DjAskEngine"/> context and learned requests, never the dashboard voice (Alexa speaks for itself).</summary>
public sealed class AlexaAskBrain(DjAskEngine engine, Func<IAlexaUser, IDjAskUser?> userOf, Action<IAlexaUser, ListenEvent> append) : IAlexaBrain
{
    public const string Surface = "alexa";

    public async Task<DjAskAnswer?> AskAsync(IAlexaUser user, string text, string? current, IReadOnlyList<string> recent, CancellationToken ct) =>
        userOf(user) is { } u ? await engine.AnswerAsync(u, text, current, recent, ct) : null;

    public void Listened(IAlexaUser user, Track t, double heard, double length)
    {
        try { append(user, ListenEvent.Of(ListenKind.Play, t, heard, length, surface: Surface)); } catch (Exception) { }
    }

    /// <summary>The server's: the person behind a link is a <see cref="SessionAlexaUser"/>; their DJ user and log are the in-app ones.</summary>
    public static AlexaAskBrain For(DjAskEngine engine, DjAskService ask, ListenLogStore log) => new(engine,
        u => u is SessionAlexaUser s ? ask.UserOf(s.Session) : null,
        (u, e) => { if (u is SessionAlexaUser s) log.Append(s.Session, [e]); });
}
