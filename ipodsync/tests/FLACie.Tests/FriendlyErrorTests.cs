using System.Net;
using System.Net.Sockets;
using FLACie.Core;
using Xunit;

namespace FLACie.Tests;

/// <summary>What a person is told when something fails: what happened in plain words, never an exception's own text (a stack trace, a socket error code).</summary>
public class FriendlyErrorTests
{
    [Fact]
    public void A_server_that_cannot_be_reached_says_so() =>
        Assert.Equal("Can't reach the server. Check the address and that it's switched on.", FriendlyError.Of(new HttpRequestException("No such host is known. (jf.example:8096)", new SocketException(11001))));

    [Fact]
    public void A_refused_sign_in_says_so() =>
        Assert.Equal("The server refused the sign-in. Check the user name and password.", FriendlyError.Of(new HttpRequestException("401", null, HttpStatusCode.Unauthorized)));

    [Fact]
    public void A_server_error_says_the_server_had_a_problem() =>
        Assert.Equal("The server had a problem answering. Try again in a moment.", FriendlyError.Of(new HttpRequestException("500", null, HttpStatusCode.InternalServerError)));

    [Fact]
    public void A_timeout_says_it_took_too_long() =>
        Assert.Equal("The server took too long to answer. Try again in a moment.", FriendlyError.Of(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout")));

    [Fact]
    public void Our_own_explanations_are_kept() =>
        Assert.Equal("That file isn't a playlist FLACie can read.", FriendlyError.Of(new InvalidDataException("That file isn't a playlist FLACie can read.")));

    [Fact]
    public void Anything_else_gets_a_plain_line_with_no_raw_text() =>
        Assert.Equal("Something went wrong. Try again.", FriendlyError.Of(new NullReferenceException("Object reference not set to an instance of an object.")));

    [Fact]
    public void A_prefix_names_what_failed() =>
        Assert.Equal("Couldn't reach Jamendo: the server took too long to answer. Try again in a moment.", FriendlyError.Of(new TimeoutException(), "Couldn't reach Jamendo"));
}
