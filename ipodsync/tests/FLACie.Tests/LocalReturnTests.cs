using FLACie.Server;
using Xunit;

namespace FLACie.Tests;

/// <summary>After signing in, a person goes back to the page that asked (a shared link), never to another site.</summary>
public class LocalReturnTests
{
    [Theory]
    [InlineData("/s/abcDEF2345", "/s/abcDEF2345")]
    [InlineData("/album/x?y=1", "/album/x?y=1")]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("//evil.example/x", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("https://evil.example", "/")]
    [InlineData("/login?ReturnUrl=/x", "/")]
    [InlineData("/redirect?to=https://evil.example", "/")]
    public void Only_pages_on_this_site(string? asked, string expected) => Assert.Equal(expected, AuthEndpoints.LocalReturn(asked));
}
