using System.Net;
using System.Net.Sockets;

namespace FLACie.Core;

/// <summary>
/// What a person is told when something fails: what happened, in plain words, and what to try. An exception's own text (a socket error, a stack of
/// "Object reference not set…") is never shown; only the explanations FLACie writes itself (<see cref="InvalidDataException"/>) pass through.
/// </summary>
public static class FriendlyError
{
    public static string Of(Exception e, string? what = null)
    {
        var line = Line(e);
        return what is null ? line : what + ": " + char.ToLowerInvariant(line[0]) + line[1..];
    }

    static string Line(Exception e) => e switch
    {
        InvalidDataException d when d.Message.Length > 0 => d.Message,
        TaskCanceledException or TimeoutException or OperationCanceledException => "The server took too long to answer. Try again in a moment.",
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } => "The server refused the sign-in. Check the user name and password.",
        HttpRequestException { StatusCode: HttpStatusCode.NotFound } => "The server doesn't have that. It may have been moved or removed.",
        HttpRequestException { StatusCode: >= HttpStatusCode.InternalServerError } => "The server had a problem answering. Try again in a moment.",
        HttpRequestException or SocketException or IOException => "Can't reach the server. Check the address and that it's switched on.",
        _ when e.InnerException is { } inner && inner is not null && Line(inner) is var l && l != Generic => l,
        _ => Generic,
    };

    const string Generic = "Something went wrong. Try again.";
}
