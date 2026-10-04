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
            // with a fixed Jellyfin the token is checked there, whatever address the phone uses for the same server (LAN, Tailscale, public)
            var server = fixedServer ?? ctx.Request.Headers["X-Jellyfin-Server"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(server)) return null;
            server = JellyfinClient.Normalise(server);
            var known = fixedServer is not null || store.Active.Any(s => s.Jellyfin?.Server == server);
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

        app.MapPost("/api/import", async (HttpContext ctx, string? name, bool? replace, SessionStore store, JellyfinClient jf, ImportManager imports) =>
        {
            if (await Who(ctx, store, jf) is not { } s) return Results.Unauthorized();
            if (ctx.Request.ContentLength is > 20 * 1024 * 1024) return Results.BadRequest(new { error = "That file is too big (limit 20 MB)." });
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            try { return Results.Ok(Summary(imports.Start(s, string.IsNullOrWhiteSpace(name) ? "playlist.txt" : Path.GetFileName(name), ms.ToArray(), replace == true))); }
            catch (InvalidDataException e) { return Results.BadRequest(new { error = e.Message }); }
        }).DisableAntiforgery();

        app.MapGet("/api/import", async (HttpContext ctx, SessionStore store, JellyfinClient jf, UserStateStore states) =>
        {
            if (await Who(ctx, store, jf) is not { } s) return Results.Unauthorized();
            return Results.Ok(states.For(s).Imports.Select(Summary));
        });

        // the server-side settings and storage the phone shows next to its own (the same numbers as the web Settings page)
        object SettingsOf(UserSession s, UserStateStore states, StorageService storage)
        {
            var st = states.For(s); var r = storage.Quick(s);
            return new
            {
                admin = AdminAccess.Is(s, app.Configuration), prefetch = st.Prefetch, minFreeGb = st.MinFreeGb,
                charts = new { enabled = st.Charts.Enabled, lists = st.Charts.Lists, perList = st.Charts.PerList, lastRun = st.Charts.LastRun, lastNote = st.Charts.LastNote },
                available = ChartsService.Available.Select(c => new { id = c.Id, name = c.Name }),
                storage = new { musicFree = r.Music?.Free ?? -1, musicTotal = r.Music?.Total ?? -1, nasSongs = r.NasSongs, nasBytes = r.NasBytes, diskFree = r.DiskFree, diskTotal = r.DiskTotal },
            };
        }

        app.MapGet("/api/settings", async (HttpContext ctx, SessionStore store, JellyfinClient jf, UserStateStore states, StorageService storage) =>
            await Who(ctx, store, jf) is not { } s ? Results.Unauthorized() : Results.Json(SettingsOf(s, states, storage)));

        app.MapPost("/api/settings", async (HttpContext ctx, SessionStore store, JellyfinClient jf, UserStateStore states, StorageService storage) =>
        {
            if (await Who(ctx, store, jf) is not { } s) return Results.Unauthorized();
            if (!AdminAccess.Is(s, app.Configuration)) return Results.StatusCode(403);
            var body = await System.Text.Json.Nodes.JsonNode.ParseAsync(ctx.Request.Body) as System.Text.Json.Nodes.JsonObject;
            var st = states.For(s);
            if (body?["prefetch"]?.GetValue<bool>() is { } pf) st.Prefetch = pf;
            if (body?["minFreeGb"]?.GetValue<int>() is { } mf) st.MinFreeGb = Math.Clamp(mf, 0, 2000);
            if (body?["chartsEnabled"]?.GetValue<bool>() is { } ce) st.Charts.Enabled = ce;
            if (body?["perList"]?.GetValue<int>() is { } pl) st.Charts.PerList = Math.Clamp(pl, 1, 50);
            if (body?["lists"] is System.Text.Json.Nodes.JsonArray la)
                st.Charts.Lists = la.Select(x => x!.GetValue<int>()).Where(id => ChartsService.Available.Any(c => c.Id == id)).Distinct().ToList();
            states.Save(s);
            return Results.Json(SettingsOf(s, states, storage));
        }).DisableAntiforgery();

        app.MapPost("/api/charts/run", async (HttpContext ctx, SessionStore store, JellyfinClient jf, ChartsService charts) =>
        {
            if (await Who(ctx, store, jf) is not { } s) return Results.Unauthorized();
            if (!AdminAccess.Is(s, app.Configuration)) return Results.StatusCode(403);
            _ = Task.Run(async () => { try { await charts.RunAsync(s, true, CancellationToken.None); } catch (Exception) { } });
            return Results.Ok();
        }).DisableAntiforgery();

        app.MapGet("/api/charts/{id:int}", async (HttpContext ctx, int id, SessionStore store, JellyfinClient jf, ChartsService charts) =>
        {
            if (await Who(ctx, store, jf) is null) return Results.Unauthorized();
            var songs = await charts.ChartAsync(id, ctx.RequestAborted);
            return Results.Json(songs.Select((s, i) => new { rank = i + 1, title = s.Title, artist = s.Artist, art = s.ArtUrl }));
        });

        app.MapGet("/api/account", async (HttpContext ctx, SessionStore store, JellyfinClient jf) =>
        {
            if (await Who(ctx, store, jf) is not { } s) return Results.Unauthorized();
            return Results.Json(new { songs = s.Library.Songs.Count, albums = s.Library.Albums.Count, artists = s.Library.Artists.Count, playlists = s.Playlists.Count, favourites = s.Favorites.Count, admin = AdminAccess.Is(s, app.Configuration) });
        });

        app.MapPost("/api/import/{id}/cancel", async (HttpContext ctx, string id, SessionStore store, JellyfinClient jf, ImportManager imports) =>
        {
            if (await Who(ctx, store, jf) is not { } s) return Results.Unauthorized();
            imports.Cancel(s, id); return Results.Ok();
        }).DisableAntiforgery();
    }
}
