using IpodSync.Shared.Backend;
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
builder.Services.AddScoped<IHostPickers>(_ => new InAppPickers());

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

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(IpodSync.Shared.Dashboard).Assembly);

app.Run();
