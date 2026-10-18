using FLACie.Core;
using FLACie.RetroWasm;
using FLACie.Server;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// the page's own files (in memory): caches that last while the Retro screen is open
const string data = "/flacie";
Directory.CreateDirectory(data);
var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
var jf = new JellyfinClient(http, "flacie-android-retro", "FLACie Android");
builder.Services.AddSingleton(jf);
builder.Services.AddSingleton(new WebCatalog(http));
builder.Services.AddSingleton(new LyricsService(http, jf, Path.Combine(data, "lyrics")));
builder.Services.AddSingleton(new MbOverlay(Path.Combine(data, "mb-overlay.json")));
builder.Services.AddSingleton(new PlayTracker((_, _) => { }, null, () => false));

builder.Services.AddSingleton<AppSession>();
builder.Services.AddSingleton<ISessionStore, AppSessions>();
builder.Services.AddSingleton<IAutoplayPlanner, NoAutoplay>();
builder.Services.AddSingleton<ITrackInfo, AppTrackInfo>();
builder.Services.AddAuthorizationCore();
builder.Services.AddSingleton<AuthenticationStateProvider, AppAuth>();
builder.Services.AddSingleton<AppOutput>();
builder.Services.AddSingleton<IPlayerOutput>(sp => sp.GetRequiredService<AppOutput>());
builder.Services.AddSingleton<PlayerState>();

var host = builder.Build();
RetroHost.Version = "FLACie for Android " + AppBridge.Version(host.Services);
host.Services.GetRequiredService<AppSession>().Load();
host.Services.GetRequiredService<AppOutput>().Start(host.Services);
await host.RunAsync();
