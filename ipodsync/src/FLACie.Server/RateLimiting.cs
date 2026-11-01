using System.Globalization;
using System.Text.Encodings.Web;
using FLACie.Core;

namespace FLACie.Server;

/// <summary>Which named limit (<see cref="RateLimits"/>) an endpoint falls under; set with <see cref="RateLimiting.RateLimited{TBuilder}"/>.</summary>
public sealed record RateLimitMetadata(string Policy);

/// <summary>A limit reached inside a handler (a phone token that would cost one more Jellyfin call, say): the request is answered 429 like any other.</summary>
public sealed class RateLimitedException(TimeSpan retryAfter) : Exception("Too many requests")
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}

/// <summary>
/// The rate limits at the doors (docs/SELF-HOSTING.md, Rate limits). Runs after sign-in is read and before anything is done for the request. The limit is
/// the endpoint's own (sign-in, Quick Connect, covers, streams, the DJ, Alexa ...), else every other /api call falls under "api" and a share page under
/// "share-page"; pages, static files and Blazor's live connection are never limited. Doors anyone can reach count the client's address (the one
/// UseForwardedHeaders settled on: a trusted proxy's X-Forwarded-For, never a header a client sent by itself); the others count the signed-in account,
/// else the phone's device token, else the address. A refused request gets 429 with Retry-After and a message: JSON for the apps and the sign-in page's
/// script, a short page with a way back for a browser that navigated.
/// </summary>
public static class RateLimiting
{
    /// <summary>Limits counted by address whoever is calling: the doors outsiders knock on.</summary>
    static readonly HashSet<string> ByAddress = ["signin", "qc-poll", "public", "token-check", "alexa"];

    public static TBuilder RateLimited<TBuilder>(this TBuilder builder, string policy) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new RateLimitMetadata(policy));

    public static IApplicationBuilder UseRateLimits(this IApplicationBuilder app)
    {
        var limits = app.ApplicationServices.GetRequiredService<RateLimits>();
        return app.Use((ctx, next) => InvokeAsync(ctx, c => next(c), limits));
    }

    /// <summary>The limit this request falls under, or null.</summary>
    public static string? PolicyOf(HttpContext ctx)
    {
        if (ctx.GetEndpoint()?.Metadata.GetMetadata<RateLimitMetadata>() is { } m) return m.Policy;
        var p = ctx.Request.Path;
        if (p.StartsWithSegments("/api")) return "api";
        if (p.StartsWithSegments("/s")) return "share-page";
        return null;
    }

    /// <summary>Who this request is counted against under [policy].</summary>
    public static RateKey KeyOf(HttpContext ctx, string policy)
    {
        if (!ByAddress.Contains(policy))
        {
            if (AccountOf(ctx) is { } account) return RateKey.Of(account);
            if (DeviceToken(ctx) is { Length: > 0 } token) return RateKey.OfToken(token);
        }
        return RateKey.Of(ctx.Connection.RemoteIpAddress);
    }

    /// <summary>The signed-in account (its Jellyfin user id, or the NAS sign-in), or null.</summary>
    public static string? AccountOf(HttpContext ctx)
    {
        if (ctx.User.Identity?.IsAuthenticated != true) return null;
        if (ctx.User.FindFirst("jf.user")?.Value is { Length: > 0 } id) return id;
        return ctx.User.FindFirst("nas.host")?.Value is { Length: > 0 } host ? "nas|" + host + "|" + ctx.User.FindFirst("nas.user")?.Value : null;
    }

    static string? DeviceToken(HttpContext ctx)
    {
        var h = ctx.Request.Headers;
        if (h["X-Emby-Token"].FirstOrDefault() is { Length: > 0 } t) return t;
        var auth = h.Authorization.ToString();
        return auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? auth[7..] : null;
    }

    public static async Task InvokeAsync(HttpContext ctx, RequestDelegate next, RateLimits limits)
    {
        if (PolicyOf(ctx) is { } policy && !limits.TryTake(policy, KeyOf(ctx, policy), out var wait))
        {
            await RefuseAsync(ctx, wait, policy);
            return;
        }
        try { await next(ctx); }
        catch (RateLimitedException e) when (!ctx.Response.HasStarted) { await RefuseAsync(ctx, e.RetryAfter, null); }
    }

    /// <summary>"40 seconds", "a minute" ...: how long, the way the message says it.</summary>
    public static string Say(int seconds) => seconds switch { <= 1 => "a second", < 60 => $"{seconds} seconds", 60 => "a minute", _ => $"{(seconds + 59) / 60} minutes" };

    /// <summary>The 429 itself: Retry-After in whole seconds (at least 1), and a message in the form the caller can show.</summary>
    public static Task RefuseAsync(HttpContext ctx, TimeSpan wait, string? policy)
    {
        var secs = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds));
        var res = ctx.Response;
        res.StatusCode = StatusCodes.Status429TooManyRequests;
        res.Headers.RetryAfter = secs.ToString(CultureInfo.InvariantCulture);
        res.Headers.CacheControl = "no-store";
        var what = policy == "signin" ? "Too many sign-in attempts from here just now." : "Too many requests just now.";
        var msg = $"{what} Try again in {Say(secs)}.";
        var p = ctx.Request.Path;
        var json = p.StartsWithSegments("/api") || p.StartsWithSegments("/auth/qc") || p.StartsWithSegments("/alexa")
                   || ctx.Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
        if (json) return res.WriteAsJsonAsync(new { error = msg });
        if (ctx.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase))
        {
            res.ContentType = "text/html; charset=utf-8";
            var back = p.StartsWithSegments("/auth") ? "/login" : "/";
            return res.WriteAsync($"""
                <!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Too many requests · FLACie</title>
                <body style="font:16px/1.5 system-ui,sans-serif;max-width:520px;margin:15vh auto;padding:0 16px;background:#0b0b0e;color:#eee">
                <h1 style="font-size:1.3em">Slow down a little</h1><p>{HtmlEncoder.Default.Encode(msg)}</p><p><a href="{back}" style="color:#9cf">Back to {(back == "/login" ? "sign-in" : "FLACie")}</a></p>
                </body></html>
                """);
        }
        res.ContentType = "text/plain; charset=utf-8";
        return res.WriteAsync(msg);
    }
}
