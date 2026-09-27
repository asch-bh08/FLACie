using IpodSync.Shared.Backend;
using IpodSync.Web;
using IpodSync.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddHttpClient();

// The web host always runs on the PC the iPod is physically plugged into, so it
// talks to IpodSync.Core directly -- see MauiProgram.cs for why Android can't.
builder.Services.AddScoped<IIpodSyncBackend, LocalIpodSyncBackend>();
builder.Services.AddScoped<IpodSync.Shared.State.AppState>();
builder.Services.AddScoped<IpodSync.Shared.Playback.IMediaSource, IpodSync.Web.WebMediaSource>();
builder.Services.AddScoped<IpodSync.Shared.Playback.AudioPlayer, IpodSync.Shared.Playback.HtmlAudioPlayer>();
builder.Services.AddScoped<IpodSync.Shared.Playback.PlayerState>();
builder.Services.AddScoped<IHostPickers>(_ => new InAppPickers());

// Jellyfin connection details: dotnet user-secrets (set "Jellyfin:BaseUrl"/"Jellyfin:ApiKey" --
// see README) for local dev, or the Jellyfin__BaseUrl / Jellyfin__ApiKey environment variables
// in any environment. Never hardcode the real key here or in a committed file.
builder.Services.AddSingleton(new IpodSync.Shared.JellyfinSettings(
    builder.Configuration["Jellyfin:BaseUrl"], builder.Configuration["Jellyfin:ApiKey"]));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapIpodMedia();
app.MapLocalMedia();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(IpodSync.Shared.Dashboard).Assembly);

// Read-only JSON API for remote clients (ipodplayer's Sync mode) on the local
// network/Tailscale -- reuses the same IIpodSyncBackend the Blazor UI calls,
// so nothing about the verified read path changes. See EDIT-PROTOCOL.md for
// the write side this is expected to grow into.
app.MapGet("/api/devices", async (IIpodSyncBackend backend, CancellationToken ct) =>
    Results.Ok(await backend.DetectDevicesAsync(ct)));

app.MapGet("/api/library", async (string root, IIpodSyncBackend backend, CancellationToken ct) =>
{
    try { return Results.Ok(await backend.LoadLibraryAsync(root, ct)); }
    catch (Exception ex) { return Results.Problem(ex.Message, statusCode: 500); }
});

// A folder of local files, independent of any iPod (LocalLibraryScanner). Audio for these
// tracks is served by /local-media (see MediaEndpoint.cs), the same way iPod tracks are
// served by /media -- both just stream a file from disk with Range support.
app.MapGet("/api/local-library", async (string folder, IIpodSyncBackend backend, CancellationToken ct) =>
{
    try { return Results.Ok(await backend.ScanLocalAsync(folder, ct)); }
    catch (Exception ex) { return Results.Problem(ex.Message, statusCode: 500); }
});

app.Run();
