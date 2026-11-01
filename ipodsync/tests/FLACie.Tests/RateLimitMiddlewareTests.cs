using System.Net;
using System.Security.Claims;
using FLACie.Core;
using FLACie.Server;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace FLACie.Tests;

/// <summary>
/// The rate limits at the server's doors: which limit a request falls under (its endpoint's, else /api and /s/ by path), who it is counted against (the
/// address for doors anyone can reach; the account, the device's token or the address for the rest), and what a refused caller gets: 429, Retry-After,
/// and a message it can show (JSON for the apps and scripts, a page with a way back for a browser).
/// </summary>
public class RateLimitMiddlewareTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    static RateLimits Limits(params (string Name, string Value)[] s) => new(n => s.FirstOrDefault(x => x.Name == n).Value, () => Now);

    static DefaultHttpContext Request(string path, string? policy = null, string ip = "198.51.100.7", string? account = null, string? token = null, string accept = "*/*")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Request.Headers.Accept = accept;
        ctx.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        ctx.Response.Body = new MemoryStream();
        if (policy is not null) ctx.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new RateLimitMetadata(policy)), path));
        if (account is not null) ctx.User = SessionStore.Principal(new JellyfinAccount("http://jf", account, "name", "tok-" + account), null);
        if (token is not null) ctx.Request.Headers["X-Emby-Token"] = token;
        return ctx;
    }

    static async Task<int> Run(RateLimits limits, DefaultHttpContext ctx, RequestDelegate? next = null)
    {
        await RateLimiting.InvokeAsync(ctx, next ?? (c => { c.Response.StatusCode = 200; return Task.CompletedTask; }), limits);
        return ctx.Response.StatusCode;
    }

    static string Body(HttpContext ctx) { ctx.Response.Body.Position = 0; return new StreamReader(ctx.Response.Body).ReadToEnd(); }

    [Fact]
    public async Task Sign_in_is_counted_by_address_and_refused_with_429_and_Retry_After()
    {
        var limits = Limits(("FLACIE_RATE_SIGNIN", "3"));
        for (var i = 0; i < 3; i++) Assert.Equal(200, await Run(limits, Request("/auth/jellyfin", "signin")));
        var refused = Request("/auth/jellyfin", "signin", accept: "text/html");
        Assert.Equal(429, await Run(limits, refused));
        Assert.Equal("60", refused.Response.Headers.RetryAfter.ToString());
        Assert.Contains("text/html", refused.Response.ContentType);
        var page = Body(refused);
        Assert.Contains("sign-in", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"/login\"", page);
        // another address is not held up by this one
        Assert.Equal(200, await Run(limits, Request("/auth/jellyfin", "signin", ip: "198.51.100.8")));
    }

    [Fact]
    public async Task A_client_sent_X_Forwarded_For_is_never_believed_here()
    {
        // the address comes from the connection (UseForwardedHeaders has already replaced it when, and only when, a trusted proxy sent the request)
        var limits = Limits(("FLACIE_RATE_SIGNIN", "2"));
        for (var i = 0; i < 2; i++) { var c = Request("/auth/jellyfin", "signin", ip: "203.0.113.9"); c.Request.Headers["X-Forwarded-For"] = "10.0.0." + i; Assert.Equal(200, await Run(limits, c)); }
        var last = Request("/auth/jellyfin", "signin", ip: "203.0.113.9"); last.Request.Headers["X-Forwarded-For"] = "10.0.0.99";
        Assert.Equal(429, await Run(limits, last));
    }

    [Fact]
    public async Task A_refused_app_call_gets_its_message_as_JSON()
    {
        var limits = Limits(("FLACIE_RATE_QC_POLL", "1"));
        Assert.Equal(200, await Run(limits, Request("/auth/qc/poll", "qc-poll")));
        var c = Request("/auth/qc/poll", "qc-poll", accept: "*/*");
        Assert.Equal(429, await Run(limits, c));
        Assert.Contains("application/json", c.Response.ContentType);
        Assert.Matches("\"error\":\"Too many requests just now. Try again in (a minute|\\d+ seconds)\\.\"", Body(c));
    }

    [Fact]
    public async Task A_signed_in_account_is_one_bucket_whatever_address_it_comes_from()
    {
        var limits = Limits(("FLACIE_RATE_ART", "2"));
        Assert.Equal(200, await Run(limits, Request("/art/x", "art", ip: "198.51.100.1", account: "u1")));
        Assert.Equal(200, await Run(limits, Request("/art/y", "art", ip: "198.51.100.2", account: "u1")));
        Assert.Equal(429, await Run(limits, Request("/art/z", "art", ip: "198.51.100.3", account: "u1")));
        Assert.Equal(200, await Run(limits, Request("/art/z", "art", ip: "198.51.100.3", account: "u2")));
    }

    [Fact]
    public async Task The_phone_app_is_counted_by_its_device_token()
    {
        var limits = Limits(("FLACIE_RATE_WAVE", "1"));
        Assert.Equal(200, await Run(limits, Request("/api/wave", "wave", token: "aaaa")));
        Assert.Equal(429, await Run(limits, Request("/api/wave", "wave", token: "aaaa")));
        Assert.Equal(200, await Run(limits, Request("/api/wave", "wave", token: "bbbb")));
    }

    [Fact]
    public async Task Any_other_api_call_falls_under_the_api_limit_and_share_pages_under_theirs()
    {
        var limits = Limits(("FLACIE_RATE_API", "1"), ("FLACIE_RATE_SHARE_PAGE", "1"));
        Assert.Equal(200, await Run(limits, Request("/api/settings", account: "u1")));
        Assert.Equal(429, await Run(limits, Request("/api/account", account: "u1")));
        Assert.Equal(200, await Run(limits, Request("/s/abcdefghjk", account: "u1")));
        Assert.Equal(429, await Run(limits, Request("/s/zzzzzzzzzz", account: "u1")));
    }

    [Fact]
    public async Task Pages_static_files_and_the_live_connection_are_never_limited()
    {
        var limits = Limits(("FLACIE_RATE_API", "1"), ("FLACIE_RATE_PUBLIC", "1"));
        foreach (var p in new[] { "/", "/songs", "/login", "/app.css", "/_blazor", "/_framework/blazor.web.js" })
            for (var i = 0; i < 5; i++) Assert.Equal(200, await Run(limits, Request(p)));
    }

    [Fact]
    public async Task A_limit_reached_deeper_in_turns_into_the_same_answer()
    {
        var c = Request("/api/listen", accept: "application/json");
        Assert.Equal(429, await Run(Limits(), c, _ => throw new RateLimitedException(TimeSpan.FromSeconds(12.2))));
        Assert.Equal("13", c.Response.Headers.RetryAfter.ToString());
        Assert.Contains("13 seconds", Body(c));
    }

    [Fact]
    public async Task Switched_off_limits_let_everything_through()
    {
        var limits = Limits(("FLACIE_RATE_LIMITS", "off"));
        for (var i = 0; i < 50; i++) Assert.Equal(200, await Run(limits, Request("/auth/jellyfin", "signin")));
    }
}
