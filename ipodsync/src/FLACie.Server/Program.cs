using FLACie.Core;
using FLACie.Server;
using FLACie.Server.Components;
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
});
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpClient("jellyfin", c => c.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddHttpClient("media", c => { c.Timeout = Timeout.InfiniteTimeSpan; c.DefaultRequestHeaders.UserAgent.ParseAdd("FLACie/1.0"); });
builder.Services.AddHttpClient("catalog", c => { c.Timeout = TimeSpan.FromSeconds(12); c.DefaultRequestHeaders.UserAgent.ParseAdd("FLACie/1.0"); });
builder.Services.AddHttpClient("downloads", c => { c.Timeout = TimeSpan.FromSeconds(30); c.DefaultRequestHeaders.UserAgent.ParseAdd("FLACie/1.0"); });
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
builder.Services.AddScoped<ExploreState>();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// behind a reverse proxy (Caddy, Nginx, Traefik, Tailscale Funnel) the browser talks HTTPS to the proxy, not to us
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost;
    o.KnownNetworks.Clear(); o.KnownProxies.Clear();
});

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
app.UseForwardedHeaders();
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
