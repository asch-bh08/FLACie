using System.Text.Json.Nodes;
using FLACie.Core;

namespace FLACie.Server.Alexa;

/// <summary>The two public doors of the Alexa feature: the skill endpoint (every request verified) and the signed stream links. Both answer 404 while Alexa is off.</summary>
public static class AlexaEndpoints
{
    public static void AddAlexa(this IServiceCollection services, string dataDir)
    {
        services.AddSingleton<AlexaStore>();
        services.AddSingleton<AlexaQueues>();
        services.AddSingleton(sp => new AlexaEngine(sp.GetRequiredService<AlexaQueues>(), () => sp.GetRequiredService<AlexaStore>().Config));
        services.AddSingleton<AlexaPublic>();
        services.AddSingleton(new AlexaUrlSigner(AlexaUrlSigner.LoadOrCreateSecret(Path.Combine(dataDir, "alexa-url.key")), () => DateTime.UtcNow));
        services.AddSingleton<AlexaProactive>();
        services.AddSingleton<IProgressive, HttpProgressive>();
        services.AddSingleton(sp => new SessionAlexaDirectory(sp.GetRequiredService<AlexaStore>(), sp.GetRequiredService<SessionStore>(), sp.GetRequiredService<JellyfinClient>(), sp.GetRequiredService<DownloadManager>(),
            sp.GetRequiredService<WebCatalog>(), new Lazy<AlexaEngine>(() => sp.GetRequiredService<AlexaEngine>()), sp.GetRequiredService<AlexaProactive>()));
        services.AddSingleton<IAlexaDirectory>(sp => sp.GetRequiredService<SessionAlexaDirectory>());
        services.AddSingleton(sp => new AlexaSkill(sp.GetRequiredService<AlexaEngine>(), sp.GetRequiredService<AlexaStore>(), sp.GetRequiredService<IAlexaDirectory>(), sp.GetRequiredService<AlexaUrlSigner>(),
            () => sp.GetRequiredService<AlexaPublic>().Base, sp.GetRequiredService<IProgressive>(), sp.GetRequiredService<ILoggerFactory>().CreateLogger("Alexa")));
        services.AddSingleton<AlexaGuards>();
    }

    public static void MapAlexa(this WebApplication app)
    {
        var log = app.Logger;

        app.MapPost("/alexa/skill", async (HttpContext ctx, AlexaStore store, AlexaGuards guards, AlexaPublic pub, AlexaSkill skill) =>
        {
            if (!store.Config.Enabled) return Results.NotFound();
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "?";
            if (!guards.PerAddress.TryTake(ip)) return Results.StatusCode(429);
            // read at most 1 MB
            if (ctx.Request.ContentLength is > AlexaRequestVerifier.MaxBodyBytes) return Results.StatusCode(413);
            using var ms = new MemoryStream();
            var buf = new byte[8192]; int n;
            while ((n = await ctx.Request.Body.ReadAsync(buf, ctx.RequestAborted)) > 0) { ms.Write(buf, 0, n); if (ms.Length > AlexaRequestVerifier.MaxBodyBytes) return Results.StatusCode(413); }
            var body = ms.ToArray();
            // the skill ids are read fresh: they can be changed in Settings without a restart
            var verifier = guards.VerifierFor(store.Config.SkillIds, ctx.RequestServices);
            var v = await verifier.VerifyAsync(ctx.Request.Headers["SignatureCertChainUrl"].ToString(), ctx.Request.Headers["Signature-256"].ToString(), ctx.Request.Headers["Signature"].ToString(), body, ctx.RequestAborted);
            if (!v.Ok) { log.LogWarning("Alexa request refused: {Reason}", v.Reason); return Results.StatusCode(400); }
            var userId = (string?)v.Request!["context"]?["System"]?["user"]?["userId"] ?? (string?)v.Request["session"]?["user"]?["userId"] ?? "";
            if (userId.Length > 0 && !guards.PerAccount.TryTake(AlexaStore.HashUser(userId))) return Results.StatusCode(429);
            pub.Learn(ctx.Request);
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted); limit.CancelAfter(TimeSpan.FromSeconds(7));
            try { return Results.Json(await skill.HandleAsync(v.Request, limit.Token)); }
            catch (Exception e)
            {
                log.LogWarning("Alexa request failed: {Kind}", e.GetType().Name);
                return Results.Json(new JsonObject { ["version"] = "1.0", ["response"] = new JsonObject { ["outputSpeech"] = new JsonObject { ["type"] = "PlainText", ["text"] = "Sorry, something went wrong." }, ["shouldEndSession"] = true } });
            }
        });

        // a signed-in person asks for a pairing code (Settings does the same in process)
        app.MapPost("/api/alexa/code", (HttpContext ctx, AlexaStore store, SessionStore sessions) =>
        {
            if (!store.Config.Enabled) return Results.NotFound();
            if (sessions.For(ctx.User).Jellyfin is not { } account) return Results.BadRequest();
            var c = store.NewCode(account);
            return Results.Json(new { code = c.Code, expires = c.Expires });
        }).RequireAuthorization();

        // signed links only: t = a song (an MP3 for the speaker), a = its cover
        app.MapGet("/alexa/stream/{kind}/{file}", async (HttpContext ctx, string kind, string file, string? u, long? e, string? s, AlexaStore store, AlexaUrlSigner signer, SessionAlexaDirectory dir, JellyfinClient jf, IHttpClientFactory hf, PlaybackActivity activity) =>
        {
            if (!store.Config.Enabled || kind is not ("t" or "a") || u is null || e is null || s is null) return Results.NotFound();
            var dot = file.LastIndexOf('.'); if (dot < 1) return Results.NotFound();
            var id = file[..dot]; var ext = file[(dot + 1)..];
            if (id.Length > 80 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') || ext is not ("mp3" or "jpg")) return Results.NotFound();
            switch (signer.Verify(kind, id, ext, u, e.Value, s))
            {
                case SignedUrlResult.BadSignature: return Results.StatusCode(403);
                case SignedUrlResult.Expired: return Results.StatusCode(410);
            }
            if (store.LinkIdByUrlHash(u) is not { } linkId) return Results.NotFound();
            var session = dir.SessionOf(linkId, out _);
            if (session?.Jellyfin is not { } a) return Results.NotFound();
            ctx.Response.Headers.CacheControl = "private, max-age=3600";
            var http = hf.CreateClient("media");

            async Task<IResult> Proxy(HttpRequestMessage req, string type)
            {
                if (ctx.Request.Headers.Range.Count > 0) req.Headers.TryAddWithoutValidation("Range", ctx.Request.Headers.Range.ToString());
                try
                {
                    using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ctx.RequestAborted);
                    ctx.Response.StatusCode = (int)res.StatusCode;
                    ctx.Response.ContentType = type;
                    foreach (var h in new[] { "Content-Length", "Content-Range", "Accept-Ranges" })
                        if (res.Content.Headers.TryGetValues(h, out var v) || res.Headers.TryGetValues(h, out v)) ctx.Response.Headers[h] = v.ToArray();
                    await using var body = await res.Content.ReadAsStreamAsync(ctx.RequestAborted);
                    await body.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);
                }
                catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested) { }
                catch (JellyfinUnavailableException ex) when (!ctx.Response.HasStarted) { ctx.Response.StatusCode = 503; ctx.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(ex.Wait.TotalSeconds)).ToString(); }
                catch (HttpRequestException) when (!ctx.Response.HasStarted) { ctx.Response.StatusCode = 502; }
                return Results.Empty;
            }

            if (kind == "a")
            {
                var parts = id.Split('-'); var tag = parts.Length > 1 ? parts[1] : null;
                using var prio = JellyfinGate.Use(GatePriority.Visible);
                using var req = jf.Authorized(a, jf.ImageUrl(a, parts[0], 600, tag, webp: false));
                return await Proxy(req, "image/jpeg");
            }

            var track = session.Library.ByJellyfinId(id);
            if (track?.JellyfinId is null) return Results.NotFound();
            using var busy = activity.BeginStream();   // background jobs wait while a song is being served
            using var playing = JellyfinGate.Use(GatePriority.Playing);
            // an MP3 is sent as it is; anything else (FLAC, ALAC, Ogg ...) is turned into 256 kbps stereo MP3 by Jellyfin, which the speakers play (they do not play FLAC)
            var direct = string.Equals(Path.GetExtension(track.FilePath ?? ""), ".mp3", StringComparison.OrdinalIgnoreCase);
            using var sreq = jf.Authorized(a, direct ? jf.StreamUrl(a, track.JellyfinId) : $"{a.Server}/Audio/{track.JellyfinId}/stream.mp3?AudioCodec=mp3&AudioBitRate=256000&MaxAudioChannels=2");
            return await Proxy(sreq, "audio/mpeg");
        });
    }
}

/// <summary>The rate limiters and the verifier (rebuilt when the skill ids change in Settings).</summary>
public sealed class AlexaGuards
{
    public AlexaRateLimiter PerAddress { get; } = new(perMinute: 120);
    public AlexaRateLimiter PerAccount { get; } = new(perMinute: 30);
    AlexaRequestVerifier? verifier; string ids = "\0";
    readonly object gate = new();

    public AlexaRequestVerifier VerifierFor(string skillIds, IServiceProvider sp)
    {
        lock (gate)
        {
            if (verifier is null || ids != skillIds)
            {
                // test seam, the same one as the catalog redirect (loopback only): a test authority replaces the system trust store, so a test can sign requests
                var conf = sp.GetRequiredService<IConfiguration>();
                System.Security.Cryptography.X509Certificates.X509Certificate2Collection? roots = null;
                if (CatalogRedirectHandler.Parse(conf["FLACIE_CATALOG_REDIRECT"]) is not null && conf["FLACIE_ALEXA_TEST_ROOT"] is { Length: > 0 } pem && File.Exists(pem))
                    roots = [System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificateFromFile(pem)];
                verifier = new AlexaRequestVerifier(new HttpAlexaCertSource(sp.GetRequiredService<IHttpClientFactory>().CreateClient("catalog")), skillIds, null, roots); ids = skillIds;
            }
            return verifier;
        }
    }
}
