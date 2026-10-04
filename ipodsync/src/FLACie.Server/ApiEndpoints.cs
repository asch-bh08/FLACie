using FLACie.Core;

namespace FLACie.Server;

/// <summary>
/// A small API for the FLACie phone app, so the phone can hand over a playlist file and check on it without doing any of the downloading. The
/// caller proves who it is with its Jellyfin token (<c>X-Emby-Token</c>) and says which Jellyfin it is signed in to (<c>X-Jellyfin-Server</c>); the
/// server asks that Jellyfin who the token belongs to. Only a Jellyfin this server already works with is accepted.
/// </summary>
public static class ApiEndpoints
{
    public static void MapApi(this WebApplication app)
    {
        var fixedServer = app.Configuration["FLACIE_JELLYFIN_URL"];

        async Task<UserSession?> Who(HttpContext ctx, SessionStore store, JellyfinClient jf)
        {
            var token = ctx.Request.Headers["X-Emby-Token"].FirstOrDefault() ?? ctx.Request.Headers.Authorization.ToString().Replace("Bearer ", "");
            var server = ctx.Request.Headers["X-Jellyfin-Server"].FirstOrDefault() ?? fixedServer;
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(server)) return null;
            server = JellyfinClient.Normalise(server);
            var known = fixedServer is not null ? JellyfinClient.Normalise(fixedServer) == server : store.Active.Any(s => s.Jellyfin?.Server == server);
            if (!known) return null;
            try
            {
                var me = await jf.GetAsync(new JellyfinAccount(server, "", "", token), "/Users/Me");
                var id = me?["Id"]?.GetValue<string>();
                if (id is null) return null;
                return store.For(SessionStore.Principal(new JellyfinAccount(server, id, me?["Name"]?.GetValue<string>() ?? "", token), null));
            }
            catch (Exception) { return null; }
        }

        object Summary(ImportJob j) => new
        {
            id = j.Id, file = j.File, state = j.State, total = j.Total, done = j.Finished, failed = j.Failed, note = j.Note, created = j.Created,
            lists = j.Lists.Select(l => new { name = l.Name, total = l.Items.Count, done = l.Items.Count(i => i.State != "pending") }),
        };

        app.MapGet("/api/ping", () => Results.Ok(new { name = "FLACie Web", import = true }));

        app.MapPost("/api/import", async (HttpContext ctx, string? name, SessionStore store, JellyfinClient jf, ImportManager imports) =>
        {
            if (await Who(ctx, store, jf) is not { } s) return Results.Unauthorized();
            if (ctx.Request.ContentLength is > 20 * 1024 * 1024) return Results.BadRequest(new { error = "That file is too big (limit 20 MB)." });
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            try { return Results.Ok(Summary(imports.Start(s, string.IsNullOrWhiteSpace(name) ? "playlist.txt" : Path.GetFileName(name), ms.ToArray()))); }
            catch (InvalidDataException e) { return Results.BadRequest(new { error = e.Message }); }
        }).DisableAntiforgery();

        app.MapGet("/api/import", async (HttpContext ctx, SessionStore store, JellyfinClient jf, UserStateStore states) =>
        {
            if (await Who(ctx, store, jf) is not { } s) return Results.Unauthorized();
            return Results.Ok(states.For(s).Imports.Select(Summary));
        });

        app.MapPost("/api/import/{id}/cancel", async (HttpContext ctx, string id, SessionStore store, JellyfinClient jf, ImportManager imports) =>
        {
            if (await Who(ctx, store, jf) is not { } s) return Results.Unauthorized();
            imports.Cancel(s, id); return Results.Ok();
        }).DisableAntiforgery();
    }
}
