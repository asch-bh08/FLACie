using FLACie.Core;

namespace FLACie.Server;

/// <summary>
/// A small API for the FLACie phone app, so the phone can hand over a playlist file and check on it without doing any of the downloading. The
/// caller proves who it is with its Jellyfin token (<c>X-Emby-Token</c>) and says which Jellyfin it is signed in to (<c>X-Jellyfin-Server</c>); the
/// server asks that Jellyfin who the token belongs to. Only a Jellyfin this server already works with is accepted.
/// </summary>
public static class ApiEndpoints
{
    /// <summary>The phone app's caller, from its Jellyfin token (as every /api call checks it); set when the API is mapped. Other endpoints that a
    /// browser and the phone both call (the seek bar's shape, sharing) use it when there is no signed-in browser.</summary>
    public static Func<HttpContext, SessionStore, JellyfinClient, Task<UserSession?>>? PhoneSession { get; private set; }

    /// <summary>Whoever is calling: the signed-in browser, else the phone by its token; null for neither.</summary>
    public static async Task<UserSession?> CallerAsync(HttpContext ctx, SessionStore store, JellyfinClient jf) =>
        ctx.User.Identity?.IsAuthenticated == true ? store.For(ctx.User) : PhoneSession is { } who ? await who(ctx, store, jf) : null;

    public static void MapApi(this WebApplication app)
    {
        var fixedServer = app.Configuration["FLACIE_JELLYFIN_URL"];
        var whoFlights = new SingleFlight<TokenCache.Who?>();

        var limits = app.Services.GetRequiredService<RateLimits>();

        async Task<UserSession?> Who(HttpContext ctx, SessionStore store, JellyfinClient jf)
        {
            var token = ctx.Request.Headers["X-Emby-Token"].FirstOrDefault() ?? ctx.Request.Headers.Authorization.ToString().Replace("Bearer ", "");
            // with a fixed Jellyfin the token is checked there, whatever address the phone uses for the same server (LAN, Tailscale, public)
            var server = fixedServer ?? ctx.Request.Headers["X-Jellyfin-Server"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(server)) return null;
            // the token ends up inside an Authorization header sent to Jellyfin: a Jellyfin token is hex, so anything with quotes, commas or spaces is not one (and must not add parameters to that header)
            if (token.Length > 256 || token.Any(c => c is '"' or ',' or ' ' or '\\' or ';' || char.IsControl(c))) return null;
            server = JellyfinClient.Normalise(server);
            var known = fixedServer is not null || store.Active.Any(s => s.Jellyfin?.Server == server);
            if (!known) return null;
            // a burst of calls with one token asks Jellyfin once: the answer is kept for TokenCache.Window (sign-out and any 401 drop it at once)
            var tokens = ctx.RequestServices.GetRequiredService<TokenCache>();
            try
            {
                var known2 = tokens.Get(server, token);
                // calls arriving together while the answer is not known yet share one question to Jellyfin
                known2 ??= await whoFlights.RunAsync(server + "|" + token, async () =>
                {
                    // each token nobody has checked in the last 45 s costs one call to Jellyfin: an address that sends made-up tokens is stopped here
                    // ("token-check"), so it cannot turn this server into a way of hammering Jellyfin. A real phone is checked about once a minute.
                    if (!limits.TryTake("token-check", RateKey.Of(ctx.Connection.RemoteIpAddress), out var wait)) throw new RateLimitedException(wait);
                    var me = await jf.GetAsync(new JellyfinAccount(server, "", "", token), "/Users/Me");
                    var id = me?["Id"]?.GetValue<string>();
                    if (id is null) return null;
                    var who = new TokenCache.Who(id, me?["Name"]?.GetValue<string>() ?? "");
                    tokens.Put(server, token, who);
                    return who;
                });
                if (known2 is null) return null;
                return store.For(SessionStore.Principal(new JellyfinAccount(server, known2.UserId, known2.Name, token), null));
            }
            catch (UnauthorizedAccessException) { tokens.Invalidate(token); return null; }
            catch (RateLimitedException) { throw; }   // answered 429 with Retry-After (RateLimiting), not "who are you"
            catch (Exception) { return null; }
        }

        PhoneSession = Who;

        object Summary(ImportJob j) => new
        {
            id = j.Id, file = j.File, state = j.State, total = j.Total, done = j.Finished, failed = j.Failed, note = j.Note, created = j.Created,
            lists = j.Lists.Select(l => new { name = l.Name, total = l.Items.Count, done = l.Items.Count(i => i.State != "pending") }),
        };

        app.MapGet("/api/ping", () => Results.Ok(new { name = "FLACie Web", import = true })).RateLimited("public");

        app.MapPost("/api/import", async (HttpContext ctx, string? name, bool? replace, SessionStore store, JellyfinClient jf, ImportManager imports) =>
        {
            // the server reads at most 2 MB of any other request (Program.cs). A file here may be up to 20 MB, and one a little over that still gets its
            // "too big" answer: Kestrel can only let the phone finish sending an unread body that is within the limit, and otherwise drops the connection,
            // which the phone would show as "can't reach the server"
            if (ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } allow) allow.MaxRequestBodySize = 30 * 1024 * 1024;
            if (await Who(ctx, store, jf) is not { } s) return Results.Unauthorized();
            // administrators, or everyone when the Dashboard lets them, and only while the server downloads (DownloadPolicy.MayBulk; ImportManager checks it too)
            if (!ctx.RequestServices.GetRequiredService<DownloadPolicy>().MayBulk(s)) return Results.Json(new { error = new ImportNotAllowedException().Message }, statusCode: 403);
            if (ctx.Request.ContentLength is > 20 * 1024 * 1024) return Results.BadRequest(new { error = "That file is too big (limit 20 MB)." });
            // a body sent in chunks has no length to check up front: the server stops reading at the limit
            if (ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = 20 * 1024 * 1024;
            using var ms = new MemoryStream();
            try { await ctx.Request.Body.CopyToAsync(ms); }
            catch (Microsoft.AspNetCore.Http.BadHttpRequestException) { return Results.Json(new { error = "That file is too big (limit 20 MB)." }, statusCode: 413); }
            try { return Results.Ok(Summary(imports.Start(s, string.IsNullOrWhiteSpace(name) ? "playlist.txt" : Path.GetFileName(name), ms.ToArray(), replace == true))); }
            catch (InvalidDataException e) { return Results.BadRequest(new { error = e.Message }); }
        }).DisableAntiforgery();

        // POST { "url": "<Spotify or Deezer playlist/album link>", "replace": false } -> the job (as POST /api/import), or { error } with the words the Import page shows
        app.MapPost("/api/import/link", async (HttpContext ctx, SessionStore store, JellyfinClient jf, ImportManager imports, PlaylistLinkReader reader) =>
        {
            if (await Who(ctx, store, jf) is not { } s) return Results.Unauthorized();
            if (!ctx.RequestServices.GetRequiredService<DownloadPolicy>().MayBulk(s)) return Results.Json(new { error = new ImportNotAllowedException().Message }, statusCode: 403);
            const int MaxBody = 16 * 1024;
            if (ctx.Request.ContentLength is > MaxBody) return Results.Json(new { error = "That request is too big." }, statusCode: 413);
            if (ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = MaxBody;
            System.Text.Json.Nodes.JsonObject? body;
            try { body = await System.Text.Json.Nodes.JsonNode.ParseAsync(ctx.Request.Body) as System.Text.Json.Nodes.JsonObject; }
            catch (System.Text.Json.JsonException) { body = null; }
            catch (Microsoft.AspNetCore.Http.BadHttpRequestException) { return Results.Json(new { error = "That request is too big." }, statusCode: 413); }
            if (body is null) return Results.BadRequest(new { error = "Send a JSON object." });
            var url = body["url"] is System.Text.Json.Nodes.JsonValue uv && uv.TryGetValue<string>(out var u) ? u : null;
            var replace = body["replace"] is System.Text.Json.Nodes.JsonValue rv && rv.TryGetValue<bool>(out var r) && r;
            try { return Results.Ok(Summary(await imports.StartLinkAsync(s, reader, url, replace, ctx.RequestAborted))); }
            catch (Exception e) when (e is not OperationCanceledException) { var (status, msg) = LinkImport.Describe(e); return Results.Json(new { error = msg }, statusCode: status); }
        }).DisableAntiforgery();

        // a device's playback problems (stalls, errors, songs that never started, downloads that failed, what the DJ mic heard), so a bad drive can be read
        // afterwards on the server (owner, 2 Nov)
        app.MapPost("/api/diag", async (HttpContext ctx, DiagBody body, SessionStore store, JellyfinClient jf, DiagLogStore diag) =>
            await Who(ctx, store, jf) is { } s ? Results.Ok(new { kept = diag.Add(s.Id, body.Events ?? []) }) : Results.Unauthorized()).DisableAntiforgery();

        // what the person listened to and liked on a device, for their recommendations; the first call also starts the log from what the server already knows
        app.MapPost("/api/listen", async (HttpContext ctx, ListenBody body, SessionStore store, JellyfinClient jf, ListenLogStore log) =>
        {
            var s = await Who(ctx, store, jf);
            if (s is not null && !log.Exists(s)) await s.RefreshPlayStatsAsync(jf, TimeSpan.FromHours(1));   // Jellyfin's play counts may not have been read yet
            return ListenApi.Accept(log, s, (body.Events ?? []).Take(2000).ToList(), () => ListenSeed.From(s!.History, s.Favorites, s.PlayStats, s.Library));
        }).DisableAntiforgery();

        app.MapGet("/api/import", async (HttpContext ctx, SessionStore store, JellyfinClient jf, UserStateStore states) =>
        {
            if (await Who(ctx, store, jf) is not { } s) return Results.Unauthorized();
            return Results.Ok(states.For(s).Imports.Select(Summary));
        });

        // the server-side settings and storage the phone shows next to its own (the same numbers as the web Settings page)
        object SettingsOf(UserSession s, UserStateStore states, StorageService storage)
        {
            var st = states.For(s); var r = storage.Quick(s); var policy = app.Services.GetRequiredService<DownloadPolicy>();
            return new
            {
                admin = AdminAccess.Is(s, app.Configuration), prefetch = st.Prefetch, minFreeGb = st.MinFreeGb,
                // whether this server downloads at all, and whether this person may import playlists and use Download all: the phone hides what the server won't do
                downloads = policy.Allowed, bulk = policy.MayBulk(s),
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
            // a body that is not a JSON object, or a field of the wrong type, is the caller's mistake (400), not a server error
            System.Text.Json.Nodes.JsonObject? body;
            try { body = await System.Text.Json.Nodes.JsonNode.ParseAsync(ctx.Request.Body) as System.Text.Json.Nodes.JsonObject; }
            catch (System.Text.Json.JsonException) { body = null; }
            if (body is null) return Results.BadRequest(new { error = "Send a JSON object." });
            bool? Bool(string k) => body[k] is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;
            int? Int(string k) => body[k] is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<int>(out var i) ? i : null;
            if ((body["prefetch"] is not null && Bool("prefetch") is null) || (body["chartsEnabled"] is not null && Bool("chartsEnabled") is null)
                || (body["minFreeGb"] is not null && Int("minFreeGb") is null) || (body["perList"] is not null && Int("perList") is null)
                || (body["lists"] is not null && (body["lists"] is not System.Text.Json.Nodes.JsonArray chk || chk.Any(x => x is not System.Text.Json.Nodes.JsonValue jv || !jv.TryGetValue<int>(out _)))))
                return Results.BadRequest(new { error = "A setting has the wrong type." });
            var st = states.For(s);
            if (Bool("prefetch") is { } pf) st.Prefetch = pf;
            if (Int("minFreeGb") is { } mf) st.MinFreeGb = Math.Clamp(mf, 0, 2000);
            if (Bool("chartsEnabled") is { } ce) st.Charts.Enabled = ce;
            if (Int("perList") is { } pl) st.Charts.PerList = Math.Clamp(pl, 1, 50);
            if (body["lists"] is System.Text.Json.Nodes.JsonArray la)
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

        // GET -> { rev }: a stamp of this account's playlists, cheap enough for the phone to ask every ten seconds; it fetches them only when the stamp changed (owner, 5 Nov)
        app.MapGet("/api/playlists/rev", async (HttpContext ctx, SessionStore store, JellyfinClient jf) =>
        {
            if (await Who(ctx, store, jf) is not { } s) return Results.Unauthorized();
            ctx.Response.Headers.CacheControl = "no-store";
            return Results.Json(new { rev = s.PlaylistsRevision });
        });

        // "music played" from the phone for songs that do not go through Jellyfin (the NAS, the file mover): the server cannot see those streams, so the app says
        // when one starts, that it is still playing (about every ten seconds) and when it stops. Jellyfin songs are never reported: Jellyfin announces them itself.
        app.MapPost("/api/played", async (HttpContext ctx, SessionStore store, JellyfinClient jf, PlayTracker plays, Notifier notify, PlayedReport r) =>
        {
            if (await Who(ctx, store, jf) is not { } s) return Results.Unauthorized();
            if (r.Via is not ("NAS" or "direct" or "Jellyfin") || string.IsNullOrEmpty(r.Key) || r.Key.Length > 600 || r.Event is not ("start" or "progress" or "stop")) return Results.BadRequest();
            if (r.Via == "Jellyfin" && !notify.Current.JellyfinPlays) return Results.NoContent();   // the Jellyfin watcher announces those
            var key = "phone:" + r.Key;
            var device = string.IsNullOrWhiteSpace(r.Device) ? "phone" : r.Device.Trim()[..Math.Min(r.Device.Trim().Length, 40)];
            string Cut(string? x, int n) => (x ?? "").Trim() is var y && y.Length > n ? y[..n] : y;
            plays.Served(s.Id, key, r.Via);
            switch (r.Event)
            {
                case "start": plays.Begin(s.Id, s.DisplayName, key, Cut(r.Title, 120), Cut(r.Artist, 120), device); plays.Tick(s.Id, key, true, 15); break;
                case "progress": plays.Tick(s.Id, key, true, 15); break;
                case "stop": plays.Tick(s.Id, key, false, 15); break;
                default: return Results.BadRequest();
            }
            return Results.NoContent();
        }).DisableAntiforgery();

        // "I downloaded this on the phone": the phone does its own downloads (its slskd and file mover), so the server never saw them. Telling it makes the song show as
        // recently downloaded here, playable as a "new download" before Jellyfin has scanned it, and not offered (or fetched by Autoplay) again.
        // { artist, title, album?, durationMs?, path?, source?, finishedAtMs? } -> { ok, known }; known is true when the song is playable here now.
        app.MapPost("/api/downloaded", async (HttpContext ctx, SessionStore store, JellyfinClient jf, DownloadManager downloads) =>
        {
            if (await Who(ctx, store, jf) is not { } s) return Results.Unauthorized();
            const int MaxBody = 16 * 1024;
            if (ctx.Request.ContentLength is > MaxBody) return Results.Json(new { error = "That report is too big." }, statusCode: 413);
            if (ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = MaxBody;
            System.Text.Json.Nodes.JsonObject? body;
            try { body = await System.Text.Json.Nodes.JsonNode.ParseAsync(ctx.Request.Body) as System.Text.Json.Nodes.JsonObject; }
            catch (System.Text.Json.JsonException) { body = null; }
            catch (Microsoft.AspNetCore.Http.BadHttpRequestException) { return Results.Json(new { error = "That report is too big." }, statusCode: 413); }
            if (body is null) return Results.BadRequest(new { error = "Send a JSON object." });
            string? Str(string k, int max) => body[k] is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<string>(out var x) && x.Trim() is { Length: > 0 } t ? (t.Length > max ? t[..max] : t) : null;
            var artist = Str("artist", 200); var title = Str("title", 200);
            if (artist is null || title is null) return Results.BadRequest(new { error = "Say which song: artist and title." });
            var album = Str("album", 200) ?? "";
            long Num(string k) => body[k] is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<long>(out var n) ? n : 0;
            var durationMs = Num("durationMs") is > 0 and <= 86_400_000 and var d ? d : 0;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (!DownloadManager.TakeReportTurn(s.Id, now)) return Results.Json(new { error = "Too many reports; try again later." }, statusCode: 429);
            // a client's clock must not backdate or future-date a download: at most ten minutes back, never ahead
            var finished = DownloadTags.ClampReported(Num("finishedAtMs"), now);
            var source = Str("source", 20)?.ToLowerInvariant() is { } src && DownloadReports.Sources.Contains(src) ? src : null;
            // the path is trusted only when it is a file of this user's own file mover (never fetched from anywhere else)
            var path = Str("path", 2000) is { } p && DownloadReports.AcceptPath(s.Services.FileMoverUrl, p) ? p : null;
            Track? track = path is null ? null
                : new Track(path, title, artist, album, artist, 0, 0, durationMs, 0, Art.LookupKey(artist, album, title), finished, TrackSource.Cloud);
            downloads.Reported(s, artist, title, album, durationMs, source, finished, path is null ? null : DownloadReports.RelativeFile(path), track);
            var known = track is not null || s.Library.Find(title, artist) is not null || s.Library.FindLoose(title, artist) is not null;
            return Results.Json(new { ok = true, known });
        }).DisableAntiforgery();

        // smart downloads, the phone's side (spec section 4): the phone hands a download to FLACie Web, which runs the race and the upgrades for it, and follows
        // it by id. The same rules as the web's Download button: signed in, downloads allowed on this server, the person's
        // download services set up (the phone proves who it is with its Jellyfin token, as every /api call here does); the "api" rate limit, and a song already on its way is shared, not fetched twice.
        // POST { artist, title, album?, durationMs?, mode: "quick" | "hires" } -> { id }
        app.MapPost("/api/download", async (HttpContext ctx, SessionStore store, JellyfinClient jf, DownloadManager downloads) =>
        {
            var s = await Who(ctx, store, jf);
            if (s is null) return Results.Unauthorized();
            if (!downloads.Allowed) return Results.Json(new { error = DownloadManager.OffMessage }, statusCode: 403);
            if (!s.Services.Any) return Results.Json(new { error = "Downloads aren't set up on FLACie Web for this account (Settings > Downloads)." }, statusCode: 409);
            const int MaxBody = 16 * 1024;
            if (ctx.Request.ContentLength is > MaxBody) return Results.Json(new { error = "That request is too big." }, statusCode: 413);
            if (ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = MaxBody;
            System.Text.Json.Nodes.JsonObject? body;
            try { body = await System.Text.Json.Nodes.JsonNode.ParseAsync(ctx.Request.Body) as System.Text.Json.Nodes.JsonObject; }
            catch (System.Text.Json.JsonException) { body = null; }
            catch (Microsoft.AspNetCore.Http.BadHttpRequestException) { return Results.Json(new { error = "That request is too big." }, statusCode: 413); }
            if (body is null) return Results.BadRequest(new { error = "Send a JSON object." });
            string? Str(string k, int max) => body[k] is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<string>(out var x) && x.Trim() is { Length: > 0 } t ? (t.Length > max ? t[..max] : t) : null;
            var artist = Str("artist", 200); var title = Str("title", 200);
            if (artist is null || title is null) return Results.BadRequest(new { error = "Say which song: artist and title." });
            var durationMs = body["durationMs"] is System.Text.Json.Nodes.JsonValue dv && dv.TryGetValue<long>(out var d) && d is > 0 and <= 86_400_000 ? d : 0;
            DownloadMode? mode = body["mode"] is null ? DownloadMode.Quick : Str("mode", 10)?.ToLowerInvariant() switch { "quick" => DownloadMode.Quick, "hires" => DownloadMode.HiRes, _ => null };
            if (mode is null) return Results.BadRequest(new { error = "mode is \"quick\" or \"hires\"." });
            // asked for by someone (quick) or fetched ahead for the phone's queue (hires): the job's kind says which on the Downloads page
            var job = downloads.Start(s, mode == DownloadMode.HiRes ? "Autoplay" : "Search", artist, title, Str("album", 200) ?? "", durationMs, mode.Value);
            return job is null ? Results.Json(new { error = DownloadManager.OffMessage }, statusCode: 403) : Results.Json(new { id = job.Id });
        }).DisableAntiforgery();

        // GET -> { stage, message, source, done, failed, path, format, hiRes }: stage is requested / searching / downloading / importing / scanning / done / failed;
        // source is one of the usual ids (soulseek, ytdl, lidarr, archive, audius, jamendo); path is the file once done, relative to the file mover's root with
        // the NAS folder in front (as the phone files its own downloads and the download log keeps them; null for Lidarr, which files it itself); format is the
        // file's extension. Only the caller's own jobs: anyone else's id is 404, as an unknown one is.
        app.MapGet("/api/download/{id}", async (HttpContext ctx, string id, SessionStore store, JellyfinClient jf, DownloadManager downloads) =>
        {
            var s = await Who(ctx, store, jf);
            if (s is null) return Results.Unauthorized();
            if (id.Length is 0 or > 64) return Results.NotFound();
            if (downloads.JobsOf(s).FirstOrDefault(j => j.Id == id) is not { } job) return Results.NotFound();
            var path = job.Stage == DownloadStage.Done ? job.File : null;
            var format = job.File is { } f && Path.GetExtension(f) is { Length: > 1 } ext ? ext[1..].ToLowerInvariant() : null;
            return Results.Json(new
            {
                stage = job.Stage.ToString().ToLowerInvariant(), message = job.Message, source = job.Source ?? job.Current, done = job.Stage == DownloadStage.Done,
                failed = job.Stage == DownloadStage.Failed, path, format, hiRes = job.HiRes,
            });
        });

        // the real format of every song the server has read (ffprobe / Jellyfin, never guessed from the extension), keyed like the apps key a file
        // (the last three path segments): the phone uses it for the Hi-Res badge and the Explore quality filter. [codec, sampleRate, bitDepth, kbps]
        // an Apple Lossless song as WAV, for players (the phone app) that have no ALAC decoder: this server decodes it itself, with the caller's own Jellyfin sign-in
        app.MapGet("/api/alac", async (HttpContext ctx, string id, SessionStore store, JellyfinClient jf, IHttpClientFactory hf, AlacCache alac) =>
        {
            if (await Who(ctx, store, jf) is not { } s) return Results.Unauthorized();
            if (id.Length != 32 || !id.All(Uri.IsHexDigit)) return Results.BadRequest();
            var t = new Track("alac:" + id, "", "", "", "", 0, 0, 0, 0, null, 0, TrackSource.Jellyfin, "x.m4a", id);
            return await AlacServe.TryServeAsync(ctx, s, t, jf, hf, alac) ? Results.Empty : Results.StatusCode(422);
        }).DisableAntiforgery().RateLimited("stream");

        app.MapGet("/api/audiofacts", async (HttpContext ctx, SessionStore store, JellyfinClient jf, FormatIndex index, FormatProbeService probe) =>
        {
            if (await Who(ctx, store, jf) is not { } s) return Results.Unauthorized();
            var facts = new Dictionary<string, object[]>();
            foreach (var t in s.Library.Songs)
                if (index.Get(t) is { } f && Matching.FileKey(t) is { } k) facts[k] = [f.Codec, f.Rate, f.Depth, f.Kbps];
            return Results.Json(new { pending = probe.Pending, facts });
        });

        // the Downloads page on the phone: what is running now and the log of everything the server fetched for this account (newest first)
        app.MapGet("/api/downloads", async (HttpContext ctx, SessionStore store, JellyfinClient jf, DownloadManager downloads, DownloadLog log, int? limit) =>
        {
            if (await Who(ctx, store, jf) is not { } s) return Results.Unauthorized();
            object Step(TrailStep t) => new { at = DateTime.SpecifyKind(t.At, DateTimeKind.Utc), source = t.Source, text = t.Text, miss = t.Miss };
            var running = downloads.JobsOf(s).Where(j => !j.Finished).Select(j => new
            {
                id = j.Id, label = j.Label, kind = j.Kind, startedAt = DateTime.SpecifyKind(j.At, DateTimeKind.Utc), message = j.Message, current = j.Current, artKey = j.ArtKey,
            });
            var done = log.For(s.Id).Take(Math.Clamp(limit ?? 300, 1, 1000)).Select(r => new
            {
                id = r.Id, label = r.Label, kind = r.Kind, startedAt = DateTime.SpecifyKind(r.StartedAt, DateTimeKind.Utc), finishedAt = DateTime.SpecifyKind(r.FinishedAt, DateTimeKind.Utc),
                outcome = r.Outcome, source = r.Source, message = r.Message, file = r.File, artKey = r.ArtKey, trail = r.Trail.Select(Step),
            });
            return Results.Json(new { running, log = done });
        });

        app.MapPost("/api/import/{id}/cancel", async (HttpContext ctx, string id, SessionStore store, JellyfinClient jf, ImportManager imports) =>
        {
            if (await Who(ctx, store, jf) is not { } s) return Results.Unauthorized();
            imports.Cancel(s, id); return Results.Ok();
        }).DisableAntiforgery();
    }
}

/// <summary>Rules for what the phone says in POST /api/downloaded.</summary>
public static class DownloadReports
{
    public static readonly HashSet<string> Sources = ["soulseek", "lidarr", "ytdl", "archive", "audius", "jamendo"];

    /// <summary>
    /// A reported path is accepted only when it is exactly this user's file mover address followed by <c>/file?path=</c> and a relative file path: no other host, no
    /// other endpoint, no extra query parameters, no way out of the music folder (absolute paths, "..", control characters).
    /// </summary>
    public static bool AcceptPath(string fileMoverUrl, string path)
    {
        var fm = fileMoverUrl.Trim().TrimEnd('/');
        if (fm.Length == 0 || path.Length > 2000) return false;
        var prefix = fm + "/file?path=";
        if (!path.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var rel = path[prefix.Length..];
        if (rel.Length == 0 || rel.Any(c => c is '&' or '#' or '?' or ' ' || char.IsControl(c))) return false;
        string decoded;
        try { decoded = Uri.UnescapeDataString(rel); } catch (Exception) { return false; }
        if (decoded.Length == 0 || decoded.Any(char.IsControl)) return false;
        if (decoded[0] is '/' or '\\' || (decoded.Length > 1 && decoded[1] == ':')) return false;
        return !decoded.Replace('\\', '/').Split('/').Any(seg => seg == "..");
    }

    /// <summary>The file's path relative to the file mover's root (what the download log keeps), from an accepted path.</summary>
    public static string? RelativeFile(string path)
    {
        var i = path.IndexOf("/file?path=", StringComparison.Ordinal);
        if (i < 0) return null;
        try { return Uri.UnescapeDataString(path[(i + "/file?path=".Length)..]); } catch (Exception) { return null; }
    }
}

/// <summary>What the phone says about a song that does not go through Jellyfin (see /api/played).</summary>
public sealed record PlayedReport(string Event, string Key, string? Title, string? Artist, string Via, string? Device);

/// <summary>The body of POST /api/listen.</summary>
public sealed record ListenBody(List<ListenEvent>? Events);
