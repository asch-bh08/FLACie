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
// one device id per server install, so Jellyfin lists FLACie Web as one device
var deviceIdFile = Path.Combine(dataDir, "device-id");
if (!File.Exists(deviceIdFile)) File.WriteAllText(deviceIdFile, Guid.NewGuid().ToString("N"));
var deviceId = File.ReadAllText(deviceIdFile).Trim();
builder.Services.AddSingleton(sp => new JellyfinClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("jellyfin"), deviceId));
builder.Services.AddSingleton<SessionStore>();
builder.Services.AddScoped<PlayerState>();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// behind a reverse proxy (Caddy, Nginx, Traefik, Tailscale Funnel) the browser talks HTTPS to the proxy, not to us
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost;
    o.KnownNetworks.Clear(); o.KnownProxies.Clear();
});

var app = builder.Build();
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/error", createScopeForErrors: true);
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapAuth();
app.MapMedia(dataDir);
app.MapGet("/healthz", () => Results.Ok("ok"));
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
