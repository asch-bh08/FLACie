using FLACie.Server;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace FLACie.Tests;

/// <summary>
/// The headers on every answer: no search engine indexes a self-hosted server, no other site can frame it, nothing is sniffed into a script, and a
/// Content-Security-Policy that keeps what the pages need working (Blazor's inline start-up script and its WebSocket, the Retro WebAssembly, covers from
/// the catalogs and data:/blob: pictures, audio from the server itself) while shutting out plugins, foreign base URLs and forms posting elsewhere.
/// </summary>
public class SecurityHeadersTests
{
    static IHeaderDictionary Apply(bool https = false)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = https ? "https" : "http";
        SecurityHeaders.Apply(ctx);
        return ctx.Response.Headers;
    }

    static string Directive(string csp, string name) => csp.Split(';', StringSplitOptions.TrimEntries).FirstOrDefault(d => d.StartsWith(name + " ")) ?? "";

    [Fact]
    public void Every_answer_says_do_not_index_frame_or_sniff()
    {
        var h = Apply();
        Assert.Equal("noindex, nofollow", h["X-Robots-Tag"]);
        Assert.Equal("nosniff", h["X-Content-Type-Options"]);
        Assert.Equal("SAMEORIGIN", h["X-Frame-Options"]);
        Assert.Equal("same-origin", h["Referrer-Policy"]);
        Assert.False(h.ContainsKey("Strict-Transport-Security"));
        Assert.StartsWith("max-age=", Apply(https: true)["Strict-Transport-Security"].ToString());
    }

    [Fact]
    public void The_content_security_policy_keeps_the_pages_working()
    {
        var csp = Apply()["Content-Security-Policy"].ToString();
        Assert.Contains("'self'", Directive(csp, "default-src"));
        var script = Directive(csp, "script-src");
        Assert.Contains("'unsafe-inline'", script);       // Blazor.start and the theme script in App.razor are inline
        Assert.Contains("'wasm-unsafe-eval'", script);    // the Retro WebAssembly
        Assert.DoesNotContain("'unsafe-eval'", script.Replace("'wasm-unsafe-eval'", ""));
        Assert.Contains("wss:", Directive(csp, "connect-src"));   // Blazor's live connection
        foreach (var s in new[] { "data:", "blob:", "https:" }) Assert.Contains(s, Directive(csp, "img-src"));   // catalog covers, generated pictures
        Assert.Contains("'self'", Directive(csp, "media-src"));
        Assert.Contains("blob:", Directive(csp, "media-src"));
        Assert.Equal("frame-ancestors 'self'", Directive(csp, "frame-ancestors"));
        Assert.Equal("object-src 'none'", Directive(csp, "object-src"));
        Assert.Equal("base-uri 'self'", Directive(csp, "base-uri"));
        Assert.Equal("form-action 'self'", Directive(csp, "form-action"));
    }

    [Fact]
    public void The_microphone_is_allowed_for_the_page_itself_so_talking_to_the_DJ_works()
    {
        // the web player's mic button uses the browser's speech recognition, which needs the microphone; camera and location stay off
        var p = Apply()["Permissions-Policy"].ToString();
        Assert.Contains("microphone=(self)", p);
        Assert.Contains("camera=()", p);
        Assert.Contains("geolocation=()", p);
    }

    [Fact]
    public void Robots_are_told_to_keep_out() => Assert.Equal("User-agent: *\nDisallow: /\n", SecurityHeaders.RobotsTxt);
}
