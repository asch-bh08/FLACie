using FLACie.Core;
using FLACie.Server;
using FLACie.Server.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

// Everything the server keeps (sign-in key ring, cover cache) lives in one folder: mount it as a volume in Docker.
var dataDir = Directory.CreateDirectory(builder.Configuration["FLACIE_DATA"] ?? Path.Combine(AppContext.BaseDirectory, "data")).FullName;

builder.Services.AddDataProtection().PersistKeysToFileSystem(Directory.CreateDirectory(Path.Combine(dataDir, "keys"))).SetApplicationName("FLACie");
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.LoginPath = "/login";
    o.Cookie.Name = "flacie";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.SlidingExpiration = true;
    o.ExpireTimeSpan = TimeSpan.FromDays(30);
    // a cookie whose Jellyfin token Jellyfin has dropped is useless: sign it out, so the person lands on the sign-in page instead of an empty, non-admin page
    o.Events.OnValidatePrincipal = async ctx =>
    {
        if (ctx.Principal is { } p && ctx.HttpContext.RequestServices.GetRequiredService<SessionStore>().IsDead(p))
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
// FLACIE_SERVICE_ROUTES: "public=>internal" pairs, so this server reaches a download service on its own machine without going through the public name
var serviceRoutes = ServiceRouteHandler.Parse(builder.Configuration["FLACIE_SERVICE_ROUTES"]);
builder.Services.AddHttpClient("jellyfin", c => c.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddHttpClient("media", c => { c.Timeout = Timeout.InfiniteTimeSpan; c.DefaultRequestHeaders.UserAgent.ParseAdd("FLACie/1.0"); }).AddHttpMessageHandler(() => new ServiceRouteHandler(serviceRoutes));
builder.Services.AddHttpClient("catalog", c => { c.Timeout = TimeSpan.FromSeconds(12); c.DefaultRequestHeaders.UserAgent.ParseAdd("FLACie/1.0"); });
// the yt-dlp call (search, search, download) and the open-source file fetches run for minutes: the call carries its own time limit, so no client one
builder.Services.AddHttpClient("downloads-long", c => { c.Timeout = Timeout.InfiniteTimeSpan; c.DefaultRequestHeaders.UserAgent.ParseAdd("FLACie/1.0"); }).AddHttpMessageHandler(() => new ServiceRouteHandler(serviceRoutes));
builder.Services.AddHttpClient("downloads", c => { c.Timeout = TimeSpan.FromSeconds(30); c.DefaultRequestHeaders.UserAgent.ParseAdd("FLACie/1.0"); }).AddHttpMessageHandler(() => new ServiceRouteHandler(serviceRoutes));
builder.Services.AddSingleton(sp => new WebCatalog(sp.GetRequiredService<IHttpClientFactory>().CreateClient("catalog")));
builder.Services.AddSingleton(new DataPaths(dataDir));
builder.Services.AddSingleton<UserStateStore>();
builder.Services.AddSingleton<StorageGuard>();
builder.Services.AddSingleton<ActivityLog>();
builder.Services.AddSingleton<ClientRegistry>();
builder.Services.AddSingleton<Notifier>();
builder.Services.AddSingleton<StorageService>();
builder.Services.AddSingleton<DownloadLog>();
builder.Services.AddSingleton<DownloadManager>();
builder.Services.AddSingleton<AutoplayPlanner>();
builder.Services.AddSingleton<ImportManager>();
builder.Services.AddSingleton<ChartsService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ChartsService>());
builder.Services.AddSingleton<InfoService>();
builder.Services.AddSingleton<FormatIndex>();
builder.Services.AddSingleton<FormatProbeService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<FormatProbeService>());
builder.Services.AddHostedService<ProfileSyncService>();
builder.Services.AddSingleton(sp => new LyricsService(sp.GetRequiredService<IHttpClientFactory>().CreateClient("catalog"), sp.GetRequiredService<JellyfinClient>(), Path.Combine(dataDir, "lyrics")));
// one device id per server install, so Jellyfin lists FLACie Web as one device
var deviceIdFile = Path.Combine(dataDir, "device-id");
if (!File.Exists(deviceIdFile)) File.WriteAllText(deviceIdFile, Guid.NewGuid().ToString("N"));
var deviceId = File.ReadAllText(deviceIdFile).Trim();
builder.Services.AddSingleton(sp => new JellyfinClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("jellyfin"), deviceId)
{
    // FLACIE_JELLYFIN_INTERNAL_URL: where this server reaches Jellyfin when that differs from FLACIE_JELLYFIN_URL (e.g. http://127.0.0.1:8096)
    PublicBase = builder.Configuration["FLACIE_JELLYFIN_URL"] is { Length: > 0 } pub ? JellyfinClient.Normalise(pub) : null,
    InternalBase = builder.Configuration["FLACIE_JELLYFIN_INTERNAL_URL"] is { Length: > 0 } inner ? JellyfinClient.Normalise(inner) : null,
});
builder.Services.AddSingleton<SessionStore>();
builder.Services.AddScoped<PlayerState>();
builder.Services.AddScoped<Toasts>();
builder.Services.AddScoped<ExploreState>();
// the live page connection: a phone that slept or lost signal for a while picks its page up again (30 minutes) instead of getting "connection was reset"
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(o => { o.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(30); o.DisconnectedCircuitMaxRetained = 300; })
    .AddHubOptions(o => { o.KeepAliveInterval = TimeSpan.FromSeconds(10); o.ClientTimeoutInterval = TimeSpan.FromSeconds(90); o.HandshakeTimeout = TimeSpan.FromSeconds(30); });

// behind a reverse proxy (Caddy, Nginx, Traefik, Tailscale Funnel) the browser talks HTTPS to the proxy, not to us
// ... but only a configured proxy is believed (FLACIE_TRUSTED_PROXIES; by default loopback and private networks, never the public internet)
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(o => ProxyTrust.Configure(o, builder.Configuration));
builder.Services.AddSingleton<LoginThrottle>();

var app = builder.Build();
// hosted by the Windows app: stop when that app does, so closing it never leaves a server running
if (int.TryParse(builder.Configuration["FLACIE_PARENT_PID"], out var parentPid))
{
    _ = Task.Run(async () =>
    {
        try { using var parent = System.Diagnostics.Process.GetProcessById(parentPid); await parent.WaitForExitAsync(); } catch (Exception) { }
        app.Lifetime.StopApplication();
    });
}
app.Use((ctx, next) => { ctx.Items["viaProxy"] = ProxyTrust.IsTrusted(ctx.Connection.RemoteIpAddress); return next(); });
app.UseForwardedHeaders();
// a Tailscale name (*.ts.net) is only ever reached over https (Tailscale ends the TLS itself and hands the request on as plain http, and a second proxy in between
// may not pass that on), so redirects, cookies and links must say https. Only when the request came through a configured proxy: the Host header alone proves nothing.
app.Use((ctx, next) =>
{
    if (ctx.Items["viaProxy"] is true && ctx.Request.Host.Host.EndsWith(".ts.net", StringComparison.OrdinalIgnoreCase)) ctx.Request.Scheme = "https";
    return next();
});
app.Use((ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["Referrer-Policy"] = "same-origin";
    h["X-Frame-Options"] = "SAMEORIGIN";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    if (ctx.Request.IsHttps) h["Strict-Transport-Security"] = "max-age=15552000";
    return next();
});
if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/error", createScopeForErrors: true);
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapAuth();
app.MapMedia(dataDir);
app.MapApi();
// local diagnostics only (FLACIE_DEBUG=1): the signed-in user's own Jellyfin, GET or POST, so a session problem can be looked at directly
if (builder.Configuration["FLACIE_DEBUG"] == "1")
{
    app.Logger.LogWarning("FLACIE_DEBUG=1: the /debug endpoints are on (loopback only). Never leave this on a server other people can reach.");
    // local testing only: sign the browser in as an account this server already remembers (it was approved through Quick Connect earlier)
    app.MapGet("/debug/login", async (HttpContext ctx, UserStateStore states) =>
    {
        if (!System.Net.IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress!)) return Results.NotFound();
        var j = states.Remembered().FirstOrDefault();
        if (j is null) return Results.NotFound();
        await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.SignInAsync(ctx, Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme, SessionStore.Principal(j, null),
            new Microsoft.AspNetCore.Authentication.AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30) });
        return Results.Redirect("/");
    });
    // local testing of /api/*: the signed-in account's own token, loopback only (never printed or stored anywhere)
    app.MapGet("/debug/token", (HttpContext ctx, SessionStore store) =>
        !System.Net.IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress!) || store.For(ctx.User).Jellyfin is not { } a ? Results.NotFound() : Results.Text(a.Token)).RequireAuthorization();
    // the signed-in user's library as JSON (title, artist, album), to compare another folder against it
    app.MapGet("/debug/library", (HttpContext ctx, SessionStore store) =>
        Results.Json(store.For(ctx.User).Library.Songs.Select(t => new { t = t.Title, a = t.Artist, al = t.Album, p = t.Path }))).RequireAuthorization();
    app.MapMethods("/debug/jf", ["GET", "POST", "DELETE"], async (HttpContext ctx, string path, SessionStore store, JellyfinClient jf) =>
    {
        var a = store.For(ctx.User).Jellyfin; if (a is null) return Results.NotFound();
        System.Text.Json.Nodes.JsonNode? body = null;
        if (ctx.Request.Method == "POST" && ctx.Request.ContentLength > 0) body = System.Text.Json.Nodes.JsonNode.Parse(await new StreamReader(ctx.Request.Body).ReadToEndAsync());
        try { var r = ctx.Request.Method == "POST" ? await jf.PostAsync(a, path, body) : ctx.Request.Method == "DELETE" ? await jf.DeleteAsync(a, path) : await jf.GetAsync(a, path); return Results.Text(r?.ToJsonString() ?? "(empty)", "application/json"); }
        catch (Exception e) { return Results.Text(e.Message, "text/plain", statusCode: 500); }
    }).RequireAuthorization();
}
// who and what is asking, for the admin dashboard (the address as the server sees it)
app.MapGet("/api/whoami", (HttpContext ctx) => Results.Json(new { ip = ctx.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "", ua = ctx.Request.Headers.UserAgent.ToString() })).RequireAuthorization();
app.MapGet("/healthz", () => Results.Ok("ok"));
app.Lifetime.ApplicationStarted.Register(() => { var a = app.Services.GetRequiredService<ActivityLog>(); a.Add("server", "", "FLACie started"); _ = app.Services.GetRequiredService<Notifier>().NotifyAsync("started", "FLACie started", "The server is up again.", 2, "rocket"); });
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
