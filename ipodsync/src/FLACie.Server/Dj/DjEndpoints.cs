using System.Text.Json.Nodes;
using FLACie.Core;

namespace FLACie.Server.Dj;

/// <summary>
/// The DJ's doors (docs/spec/ai-dj.md): settings per person, "what before the next song" for the phone, the clips, and the admin's test. The caller is the signed-in
/// browser or the phone by its Jellyfin token (<see cref="ApiEndpoints.CallerAsync"/>). Clips are served here, never under /stream, and are never counted as plays.
/// </summary>
public static class DjEndpoints
{
    const int MaxBody = 64 * 1024;

    public static void AddDj(this IServiceCollection services)
    {
        services.AddSingleton<CloudflareVoice>();
        services.AddSingleton<DjClips>();
        services.AddSingleton<DjService>();
        services.AddSingleton<IDjService>(sp => sp.GetRequiredService<DjService>());
    }

    /// <summary>The body as a JSON object; null with a 400 or 413 answer when it is not one.</summary>
    static async Task<(JsonObject? Body, IResult? Error)> ReadAsync(HttpContext ctx)
    {
        if (ctx.Request.ContentLength is > MaxBody) return (null, Results.Json(new { error = "That request is too big." }, statusCode: 413));
        if (ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = MaxBody;
        try { if (await JsonNode.ParseAsync(ctx.Request.Body, cancellationToken: ctx.RequestAborted) is JsonObject o) return (o, null); }
        catch (System.Text.Json.JsonException) { }
        catch (Microsoft.AspNetCore.Http.BadHttpRequestException) { return (null, Results.Json(new { error = "That request is too big." }, statusCode: 413)); }
        return (null, Results.BadRequest(new { error = "Send a JSON object." }));
    }

    public static void MapDj(this WebApplication app)
    {
        app.MapGet("/api/dj/settings", async (HttpContext ctx, SessionStore store, JellyfinClient jf, DjService dj) =>
            await ApiEndpoints.CallerAsync(ctx, store, jf) is not { } s ? Results.Unauthorized() : Results.Text(DjJson.Settings(dj.Settings(s), dj.VoiceReady).ToJsonString(), "application/json"));

        // any signed-in person, for their own settings only
        app.MapPost("/api/dj/settings", async (HttpContext ctx, SessionStore store, JellyfinClient jf, DjService dj) =>
        {
            if (await ApiEndpoints.CallerAsync(ctx, store, jf) is not { } s) return Results.Unauthorized();
            var (body, bad) = await ReadAsync(ctx);
            if (body is null) return bad!;
            var st = dj.Settings(s);
            if (DjJson.Apply(body, st) is { } error) return Results.BadRequest(new { error });
            dj.Save(s, st);
            return Results.Text(DjJson.Settings(dj.Settings(s), dj.VoiceReady).ToJsonString(), "application/json");
        }).DisableAntiforgery();

        app.MapPost("/api/dj/next", async (HttpContext ctx, SessionStore store, JellyfinClient jf, DjService dj) =>
        {
            if (await ApiEndpoints.CallerAsync(ctx, store, jf) is not { } s) return Results.Unauthorized();
            var (body, bad) = await ReadAsync(ctx);
            if (body is null) return bad!;
            if (DjJson.Request(body, out var req) is { } error) return Results.BadRequest(new { error });
            var cue = await dj.PrepareAsync(s, req!, ctx.RequestAborted);
            return Results.Text(DjJson.Cue(cue).ToJsonString(), "application/json");
        }).DisableAntiforgery();

        // the clip: waits up to 10 s while it is being made; 404 when it failed, is unknown or was pruned
        app.MapGet("/api/dj/clip/{id}.mp3", async (HttpContext ctx, string id, SessionStore store, JellyfinClient jf, DjClips clips) =>
        {
            if (await ApiEndpoints.CallerAsync(ctx, store, jf) is null) return Results.Unauthorized();
            if (!DjClips.IsId(id)) return Results.NotFound();
            if (await clips.WaitAsync(id, TimeSpan.FromSeconds(10), ctx.RequestAborted) is not { } path) return Results.NotFound();
            ctx.Response.Headers.CacheControl = "private, max-age=86400";   // an id is a hash of the voice and the words: the clip never changes
            return Results.File(path, "audio/mpeg", enableRangeProcessing: true);
        });

        // admin only: says the test line with the saved Cloudflare settings
        app.MapPost("/api/dj/test", async (HttpContext ctx, SessionStore store, JellyfinClient jf, IConfiguration conf, DjService dj) =>
        {
            if (await ApiEndpoints.CallerAsync(ctx, store, jf) is not { } s) return Results.Unauthorized();
            if (!AdminAccess.Is(s, conf)) return Results.StatusCode(403);
            var (clip, msg) = await dj.TestAsync(ctx.RequestAborted);
            return clip is not null ? Results.Json(new { clip }) : Results.Json(new { error = msg }, statusCode: 502);
        }).DisableAntiforgery();
    }
}
